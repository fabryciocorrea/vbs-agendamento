using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacotes;

/// <summary>
/// Modelos de pacote de consultas particulares (a tela Pacotes). Cada modelo define a especialidade, a quantidade de sessões,
/// o valor total e a validade em dias. Mudar um modelo não altera os pacotes que pacientes já compraram (a venda guarda uma
/// cópia das condições). RN14: o modelo se inativa, não se exclui.
/// </summary>
public sealed class PacoteService
{
    private const string NaoPreenchido = "Campo Não Preenchido";

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public PacoteService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectPacote = @"
SELECT P.CODIGO_PACOTE AS Id, P.ATIVO AS Ativo, P.NOME AS Nome, P.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS EspecialidadeNome,
       P.QUANTIDADE_SESSOES AS QuantidadeSessoes, P.VALOR AS Valor, P.VALIDADE_DIAS AS ValidadeDias,
       P.DATA_CRIACAO AS CriadoEm, P.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       P.DATA_ATUALIZACAO AS AtualizadoEm, P.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.PACOTE P
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = P.CODIGO_ESPECIALIDADE
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = P.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = P.ATUALIZACAO_USUARIO";

    private sealed class Linha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public int EspecialidadeId { get; set; }
        public string EspecialidadeNome { get; set; } = string.Empty;
        public int QuantidadeSessoes { get; set; }
        public decimal Valor { get; set; }
        public int ValidadeDias { get; set; }
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    /// <summary>Valor de uma sessão: o total dividido pela quantidade, arredondado em centavos.</summary>
    public static decimal ValorDaSessao(decimal valor, int quantidade) =>
        quantidade <= 0 ? 0m : decimal.Round(valor / quantidade, 2, MidpointRounding.AwayFromZero);

