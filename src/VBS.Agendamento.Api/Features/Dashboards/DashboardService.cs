using Dapper;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Dashboards;

/// <summary>Painel (RF15): contagem das consultas de um dia ou de um período por status, pendências e consultas em andamento. Nunca para o médico (RN20).</summary>
public sealed class DashboardService
{
    private static readonly string[] Ativas = { "AGENDADA", "CONFIRMADA", "CHECK_IN", "EM_ATENDIMENTO" };
    private readonly IConexaoFactory _db;

    public DashboardService(IConexaoFactory db) => _db = db;

    private sealed class LinhaDia
    {
        public string Status { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public bool Encaixe { get; set; }
        public bool Pago { get; set; }
        public DateTime Inicio { get; set; }
    }

    private sealed class LinhaProxima
    {
        public Guid ConsultaId { get; set; }
        public DateTime Inicio { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Paciente { get; set; } = string.Empty;
        public string Profissional { get; set; } = string.Empty;
        public string Especialidade { get; set; } = string.Empty;
        public string Unidade { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string? Convenio { get; set; }
        public bool Encaixe { get; set; }
        public bool Pago { get; set; }
        public string? GuiaStatus { get; set; }
    }

    private sealed class LinhaDistribuicao
    {
        public string Tipo { get; set; } = string.Empty;
        public string? Nome { get; set; }
        public int Quantidade { get; set; }
    }

    private sealed class LinhaFila
    {
        public string Status { get; set; } = string.Empty;
        public int Quantidade { get; set; }
    }

    public async Task<PainelDoDiaDto> ObterPainelDoDiaAsync(FiltroPainel f, UsuarioAtual usuario, bool verFaturamento, bool verListaEspera, CancellationToken ct)
    {
        if (usuario.EhMedico)
            throw Erro.SemPermissao("O perfil Médico não acessa os dashboards.", "MEDICO_SEM_ACESSO");

        var tipo = string.IsNullOrWhiteSpace(f.Tipo) ? null : f.Tipo.Trim().ToUpperInvariant();
        if (tipo is not null and not ("PARTICULAR" or "CONVENIO")) throw Erro.Validacao("tipo", "Informe PARTICULAR ou CONVENIO");

        var data = f.DataInicial ?? f.Data ?? DataBrasil.Hoje();
        var dataFim = f.DataFinal ?? (f.DataInicial is null ? data : data);
        if (dataFim < data) throw Erro.Validacao("dataFinal", "A data final não pode ser anterior à inicial");
        if (dataFim.DayNumber - data.DayNumber > 365) throw Erro.Validacao("dataFinal", "O período pode ter no máximo 366 dias");
        var de = DataBrasil.ParaUtc(data.ToDateTime(TimeOnly.MinValue));
        var ate = DataBrasil.ParaUtc(dataFim.AddDays(1).ToDateTime(TimeOnly.MinValue));

        await using var con = await _db.AbrirAsync(ct);

        ReferenciaDto? unidade = null, profissional = null;
        if (f.UnidadeId.HasValue)
        {
            var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd(
                "SELECT NOME_FANTASIA FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = f.UnidadeId.Value }, ct: ct));
            if (nome is null) throw Erro.Validacao("unidadeId", "Unidade não encontrada");
            unidade = new ReferenciaDto { Id = f.UnidadeId.Value, Nome = nome };
        }
        if (f.ProfissionalId.HasValue)
        {
            var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd(
                "SELECT NOME FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = f.ProfissionalId.Value }, ct: ct));
            if (nome is null) throw Erro.Validacao("profissionalId", "Profissional não encontrado");
            profissional = new ReferenciaDto { Id = f.ProfissionalId.Value, Nome = nome };
        }

        var p = new { De = de, Ate = ate, Unidade = f.UnidadeId, Prof = f.ProfissionalId, Tipo = tipo };
        const string FiltroLocal = " AND (@Unidade IS NULL OR C.CODIGO_UNIDADE = @Unidade) AND (@Prof IS NULL OR C.CODIGO_PROFISSIONAL = @Prof) AND (@Tipo IS NULL OR C.TIPO_ATENDIMENTO = @Tipo)";

        // Um dia tem poucas consultas: traz as linhas leves e conta aqui.
        var linhas = (await con.QueryAsync<LinhaDia>(Sql.Cmd(
            "SELECT C.STATUS AS Status, C.TIPO_ATENDIMENTO AS Tipo, C.ENCAIXE AS Encaixe, C.PAGAMENTO_CONFIRMADO AS Pago, C.DATA_INICIO AS Inicio " +
            "FROM DBO.CONSULTA C WHERE C.DATA_INICIO >= @De AND C.DATA_INICIO < @Ate" + FiltroLocal + ";", p, ct: ct))).ToList();

        var agora = DateTime.UtcNow;
        int Conta(string status) => linhas.Count(l => l.Status == status);

        var painel = new PainelDoDiaDto
        {
            Data = data,
            DataInicial = data,
            DataFinal = dataFim,
            Unidade = unidade,
            Profissional = profissional,
            AtualizadoEm = agora,
            Tipo = tipo,
            Totais = new PainelTotaisDto
            {
                Agendadas = Conta("AGENDADA"),
                Confirmadas = Conta("CONFIRMADA"),
                CheckIn = Conta("CHECK_IN"),
                EmAtendimento = Conta("EM_ATENDIMENTO"),
                Concluidas = Conta("CONCLUIDA"),
                Canceladas = Conta("CANCELADA"),
                Faltas = Conta("FALTA"),
                Remarcadas = Conta("REMARCADA"),
                Encaixes = linhas.Count(l => l.Encaixe),
                Total = linhas.Count
            },
            Pendencias = new PainelPendenciasDto
            {
                EmAtraso = linhas.Count(l => (l.Status is "AGENDADA" or "CONFIRMADA") && DataBrasil.Utc(l.Inicio) < agora),
                PagamentoParticularPendente = linhas.Count(l => l.Tipo == "PARTICULAR" && (l.Status is "CHECK_IN" or "EM_ATENDIMENTO") && !l.Pago)
            }
        };

        var dist = await con.QueryAsync<LinhaDistribuicao>(Sql.Cmd(@"
SELECT C.TIPO_ATENDIMENTO AS Tipo, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL) AS Nome, COUNT(1) AS Quantidade
FROM DBO.CONSULTA C
LEFT JOIN DBO.CONVENIO CV ON CV.CODIGO_CONVENIO = C.CODIGO_CONVENIO
WHERE C.DATA_INICIO >= @De AND C.DATA_INICIO < @Ate AND C.STATUS NOT IN ('CANCELADA', 'REMARCADA', 'FALTA')" + FiltroLocal + @"
GROUP BY C.TIPO_ATENDIMENTO, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL)
ORDER BY COUNT(1) DESC, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL);", p, ct: ct));
        painel.Distribuicao = dist.Select(x => new PainelDistribuicaoDto
        {
            Tipo = x.Tipo,
            Nome = x.Tipo == "PARTICULAR" ? "Particular" : (x.Nome ?? "Convênio"),
            Quantidade = x.Quantidade
        }).ToList();

        if (verFaturamento)
        {
            painel.Pendencias.GuiasPendentes = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.CONSULTA C WHERE C.TIPO_ATENDIMENTO = 'CONVENIO' AND C.STATUS IN ('AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO') " +
                "AND ISNULL(C.GUIA_STATUS, 'PENDENTE') = 'PENDENTE' AND C.DATA_INICIO >= @De" + FiltroLocal + ";", p, ct: ct));
            painel.Pendencias.GuiasParaCancelar = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.CONSULTA C WHERE C.STATUS = 'CANCELADA' AND C.GUIA_STATUS = 'AUTORIZADA'" + FiltroLocal + ";", p, ct: ct));
        }

        var proximas = await con.QueryAsync<LinhaProxima>(Sql.Cmd(@"
SELECT TOP 12 C.CODIGO_PUBLIC AS ConsultaId, C.DATA_INICIO AS Inicio, C.STATUS AS Status, P.NOME AS Paciente, PR.NOME AS Profissional,
       E.NOME AS Especialidade, UN.NOME_FANTASIA AS Unidade, C.TIPO_ATENDIMENTO AS Tipo, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL) AS Convenio,
       C.ENCAIXE AS Encaixe, C.PAGAMENTO_CONFIRMADO AS Pago, C.GUIA_STATUS AS GuiaStatus
FROM DBO.CONSULTA C
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = C.CODIGO_ESPECIALIDADE
JOIN DBO.UNIDADE UN ON UN.CODIGO_UNIDADE = C.CODIGO_UNIDADE
LEFT JOIN DBO.CONVENIO CV ON CV.CODIGO_CONVENIO = C.CODIGO_CONVENIO
WHERE C.DATA_INICIO >= @De AND C.DATA_INICIO < @Ate AND C.STATUS IN ('AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO')" + FiltroLocal + @"
ORDER BY C.DATA_INICIO, C.CODIGO_CONSULTA;", p, ct: ct));

        painel.Proximas = proximas.Select(l =>
        {
            var alertas = new List<string>();
            if ((l.Status is "AGENDADA" or "CONFIRMADA") && DataBrasil.Utc(l.Inicio) < agora) alertas.Add("Atrasada");
            if (l.Tipo == "PARTICULAR" && (l.Status is "CHECK_IN" or "EM_ATENDIMENTO") && !l.Pago) alertas.Add("Pagamento pendente");
            if (verFaturamento && l.Tipo == "CONVENIO" && (l.GuiaStatus ?? "PENDENTE") == "PENDENTE") alertas.Add("Guia pendente");
            return new PainelConsultaDto
            {
                ConsultaId = l.ConsultaId, Inicio = DataBrasil.Utc(l.Inicio), Status = l.Status, Paciente = l.Paciente,
                Profissional = l.Profissional, Especialidade = l.Especialidade, Unidade = l.Unidade, TipoAtendimento = l.Tipo,
                Convenio = l.Convenio, Encaixe = l.Encaixe, Alertas = alertas
            };
        }).ToList();

        if (verListaEspera)
        {
            var fila = (await con.QueryAsync<LinhaFila>(Sql.Cmd(
                "SELECT [STATUS] AS Status, COUNT(1) AS Quantidade FROM DBO.LISTA_ESPERA WHERE [STATUS] IN ('AGUARDANDO', 'NOTIFICADO') GROUP BY [STATUS];", ct: ct))).ToList();
            painel.ListaEspera = new PainelListaEsperaDto
            {
                Aguardando = fila.FirstOrDefault(x => x.Status == "AGUARDANDO")?.Quantidade ?? 0,
                HorarioLiberado = fila.FirstOrDefault(x => x.Status == "NOTIFICADO")?.Quantidade ?? 0
            };
        }

        return painel;
    }
}
