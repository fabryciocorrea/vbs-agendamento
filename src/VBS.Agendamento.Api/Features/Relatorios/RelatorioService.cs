using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Agenda;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Relatorios;

/// <summary>
/// Relatórios operacionais (RF17 a RF19, RF22, RF23): consultas por profissional, ocupação da agenda e cancelamentos e faltas.
/// Nunca para o médico (RN20). Datas e horas no horário de Brasília. Cada geração e cada exportação vão para o log.
/// </summary>
public sealed partial class RelatorioService
{
    private const int LimiteExportacao = 10_000;
    private static readonly CultureInfo Pt = new("pt-BR");

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;
    private readonly IPermissaoService _permissoes;

    public RelatorioService(IConexaoFactory db, IAuditoria auditoria, IPermissaoService permissoes)
    {
        _db = db;
        _auditoria = auditoria;
        _permissoes = permissoes;
    }

    // ------------------------------------------------------------ filtros comuns

    private sealed class Janela
    {
        public DateOnly Inicio { get; init; }
        public DateOnly Fim { get; init; }
        public DateTime De { get; init; }
        public DateTime Ate { get; init; }
        public string? Tipo { get; init; }
        public FiltrosAplicadosDto Aplicados { get; init; } = new();
    }

    private static void ExigirNaoMedico(UsuarioAtual usuario)
    {
        if (usuario.EhMedico) throw Erro.SemPermissao("O perfil Médico não acessa os relatórios.", "MEDICO_SEM_ACESSO");
    }

    private async Task<Janela> PrepararAsync(SqlConnection con, FiltroRelatorio f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.DataInicial is null) erros.Add(new ErroCampo("dataInicial", "Campo Não Preenchido"));
        if (f.DataFinal is null) erros.Add(new ErroCampo("dataFinal", "Campo Não Preenchido"));
        if (f.DataInicial is not null && f.DataFinal is not null)
        {
            if (f.DataFinal < f.DataInicial) erros.Add(new ErroCampo("dataFinal", "A data final não pode ser anterior à inicial"));
            else if (f.DataFinal.Value.DayNumber - f.DataInicial.Value.DayNumber > 365) erros.Add(new ErroCampo("dataFinal", "O período pode ter no máximo 366 dias"));
        }
        var tipo = string.IsNullOrWhiteSpace(f.TipoAtendimento) ? null : f.TipoAtendimento.Trim().ToUpperInvariant();
        if (tipo is not null and not ("PARTICULAR" or "CONVENIO")) erros.Add(new ErroCampo("tipoAtendimento", "Informe PARTICULAR ou CONVENIO"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var inicio = f.DataInicial!.Value;
        var fim = f.DataFinal!.Value;
        var aplicados = new FiltrosAplicadosDto { DataInicial = inicio, DataFinal = fim, TipoAtendimento = tipo };

        if (f.ProfissionalId.HasValue)
        {
            var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT NOME FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = f.ProfissionalId.Value }, ct: ct))
                ?? throw Erro.Validacao("profissionalId", "Profissional não encontrado");
            aplicados.Profissional = new ReferenciaDto { Id = f.ProfissionalId.Value, Nome = nome };
        }
        if (f.UnidadeId.HasValue)
        {
            var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT NOME_FANTASIA FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = f.UnidadeId.Value }, ct: ct))
                ?? throw Erro.Validacao("unidadeId", "Unidade não encontrada");
            aplicados.Unidade = new ReferenciaDto { Id = f.UnidadeId.Value, Nome = nome };
        }
        if (f.ConvenioId.HasValue)
        {
            var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT COALESCE(NOME_FANTASIA, RAZAO_SOCIAL) FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;", new { Id = f.ConvenioId.Value }, ct: ct))
                ?? throw Erro.Validacao("convenioId", "Convênio não encontrado");
            aplicados.Convenio = new ReferenciaDto { Id = f.ConvenioId.Value, Nome = nome };
        }

        return new Janela
        {
            Inicio = inicio,
            Fim = fim,
            De = DataBrasil.ParaUtc(inicio.ToDateTime(TimeOnly.MinValue)),
            Ate = DataBrasil.ParaUtc(fim.AddDays(1).ToDateTime(TimeOnly.MinValue)),
            Tipo = tipo,
            Aplicados = aplicados
        };
    }

    private const string OndeConsulta = @"
