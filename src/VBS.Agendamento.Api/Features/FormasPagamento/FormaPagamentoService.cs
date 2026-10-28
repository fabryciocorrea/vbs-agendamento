using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.FormasPagamento;

/// <summary>
/// Bloco 3 do contrato: formas de pagamento. Regras: tipo CONVENIO exige um convênio ativo (um só por convênio) e os
/// outros tipos não aceitam convênio; a forma criada com o convênio (F7) não troca de convênio; RN14 (inativar, não excluir).
/// </summary>
public sealed class FormaPagamentoService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly string[] Tipos = { "DINHEIRO", "PIX", "CARTAO_CREDITO", "CARTAO_DEBITO", "CONVENIO" };
    private static readonly string[] Condicoes = { "A_VISTA", "A_PRAZO" };

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public FormaPagamentoService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectForma = @"
SELECT F.CODIGO_PAGAMENTO AS Id, F.ATIVO AS Ativo, F.DESCRICAO AS Descricao, F.TIPO AS Tipo, F.CONDICAO AS Condicao,
       F.CODIGO_CONVENIO AS ConvenioId, COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL) AS ConvenioNome,
       F.DATA_CRIACAO AS CriadoEm, F.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       F.DATA_ATUALIZACAO AS AtualizadoEm, F.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.FORMA_PAGAMENTO F
