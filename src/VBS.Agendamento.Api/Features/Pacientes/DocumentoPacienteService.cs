using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>
/// Documentos do paciente (carteira de identidade, encaminhamento, autorização, carteirinha, exames...).
/// Permissão própria: paciente.documento (visualizar, incluir, excluir). O profissional vinculado só acessa os
/// documentos dos pacientes de que é o responsável atual (RN16, RN49). PDF, JPG ou PNG de até 5 MB, conferidos pelo
/// conteúdo. Todo envio, abertura e exclusão vai para o log.
/// </summary>
public sealed class DocumentoPacienteService
{
    public const int TamanhoMaximo = 5 * 1024 * 1024;
    public const int MaximoPorPaciente = 50;

    public static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string>
    {
        ["IDENTIDADE"] = "Documento de identidade",
        ["ENCAMINHAMENTO"] = "Encaminhamento",
        ["AUTORIZACAO"] = "Autorização",
        ["CARTEIRINHA"] = "Carteirinha do convênio",
        ["EXAME"] = "Exame ou laudo",
        ["OUTRO"] = "Outro"
    };

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public DocumentoPacienteService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class PacienteBase
    {
        public int Codigo { get; set; }
        public int ProfissionalResponsavel { get; set; }
    }

    private sealed class Linha
    {
        public Guid Id { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string NomeArquivo { get; set; } = string.Empty;
        public string TipoConteudo { get; set; } = string.Empty;
        public int TamanhoBytes { get; set; }
        public DateTime EnviadoEm { get; set; }
        public int? UsuarioId { get; set; }
        public string? UsuarioNome { get; set; }
    }

    private sealed class Conteudo
    {
        public long Codigo { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string TipoConteudo { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
    }

    /// <summary>O paciente de outro profissional aparece como não encontrado para o usuário vinculado a um profissional.</summary>
    private static async Task<PacienteBase> ExigirPacienteAsync(SqlConnection con, SqlTransaction? tx, Guid pacienteId, UsuarioAtual usuario, CancellationToken ct)
    {
        var p = await con.QueryFirstOrDefaultAsync<PacienteBase>(Sql.Cmd(
            "SELECT CODIGO_PACIENTE AS Codigo, CODIGO_PROFISSIONAL_RESPONSAVEL AS ProfissionalResponsavel FROM DBO.PACIENTE WHERE CODIGO_PUBLIC = @Id;",
            new { Id = pacienteId }, tx, ct)) ?? throw Erro.NaoEncontrado("Paciente não encontrado.");
        if (!usuario.Administrador && usuario.EhMedico && usuario.ProfissionalId != p.ProfissionalResponsavel)
            throw Erro.NaoEncontrado("Paciente não encontrado.");
        return p;
    }

    /// <summary>Tipo real do arquivo pelos primeiros bytes (PDF, JPEG ou PNG); a extensão não vale.</summary>
    private static string? TipoDoArquivo(byte[] b)
    {
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D) return "application/pdf";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";
        return null;
    }

