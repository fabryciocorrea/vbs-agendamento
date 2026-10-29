using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Agenda;

/// <summary>
/// Bloco da agenda: parâmetros (RN03, RN06), feriados, horários disponíveis (RN03, RN09, RN10) e agenda do profissional (RF10).
/// Os horários de expediente ficam no banco como hora de parede de Brasília; as consultas ficam em UTC.
/// </summary>
public sealed class AgendaService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private const int PeriodoMaximoDias = 31;

    private const string ChaveDuracao = "DURACAO_PADRAO_CONSULTA_MIN";
    private const string ChaveAntecedencia = "ANTECEDENCIA_CANCELAMENTO_H";
    private const string ChaveLimiteEncaixes = "LIMITE_ENCAIXES_DIA";
    private const string ChaveRecessoInicio = "RECESSO_FIM_ANO_INICIO";
    private const string ChaveRecessoFim = "RECESSO_FIM_ANO_FIM";

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;
    private readonly ConsultaService _consultas;

    public AgendaService(IConexaoFactory db, IAuditoria auditoria, ConsultaService consultas)
    {
        _db = db;
        _auditoria = auditoria;
        _consultas = consultas;
    }

    // ------------------------------------------------------------ parâmetros
    private sealed class ParametroLinha
    {
        public string Chave { get; set; } = string.Empty;
        public string Valor { get; set; } = string.Empty;
    }

    public async Task<ParametrosAgendaDto> ObterParametrosAsync(CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        return await LerParametrosAsync(con, null, ct);
    }

    internal static async Task<ParametrosAgendaDto> LerParametrosAsync(SqlConnection con, SqlTransaction? tx, CancellationToken ct)
    {
        var linhas = (await con.QueryAsync<ParametroLinha>(Sql.Cmd(
            "SELECT CHAVE_PARAMETRO AS Chave, VALOR AS Valor FROM DBO.PARAMETRO_SISTEMA WHERE CHAVE_PARAMETRO IN (@A, @B, @C, @D, @E);",
            new { A = ChaveDuracao, B = ChaveAntecedencia, C = ChaveLimiteEncaixes, D = ChaveRecessoInicio, E = ChaveRecessoFim }, tx, ct)))
            .ToDictionary(l => l.Chave, l => l.Valor);

        int? Inteiro(string chave) => linhas.TryGetValue(chave, out var v) && int.TryParse(v, out var n) ? n : null;
        DateOnly? Data(string chave) => linhas.TryGetValue(chave, out var v) &&
            DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        return new ParametrosAgendaDto
        {
            DuracaoPadraoConsultaMin = Inteiro(ChaveDuracao) ?? 45,
            AntecedenciaCancelamentoHoras = Inteiro(ChaveAntecedencia) ?? 48,
            LimiteEncaixesDia = Inteiro(ChaveLimiteEncaixes),
            RecessoFimAnoInicio = Data(ChaveRecessoInicio),
            RecessoFimAnoFim = Data(ChaveRecessoFim)
        };
    }

    public async Task<ParametrosAgendaDto> AtualizarParametrosAsync(ParametrosAgendaDto? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (d is null) throw Erro.Validacao("corpo", NaoPreenchido);

        if (d.DuracaoPadraoConsultaMin is null) erros.Add(new ErroCampo("duracaoPadraoConsultaMin", NaoPreenchido));
        else if (d.DuracaoPadraoConsultaMin is < 5 or > 240) erros.Add(new ErroCampo("duracaoPadraoConsultaMin", "Deve estar entre 5 e 240 minutos"));

        if (d.AntecedenciaCancelamentoHoras is null) erros.Add(new ErroCampo("antecedenciaCancelamentoHoras", NaoPreenchido));
        else if (d.AntecedenciaCancelamentoHoras is < 0 or > 720) erros.Add(new ErroCampo("antecedenciaCancelamentoHoras", "Deve estar entre 0 e 720 horas"));

        if (d.LimiteEncaixesDia is < 0) erros.Add(new ErroCampo("limiteEncaixesDia", "Deve ser 0 ou mais"));

        if (d.RecessoFimAnoInicio is null && d.RecessoFimAnoFim is not null)
            erros.Add(new ErroCampo("recessoFimAnoInicio", NaoPreenchido));
        else if (d.RecessoFimAnoInicio is not null && d.RecessoFimAnoFim is null)
            erros.Add(new ErroCampo("recessoFimAnoFim", NaoPreenchido));
        else if (d.RecessoFimAnoInicio is not null && d.RecessoFimAnoFim < d.RecessoFimAnoInicio)
            erros.Add(new ErroCampo("recessoFimAnoFim", "A data final não pode ser anterior à inicial"));

        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await GravarAsync(con, tx, ChaveDuracao, d.DuracaoPadraoConsultaMin!.Value.ToString(CultureInfo.InvariantCulture),
            "Duração padrão da consulta, em minutos (RN03).", ct);
        await GravarAsync(con, tx, ChaveAntecedencia, d.AntecedenciaCancelamentoHoras!.Value.ToString(CultureInfo.InvariantCulture),
            "Antecedência mínima para cancelamento, em horas.", ct);
        await GravarAsync(con, tx, ChaveLimiteEncaixes, d.LimiteEncaixesDia?.ToString(CultureInfo.InvariantCulture),
            "Máximo de encaixes por profissional e por dia. Sem a chave, não há limite.", ct);
        await GravarAsync(con, tx, ChaveRecessoInicio, d.RecessoFimAnoInicio?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "Início do recesso de fim de ano (agenda fechada).", ct);
        await GravarAsync(con, tx, ChaveRecessoFim, d.RecessoFimAnoFim?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "Fim do recesso de fim de ano (agenda fechada).", ct);

        await _auditoria.RegistrarAsync("EDITAR", "PARAMETRO_AGENDA", null, null, usuario.Id,
            new { campos = new[] { "duracaoPadraoConsultaMin", "antecedenciaCancelamentoHoras", "limiteEncaixesDia", "recessoFimAno" } }, con, tx, ct);

        var saida = await LerParametrosAsync(con, tx, ct);
        await tx.CommitAsync(ct);
        return saida;
    }

    /// <summary>Valor nulo remove a chave (a coluna VALOR do banco não aceita nulo).</summary>
    private static async Task GravarAsync(SqlConnection con, SqlTransaction tx, string chave, string? valor, string descricao, CancellationToken ct)
    {
        if (valor is null)
        {
            await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PARAMETRO_SISTEMA WHERE CHAVE_PARAMETRO = @Chave;", new { Chave = chave }, tx, ct));
            return;
        }
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PARAMETRO_SISTEMA SET VALOR = @Valor WHERE CHAVE_PARAMETRO = @Chave;
IF @@ROWCOUNT = 0
    INSERT INTO DBO.PARAMETRO_SISTEMA (CHAVE_PARAMETRO, VALOR, DESCRICAO_PARAMETRO) VALUES (@Chave, @Valor, @Descricao);",
            new { Chave = chave, Valor = valor, Descricao = descricao }, tx, ct));
    }

    // -------------------------------------------------------------- feriados
    public async Task<List<FeriadoDto>> ListarFeriadosAsync(int? ano, CancellationToken ct)
    {
        if (ano is null) throw Erro.Validacao("ano", NaoPreenchido);
        if (ano is < 2000 or > 2100) throw Erro.Validacao("ano", "Deve estar entre 2000 e 2100");

        await using var con = await _db.AbrirAsync(ct);
        var linhas = await con.QueryAsync<FeriadoLinha>(Sql.Cmd(
            "SELECT [DATA] AS Data, NOME_FERIADO AS Nome FROM DBO.FERIADO_NACIONAL WHERE [DATA] >= @De AND [DATA] < @Ate ORDER BY [DATA];",
            new { De = new DateTime(ano.Value, 1, 1), Ate = new DateTime(ano.Value + 1, 1, 1) }, ct: ct));
        var automaticos = FeriadosNacionais.Do(ano.Value);
        var saida = automaticos.Select(a => new FeriadoDto { Data = a.Key, Nome = a.Value, Automatico = true }).ToList();
        foreach (var l in linhas)
        {
            var data = DateOnly.FromDateTime(l.Data);
            if (!automaticos.ContainsKey(data)) saida.Add(new FeriadoDto { Data = data, Nome = l.Nome, Automatico = false });
        }
        return saida.OrderBy(f => f.Data).ToList();
    }

    public async Task<FeriadoDto> IncluirFeriadoAsync(FeriadoDto? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        var nome = d?.Nome?.Trim();
        if (d?.Data is null) erros.Add(new ErroCampo("data", NaoPreenchido));
        else if (d.Data.Value.Year is < 2000 or > 2100) erros.Add(new ErroCampo("data", "Informe uma data entre 2000 e 2100"));
        if (string.IsNullOrEmpty(nome)) erros.Add(new ErroCampo("nome", NaoPreenchido));
        else if (nome.Length > 80) erros.Add(new ErroCampo("nome", "No máximo 80 caracteres"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var data = d!.Data!.Value;
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.FERIADO_NACIONAL WHERE [DATA] = @Data;", new { Data = data.ToDateTime(TimeOnly.MinValue) }, tx, ct));
        if (existe > 0 || FeriadosNacionais.Eh(data)) throw Erro.Conflito("Já existe um feriado cadastrado nesta data.", "FERIADO_DUPLICADO");

        try
        {
            await con.ExecuteAsync(Sql.Cmd(
                "INSERT INTO DBO.FERIADO_NACIONAL ([DATA], NOME_FERIADO) VALUES (@Data, @Nome);",
                new { Data = data.ToDateTime(TimeOnly.MinValue), Nome = nome }, tx, ct));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw Erro.Conflito("Já existe um feriado cadastrado nesta data.", "FERIADO_DUPLICADO");
        }

        await _auditoria.RegistrarAsync("INCLUIR", "FERIADO", null, null, usuario.Id, new { data = data.ToString("yyyy-MM-dd") }, con, tx, ct);
        await tx.CommitAsync(ct);
        return new FeriadoDto { Data = data, Nome = nome, Automatico = false };
    }

    public async Task RemoverFeriadoAsync(DateOnly data, UsuarioAtual usuario, CancellationToken ct)
    {
        if (FeriadosNacionais.Eh(data))
            throw Erro.Conflito("Feriado nacional calculado pelo sistema não pode ser removido.", "FERIADO_AUTOMATICO");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var linhas = await con.ExecuteAsync(Sql.Cmd(
            "DELETE FROM DBO.FERIADO_NACIONAL WHERE [DATA] = @Data;", new { Data = data.ToDateTime(TimeOnly.MinValue) }, tx, ct));
        if (linhas == 0) throw Erro.NaoEncontrado("Feriado não encontrado.");

        await _auditoria.RegistrarAsync("EXCLUIR", "FERIADO", null, null, usuario.Id, new { data = data.ToString("yyyy-MM-dd") }, con, tx, ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------ calendário
    internal readonly record struct Periodo(DateTime Inicio, DateTime Fim);

    internal sealed class Calendario
    {
        public List<(int UnidadeId, string UnidadeNome, Periodo P)> Expediente { get; } = new();
        public List<(int UnidadeId, Periodo P)> Intervalos { get; } = new();
        public List<BloqueioDto> Bloqueios { get; } = new();
        /// <summary>Feriados e recesso (dia inteiro, em UTC). Valem para encaixe também.</summary>
        public List<Periodo> DiasFechados { get; } = new();
    }

    private sealed class FeriadoLinha
    {
        public DateTime Data { get; set; }
        public string Nome { get; set; } = string.Empty;
    }

    private sealed class HorarioLinha
    {
        public int UnidadeId { get; set; }
        public string UnidadeNome { get; set; } = string.Empty;
        public byte DiaSemana { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraFim { get; set; }
    }

    /// <summary>Monta expediente, intervalos e dias fechados do profissional entre duas datas de Brasília (inclusive).</summary>
    internal static async Task<Calendario> MontarCalendarioAsync(SqlConnection con, int profissionalId, int? unidadeId,
        DateOnly de, DateOnly ate, CancellationToken ct, SqlTransaction? tx = null)
    {
        var horarios = (await con.QueryAsync<HorarioLinha>(Sql.Cmd(@"
SELECT H.CODIGO_UNIDADE AS UnidadeId, U.NOME_FANTASIA AS UnidadeNome, H.DIA_SEMANA AS DiaSemana, H.TIPO AS Tipo,
       H.HORA_INICIO AS HoraInicio, H.HORA_FIM AS HoraFim
FROM DBO.PROFISSIONAL_HORARIO H JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = H.CODIGO_UNIDADE
WHERE H.CODIGO_PROFISSIONAL = @Prof AND (@Unidade IS NULL OR H.CODIGO_UNIDADE = @Unidade)
ORDER BY H.HORA_INICIO;", new { Prof = profissionalId, Unidade = unidadeId }, tx, ct))).ToList();

        var feriados = (await con.QueryAsync<FeriadoLinha>(Sql.Cmd(
            "SELECT [DATA] AS Data, NOME_FERIADO AS Nome FROM DBO.FERIADO_NACIONAL WHERE [DATA] BETWEEN @De AND @Ate;",
            new { De = de.ToDateTime(TimeOnly.MinValue), Ate = ate.ToDateTime(TimeOnly.MinValue) }, tx, ct)))
            .ToDictionary(f => DateOnly.FromDateTime(f.Data), f => f.Nome);
        // Feriados nacionais calculados pelo sistema valem sem cadastro; os da tabela são os incluídos pela clínica.
        for (var ano = de.Year; ano <= ate.Year; ano++)
            foreach (var (dataFeriado, nomeFeriado) in FeriadosNacionais.Do(ano))
                feriados[dataFeriado] = nomeFeriado;

        var parametros = await LerParametrosAsync(con, tx, ct);

        var cal = new Calendario();
        for (var dia = de; dia <= ate; dia = dia.AddDays(1))
        {
            var ini = DataBrasil.ParaUtc(dia.ToDateTime(TimeOnly.MinValue));
            var fim = DataBrasil.ParaUtc(dia.AddDays(1).ToDateTime(TimeOnly.MinValue));

            if (feriados.TryGetValue(dia, out var nomeFeriado))
            {
                cal.Bloqueios.Add(new BloqueioDto { Tipo = "FERIADO", Inicio = ini, Fim = fim, Descricao = nomeFeriado });
                cal.DiasFechados.Add(new Periodo(ini, fim));
                continue;
            }
            if (parametros.RecessoFimAnoInicio is not null && parametros.RecessoFimAnoFim is not null &&
                dia >= parametros.RecessoFimAnoInicio && dia <= parametros.RecessoFimAnoFim)
            {
                cal.Bloqueios.Add(new BloqueioDto { Tipo = "RECESSO", Inicio = ini, Fim = fim, Descricao = "Recesso de fim de ano" });
                cal.DiasFechados.Add(new Periodo(ini, fim));
                continue;
            }

            var diaSemana = dia.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dia.DayOfWeek;
            foreach (var h in horarios.Where(x => x.DiaSemana == diaSemana))
            {
                var p = new Periodo(
                    DataBrasil.ParaUtc(dia.ToDateTime(TimeOnly.MinValue).Add(h.HoraInicio)),
                    DataBrasil.ParaUtc(dia.ToDateTime(TimeOnly.MinValue).Add(h.HoraFim)));
                if (h.Tipo == "ATENDIMENTO") cal.Expediente.Add((h.UnidadeId, h.UnidadeNome, p));
                else if (h.Tipo == "INTERVALO")
                {
                    cal.Intervalos.Add((h.UnidadeId, p));
                    cal.Bloqueios.Add(new BloqueioDto { Tipo = "INTERVALO", Inicio = p.Inicio, Fim = p.Fim, Descricao = "Intervalo" });
                }
            }
        }
        return cal;
    }

    /// <summary>Tira de cada trecho livre os períodos ocupados.</summary>
    private static List<Periodo> Subtrair(IEnumerable<Periodo> baseLivre, IEnumerable<Periodo> ocupados)
    {
        var ordenados = ocupados.OrderBy(o => o.Inicio).ToList();
        var saida = new List<Periodo>();
        foreach (var livre in baseLivre)
        {
            var cursor = livre.Inicio;
            foreach (var o in ordenados)
            {
                if (o.Fim <= cursor || o.Inicio >= livre.Fim) continue;
                if (o.Inicio > cursor) saida.Add(new Periodo(cursor, o.Inicio));
                if (o.Fim > cursor) cursor = o.Fim;
            }
            if (cursor < livre.Fim) saida.Add(new Periodo(cursor, livre.Fim));
        }
        return saida;
    }

    private static void ConferirPeriodo(DateOnly? de, DateOnly? ate, List<ErroCampo> erros, string nomeDe = "dataInicial", string nomeAte = "dataFinal")
    {
        if (de is null) erros.Add(new ErroCampo(nomeDe, NaoPreenchido));
        if (ate is null) erros.Add(new ErroCampo(nomeAte, NaoPreenchido));
        if (de is null || ate is null) return;
        if (ate < de) erros.Add(new ErroCampo(nomeAte, "A data final não pode ser anterior à inicial"));
        else if (ate.Value.DayNumber - de.Value.DayNumber + 1 > PeriodoMaximoDias)
            erros.Add(new ErroCampo(nomeAte, $"O período pode ter no máximo {PeriodoMaximoDias} dias"));
    }

    // ----------------------------------------------- horários disponíveis
    public async Task<List<SlotDto>> ListarHorariosAsync(FiltroHorarios f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.ProfissionalId is null) erros.Add(new ErroCampo("profissionalId", NaoPreenchido));
        if (f.UnidadeId is null) erros.Add(new ErroCampo("unidadeId", NaoPreenchido));
        ConferirPeriodo(f.DataInicial, f.DataFinal, erros);
        if (f.DuracaoMin is < 5 or > 240) erros.Add(new ErroCampo("duracaoMin", "Deve estar entre 5 e 240 minutos"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        if (usuario.EhMedico && usuario.ProfissionalId != f.ProfissionalId)
            throw Erro.NaoEncontrado("Profissional não encontrado.");

        await using var con = await _db.AbrirAsync(ct);
        var duracao = f.DuracaoMin ?? (await LerParametrosAsync(con, null, ct)).DuracaoPadraoConsultaMin ?? 45;

        var cal = await MontarCalendarioAsync(con, f.ProfissionalId!.Value, f.UnidadeId, f.DataInicial!.Value, f.DataFinal!.Value, ct);
        var deUtc = DataBrasil.ParaUtc(f.DataInicial.Value.ToDateTime(TimeOnly.MinValue));
        var ateUtc = DataBrasil.ParaUtc(f.DataFinal.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var ocupadas = await _consultas.PeriodosOcupadosAsync(con, f.ProfissionalId.Value, deUtc, ateUtc, ct);

        var ocupados = cal.Intervalos.Select(i => i.P).Concat(ocupadas.Select(o => new Periodo(o.Inicio, o.Fim))).ToList();
        var livres = Subtrair(cal.Expediente.Select(e => e.P), ocupados);

        var agora = DateTime.UtcNow;
        var passo = TimeSpan.FromMinutes(duracao);
        var slots = new List<SlotDto>();
        foreach (var trecho in livres.OrderBy(t => t.Inicio))
        {
            for (var ini = trecho.Inicio; ini + passo <= trecho.Fim; ini += passo)
            {
                if (ini <= agora) continue;
                slots.Add(new SlotDto { Inicio = ini, Fim = ini + passo });
            }
        }

        // O fim do expediente do dia é o horário do último paciente: ele começa nesse horário e termina depois dele.
        foreach (var fimDoDia in FimDoExpedientePorDia(cal).Values)
        {
            var ini = fimDoDia;
            var fim = ini + passo;
            if (ini <= agora) continue;
            if (slots.Any(s => s.Inicio < fim && s.Fim > ini)) continue;
            if (ocupados.Any(o => o.Inicio < fim && o.Fim > ini)) continue;
            slots.Add(new SlotDto { Inicio = ini, Fim = fim });
        }
        return slots.OrderBy(s => s.Inicio).ToList();
    }

    /// <summary>Último fim de expediente de cada dia (data de Brasília).</summary>
    internal static Dictionary<DateOnly, DateTime> FimDoExpedientePorDia(Calendario cal) =>
        cal.Expediente
            .GroupBy(e => DateOnly.FromDateTime(DataBrasil.ParaBrasilia(e.P.Inicio)))
            .ToDictionary(g => g.Key, g => g.Max(e => e.P.Fim));

    // ------------------------------------------------ agenda de todos os profissionais
    /// <summary>Consultas de todos os profissionais no período, com feriados e recesso. O médico recebe só a própria agenda.</summary>
    public async Task<AgendaSaidaDto> ObterAgendaGeralAsync(FiltroAgenda f, UsuarioAtual usuario, CancellationToken ct)
    {
        if (usuario.EhMedico) return await ObterAgendaAsync(usuario.ProfissionalId!.Value, f, usuario, ct);

        var erros = new List<ErroCampo>();
        ConferirPeriodo(f.DataInicial, f.DataFinal, erros);
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        // Profissional 0 não tem grade: o calendário traz só feriados e recesso.
        var cal = await MontarCalendarioAsync(con, 0, null, f.DataInicial!.Value, f.DataFinal!.Value, ct);
        var deUtc = DataBrasil.ParaUtc(f.DataInicial.Value.ToDateTime(TimeOnly.MinValue));
        var ateUtc = DataBrasil.ParaUtc(f.DataFinal.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var consultas = await _consultas.ListarPorPeriodoAsync(con, null, f.UnidadeId, deUtc, ateUtc, f.IncluirCanceladas, ocultarValor: false, ct);

        return new AgendaSaidaDto
        {
            Profissional = new ReferenciaDto { Id = 0, Nome = "Todos os profissionais" },
            Expediente = new List<PeriodoExpedienteDto>(),
            Bloqueios = cal.Bloqueios.OrderBy(b => b.Inicio).ToList(),
            Consultas = consultas
        };
    }

    // ------------------------------------------------ agenda do profissional
    public async Task<AgendaSaidaDto> ObterAgendaAsync(int profissionalId, FiltroAgenda f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        ConferirPeriodo(f.DataInicial, f.DataFinal, erros);
        if (erros.Count > 0) throw Erro.Validacao(erros);

        // RF10: o médico só abre a própria agenda; a dos outros "não existe".
        if (usuario.EhMedico && usuario.ProfissionalId != profissionalId)
            throw Erro.NaoEncontrado("Profissional não encontrado.");

        await using var con = await _db.AbrirAsync(ct);
        var prof = await con.QueryFirstOrDefaultAsync<ReferenciaDto>(Sql.Cmd(
            "SELECT CODIGO_PROFISSIONAL AS Id, NOME AS Nome FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = profissionalId }, ct: ct))
            ?? throw Erro.NaoEncontrado("Profissional não encontrado.");

        var cal = await MontarCalendarioAsync(con, profissionalId, f.UnidadeId, f.DataInicial!.Value, f.DataFinal!.Value, ct);
        var deUtc = DataBrasil.ParaUtc(f.DataInicial.Value.ToDateTime(TimeOnly.MinValue));
        var ateUtc = DataBrasil.ParaUtc(f.DataFinal.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var consultas = await _consultas.ListarPorPeriodoAsync(con, profissionalId, f.UnidadeId, deUtc, ateUtc,
            f.IncluirCanceladas, ocultarValor: usuario.EhMedico, ct);

        return new AgendaSaidaDto
        {
            Profissional = prof,
            Expediente = cal.Expediente.OrderBy(e => e.P.Inicio).Select(e => new PeriodoExpedienteDto
            {
                Unidade = new ReferenciaDto { Id = e.UnidadeId, Nome = e.UnidadeNome },
                Inicio = e.P.Inicio,
                Fim = e.P.Fim
            }).ToList(),
            Bloqueios = cal.Bloqueios.OrderBy(b => b.Inicio).ToList(),
            Consultas = consultas
        };
    }
}
