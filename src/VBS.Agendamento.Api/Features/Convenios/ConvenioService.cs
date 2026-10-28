using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Convenios;

/// <summary>
/// Bloco 3 do contrato: convênios. Regras: RN13 (CNPJ único e válido), RN14 (inativar, não excluir),
/// F4 (não inativa com consulta futura marcada), F7 (cria e acompanha a forma de pagamento do tipo Convênio).
/// </summary>
public sealed class ConvenioService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public ConvenioService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectConvenio = @"
SELECT C.CODIGO_CONVENIO AS Id, C.ATIVO AS Ativo, C.RAZAO_SOCIAL AS RazaoSocial, C.NOME_FANTASIA AS NomeFantasia,
       C.CNPJ AS Cnpj, C.CODIGO_ANS AS CodigoAns, C.DATA_INSCRICAO AS DataInscricao,
       C.INSCRICAO_MUNICIPAL AS InscricaoMunicipal, C.INSCRICAO_ESTADUAL AS InscricaoEstadual,
       (SELECT TOP (1) F.CODIGO_PAGAMENTO FROM DBO.FORMA_PAGAMENTO F
         WHERE F.CODIGO_CONVENIO = C.CODIGO_CONVENIO AND F.TIPO = 'CONVENIO' ORDER BY F.CODIGO_PAGAMENTO) AS FormaPagamentoId,
       C.DATA_CRIACAO AS CriadoEm, C.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       C.DATA_ATUALIZACAO AS AtualizadoEm, C.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.CONVENIO C
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = C.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = C.ATUALIZACAO_USUARIO";

    private sealed class Linha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string RazaoSocial { get; set; } = string.Empty;
        public string? NomeFantasia { get; set; }
        public string Cnpj { get; set; } = string.Empty;
        public string CodigoAns { get; set; } = string.Empty;
        public DateTime DataInscricao { get; set; }
        public string? InscricaoMunicipal { get; set; }
        public string? InscricaoEstadual { get; set; }
        public int? FormaPagamentoId { get; set; }
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    // ---------------------------------------------------------------- lista
    public async Task<ConvenioPaginaDto> ListarAsync(FiltroConvenios f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND (C.NOME_FANTASIA LIKE @Busca OR C.RAZAO_SOCIAL LIKE @Busca)");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND C.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.CONVENIO C" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectConvenio + onde + " ORDER BY COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL), C.CODIGO_CONVENIO OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new ConvenioPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    public async Task<ConvenioDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Convênio não encontrado.");
        return ParaDto(linha);
    }

    // ------------------------------------------------------------- cadastro
    public async Task<ConvenioDto> CriarAsync(ConvenioEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirCnpjDuplicadoAsync(con, tx, dados.Cnpj, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.CONVENIO (RAZAO_SOCIAL, NOME_FANTASIA, CNPJ, CODIGO_ANS, DATA_INSCRICAO, INSCRICAO_MUNICIPAL, INSCRICAO_ESTADUAL,
                          CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@RazaoSocial, @NomeFantasia, @Cnpj, @CodigoAns, @DataInscricao, @InscricaoMunicipal, @InscricaoEstadual, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", Parametros(dados, usuario.Id, null), tx, ct));
        }
        catch (SqlException ex) when (EhCnpjDuplicado(ex))
        {
            throw CnpjDuplicado();
        }

        // F7: toda vez que um convênio é cadastrado, nasce a forma de pagamento do tipo Convênio ligada a ele.
        var nome = dados.NomeFantasia ?? dados.RazaoSocial;
        var descricao = ("Convênio " + nome);
        if (descricao.Length > 80) descricao = descricao[..80];
        await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.FORMA_PAGAMENTO (DESCRICAO, TIPO, CONDICAO, CODIGO_CONVENIO, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@Descricao, 'CONVENIO', 'A_PRAZO', @Convenio, @Usuario, @Usuario);",
            new { Descricao = descricao, Convenio = id, Usuario = usuario.Id }, tx, ct));

        await _auditoria.RegistrarAsync("INCLUIR", "CONVENIO", id, null, usuario.Id, null, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Convênio recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    // ------------------------------------------------------------ alteração
    public async Task<ConvenioDto> AtualizarAsync(int id, ConvenioEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Convênio não encontrado.");
        await ConferirCnpjDuplicadoAsync(con, tx, dados.Cnpj, id, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CONVENIO SET RAZAO_SOCIAL = @RazaoSocial, NOME_FANTASIA = @NomeFantasia, CNPJ = @Cnpj, CODIGO_ANS = @CodigoAns,
       DATA_INSCRICAO = @DataInscricao, INSCRICAO_MUNICIPAL = @InscricaoMunicipal, INSCRICAO_ESTADUAL = @InscricaoEstadual,
       DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_CONVENIO = @Id;", Parametros(dados, usuario.Id, id), tx, ct));
        }
        catch (SqlException ex) when (EhCnpjDuplicado(ex))
        {
            throw CnpjDuplicado();
        }

        var alterados = new List<string>();
        void Dif(string campo, string? antes, string? depois) { if (!string.Equals(antes, depois, StringComparison.Ordinal)) alterados.Add(campo); }
        Dif("razaoSocial", atual.RazaoSocial, dados.RazaoSocial);
        Dif("nomeFantasia", atual.NomeFantasia, dados.NomeFantasia);
        Dif("cnpj", atual.Cnpj, dados.Cnpj);
        Dif("codigoAns", atual.CodigoAns, dados.CodigoAns);
        Dif("dataInscricao", DateOnly.FromDateTime(atual.DataInscricao).ToString("O"), dados.DataInscricao.ToString("O"));
        Dif("inscricaoMunicipal", atual.InscricaoMunicipal, dados.InscricaoMunicipal);
        Dif("inscricaoEstadual", atual.InscricaoEstadual, dados.InscricaoEstadual);

        await _auditoria.RegistrarAsync("EDITAR", "CONVENIO", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Convênio não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // --------------------------------------------------------------- status
    public async Task<ConvenioDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Convênio não encontrado.");

        if (!d.Ativo.Value)
        {
            // F4: não inativa o convênio com consulta marcada ou em andamento por ele.
            var quantidade = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONSULTA
WHERE CODIGO_CONVENIO = @Id
  AND STATUS IN ('AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO')
  AND DATA_FIM > SYSUTCDATETIME();", new { Id = id }, tx, ct));

            if (quantidade > 0)
                throw Erro.Conflito(
                    $"O convênio tem {quantidade} consulta(s) marcada(s) ou em andamento. Remarque ou cancele antes de inativar.",
                    "CONVENIO_COM_CONSULTAS",
                    new Dictionary<string, object?> { ["quantidadeConsultas"] = quantidade });
        }

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CONVENIO SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_CONVENIO = @Id;
-- A forma de pagamento ligada ao convênio acompanha o status dele.
UPDATE DBO.FORMA_PAGAMENTO SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_CONVENIO = @Id AND TIPO = 'CONVENIO';",
            new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "CONVENIO", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Convênio não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private sealed record Dados(string RazaoSocial, string? NomeFantasia, string Cnpj, string CodigoAns, DateOnly DataInscricao,
        string? InscricaoMunicipal, string? InscricaoEstadual);

    private static string? Opcional(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static Dados Validar(ConvenioEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        void Tamanho(string campo, string? valor, int max)
        {
            if (!string.IsNullOrWhiteSpace(valor) && valor.Trim().Length > max) e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }

        if (string.IsNullOrWhiteSpace(d.RazaoSocial)) e.Add(new ErroCampo("razaoSocial", NaoPreenchido));
        Tamanho("razaoSocial", d.RazaoSocial, 150);
        Tamanho("nomeFantasia", d.NomeFantasia, 150);

        if (string.IsNullOrWhiteSpace(d.Cnpj)) e.Add(new ErroCampo("cnpj", NaoPreenchido));
        else if (!CnpjUtil.Valido(d.Cnpj.Trim())) e.Add(new ErroCampo("cnpj", "CNPJ inválido"));

        if (string.IsNullOrWhiteSpace(d.CodigoAns)) e.Add(new ErroCampo("codigoAns", NaoPreenchido));
        Tamanho("codigoAns", d.CodigoAns, 10);

        if (d.DataInscricao is null) e.Add(new ErroCampo("dataInscricao", NaoPreenchido));
        else if (d.DataInscricao.Value > DataBrasil.Hoje()) e.Add(new ErroCampo("dataInscricao", "A data não pode ser futura"));

        Tamanho("inscricaoMunicipal", d.InscricaoMunicipal, 30);
        Tamanho("inscricaoEstadual", d.InscricaoEstadual, 30);

        if (e.Count > 0) throw Erro.Validacao(e);

        return new Dados(d.RazaoSocial!.Trim(), Opcional(d.NomeFantasia), d.Cnpj!.Trim(), d.CodigoAns!.Trim(), d.DataInscricao!.Value,
            Opcional(d.InscricaoMunicipal), Opcional(d.InscricaoEstadual));
    }

    private static object Parametros(Dados d, int usuarioId, int? id) => new
    {
        Id = id,
        d.RazaoSocial,
        d.NomeFantasia,
        Cnpj = Sql.Char(d.Cnpj, 14),
        d.CodigoAns,
        DataInscricao = d.DataInscricao.ToDateTime(TimeOnly.MinValue),
        d.InscricaoMunicipal,
        d.InscricaoEstadual,
        Usuario = usuarioId
    };

    private static async Task ConferirCnpjDuplicadoAsync(SqlConnection con, SqlTransaction tx, string cnpj, int? ignorarId, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONVENIO WHERE CNPJ = @Cnpj AND (@Ignorar IS NULL OR CODIGO_CONVENIO <> @Ignorar);",
            new { Cnpj = Sql.Char(cnpj, 14), Ignorar = ignorarId }, tx, ct));
        if (existe > 0) throw CnpjDuplicado();
    }

    private static ApiException CnpjDuplicado() => Erro.Conflito("Este CNPJ já está cadastrado.", "CNPJ_DUPLICADO");

    private static bool EhCnpjDuplicado(SqlException ex) =>
        ex.Number is 2601 or 2627 && ex.Message.Contains("UQ_CONVENIO_CNPJ", StringComparison.OrdinalIgnoreCase);

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectConvenio + " WHERE C.CODIGO_CONVENIO = @Id;", new { Id = id }, tx, ct));

    private static ConvenioDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        Ativo = l.Ativo,
        RazaoSocial = l.RazaoSocial,
        NomeFantasia = l.NomeFantasia,
        Cnpj = l.Cnpj,
        CodigoAns = l.CodigoAns,
        DataInscricao = DateOnly.FromDateTime(l.DataInscricao),
        InscricaoMunicipal = l.InscricaoMunicipal,
        InscricaoEstadual = l.InscricaoEstadual,
        FormaPagamentoId = l.FormaPagamentoId,
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