LEFT JOIN DBO.CONVENIO C ON C.CODIGO_CONVENIO = F.CODIGO_CONVENIO
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = F.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = F.ATUALIZACAO_USUARIO";

    private sealed class Linha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string Condicao { get; set; } = string.Empty;
        public int? ConvenioId { get; set; }
        public string? ConvenioNome { get; set; }
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    public async Task<FormaPagamentoPaginaDto> ListarAsync(FiltroFormasPagamento f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.Tipo) && !Tipos.Contains(f.Tipo)) erros.Add(new ErroCampo("tipo", "Tipo inválido"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND F.DESCRICAO LIKE @Busca");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (!string.IsNullOrWhiteSpace(f.Tipo))
        {
            onde.Append(" AND F.TIPO = @Tipo");
            p.Add("Tipo", f.Tipo);
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND F.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.FORMA_PAGAMENTO F" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectForma + onde + " ORDER BY F.DESCRICAO, F.CODIGO_PAGAMENTO OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new FormaPagamentoPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    public async Task<FormaPagamentoDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Forma de pagamento não encontrada.");
        return ParaDto(linha);
    }

    public async Task<FormaPagamentoDto> CriarAsync(FormaPagamentoEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirRegrasAsync(con, tx, dados, null, ct);

        var id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.FORMA_PAGAMENTO (DESCRICAO, TIPO, CONDICAO, CODIGO_CONVENIO, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@Descricao, @Tipo, @Condicao, @ConvenioId, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", Parametros(dados, usuario.Id, null), tx, ct));

        await _auditoria.RegistrarAsync("INCLUIR", "FORMA_PAGAMENTO", id, null, usuario.Id, null, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Forma de pagamento recém-criada não encontrada.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    public async Task<FormaPagamentoDto> AtualizarAsync(int id, FormaPagamentoEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Forma de pagamento não encontrada.");

        // A forma ligada a um convênio mantém o tipo e o convênio; só descrição e condição mudam.
        if (atual.Tipo == "CONVENIO" && (dados.Tipo != "CONVENIO" || dados.ConvenioId != atual.ConvenioId))
            throw Erro.Validacao(new[] { new ErroCampo("tipo", "A forma de pagamento de um convênio não pode mudar de tipo nem de convênio") });

        await ConferirRegrasAsync(con, tx, dados, id, ct);

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.FORMA_PAGAMENTO SET DESCRICAO = @Descricao, TIPO = @Tipo, CONDICAO = @Condicao, CODIGO_CONVENIO = @ConvenioId,
       DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PAGAMENTO = @Id;", Parametros(dados, usuario.Id, id), tx, ct));

        var alterados = new List<string>();
        void Dif(string campo, object? antes, object? depois) { if (!Equals(antes, depois)) alterados.Add(campo); }
        Dif("descricao", atual.Descricao, dados.Descricao);
        Dif("tipo", atual.Tipo, dados.Tipo);
        Dif("condicao", atual.Condicao, dados.Condicao);
        Dif("convenioId", atual.ConvenioId, dados.ConvenioId);

        await _auditoria.RegistrarAsync("EDITAR", "FORMA_PAGAMENTO", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Forma de pagamento não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    public async Task<FormaPagamentoDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Forma de pagamento não encontrada.");

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.FORMA_PAGAMENTO SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PAGAMENTO = @Id;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "FORMA_PAGAMENTO", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Forma de pagamento não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private sealed class InfoConvenio
    {
        public bool Ativo { get; set; }
        public int Formas { get; set; }
    }

    private sealed record Dados(string Descricao, string Tipo, string Condicao, int? ConvenioId);

    private static Dados Validar(FormaPagamentoEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        if (string.IsNullOrWhiteSpace(d.Descricao)) e.Add(new ErroCampo("descricao", NaoPreenchido));
        else if (d.Descricao.Trim().Length > 80) e.Add(new ErroCampo("descricao", "Máximo de 80 caracteres"));

        if (string.IsNullOrWhiteSpace(d.Tipo)) e.Add(new ErroCampo("tipo", NaoPreenchido));
        else if (!Tipos.Contains(d.Tipo)) e.Add(new ErroCampo("tipo", "Tipo inválido"));

        if (string.IsNullOrWhiteSpace(d.Condicao)) e.Add(new ErroCampo("condicao", NaoPreenchido));
        else if (!Condicoes.Contains(d.Condicao)) e.Add(new ErroCampo("condicao", "Condição inválida"));

        if (d.Tipo == "CONVENIO" && d.ConvenioId is null) e.Add(new ErroCampo("convenioId", NaoPreenchido));
        if (d.Tipo is not null && d.Tipo != "CONVENIO" && d.ConvenioId is not null)
            e.Add(new ErroCampo("convenioId", "Só o tipo Convênio aceita convênio"));

        if (e.Count > 0) throw Erro.Validacao(e);
        return new Dados(d.Descricao!.Trim(), d.Tipo!, d.Condicao!, d.ConvenioId);
    }

    private static async Task ConferirRegrasAsync(SqlConnection con, SqlTransaction tx, Dados dados, int? ignorarId, CancellationToken ct)
    {
        var descricaoRepetida = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.FORMA_PAGAMENTO WHERE DESCRICAO = @Descricao AND (@Ignorar IS NULL OR CODIGO_PAGAMENTO <> @Ignorar);",
            new { dados.Descricao, Ignorar = ignorarId }, tx, ct));
        if (descricaoRepetida > 0)
            throw Erro.Conflito("Esta forma de pagamento já está cadastrada.", "FORMA_PAGAMENTO_DUPLICADA");

        if (dados.Tipo != "CONVENIO") return;

        var convenio = await con.QueryFirstOrDefaultAsync<InfoConvenio>(Sql.Cmd(@"
SELECT C.ATIVO AS Ativo,
       (SELECT COUNT(1) FROM DBO.FORMA_PAGAMENTO F
         WHERE F.CODIGO_CONVENIO = C.CODIGO_CONVENIO AND F.TIPO = 'CONVENIO' AND (@Ignorar IS NULL OR F.CODIGO_PAGAMENTO <> @Ignorar)) AS Formas
FROM DBO.CONVENIO C WHERE C.CODIGO_CONVENIO = @Convenio;", new { Convenio = dados.ConvenioId, Ignorar = ignorarId }, tx, ct));

        if (convenio is null) throw Erro.Validacao("convenioId", "Convênio não encontrado");
        if (ignorarId is null && !convenio.Ativo) throw Erro.Validacao("convenioId", "O convênio precisa estar ativo");
        if (convenio.Formas > 0)
            throw Erro.Conflito("Este convênio já tem uma forma de pagamento.", "CONVENIO_JA_TEM_FORMA_PAGAMENTO");
    }

    private static object Parametros(Dados d, int usuarioId, int? id) => new
    {
        Id = id,
        d.Descricao,
        d.Tipo,
        d.Condicao,
        d.ConvenioId,
        Usuario = usuarioId
    };

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectForma + " WHERE F.CODIGO_PAGAMENTO = @Id;", new { Id = id }, tx, ct));

    private static FormaPagamentoDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        Ativo = l.Ativo,
        Descricao = l.Descricao,
        Tipo = l.Tipo,
        Condicao = l.Condicao,
        Convenio = l.ConvenioId.HasValue ? new ReferenciaDto { Id = l.ConvenioId.Value, Nome = l.ConvenioNome ?? string.Empty } : null,
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
