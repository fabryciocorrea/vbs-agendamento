using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Credenciamentos;

/// <summary>
/// Credenciamento de convênios por unidade e especialidade (RN35, J1 a J4). Não há exclusão: a situação muda por alteração.
/// O aviso à recepção quando o credenciamento deixa de valer é feito pelo gatilho do banco (script 09).
/// </summary>
public sealed class CredenciamentoService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly string[] Situacoes = { "APROVADO", "EM_ANALISE", "NAO_CREDENCIADO" };
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public CredenciamentoService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectCred = @"
SELECT K.CODIGO_CREDENCIAMENTO AS Id, K.CODIGO_CONVENIO AS ConvenioId, COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL) AS ConvenioNome,
       K.CODIGO_UNIDADE AS UnidadeId, U.NOME_FANTASIA AS UnidadeNome, U.CIDADE AS UnidadeCidade, U.UF AS UnidadeUf,
       K.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS EspecialidadeNome,
       K.SITUACAO AS Situacao, K.DATA_INICIO AS DataInicio, K.DATA_FIM AS DataFim, K.PROTOCOLO AS Protocolo, K.OBSERVACAO AS Observacao, K.VALOR AS ValorProprio,
       CASE WHEN K.VALOR > 0 THEN K.VALOR ELSE ISNULL(CE.VALOR, 0) END AS Valor,
       K.DATA_ATUALIZACAO AS AtualizadoEm
