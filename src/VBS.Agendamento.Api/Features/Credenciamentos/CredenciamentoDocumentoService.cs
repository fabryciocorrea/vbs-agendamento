using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Documentos;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Credenciamentos;

/// <summary>
/// Documentos anexados ao credenciamento de convênios (protocolo, contrato, carta de aprovação, tabela de valores...).
/// Permissão própria: credenciamento.documento (visualizar, incluir, excluir). PDF, Word, Excel, JPG ou PNG de até 10 MB,
/// conferidos pelo conteúdo. Todo envio, abertura e exclusão vai para o log.
/// </summary>
public sealed class CredenciamentoDocumentoService
{
    public const int TamanhoMaximo = 10 * 1024 * 1024;
    public const int MaximoPorCredenciamento = 30;

    public static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string>
    {
        ["PROTOCOLO"] = "Protocolo",
        ["CONTRATO"] = "Contrato",
        ["APROVACAO"] = "Carta ou aprovação",
        ["TABELA_VALORES"] = "Tabela de valores",
        ["COMUNICADO"] = "Comunicado",
        ["OUTRO"] = "Outro"
    };

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public CredenciamentoDocumentoService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
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
        public int Codigo { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string TipoConteudo { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
    }

    private static async Task ExigirCredenciamentoAsync(SqlConnection con, SqlTransaction? tx, int convenioId, int credenciamentoId, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.CREDENCIAMENTO_CONVENIO WHERE CODIGO_CREDENCIAMENTO = @Id AND CODIGO_CONVENIO = @Conv;",
            new { Id = credenciamentoId, Conv = convenioId }, tx, ct));
        if (existe == 0) throw Erro.NaoEncontrado("Credenciamento não encontrado.");
    }

    public async Task<List<CredenciamentoDocumentoDto>> ListarAsync(int convenioId, int credenciamentoId, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await ExigirCredenciamentoAsync(con, null, convenioId, credenciamentoId, ct);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(@"
SELECT D.CODIGO_PUBLIC AS Id, D.TIPO_DOCUMENTO AS Tipo, D.DESCRICAO AS Descricao, D.NOME_ARQUIVO AS NomeArquivo,
       D.TIPO_CONTEUDO AS TipoConteudo, D.TAMANHO_BYTES AS TamanhoBytes, D.DATA_HORA AS EnviadoEm,
       D.CODIGO_USUARIO AS UsuarioId, U.NOME_COMPLETO AS UsuarioNome
FROM DBO.CREDENCIAMENTO_DOCUMENTO D
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = D.CODIGO_USUARIO
WHERE D.CODIGO_CREDENCIAMENTO = @Cred
ORDER BY D.DATA_HORA DESC, D.CODIGO_CREDENCIAMENTO_DOCUMENTO DESC;", new { Cred = credenciamentoId }, ct: ct));

        return linhas.Select(l => new CredenciamentoDocumentoDto
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

    public async Task<CredenciamentoDocumentoDto> EnviarAsync(int convenioId, int credenciamentoId, string? tipo, string? descricao, string? nomeOriginal, byte[] conteudo, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        var tipoLimpo = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (tipoLimpo.Length == 0) erros.Add(new ErroCampo("tipo", "Campo Não Preenchido"));
        else if (!Tipos.ContainsKey(tipoLimpo)) erros.Add(new ErroCampo("tipo", "Tipo de documento inválido"));

        var desc = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        if (desc is { Length: > 150 }) erros.Add(new ErroCampo("descricao", "No máximo 150 caracteres"));
        if (tipoLimpo == "OUTRO" && desc is null) erros.Add(new ErroCampo("descricao", "Descreva o documento"));

        var nome = Path.GetFileName((nomeOriginal ?? string.Empty).Replace('\\', '/')).Trim();
        if (nome.Length == 0) nome = "documento";
        if (nome.Length > 255) nome = nome[^255..];

        string? tipoConteudo = null;
        if (conteudo.Length == 0) erros.Add(new ErroCampo("arquivo", "Anexe o documento"));
        else if (conteudo.Length > TamanhoMaximo) erros.Add(new ErroCampo("arquivo", "O documento deve ter no máximo 10 MB"));
        else
        {
            tipoConteudo = DocumentoInternoService.TipoDoArquivo(nome, conteudo);
            if (tipoConteudo is null) erros.Add(new ErroCampo("arquivo", "Use um arquivo PDF, Word (doc, docx), Excel (xls, xlsx), JPG ou PNG"));
        }
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var hash = Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant();

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await ExigirCredenciamentoAsync(con, tx, convenioId, credenciamentoId, ct);

        var existentes = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.CREDENCIAMENTO_DOCUMENTO WHERE CODIGO_CREDENCIAMENTO = @Cred;", new { Cred = credenciamentoId }, tx, ct));
        if (existentes >= MaximoPorCredenciamento)
            throw Erro.Conflito($"O credenciamento já tem {MaximoPorCredenciamento} documentos. Exclua algum antes de enviar outro.", "LIMITE_DOCUMENTOS");

        var publico = Guid.NewGuid();
        var codigo = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.CREDENCIAMENTO_DOCUMENTO (CODIGO_PUBLIC, CODIGO_CREDENCIAMENTO, TIPO_DOCUMENTO, DESCRICAO, NOME_ARQUIVO, TIPO_CONTEUDO, TAMANHO_BYTES, HASH_SHA256, CONTEUDO, CODIGO_USUARIO)
VALUES (@Publico, @Cred, @Tipo, @Descricao, @Nome, @TipoConteudo, @Tamanho, @Hash, @Conteudo, @Usu);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Publico = publico, Cred = credenciamentoId, Tipo = tipoLimpo, Descricao = desc, Nome = nome, TipoConteudo = tipoConteudo, Tamanho = conteudo.Length, Hash = hash, Conteudo = conteudo, Usu = usuario.Id }, tx, ct));

        await _auditoria.RegistrarAsync("INCLUIR", "CREDENCIAMENTO_DOCUMENTO", codigo, null, usuario.Id,
            new { credenciamentoId, tipo = tipoLimpo, arquivo = nome, tamanho = conteudo.Length, sha256 = hash }, con, tx, ct);
        await tx.CommitAsync(ct);

        var nomeUsuario = await con.ExecuteScalarAsync<string?>(Sql.Cmd(
            "SELECT NOME_COMPLETO FROM DBO.USUARIO WHERE CODIGO_USUARIO = @Id;", new { Id = usuario.Id }, ct: ct));
        return new CredenciamentoDocumentoDto
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

    public async Task<(byte[] Conteudo, string Nome, string Tipo)> BaixarAsync(int convenioId, int credenciamentoId, Guid documentoId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await ExigirCredenciamentoAsync(con, null, convenioId, credenciamentoId, ct);
        var d = await con.QueryFirstOrDefaultAsync<Conteudo>(Sql.Cmd(@"
SELECT CODIGO_CREDENCIAMENTO_DOCUMENTO AS Codigo, TIPO_DOCUMENTO AS Tipo, NOME_ARQUIVO AS Nome, TIPO_CONTEUDO AS TipoConteudo, CONTEUDO AS Bytes
FROM DBO.CREDENCIAMENTO_DOCUMENTO WHERE CODIGO_PUBLIC = @Id AND CODIGO_CREDENCIAMENTO = @Cred;",
            new { Id = documentoId, Cred = credenciamentoId }, ct: ct)) ?? throw Erro.NaoEncontrado("Documento não encontrado.");
        await _auditoria.RegistrarAsync("VISUALIZAR", "CREDENCIAMENTO_DOCUMENTO", d.Codigo, null, usuario.Id,
            new { credenciamentoId, tipo = d.Tipo, arquivo = d.Nome }, con, null, ct);
        return (d.Bytes, d.Nome, d.TipoConteudo);
    }

    public async Task ExcluirAsync(int convenioId, int credenciamentoId, Guid documentoId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await ExigirCredenciamentoAsync(con, tx, convenioId, credenciamentoId, ct);
        var d = await con.QueryFirstOrDefaultAsync<Conteudo>(Sql.Cmd(@"
SELECT CODIGO_CREDENCIAMENTO_DOCUMENTO AS Codigo, TIPO_DOCUMENTO AS Tipo, NOME_ARQUIVO AS Nome, TIPO_CONTEUDO AS TipoConteudo, HASH_SHA256 AS Hash
FROM DBO.CREDENCIAMENTO_DOCUMENTO WHERE CODIGO_PUBLIC = @Id AND CODIGO_CREDENCIAMENTO = @Cred;",
            new { Id = documentoId, Cred = credenciamentoId }, tx, ct)) ?? throw Erro.NaoEncontrado("Documento não encontrado.");

        await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.CREDENCIAMENTO_DOCUMENTO WHERE CODIGO_CREDENCIAMENTO_DOCUMENTO = @Codigo;", new { d.Codigo }, tx, ct));
        await _auditoria.RegistrarAsync("EXCLUIR", "CREDENCIAMENTO_DOCUMENTO", d.Codigo, null, usuario.Id,
            new { credenciamentoId, tipo = d.Tipo, arquivo = d.Nome, sha256 = d.Hash }, con, tx, ct);
        await tx.CommitAsync(ct);
    }
}