WHERE C.DATA_INICIO >= @De AND C.DATA_INICIO < @Ate
  AND (@Prof IS NULL OR C.CODIGO_PROFISSIONAL = @Prof)
  AND (@Unidade IS NULL OR C.CODIGO_UNIDADE = @Unidade)
  AND (@Convenio IS NULL OR C.CODIGO_CONVENIO = @Convenio)
  AND (@Tipo IS NULL OR C.TIPO_ATENDIMENTO = @Tipo)";

    private static object Parametros(Janela j, FiltroRelatorio f) =>
        new { De = j.De, Ate = j.Ate, Prof = f.ProfissionalId, Unidade = f.UnidadeId, Convenio = f.ConvenioId, Tipo = j.Tipo };

    private static decimal Percentual(int parte, int todo) => todo == 0 ? 0 : Math.Round(parte * 100m / todo, 1);

    private static object DetalhesLog(Janela j, FiltroRelatorio f) =>
        new { de = j.Inicio, ate = j.Fim, profissional = f.ProfissionalId, unidade = f.UnidadeId, convenio = f.ConvenioId, tipo = j.Tipo };

    // ------------------------------------------------------------ consultas por profissional

    private sealed class LinhaProfissional
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public int Atendimentos { get; set; }
        public int Agendamentos { get; set; }
    }

    public async Task<RelatorioConsultasPorProfissionalDto> ConsultasPorProfissionalAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);

        var linhas = (await con.QueryAsync<LinhaProfissional>(Sql.Cmd(@"
SELECT PR.CODIGO_PROFISSIONAL AS Id, PR.NOME AS Nome,
       CAST(SUM(CASE WHEN C.STATUS = 'CONCLUIDA' THEN 1 ELSE 0 END) AS INT) AS Atendimentos,
       CAST(COUNT(1) AS INT) AS Agendamentos
FROM DBO.CONSULTA C
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL" + OndeConsulta + @" AND C.STATUS <> 'REMARCADA'
GROUP BY PR.CODIGO_PROFISSIONAL, PR.NOME
ORDER BY PR.NOME;", Parametros(j, f), ct: ct))).ToList();

        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_CONSULTAS_PROFISSIONAL", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);

        return new RelatorioConsultasPorProfissionalDto
        {
            Filtros = j.Aplicados,
            GeradoEm = DateTime.UtcNow,
            Itens = linhas.Select(l => new ConsultasPorProfissionalItemDto
            {
                Profissional = new ReferenciaDto { Id = l.Id, Nome = l.Nome },
                Atendimentos = l.Atendimentos,
                Agendamentos = l.Agendamentos
            }).ToList(),
            Totais = new TotaisConsultasPorProfissionalDto { Atendimentos = linhas.Sum(l => l.Atendimentos), Agendamentos = linhas.Sum(l => l.Agendamentos) }
        };
    }

    // ------------------------------------------------------------ ocupação da agenda

    private sealed class LinhaProf
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
    }

    private sealed class LinhaOcupada
    {
        public int ProfissionalId { get; set; }
        public int UnidadeId { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fim { get; set; }
        public bool Encaixe { get; set; }
    }

    /// <summary>Tira de cada trecho de expediente os intervalos do mesmo dia e da mesma unidade.</summary>
    private static List<(int UnidadeId, DateTime Inicio, DateTime Fim)> ExpedienteLivre(AgendaService.Calendario cal)
    {
        var saida = new List<(int, DateTime, DateTime)>();
        foreach (var (unidadeId, _, p) in cal.Expediente)
        {
            var cursor = p.Inicio;
            foreach (var iv in cal.Intervalos.Where(x => x.UnidadeId == unidadeId).Select(x => x.P).OrderBy(x => x.Inicio))
            {
                if (iv.Fim <= cursor || iv.Inicio >= p.Fim) continue;
                if (iv.Inicio > cursor) saida.Add((unidadeId, cursor, iv.Inicio));
                if (iv.Fim > cursor) cursor = iv.Fim;
            }
            if (cursor < p.Fim) saida.Add((unidadeId, cursor, p.Fim));
        }
        return saida;
    }

    private static int Minutos(DateTime inicio, DateTime fim) => fim <= inicio ? 0 : (int)Math.Round((fim - inicio).TotalMinutes);

    public async Task<RelatorioOcupacaoDto> OcupacaoAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);

        var profissionais = (await con.QueryAsync<LinhaProf>(Sql.Cmd(@"
SELECT CODIGO_PROFISSIONAL AS Id, NOME AS Nome FROM DBO.PROFISSIONAL
WHERE ATIVO = 1 AND (@Prof IS NULL OR CODIGO_PROFISSIONAL = @Prof) ORDER BY NOME;", new { Prof = f.ProfissionalId }, ct: ct))).ToList();

        // Canceladas e remarcadas não ocupam a agenda. Os filtros de convênio e tipo valem só para o que foi ocupado.
        var consultas = (await con.QueryAsync<LinhaOcupada>(Sql.Cmd(@"
SELECT C.CODIGO_PROFISSIONAL AS ProfissionalId, C.CODIGO_UNIDADE AS UnidadeId, C.DATA_INICIO AS Inicio, C.DATA_FIM AS Fim, C.ENCAIXE AS Encaixe
FROM DBO.CONSULTA C" + OndeConsulta + " AND C.STATUS NOT IN ('CANCELADA', 'REMARCADA');", Parametros(j, f), ct: ct))).ToList();

        var itens = new List<OcupacaoItemDto>();
        foreach (var p in profissionais)
        {
            var cal = await AgendaService.MontarCalendarioAsync(con, p.Id, f.UnidadeId, j.Inicio, j.Fim, ct);
            var livre = ExpedienteLivre(cal);
            var disponiveis = livre.Sum(l => Minutos(l.Inicio, l.Fim));

            var dele = consultas.Where(c => c.ProfissionalId == p.Id).ToList();
            var ocupados = 0;
            foreach (var c in dele.Where(c => !c.Encaixe))
            {
                var ini = DataBrasil.Utc(c.Inicio);
                var fim = DataBrasil.Utc(c.Fim);
                foreach (var l in livre.Where(x => x.UnidadeId == c.UnidadeId))
                    ocupados += Minutos(ini > l.Inicio ? ini : l.Inicio, fim < l.Fim ? fim : l.Fim);
            }
            ocupados = Math.Min(ocupados, disponiveis);

            var encaixes = dele.Count(c => c.Encaixe);
            if (disponiveis == 0 && encaixes == 0 && ocupados == 0) continue;

            itens.Add(new OcupacaoItemDto
            {
                Profissional = new ReferenciaDto { Id = p.Id, Nome = p.Nome },
                MinutosDisponiveis = disponiveis,
                MinutosOcupados = ocupados,
                OcupacaoPercentual = Percentual(ocupados, disponiveis),
                Encaixes = encaixes
            });
        }

        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_OCUPACAO_AGENDA", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);

        var totalDisp = itens.Sum(i => i.MinutosDisponiveis);
        var totalOcup = itens.Sum(i => i.MinutosOcupados);
        return new RelatorioOcupacaoDto
        {
            Filtros = j.Aplicados,
            GeradoEm = DateTime.UtcNow,
            Itens = itens,
            Totais = new TotaisOcupacaoDto
            {
                MinutosDisponiveis = totalDisp,
                MinutosOcupados = totalOcup,
                OcupacaoPercentual = Percentual(totalOcup, totalDisp),
                Encaixes = itens.Sum(i => i.Encaixes)
            }
        };
    }

    // ------------------------------------------------------------ cancelamentos e faltas

    private sealed class LinhaResumoCancelamento
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public int Total { get; set; }
        public int CanceladasPaciente { get; set; }
        public int CanceladasClinica { get; set; }
        public int Faltas { get; set; }
    }

    private sealed class LinhaDetalheCancelamento
    {
        public Guid ConsultaId { get; set; }
        public Guid PacienteId { get; set; }
        public string Paciente { get; set; } = string.Empty;
        public int ProfissionalId { get; set; }
        public string Profissional { get; set; } = string.Empty;
        public DateTime Inicio { get; set; }
        public string Situacao { get; set; } = string.Empty;
        public string? CanceladoPor { get; set; }
        public string? Motivo { get; set; }
    }

    private const string UltimoRegistroDoStatus = @"
OUTER APPLY (SELECT TOP 1 H.CANCELADO_POR, H.MOTIVO FROM DBO.CONSULTA_STATUS_HISTORICO H
             WHERE H.CODIGO_CONSULTA = C.CODIGO_CONSULTA AND H.STATUS_NOVO = C.STATUS
             ORDER BY H.CODIGO_CONS_HIST DESC) X";

    private static string NormalizarVisao(string? visao)
    {
        var v = string.IsNullOrWhiteSpace(visao) ? "RESUMO" : visao.Trim().ToUpperInvariant();
        if (v is not ("RESUMO" or "DETALHE")) throw Erro.Validacao("visao", "Informe RESUMO ou DETALHE");
        return v;
    }

    private async Task<(List<CancelamentoFaltaResumoDto> Itens, TotaisCancelamentosFaltasDto Totais)> ResumoCancelamentosAsync(SqlConnection con, Janela j, FiltroRelatorio f, CancellationToken ct)
    {
        var linhas = (await con.QueryAsync<LinhaResumoCancelamento>(Sql.Cmd(@"
SELECT PR.CODIGO_PROFISSIONAL AS Id, PR.NOME AS Nome, CAST(COUNT(1) AS INT) AS Total,
       CAST(SUM(CASE WHEN C.STATUS = 'CANCELADA' AND X.CANCELADO_POR = 'PACIENTE' THEN 1 ELSE 0 END) AS INT) AS CanceladasPaciente,
       CAST(SUM(CASE WHEN C.STATUS = 'CANCELADA' AND ISNULL(X.CANCELADO_POR, 'CLINICA') <> 'PACIENTE' THEN 1 ELSE 0 END) AS INT) AS CanceladasClinica,
       CAST(SUM(CASE WHEN C.STATUS = 'FALTA' THEN 1 ELSE 0 END) AS INT) AS Faltas
FROM DBO.CONSULTA C
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL" + UltimoRegistroDoStatus + OndeConsulta + @" AND C.STATUS <> 'REMARCADA'
GROUP BY PR.CODIGO_PROFISSIONAL, PR.NOME
ORDER BY PR.NOME;", Parametros(j, f), ct: ct))).ToList();

        var itens = linhas.Select(l => new CancelamentoFaltaResumoDto
        {
            Profissional = new ReferenciaDto { Id = l.Id, Nome = l.Nome },
            TotalConsultas = l.Total,
            CanceladasPaciente = l.CanceladasPaciente,
            CanceladasClinica = l.CanceladasClinica,
            Faltas = l.Faltas,
            PercentualCancelamento = Percentual(l.CanceladasPaciente + l.CanceladasClinica, l.Total),
            PercentualFaltas = Percentual(l.Faltas, l.Total)
        }).ToList();
        var totais = new TotaisCancelamentosFaltasDto
        {
            TotalConsultas = linhas.Sum(l => l.Total),
            CanceladasPaciente = linhas.Sum(l => l.CanceladasPaciente),
            CanceladasClinica = linhas.Sum(l => l.CanceladasClinica),
            Faltas = linhas.Sum(l => l.Faltas)
        };
        return (itens, totais);
    }

    private async Task<(List<LinhaDetalheCancelamento> Linhas, int Total)> DetalheCancelamentosAsync(SqlConnection con, Janela j, FiltroRelatorio f, int deslocamento, int tamanho, CancellationToken ct)
    {
        const string de = @"
FROM DBO.CONSULTA C
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL";
        var onde = OndeConsulta + " AND C.STATUS IN ('CANCELADA', 'FALTA')";
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1)" + de + onde + ";", Parametros(j, f), ct: ct));

        var p = new DynamicParameters(Parametros(j, f));
        p.Add("Deslocamento", deslocamento);
        p.Add("Tamanho", tamanho);
        var linhas = (await con.QueryAsync<LinhaDetalheCancelamento>(Sql.Cmd(@"
SELECT C.CODIGO_PUBLIC AS ConsultaId, P.CODIGO_PUBLIC AS PacienteId, P.NOME AS Paciente, PR.CODIGO_PROFISSIONAL AS ProfissionalId, PR.NOME AS Profissional,
       C.DATA_INICIO AS Inicio, C.STATUS AS Situacao, CASE WHEN C.STATUS = 'CANCELADA' THEN X.CANCELADO_POR END AS CanceladoPor, X.MOTIVO AS Motivo" + de + UltimoRegistroDoStatus + onde + @"
ORDER BY C.DATA_INICIO DESC, C.CODIGO_CONSULTA DESC
OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct))).ToList();
        return (linhas, total);
    }

    private static CancelamentoFaltaDetalheDto ParaDetalhe(LinhaDetalheCancelamento l) => new()
    {
        ConsultaId = l.ConsultaId,
        Paciente = new PacienteReferenciaDto { Id = l.PacienteId, Nome = l.Paciente },
        Profissional = new ReferenciaDto { Id = l.ProfissionalId, Nome = l.Profissional },
        Inicio = DataBrasil.Utc(l.Inicio),
        Situacao = l.Situacao,
        CanceladoPor = l.CanceladoPor,
        Motivo = l.Motivo
    };

    public async Task<RelatorioCancelamentosFaltasDto> CancelamentosFaltasAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        var visao = NormalizarVisao(f.Visao);
        if (f.Pagina < 1) throw Erro.Validacao("pagina", "Deve ser 1 ou mais");
        if (f.Tamanho is < 1 or > 100) throw Erro.Validacao("tamanho", "Deve estar entre 1 e 100");

        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);
        var (itens, totais) = await ResumoCancelamentosAsync(con, j, f, ct);

        CancelamentoFaltaDetalhePaginaDto? detalhe = null;
        if (visao == "DETALHE")
        {
            var (linhas, total) = await DetalheCancelamentosAsync(con, j, f, (f.Pagina - 1) * f.Tamanho, f.Tamanho, ct);
            detalhe = new CancelamentoFaltaDetalhePaginaDto { Itens = linhas.Select(ParaDetalhe).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
        }

        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_CANCELAMENTOS_FALTAS", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);
        return new RelatorioCancelamentosFaltasDto { Filtros = j.Aplicados, GeradoEm = DateTime.UtcNow, Itens = itens, Totais = totais, Detalhe = detalhe };
    }

    // ------------------------------------------------------------ exportação (PDF e CSV no navegador)

    public static readonly IReadOnlyDictionary<string, string> PermissaoDeVisualizacao = new Dictionary<string, string>
    {
        ["consultas-por-profissional"] = "relatorio.consultas_profissional:visualizar",
        ["ocupacao-agenda"] = "relatorio.ocupacao:visualizar",
        ["cancelamentos-faltas"] = "relatorio.cancelamentos_faltas:visualizar",
        ["faturamento-por-tipo"] = "relatorio.faturamento_tipo:visualizar",
        ["faturamento-por-convenio"] = "relatorio.faturamento_convenio:visualizar",
        ["faturamento-particular"] = "relatorio.faturamento_particular:visualizar",
        ["faturamento-mensal"] = "relatorio.faturamento_mensal:visualizar",
        ["lista-espera"] = "relatorio.lista_espera:visualizar",
        ["prontuarios"] = "relatorio.prontuarios:visualizar",
        ["cadastro-pacientes"] = "relatorio.cadastro_pacientes:visualizar",
        ["cadastro-profissionais"] = "relatorio.cadastro_profissionais:visualizar",
        ["cadastro-colaboradores"] = "relatorio.cadastro_colaboradores:visualizar",
        ["cadastro-convenios"] = "relatorio.cadastro_convenios:visualizar",
        ["cadastro-credenciamentos"] = "relatorio.cadastro_credenciamentos:visualizar"
    };

    private static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", Pt);
    private static string Horas(int minutos) => $"{minutos / 60}h{minutos % 60:00}";
    private static string Pct(decimal v) => v.ToString("0.0", Pt) + "%";

    private static List<string> TextoDosFiltros(FiltrosAplicadosDto a, string? visao = null)
    {
        var l = new List<string> { $"Período: {Data(a.DataInicial)} a {Data(a.DataFinal)}" };
        l.Add("Profissional: " + (a.Profissional?.Nome ?? "Todos"));
        l.Add("Unidade: " + (a.Unidade?.Nome ?? "Todas"));
        l.Add("Convênio: " + (a.Convenio?.Nome ?? "Todos"));
        l.Add("Tipo: " + (a.TipoAtendimento switch { "PARTICULAR" => "Particular", "CONVENIO" => "Convênio", _ => "Todos" }));
        if (visao is not null) l.Add("Visão: " + (visao == "DETALHE" ? "Detalhada" : "Resumo"));
        return l;
    }

    /// <summary>Monta a tabela do relatório (o mesmo conteúdo da tela, com os mesmos filtros) e grava a exportação no log.</summary>
    public async Task<TabelaExportacao> ExportarAsync(string relatorio, FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        TabelaExportacao tabela;
        switch (relatorio)
        {
            case "consultas-por-profissional":
            {
                var r = await ConsultasPorProfissionalAsync(f, usuario, ct);
                tabela = new TabelaExportacao
                {
                    Titulo = "Consultas por profissional",
                    Filtros = TextoDosFiltros(r.Filtros),
                    Colunas = { ColunaTabela.Texto("Profissional"), ColunaTabela.Numero("Atendimentos (concluídas)"), ColunaTabela.Numero("Agendamentos") },
                    Totais = new List<string> { "Total", r.Totais.Atendimentos.ToString(Pt), r.Totais.Agendamentos.ToString(Pt) }
                };
                foreach (var i in r.Itens) tabela.Linhas.Add(new List<string> { i.Profissional.Nome, i.Atendimentos.ToString(Pt), i.Agendamentos.ToString(Pt) });
                break;
            }
            case "ocupacao-agenda":
            {
                var r = await OcupacaoAsync(f, usuario, ct);
                tabela = new TabelaExportacao
                {
                    Titulo = "Ocupação da agenda",
                    Filtros = TextoDosFiltros(r.Filtros),
                    Colunas = { ColunaTabela.Texto("Profissional"), ColunaTabela.Numero("Disponível"), ColunaTabela.Numero("Ocupado"), ColunaTabela.Numero("Ocupação"), ColunaTabela.Numero("Encaixes (fora do cálculo)") },
                    Totais = new List<string> { "Total", Horas(r.Totais.MinutosDisponiveis), Horas(r.Totais.MinutosOcupados), Pct(r.Totais.OcupacaoPercentual), r.Totais.Encaixes.ToString(Pt) }
                };
                foreach (var i in r.Itens)
                    tabela.Linhas.Add(new List<string> { i.Profissional.Nome, Horas(i.MinutosDisponiveis), Horas(i.MinutosOcupados), Pct(i.OcupacaoPercentual), i.Encaixes.ToString(Pt) });
                break;
            }
            case "cancelamentos-faltas":
            {
                var visao = NormalizarVisao(f.Visao);
                await using var con = await _db.AbrirAsync(ct);
                var j = await PrepararAsync(con, f, ct);
                if (visao == "DETALHE")
                {
                    var (linhas, total) = await DetalheCancelamentosAsync(con, j, f, 0, LimiteExportacao + 1, ct);
                    if (total > LimiteExportacao) throw Erro.Regra("EXPORTACAO_MUITO_GRANDE", "O relatório passa de 10.000 linhas. Reduza o período ou use filtros.");
                    tabela = new TabelaExportacao
                    {
                        Titulo = "Cancelamentos e faltas (detalhado)",
                        Filtros = TextoDosFiltros(j.Aplicados, visao),
                        Colunas = { ColunaTabela.Texto("Data"), ColunaTabela.Texto("Hora"), ColunaTabela.Texto("Paciente"), ColunaTabela.Texto("Profissional"), ColunaTabela.Texto("Situação"), ColunaTabela.Texto("Cancelado por"), ColunaTabela.Texto("Motivo") }
                    };
                    foreach (var l in linhas)
                    {
                        var local = DataBrasil.ParaBrasilia(l.Inicio);
                        tabela.Linhas.Add(new List<string>
                        {
                            local.ToString("dd/MM/yyyy", Pt), local.ToString("HH:mm", Pt), l.Paciente, l.Profissional,
                            l.Situacao == "FALTA" ? "Falta" : "Cancelada",
                            l.CanceladoPor switch { "PACIENTE" => "Paciente", "CLINICA" => "Clínica", _ => "" },
                            l.Motivo ?? string.Empty
                        });
                    }
                    tabela.Totais = null;
                }
                else
                {
                    var (itens, totais) = await ResumoCancelamentosAsync(con, j, f, ct);
                    var canc = totais.CanceladasPaciente + totais.CanceladasClinica;
                    tabela = new TabelaExportacao
                    {
                        Titulo = "Cancelamentos e faltas (resumo)",
                        Filtros = TextoDosFiltros(j.Aplicados, visao),
                        Colunas =
                        {
                            ColunaTabela.Texto("Profissional"), ColunaTabela.Numero("Consultas"), ColunaTabela.Numero("Canceladas pelo paciente"), ColunaTabela.Numero("Canceladas pela clínica"),
                            ColunaTabela.Numero("Faltas"), ColunaTabela.Numero("% cancelamento"), ColunaTabela.Numero("% faltas")
                        },
                        Totais = new List<string>
                        {
                            "Total", totais.TotalConsultas.ToString(Pt), totais.CanceladasPaciente.ToString(Pt), totais.CanceladasClinica.ToString(Pt), totais.Faltas.ToString(Pt),
                            Pct(Percentual(canc, totais.TotalConsultas)), Pct(Percentual(totais.Faltas, totais.TotalConsultas))
                        }
                    };
                    foreach (var i in itens)
                        tabela.Linhas.Add(new List<string>
                        {
                            i.Profissional.Nome, i.TotalConsultas.ToString(Pt), i.CanceladasPaciente.ToString(Pt), i.CanceladasClinica.ToString(Pt), i.Faltas.ToString(Pt),
                            Pct(i.PercentualCancelamento), Pct(i.PercentualFaltas)
                        });
                }
                await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_CANCELAMENTOS_FALTAS", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);
                break;
            }
            case "faturamento-por-tipo":
                tabela = await TabelaFaturamentoAsync("tipo", f, usuario, ct);
                break;
            case "faturamento-por-convenio":
                tabela = await TabelaFaturamentoAsync("convenio", f, usuario, ct);
                break;
            case "faturamento-particular":
                tabela = await TabelaFaturamentoAsync("particular", f, usuario, ct);
                break;
            case "faturamento-mensal":
                tabela = await TabelaFaturamentoMensalAsync(f, usuario, ct);
                break;
            case "lista-espera":
                tabela = await TabelaListaEsperaAsync(f, usuario, ct);
                break;
            case "prontuarios":
                tabela = await TabelaProntuariosAsync(f, usuario, ct);
                break;
            case "cadastro-pacientes" or "cadastro-profissionais" or "cadastro-colaboradores" or "cadastro-convenios" or "cadastro-credenciamentos":
                tabela = await TabelaCadastroAsync(relatorio["cadastro-".Length..], f, usuario, ct);
                break;
            default:
                throw Erro.NaoEncontrado("Relatório não disponível para exportação.", "RELATORIO_NAO_ENCONTRADO");
        }

        if (tabela.Linhas.Count > LimiteExportacao) throw Erro.Regra("EXPORTACAO_MUITO_GRANDE", "O relatório passa de 10.000 linhas. Reduza o período ou use filtros.");
        tabela.GeradoEm = DateTime.UtcNow;

        await _auditoria.RegistrarAsync("EXPORTAR", "RELATORIO_" + relatorio.ToUpperInvariant().Replace('-', '_'), null, null, usuario.Id,
            new { relatorio, de = f.DataInicial, ate = f.DataFinal, profissional = f.ProfissionalId, unidade = f.UnidadeId, convenio = f.ConvenioId, tipo = f.TipoAtendimento, visao = f.Visao, situacao = f.Situacao, ativo = f.Ativo, estrangeiro = f.Estrangeiro, vinculo = f.Vinculo, linhas = tabela.Linhas.Count }, ct: ct);
        return tabela;
    }
}