    public async Task<PacotePaginaDto> ListarAsync(FiltroPacotes f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND P.NOME LIKE @Busca");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.EspecialidadeId.HasValue)
        {
            onde.Append(" AND P.CODIGO_ESPECIALIDADE = @Especialidade");
            p.Add("Especialidade", f.EspecialidadeId.Value);
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND P.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.PACOTE P" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectPacote + onde + " ORDER BY P.NOME, P.CODIGO_PACOTE OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new PacotePaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    public async Task<PacoteDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Pacote não encontrado.");
        return ParaDto(linha);
    }

    public async Task<PacoteDto> CriarAsync(PacoteEntrada? entrada, UsuarioAtual usuario, CancellationToken ct)
    {
        var d = Validar(entrada);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await ConferirRegrasAsync(con, tx, d, null, ct);

        var id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.PACOTE (NOME, CODIGO_ESPECIALIDADE, QUANTIDADE_SESSOES, VALOR, VALIDADE_DIAS, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@Nome, @EspecialidadeId, @QuantidadeSessoes, @Valor, @ValidadeDias, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", Parametros(d, usuario.Id, null), tx, ct));

        await _auditoria.RegistrarAsync("INCLUIR", "PACOTE", id, null, usuario.Id,
            new { d.Nome, d.QuantidadeSessoes, d.Valor, d.ValidadeDias }, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Pacote recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    public async Task<PacoteDto> AtualizarAsync(int id, PacoteEntrada? entrada, UsuarioAtual usuario, CancellationToken ct)
    {
        var d = Validar(entrada);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Pacote não encontrado.");
        await ConferirRegrasAsync(con, tx, d, id, ct);

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACOTE SET NOME = @Nome, CODIGO_ESPECIALIDADE = @EspecialidadeId, QUANTIDADE_SESSOES = @QuantidadeSessoes,
       VALOR = @Valor, VALIDADE_DIAS = @ValidadeDias, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PACOTE = @Id;", Parametros(d, usuario.Id, id), tx, ct));

        var alterados = new List<string>();
        void Dif(string campo, object? antes, object? depois) { if (!Equals(antes, depois)) alterados.Add(campo); }
        Dif("nome", atual.Nome, d.Nome);
        Dif("especialidadeId", atual.EspecialidadeId, d.EspecialidadeId);
        Dif("quantidadeSessoes", atual.QuantidadeSessoes, d.QuantidadeSessoes);
        Dif("valor", atual.Valor, d.Valor);
        Dif("validadeDias", atual.ValidadeDias, d.ValidadeDias);
        await _auditoria.RegistrarAsync("EDITAR", "PACOTE", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Pacote não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    public async Task<PacoteDto> AlterarStatusAsync(int id, PacoteAtivoEntrada? entrada, UsuarioAtual usuario, CancellationToken ct)
    {
        if (entrada?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Pacote não encontrado.");

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACOTE SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PACOTE = @Id;", new { Ativo = entrada.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));
        await _auditoria.RegistrarAsync("EDITAR", "PACOTE", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = entrada.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Pacote não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private sealed record Dados(string Nome, int EspecialidadeId, int QuantidadeSessoes, decimal Valor, int ValidadeDias);

    private static Dados Validar(PacoteEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();

        if (string.IsNullOrWhiteSpace(d.Nome)) e.Add(new ErroCampo("nome", NaoPreenchido));
        else if (d.Nome.Trim().Length > 100) e.Add(new ErroCampo("nome", "Máximo de 100 caracteres"));

        if (d.EspecialidadeId is null) e.Add(new ErroCampo("especialidadeId", NaoPreenchido));

        if (d.QuantidadeSessoes is null) e.Add(new ErroCampo("quantidadeSessoes", NaoPreenchido));
        else if (d.QuantidadeSessoes is < 1 or > 60) e.Add(new ErroCampo("quantidadeSessoes", "Informe de 1 a 60 sessões"));

        if (d.Valor is null) e.Add(new ErroCampo("valor", NaoPreenchido));
        else if (d.Valor <= 0) e.Add(new ErroCampo("valor", "Informe um valor maior que zero"));
        else if (d.Valor >= 100_000_000m) e.Add(new ErroCampo("valor", "Valor acima do permitido"));
        else if (decimal.Round(d.Valor.Value, 2) != d.Valor) e.Add(new ErroCampo("valor", "Use no máximo duas casas decimais"));

        if (d.ValidadeDias is null) e.Add(new ErroCampo("validadeDias", NaoPreenchido));
        else if (d.ValidadeDias is < 1 or > 365) e.Add(new ErroCampo("validadeDias", "Informe de 1 a 365 dias"));

        if (e.Count > 0) throw Erro.Validacao(e);
        return new Dados(d.Nome!.Trim(), d.EspecialidadeId!.Value, d.QuantidadeSessoes!.Value, d.Valor!.Value, d.ValidadeDias!.Value);
    }

    private sealed class EspecialidadeLinha
    {
        public bool Ativo { get; set; }
    }

    private static async Task ConferirRegrasAsync(SqlConnection con, SqlTransaction tx, Dados d, int? ignorarId, CancellationToken ct)
    {
        var esp = await con.QueryFirstOrDefaultAsync<EspecialidadeLinha>(Sql.Cmd(
            "SELECT ATIVO AS Ativo FROM DBO.ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE = @Id;", new { Id = d.EspecialidadeId }, tx, ct))
            ?? throw Erro.Validacao("especialidadeId", "Especialidade não encontrada");
        if (!esp.Ativo) throw Erro.Validacao("especialidadeId", "A especialidade está inativa");

        var repetido = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.PACOTE WHERE NOME = @Nome AND (@Ignorar IS NULL OR CODIGO_PACOTE <> @Ignorar);",
            new { d.Nome, Ignorar = ignorarId }, tx, ct));
        if (repetido > 0) throw Erro.Conflito("Já existe um pacote com este nome.", "PACOTE_DUPLICADO");
    }

    private static object Parametros(Dados d, int usuarioId, int? id) => new
    {
        Id = id,
        d.Nome,
        d.EspecialidadeId,
        d.QuantidadeSessoes,
        d.Valor,
        d.ValidadeDias,
        Usuario = usuarioId
    };

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectPacote + " WHERE P.CODIGO_PACOTE = @Id;", new { Id = id }, tx, ct));

    private static PacoteDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        Ativo = l.Ativo,
        Nome = l.Nome,
        Especialidade = new ReferenciaDto { Id = l.EspecialidadeId, Nome = l.EspecialidadeNome },
        QuantidadeSessoes = l.QuantidadeSessoes,
        Valor = l.Valor,
        ValorSessao = ValorDaSessao(l.Valor, l.QuantidadeSessoes),
        ValidadeDias = l.ValidadeDias,
        CriadoEm = DataBrasil.Utc(l.CriadoEm),
        CriadoPor = l.CriadoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = l.CriadoPorId.Value, NomeCompleto = l.CriadoPorNome ?? string.Empty }
            : null,
        AtualizadoEm = DataBrasil.Utc(l.AtualizadoEm),
        AtualizadoPor = l.AtualizadoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = l.AtualizadoPorId.Value, NomeCompleto = l.AtualizadoPorNome ?? string.Empty }
            : null
    };
}