FROM DBO.CREDENCIAMENTO_CONVENIO K
JOIN DBO.CONVENIO C ON C.CODIGO_CONVENIO = K.CODIGO_CONVENIO
JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = K.CODIGO_UNIDADE
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = K.CODIGO_ESPECIALIDADE
LEFT JOIN DBO.CONVENIO_ESPECIALIDADE CE ON CE.CODIGO_CONVENIO = K.CODIGO_CONVENIO AND CE.CODIGO_ESPECIALIDADE = K.CODIGO_ESPECIALIDADE";

    private sealed class Linha
    {
        public int Id { get; set; }
        public int ConvenioId { get; set; }
        public string ConvenioNome { get; set; } = string.Empty;
        public int UnidadeId { get; set; }
        public string UnidadeNome { get; set; } = string.Empty;
        public string UnidadeCidade { get; set; } = string.Empty;
        public string UnidadeUf { get; set; } = string.Empty;
        public int EspecialidadeId { get; set; }
        public string EspecialidadeNome { get; set; } = string.Empty;
        public string Situacao { get; set; } = string.Empty;
        public DateTime? DataInicio { get; set; }
        public DateTime? DataFim { get; set; }
        public string? Protocolo { get; set; }
        public string? Observacao { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorProprio { get; set; }
        public DateTime AtualizadoEm { get; set; }
    }

    private sealed class SituacaoLinha
    {
        public bool Ativo { get; set; }
    }

    private sealed class EspecialidadeLinha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
    }

    private static CredenciamentoDto ParaDto(Linha l)
    {
        var inicio = l.DataInicio.HasValue ? DateOnly.FromDateTime(l.DataInicio.Value) : (DateOnly?)null;
        var fim = l.DataFim.HasValue ? DateOnly.FromDateTime(l.DataFim.Value) : (DateOnly?)null;
        var hoje = DataBrasil.Hoje();
        return new CredenciamentoDto
        {
            Id = l.Id,
            Convenio = new ReferenciaDto { Id = l.ConvenioId, Nome = l.ConvenioNome },
            Unidade = new UnidadeCredenciamentoDto { Id = l.UnidadeId, Nome = l.UnidadeNome, Cidade = l.UnidadeCidade, Uf = l.UnidadeUf },
            Especialidade = new ReferenciaDto { Id = l.EspecialidadeId, Nome = l.EspecialidadeNome },
            Situacao = l.Situacao,
            DataInicio = inicio,
            DataFim = fim,
            Protocolo = l.Protocolo,
            Observacao = l.Observacao,
            Valor = l.Valor,
            ValorProprio = l.ValorProprio > 0 ? l.ValorProprio : null,
            VigenteHoje = l.Situacao == "APROVADO" && inicio.HasValue && inicio.Value <= hoje && (!fim.HasValue || fim.Value >= hoje),
            AtualizadoEm = DataBrasil.Utc(l.AtualizadoEm)
        };
    }

    // ---------------------------------------------------------------- lista
    public async Task<CredenciamentoPaginaDto> ListarAsync(FiltroCredenciamentos f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.Situacao) && !Situacoes.Contains(f.Situacao)) erros.Add(new ErroCampo("situacao", "Situação inválida"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        if (f.ConvenioId.HasValue) { onde.Append(" AND K.CODIGO_CONVENIO = @ConvenioId"); p.Add("ConvenioId", f.ConvenioId.Value); }
        if (f.UnidadeId.HasValue) { onde.Append(" AND K.CODIGO_UNIDADE = @UnidadeId"); p.Add("UnidadeId", f.UnidadeId.Value); }
        if (f.EspecialidadeId.HasValue) { onde.Append(" AND K.CODIGO_ESPECIALIDADE = @EspecialidadeId"); p.Add("EspecialidadeId", f.EspecialidadeId.Value); }
        if (!string.IsNullOrWhiteSpace(f.Situacao)) { onde.Append(" AND K.SITUACAO = @Situacao"); p.Add("Situacao", f.Situacao); }
        if (f.Vigente == true)
        {
            onde.Append(" AND K.SITUACAO = 'APROVADO' AND K.DATA_INICIO <= @Hoje AND (K.DATA_FIM IS NULL OR K.DATA_FIM >= @Hoje)");
            p.Add("Hoje", DataBrasil.Hoje().ToDateTime(TimeOnly.MinValue));
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.CREDENCIAMENTO_CONVENIO K" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectCred + onde + " ORDER BY COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL), U.NOME_FANTASIA, E.NOME, K.CODIGO_CREDENCIAMENTO OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;",
            p, ct: ct));

        return new CredenciamentoPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    public async Task<CredenciamentoPaginaDto> ListarDoConvenioAsync(int convenioId, FiltroCredenciamentos f, CancellationToken ct)
    {
        await using (var con = await _db.AbrirAsync(ct))
        {
            var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;", new { Id = convenioId }, ct: ct));
            if (existe == 0) throw Erro.NaoEncontrado("Convênio não encontrado.");
        }
        f.ConvenioId = convenioId;
        return await ListarAsync(f, ct);
    }

    // ------------------------------------------------------------- inclusão
    public async Task<CredenciamentoDto> IncluirAsync(int convenioId, CredenciamentoEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        if (d.UnidadeId is null) e.Add(new ErroCampo("unidadeId", NaoPreenchido));
        if (d.EspecialidadeId is null) e.Add(new ErroCampo("especialidadeId", NaoPreenchido));
        var v = ValidarVigencia(d.Situacao, d.DataInicio, d.DataFim, d.Protocolo, d.Observacao, d.Valor, e);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirConvenioAsync(con, tx, convenioId, ct);
        await ConferirUnidadeEspecialidadesAsync(con, tx, convenioId, d.UnidadeId!.Value, new[] { d.EspecialidadeId!.Value }, ct);

        var id = await InserirAsync(con, tx, convenioId, d.UnidadeId.Value, d.EspecialidadeId.Value, v, usuario.Id, ct);
        await _auditoria.RegistrarAsync("INCLUIR", "CREDENCIAMENTO_CONVENIO", id, null, usuario.Id, null, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Credenciamento recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    public async Task<List<CredenciamentoDto>> IncluirLoteAsync(int convenioId, CredenciamentoLoteEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        if (d.UnidadeId is null) e.Add(new ErroCampo("unidadeId", NaoPreenchido));
        var especialidades = (d.EspecialidadeIds ?? new List<int>()).Distinct().ToList();
        if (especialidades.Count == 0) e.Add(new ErroCampo("especialidadeIds", NaoPreenchido));
        else if (especialidades.Count > 100) e.Add(new ErroCampo("especialidadeIds", "No máximo 100 especialidades"));
        var v = ValidarVigencia(d.Situacao, d.DataInicio, d.DataFim, d.Protocolo, d.Observacao, d.Valor, e);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirConvenioAsync(con, tx, convenioId, ct);
        await ConferirUnidadeEspecialidadesAsync(con, tx, convenioId, d.UnidadeId!.Value, especialidades, ct);

        var ids = new List<int>();
        foreach (var esp in especialidades)
        {
            var id = await InserirAsync(con, tx, convenioId, d.UnidadeId.Value, esp, v, usuario.Id, ct);
            await _auditoria.RegistrarAsync("INCLUIR", "CREDENCIAMENTO_CONVENIO", id, null, usuario.Id, null, con, tx, ct);
            ids.Add(id);
        }

        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(SelectCred + " WHERE K.CODIGO_CREDENCIAMENTO IN @Ids ORDER BY E.NOME;", new { Ids = ids }, tx, ct));
        await tx.CommitAsync(ct);
        return linhas.Select(ParaDto).ToList();
    }

    // ------------------------------------------------------------ alteração
    public async Task<CredenciamentoDto> AtualizarAsync(int convenioId, int id, CredenciamentoAtualizacao? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        var v = ValidarVigencia(d.Situacao, d.DataInicio, d.DataFim, d.Protocolo, d.Observacao, d.Valor, e);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct);
        if (atual is null || atual.ConvenioId != convenioId) throw Erro.NaoEncontrado("Credenciamento não encontrado.");

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CREDENCIAMENTO_CONVENIO
SET SITUACAO = @Situacao, DATA_INICIO = @DataInicio, DATA_FIM = @DataFim, PROTOCOLO = @Protocolo, OBSERVACAO = @Observacao, VALOR = @Valor,
    DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_CREDENCIAMENTO = @Id;", Parametros(v, usuario.Id, id), tx, ct));

        var alterados = new List<string>();
        void Dif(string campo, string? antes, string? depois) { if (!string.Equals(antes, depois, StringComparison.Ordinal)) alterados.Add(campo); }
        Dif("situacao", atual.Situacao, v.Situacao);
        Dif("dataInicio", atual.DataInicio?.ToString("yyyy-MM-dd"), v.DataInicio?.ToString("yyyy-MM-dd"));
        Dif("dataFim", atual.DataFim?.ToString("yyyy-MM-dd"), v.DataFim?.ToString("yyyy-MM-dd"));
        Dif("protocolo", atual.Protocolo, v.Protocolo);
        Dif("observacao", atual.Observacao, v.Observacao);
        if (atual.ValorProprio != v.Valor) alterados.Add("valor");
        await _auditoria.RegistrarAsync("EDITAR", "CREDENCIAMENTO_CONVENIO", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Credenciamento não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private sealed record Vigencia(string Situacao, DateOnly? DataInicio, DateOnly? DataFim, string? Protocolo, string? Observacao, decimal Valor);

    private static Vigencia ValidarVigencia(string? situacao, DateOnly? inicio, DateOnly? fim, string? protocolo, string? observacao, decimal? valor, List<ErroCampo> e)
    {
        if (string.IsNullOrWhiteSpace(situacao)) e.Add(new ErroCampo("situacao", NaoPreenchido));
        else if (!Situacoes.Contains(situacao)) e.Add(new ErroCampo("situacao", "Informe APROVADO, EM_ANALISE ou NAO_CREDENCIADO"));

        if (!string.IsNullOrWhiteSpace(protocolo) && protocolo.Trim().Length > 50) e.Add(new ErroCampo("protocolo", "Máximo de 50 caracteres"));
        if (!string.IsNullOrWhiteSpace(observacao) && observacao.Trim().Length > 500) e.Add(new ErroCampo("observacao", "Máximo de 500 caracteres"));

        // Valor próprio (opcional): vazio = vale o valor da especialidade no cadastro do convênio.
        if (valor is not null)
        {
            if (valor < 0) e.Add(new ErroCampo("valor", "Não pode ser negativo"));
            else if (valor >= 100_000_000m) e.Add(new ErroCampo("valor", "Valor acima do permitido"));
            else if (decimal.Round(valor.Value, 2) != valor) e.Add(new ErroCampo("valor", "Use no máximo duas casas decimais"));
        }

        // Erros de vigência têm código próprio (VIGENCIA_INVALIDA), como no contrato.
        var vigencia = new List<ErroCampo>();
        if (situacao == "APROVADO" && inicio is null) vigencia.Add(new ErroCampo("dataInicio", "Informe o início da vigência para credenciamento aprovado"));
        if (inicio is not null && fim is not null && fim < inicio) vigencia.Add(new ErroCampo("dataFim", "O fim da vigência não pode ser anterior ao início"));

        if (e.Count > 0) throw Erro.Validacao(e);
        if (vigencia.Count > 0) throw Erro.Regra("VIGENCIA_INVALIDA", vigencia[0].Mensagem, vigencia);

        string? Opc(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        return new Vigencia(situacao!, inicio, fim, Opc(protocolo), Opc(observacao), valor ?? 0m);
    }

    private static object Parametros(Vigencia v, int usuarioId, int id) => new
    {
        Id = id,
        v.Situacao,
        DataInicio = v.DataInicio?.ToDateTime(TimeOnly.MinValue),
        DataFim = v.DataFim?.ToDateTime(TimeOnly.MinValue),
        v.Protocolo,
        v.Observacao,
        v.Valor,
        Usuario = usuarioId
    };

    private static async Task ConferirConvenioAsync(SqlConnection con, SqlTransaction tx, int convenioId, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;", new { Id = convenioId }, tx, ct));
        if (existe == 0) throw Erro.NaoEncontrado("Convênio não encontrado.");
    }

    private static async Task ConferirUnidadeEspecialidadesAsync(SqlConnection con, SqlTransaction tx, int convenioId, int unidadeId, IReadOnlyList<int> especialidades, CancellationToken ct)
    {
        var uni = await con.QueryFirstOrDefaultAsync<SituacaoLinha>(Sql.Cmd(
            "SELECT ATIVO AS Ativo FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = unidadeId }, tx, ct))
            ?? throw Erro.Validacao("unidadeId", "Unidade não encontrada");

        var esps = (await con.QueryAsync<EspecialidadeLinha>(Sql.Cmd(
            "SELECT CODIGO_ESPECIALIDADE AS Id, ATIVO AS Ativo FROM DBO.ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE IN @Ids;", new { Ids = especialidades }, tx, ct))).ToList();
        if (esps.Count != especialidades.Count) throw Erro.Validacao(especialidades.Count == 1 ? "especialidadeId" : "especialidadeIds", "Especialidade não encontrada");

        if (!uni.Ativo || esps.Any(x => !x.Ativo))
            throw Erro.Regra("UNIDADE_OU_ESPECIALIDADE_INATIVA", "A unidade ou a especialidade está inativa.");

        // Se o convênio tem especialidades cadastradas, o credenciamento só vale para elas.
        var doConvenio = (await con.QueryAsync<int>(Sql.Cmd(
            "SELECT CODIGO_ESPECIALIDADE FROM DBO.CONVENIO_ESPECIALIDADE WHERE CODIGO_CONVENIO = @C;", new { C = convenioId }, tx, ct))).ToList();
        if (doConvenio.Count > 0 && especialidades.Any(x => !doConvenio.Contains(x)))
            throw Erro.Regra("ESPECIALIDADE_FORA_DO_CONVENIO", "A especialidade não está entre as especialidades cadastradas para o convênio.");

        // Combinações já cadastradas (409 e nada é gravado).
        var duplicadas = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CREDENCIAMENTO_CONVENIO
WHERE CODIGO_CONVENIO = @Convenio AND CODIGO_UNIDADE = @Unidade AND CODIGO_ESPECIALIDADE IN @Ids;",
            new { Convenio = convenioId, Unidade = unidadeId, Ids = especialidades }, tx, ct));
        if (duplicadas > 0) throw Duplicado();
    }

    private static async Task<int> InserirAsync(SqlConnection con, SqlTransaction tx, int convenioId, int unidadeId, int especialidadeId, Vigencia v, int usuarioId, CancellationToken ct)
    {
        try
        {
            return await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.CREDENCIAMENTO_CONVENIO (CODIGO_CONVENIO, CODIGO_UNIDADE, CODIGO_ESPECIALIDADE, SITUACAO, DATA_INICIO, DATA_FIM,
                                         PROTOCOLO, OBSERVACAO, VALOR, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@Convenio, @Unidade, @Especialidade, @Situacao, @DataInicio, @DataFim, @Protocolo, @Observacao, @Valor, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    Convenio = convenioId,
                    Unidade = unidadeId,
                    Especialidade = especialidadeId,
                    v.Situacao,
                    DataInicio = v.DataInicio?.ToDateTime(TimeOnly.MinValue),
                    DataFim = v.DataFim?.ToDateTime(TimeOnly.MinValue),
                    v.Protocolo,
                    v.Observacao,
                    v.Valor,
                    Usuario = usuarioId
                }, tx, ct));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw Duplicado();
        }
    }

    // ------------------------------------------------- valor da consulta
    /// <summary>Valor da consulta por convênio e especialidade (cadastro do convênio); o valor do credenciamento da unidade, se houver, vale primeiro. Sem credenciamento, vale o do cadastro do convênio.</summary>
    public static Task<ValorConsultaDto?> BuscarValorAsync(SqlConnection con, SqlTransaction? tx, int convenioId, int unidadeId, int especialidadeId, DateOnly data, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<ValorConsultaDto>(Sql.Cmd(@"
SELECT TOP (1) CASE WHEN K.VALOR > 0 THEN K.VALOR ELSE ISNULL(CE.VALOR, 0) END AS Valor,
       CAST(CASE WHEN K.CODIGO_CREDENCIAMENTO IS NOT NULL AND K.SITUACAO = 'APROVADO' AND K.DATA_INICIO <= @Data AND (K.DATA_FIM IS NULL OR K.DATA_FIM >= @Data) THEN 1 ELSE 0 END AS BIT) AS Credenciado
FROM (SELECT @Convenio AS CODIGO_CONVENIO, @Especialidade AS CODIGO_ESPECIALIDADE) X
LEFT JOIN DBO.CONVENIO_ESPECIALIDADE CE ON CE.CODIGO_CONVENIO = X.CODIGO_CONVENIO AND CE.CODIGO_ESPECIALIDADE = X.CODIGO_ESPECIALIDADE
LEFT JOIN DBO.CREDENCIAMENTO_CONVENIO K ON K.CODIGO_CONVENIO = X.CODIGO_CONVENIO AND K.CODIGO_ESPECIALIDADE = X.CODIGO_ESPECIALIDADE AND K.CODIGO_UNIDADE = @Unidade
WHERE CE.CODIGO_CONVENIO IS NOT NULL OR K.CODIGO_CREDENCIAMENTO IS NOT NULL;",
            new { Convenio = convenioId, Unidade = unidadeId, Especialidade = especialidadeId, Data = data.ToDateTime(TimeOnly.MinValue) }, tx, ct));

    public async Task<ValorConsultaDto> ObterValorAsync(int convenioId, int? unidadeId, int? especialidadeId, DateOnly? data, CancellationToken ct)
    {
        if (unidadeId is null || especialidadeId is null)
            throw Erro.Validacao(new List<ErroCampo>
            {
                new("unidadeId", unidadeId is null ? NaoPreenchido : ""),
                new("especialidadeId", especialidadeId is null ? NaoPreenchido : "")
            }.Where(x => x.Mensagem != "").ToList());
        await using var con = await _db.AbrirAsync(ct);
        var r = await BuscarValorAsync(con, null, convenioId, unidadeId.Value, especialidadeId.Value, data ?? DataBrasil.Hoje(), ct);
        return r ?? new ValorConsultaDto { Valor = null, Credenciado = false };
    }

    private static ApiException Duplicado() =>
        Erro.Conflito("Já existe credenciamento deste convênio para a unidade e a especialidade. Altere o existente.", "CREDENCIAMENTO_DUPLICADO");

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectCred + " WHERE K.CODIGO_CREDENCIAMENTO = @Id;", new { Id = id }, tx, ct));
}