    public async Task<List<DocumentoPacienteDto>> ListarAsync(Guid pacienteId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirPacienteAsync(con, null, pacienteId, usuario, ct);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(@"
SELECT D.CODIGO_PUBLIC AS Id, D.TIPO_DOCUMENTO AS Tipo, D.DESCRICAO AS Descricao, D.NOME_ARQUIVO AS NomeArquivo,
       D.TIPO_CONTEUDO AS TipoConteudo, D.TAMANHO_BYTES AS TamanhoBytes, D.DATA_HORA AS EnviadoEm,
       D.CODIGO_USUARIO AS UsuarioId, U.NOME_COMPLETO AS UsuarioNome
FROM DBO.PACIENTE_DOCUMENTO D
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = D.CODIGO_USUARIO
WHERE D.CODIGO_PACIENTE = @Pac
ORDER BY D.DATA_HORA DESC, D.CODIGO_PACIENTE_DOCUMENTO DESC;", new { Pac = paciente.Codigo }, ct: ct));

        return linhas.Select(l => new DocumentoPacienteDto
        {
            Id = l.Id,
            Tipo = l.Tipo,
            TipoNome = Tipos.TryGetValue(l.Tipo, out var nome) ? nome : l.Tipo,
            Descricao = l.Descricao,
            NomeArquivo = l.NomeArquivo,
            TipoConteudo = l.TipoConteudo,
            TamanhoBytes = l.TamanhoBytes,
            EnviadoEm = DataBrasil.Utc(l.EnviadoEm),
            EnviadoPor = l.UsuarioId.HasValue ? new UsuarioReferenciaDto { Id = l.UsuarioId.Value, NomeCompleto = l.UsuarioNome ?? string.Empty } : null
        }).ToList();
    }

    public async Task<DocumentoPacienteDto> EnviarAsync(Guid pacienteId, string? tipo, string? descricao, string? nomeOriginal, byte[] conteudo, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        var tipoLimpo = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (tipoLimpo.Length == 0) erros.Add(new ErroCampo("tipo", "Campo Não Preenchido"));
        else if (!Tipos.ContainsKey(tipoLimpo)) erros.Add(new ErroCampo("tipo", "Tipo de documento inválido"));

        var desc = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        if (desc is { Length: > 150 }) erros.Add(new ErroCampo("descricao", "No máximo 150 caracteres"));
        if (tipoLimpo == "OUTRO" && desc is null) erros.Add(new ErroCampo("descricao", "Descreva o documento"));

        if (conteudo.Length == 0) erros.Add(new ErroCampo("arquivo", "Anexe o documento"));
        else if (conteudo.Length > TamanhoMaximo) erros.Add(new ErroCampo("arquivo", "O documento deve ter no máximo 5 MB"));
        var tipoConteudo = conteudo.Length > 0 ? TipoDoArquivo(conteudo) : null;
        if (conteudo.Length > 0 && tipoConteudo is null) erros.Add(new ErroCampo("arquivo", "Use um arquivo PDF, JPG ou PNG"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var nome = Path.GetFileName((nomeOriginal ?? string.Empty).Replace('\\', '/')).Trim();
        if (nome.Length == 0) nome = "documento";
        if (nome.Length > 255) nome = nome[^255..];
        var hash = Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant();

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var paciente = await ExigirPacienteAsync(con, tx, pacienteId, usuario, ct);

        var existentes = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.PACIENTE_DOCUMENTO WHERE CODIGO_PACIENTE = @Pac;", new { Pac = paciente.Codigo }, tx, ct));
        if (existentes >= MaximoPorPaciente)
            throw Erro.Conflito($"O paciente já tem {MaximoPorPaciente} documentos. Exclua algum antes de enviar outro.", "LIMITE_DOCUMENTOS");

        var publico = Guid.NewGuid();
        var codigo = await con.ExecuteScalarAsync<long>(Sql.Cmd(@"
INSERT INTO DBO.PACIENTE_DOCUMENTO (CODIGO_PUBLIC, CODIGO_PACIENTE, TIPO_DOCUMENTO, DESCRICAO, NOME_ARQUIVO, TIPO_CONTEUDO, TAMANHO_BYTES, HASH_SHA256, CONTEUDO, CODIGO_USUARIO)
VALUES (@Publico, @Pac, @Tipo, @Descricao, @Nome, @TipoConteudo, @Tamanho, @Hash, @Conteudo, @Usu);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Publico = publico, Pac = paciente.Codigo, Tipo = tipoLimpo, Descricao = desc, Nome = nome, TipoConteudo = tipoConteudo, Tamanho = conteudo.Length, Hash = hash, Conteudo = conteudo, Usu = usuario.Id }, tx, ct));

        await _auditoria.RegistrarAsync("INCLUIR", "PACIENTE_DOCUMENTO", codigo, paciente.Codigo, usuario.Id,
            new { tipo = tipoLimpo, arquivo = nome, tamanho = conteudo.Length, sha256 = hash }, con, tx, ct);
        await tx.CommitAsync(ct);

        var nomeUsuario = await con.ExecuteScalarAsync<string?>(Sql.Cmd(
            "SELECT NOME_COMPLETO FROM DBO.USUARIO WHERE CODIGO_USUARIO = @Id;", new { Id = usuario.Id }, ct: ct));
        return new DocumentoPacienteDto
        {
            Id = publico,
            Tipo = tipoLimpo,
            TipoNome = Tipos[tipoLimpo],
            Descricao = desc,
            NomeArquivo = nome,
            TipoConteudo = tipoConteudo!,
            TamanhoBytes = conteudo.Length,
            EnviadoEm = DateTime.UtcNow,
            EnviadoPor = new UsuarioReferenciaDto { Id = usuario.Id, NomeCompleto = nomeUsuario ?? string.Empty }
        };
    }

    public async Task<(byte[] Conteudo, string Nome, string Tipo)> BaixarAsync(Guid pacienteId, Guid documentoId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirPacienteAsync(con, null, pacienteId, usuario, ct);
        var d = await con.QueryFirstOrDefaultAsync<Conteudo>(Sql.Cmd(@"
SELECT CODIGO_PACIENTE_DOCUMENTO AS Codigo, TIPO_DOCUMENTO AS Tipo, NOME_ARQUIVO AS Nome, TIPO_CONTEUDO AS TipoConteudo, CONTEUDO AS Bytes
FROM DBO.PACIENTE_DOCUMENTO WHERE CODIGO_PUBLIC = @Id AND CODIGO_PACIENTE = @Pac;",
            new { Id = documentoId, Pac = paciente.Codigo }, ct: ct)) ?? throw Erro.NaoEncontrado("Documento não encontrado.");
        await _auditoria.RegistrarAsync("VISUALIZAR", "PACIENTE_DOCUMENTO", d.Codigo, paciente.Codigo, usuario.Id,
            new { tipo = d.Tipo, arquivo = d.Nome }, con, null, ct);
        return (d.Bytes, d.Nome, d.TipoConteudo);
    }

    public async Task ExcluirAsync(Guid pacienteId, Guid documentoId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var paciente = await ExigirPacienteAsync(con, tx, pacienteId, usuario, ct);
        var d = await con.QueryFirstOrDefaultAsync<Conteudo>(Sql.Cmd(@"
SELECT CODIGO_PACIENTE_DOCUMENTO AS Codigo, TIPO_DOCUMENTO AS Tipo, NOME_ARQUIVO AS Nome, TIPO_CONTEUDO AS TipoConteudo, HASH_SHA256 AS Hash
FROM DBO.PACIENTE_DOCUMENTO WHERE CODIGO_PUBLIC = @Id AND CODIGO_PACIENTE = @Pac;",
            new { Id = documentoId, Pac = paciente.Codigo }, tx, ct)) ?? throw Erro.NaoEncontrado("Documento não encontrado.");

        await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PACIENTE_DOCUMENTO WHERE CODIGO_PACIENTE_DOCUMENTO = @Codigo;", new { d.Codigo }, tx, ct));
        await _auditoria.RegistrarAsync("EXCLUIR", "PACIENTE_DOCUMENTO", d.Codigo, paciente.Codigo, usuario.Id,
            new { tipo = d.Tipo, arquivo = d.Nome, sha256 = d.Hash }, con, tx, ct);
        await tx.CommitAsync(ct);
    }
}
