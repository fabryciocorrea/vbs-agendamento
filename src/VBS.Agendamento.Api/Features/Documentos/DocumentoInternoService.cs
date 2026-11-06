using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Documentos;

/// <summary>
/// Documentos internos da empresa (folha timbrada, termos, contratos, políticas, formulários, modelos...).
/// Permissão própria: documento.interno (visualizar, incluir, excluir). PDF, Word, Excel, JPG ou PNG de até 10 MB,
/// conferidos pelo conteúdo. Todo envio, abertura e exclusão vai para o log.
/// </summary>
public sealed class DocumentoInternoService
{
    public const int TamanhoMaximo = 10 * 1024 * 1024;
    public const int MaximoDocumentos = 500;

    public static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string>
    {
        ["FOLHA_TIMBRADA"] = "Folha timbrada",
        ["TERMO"] = "Termo",
        ["CONTRATO"] = "Contrato",
        ["POLITICA"] = "Política ou norma",
        ["FORMULARIO"] = "Formulário",
        ["MODELO"] = "Modelo de documento",
        ["OUTRO"] = "Outro"
    };

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public DocumentoInternoService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class Linha
    {
        public Guid Id { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
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
        public string Titulo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string TipoConteudo { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Tipo do arquivo pela extensão, conferido pelos primeiros bytes (a extensão sozinha não vale). Nulo se não for aceito.</summary>
    internal static string? TipoDoArquivo(string nome, byte[] b)
    {
        var ext = Path.GetExtension(nome).ToLowerInvariant();
        bool Zip() => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04;
        bool Ole() => b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0 && b[4] == 0xA1 && b[5] == 0xB1 && b[6] == 0x1A && b[7] == 0xE1;
        switch (ext)
        {
            case ".pdf":
                return b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D ? "application/pdf" : null;
            case ".jpg":
            case ".jpeg":
                return b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg" : null;
            case ".png":
                return b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A ? "image/png" : null;
            case ".docx":
                return Zip() ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : null;
            case ".xlsx":
                return Zip() ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : null;
            case ".doc":
                return Ole() ? "application/msword" : null;
            case ".xls":
                return Ole() ? "application/vnd.ms-excel" : null;
            default:
                return null;
        }
    }

    public async Task<List<DocumentoInternoDto>> ListarAsync(string? tipo, string? busca, CancellationToken ct)
    {
        var tipoLimpo = string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim().ToUpperInvariant();
        if (tipoLimpo is not null && !Tipos.ContainsKey(tipoLimpo)) throw Erro.Validacao("tipo", "Tipo de documento inválido");
        var texto = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
        if (texto is { Length: > 100 }) texto = texto[..100];

        await using var con = await _db.AbrirAsync(ct);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(@"
SELECT TOP (@Max) D.CODIGO_PUBLIC AS Id, D.TIPO_DOCUMENTO AS Tipo, D.TITULO AS Titulo, D.DESCRICAO AS Descricao, D.NOME_ARQUIVO AS NomeArquivo,
       D.TIPO_CONTEUDO AS TipoConteudo, D.TAMANHO_BYTES AS TamanhoBytes, D.DATA_HORA AS EnviadoEm,
       D.CODIGO_USUARIO AS UsuarioId, U.NOME_COMPLETO AS UsuarioNome
FROM DBO.DOCUMENTO_INTERNO D
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = D.CODIGO_USUARIO
WHERE (@Tipo IS NULL OR D.TIPO_DOCUMENTO = @Tipo)
  AND (@Busca IS NULL OR D.TITULO LIKE '%' + @Busca + '%' OR D.NOME_ARQUIVO LIKE '%' + @Busca + '%' OR D.DESCRICAO LIKE '%' + @Busca + '%')
ORDER BY D.DATA_HORA DESC, D.CODIGO_DOCUMENTO_INTERNO DESC;",
            new { Max = MaximoDocumentos, Tipo = tipoLimpo, Busca = texto }, ct: ct));

        return linhas.Select(l => new DocumentoInternoDto
        {
            Id = l.Id,
            Tipo = l.Tipo,
            TipoNome = Tipos.TryGetValue(l.Tipo, out var nome) ? nome : l.Tipo,
            Titulo = l.Titulo,
            Descricao = l.Descricao,
            NomeArquivo = l.NomeArquivo,
            TipoConteudo = l.TipoConteudo,
            TamanhoBytes = l.TamanhoBytes,
            EnviadoEm = DataBrasil.Utc(l.EnviadoEm),
            EnviadoPor = l.UsuarioId.HasValue ? new UsuarioReferenciaDto { Id = l.UsuarioId.Value, NomeCompleto = l.UsuarioNome ?? string.Empty } : null
        }).ToList();
    }

    public async Task<DocumentoInternoDto> EnviarAsync(string? tipo, string? titulo, string? descricao, string? nomeOriginal, byte[] conteudo, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        var tipoLimpo = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (tipoLimpo.Length == 0) erros.Add(new ErroCampo("tipo", "Campo Não Preenchido"));
        else if (!Tipos.ContainsKey(tipoLimpo)) erros.Add(new ErroCampo("tipo", "Tipo de documento inválido"));

        var tit = (titulo ?? string.Empty).Trim();
        if (tit.Length == 0) erros.Add(new ErroCampo("titulo", "Campo Não Preenchido"));
        else if (tit.Length > 150) erros.Add(new ErroCampo("titulo", "No máximo 150 caracteres"));

        var desc = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        if (desc is { Length: > 300 }) erros.Add(new ErroCampo("descricao", "No máximo 300 caracteres"));

        var nome = Path.GetFileName((nomeOriginal ?? string.Empty).Replace('\\', '/')).Trim();
        if (nome.Length == 0) nome = "documento";
        if (nome.Length > 255) nome = nome[^255..];

        string? tipoConteudo = null;
        if (conteudo.Length == 0) erros.Add(new ErroCampo("arquivo", "Anexe o documento"));
        else if (conteudo.Length > TamanhoMaximo) erros.Add(new ErroCampo("arquivo", "O documento deve ter no máximo 10 MB"));
        else
        {
            tipoConteudo = TipoDoArquivo(nome, conteudo);
            if (tipoConteudo is null) erros.Add(new ErroCampo("arquivo", "Use um arquivo PDF, Word (doc, docx), Excel (xls, xlsx), JPG ou PNG"));
        }
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var hash = Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant();

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var existentes = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.DOCUMENTO_INTERNO;", null, tx, ct));
        if (existentes >= MaximoDocumentos)
            throw Erro.Conflito($"Já existem {MaximoDocumentos} documentos. Exclua algum antes de enviar outro.", "LIMITE_DOCUMENTOS");

        var publico = Guid.NewGuid();
        var codigo = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.DOCUMENTO_INTERNO (CODIGO_PUBLIC, TIPO_DOCUMENTO, TITULO, DESCRICAO, NOME_ARQUIVO, TIPO_CONTEUDO, TAMANHO_BYTES, HASH_SHA256, CONTEUDO, CODIGO_USUARIO)
VALUES (@Publico, @Tipo, @Titulo, @Descricao, @Nome, @TipoConteudo, @Tamanho, @Hash, @Conteudo, @Usu);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Publico = publico, Tipo = tipoLimpo, Titulo = tit, Descricao = desc, Nome = nome, TipoConteudo = tipoConteudo, Tamanho = conteudo.Length, Hash = hash, Conteudo = conteudo, Usu = usuario.Id }, tx, ct));

        await _auditoria.RegistrarAsync("INCLUIR", "DOCUMENTO_INTERNO", codigo, null, usuario.Id,
            new { tipo = tipoLimpo, titulo = tit, arquivo = nome, tamanho = conteudo.Length, sha256 = hash }, con, tx, ct);
        await tx.CommitAsync(ct);

        var nomeUsuario = await con.ExecuteScalarAsync<string?>(Sql.Cmd(
            "SELECT NOME_COMPLETO FROM DBO.USUARIO WHERE CODIGO_USUARIO = @Id;", new { Id = usuario.Id }, ct: ct));
        return new DocumentoInternoDto
        {
            Id = publico,
            Tipo = tipoLimpo,
            TipoNome = Tipos[tipoLimpo],
            Titulo = tit,
            Descricao = desc,
            NomeArquivo = nome,
            TipoConteudo = tipoConteudo!,
            TamanhoBytes = conteudo.Length,
            EnviadoEm = DateTime.UtcNow,
            EnviadoPor = new UsuarioReferenciaDto { Id = usuario.Id, NomeCompleto = nomeUsuario ?? string.Empty }
        };
    }

    public async Task<(byte[] Conteudo, string Nome, string Tipo)> BaixarAsync(Guid documentoId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var d = await con.QueryFirstOrDefaultAsync<Conteudo>(Sql.Cmd(@"
SELECT CODIGO_DOCUMENTO_INTERNO AS Codigo, TIPO_DOCUMENTO AS Tipo, TITULO AS Titulo, NOME_ARQUIVO AS Nome, TIPO_CONTEUDO AS TipoConteudo, CONTEUDO AS Bytes
FROM DBO.DOCUMENTO_INTERNO WHERE CODIGO_PUBLIC = @Id;",
            new { Id = documentoId }, ct: ct)) ?? throw Erro.NaoEncontrado("Documento não encontrado.");
        await _auditoria.RegistrarAsync("VISUALIZAR", "DOCUMENTO_INTERNO", d.Codigo, null, usuario.Id,
            new { tipo = d.Tipo, titulo = d.Titulo, arquivo = d.Nome }, con, null, ct);
        return (d.Bytes, d.Nome, d.TipoConteudo);
    }

    public async Task ExcluirAsync(Guid documentoId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var d = await con.QueryFirstOrDefaultAsync<Conteudo>(Sql.Cmd(@"
SELECT CODIGO_DOCUMENTO_INTERNO AS Codigo, TIPO_DOCUMENTO AS Tipo, TITULO AS Titulo, NOME_ARQUIVO AS Nome, TIPO_CONTEUDO AS TipoConteudo, HASH_SHA256 AS Hash
FROM DBO.DOCUMENTO_INTERNO WHERE CODIGO_PUBLIC = @Id;",
            new { Id = documentoId }, tx, ct)) ?? throw Erro.NaoEncontrado("Documento não encontrado.");

        await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.DOCUMENTO_INTERNO WHERE CODIGO_DOCUMENTO_INTERNO = @Codigo;", new { d.Codigo }, tx, ct));
        await _auditoria.RegistrarAsync("EXCLUIR", "DOCUMENTO_INTERNO", d.Codigo, null, usuario.Id,
            new { tipo = d.Tipo, titulo = d.Titulo, arquivo = d.Nome, sha256 = d.Hash }, con, tx, ct);
        await tx.CommitAsync(ct);
    }
}
