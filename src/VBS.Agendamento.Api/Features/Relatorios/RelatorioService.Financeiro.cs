using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Relatorios;

/// <summary>
/// Relatórios financeiros (faturamento por tipo, por convênio e mensal) e de lista de espera.
/// Faturado = só consultas concluídas, a mesma regra do Painel financeiro e do Faturamento.
/// </summary>
public sealed partial class RelatorioService
{
    private static string Moeda(decimal v) => v.ToString("C2", Pt);

    // ------------------------------------------------------------ faturamento: base

    private sealed class LinhaFatur
    {
        public string Tipo { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public bool Pago { get; set; }
        public string? GuiaStatus { get; set; }
        public int? ConvenioId { get; set; }
        public string? Convenio { get; set; }
        public string? Forma { get; set; }
        public DateTime Inicio { get; set; }
    }

    private async Task<List<LinhaFatur>> CarregarFaturamentoAsync(SqlConnection con, Janela j, FiltroRelatorio f, string? soTipo, CancellationToken ct) =>
        (await con.QueryAsync<LinhaFatur>(Sql.Cmd(@"
SELECT C.TIPO_ATENDIMENTO AS Tipo, C.VALOR AS Valor, C.PAGAMENTO_CONFIRMADO AS Pago, C.GUIA_STATUS AS GuiaStatus,
       C.CODIGO_CONVENIO AS ConvenioId, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL) AS Convenio, FP.DESCRICAO AS Forma, C.DATA_INICIO AS Inicio
FROM DBO.CONSULTA C
LEFT JOIN DBO.CONVENIO CV ON CV.CODIGO_CONVENIO = C.CODIGO_CONVENIO
LEFT JOIN DBO.FORMA_PAGAMENTO FP ON FP.CODIGO_PAGAMENTO = C.CODIGO_PAGAMENTO" + OndeConsulta + " AND C.STATUS = 'CONCLUIDA'" +
            (soTipo is null ? ";" : $" AND C.TIPO_ATENDIMENTO = '{soTipo}';"), Parametros(j, f), ct: ct))).ToList();

    private static bool Confirmada(LinhaFatur l) => l.Tipo == "PARTICULAR" ? l.Pago : l.GuiaStatus == "AUTORIZADA";
    private static bool Negada(LinhaFatur l) => l.Tipo == "CONVENIO" && l.GuiaStatus == "NEGADA";

    private static FaturamentoLinhaDto Resumir(string nome, IReadOnlyCollection<LinhaFatur> itens, decimal valorTotalGeral)
    {
        var valor = itens.Sum(i => i.Valor);
        return new FaturamentoLinhaDto
        {
            Nome = nome,
            Quantidade = itens.Count,
            Valor = valor,
            Percentual = valorTotalGeral == 0 ? 0 : Math.Round(valor / valorTotalGeral * 100, 1),
            Confirmado = itens.Where(Confirmada).Sum(i => i.Valor),
            Negado = itens.Where(Negada).Sum(i => i.Valor),
            Pendente = itens.Where(i => !Confirmada(i) && !Negada(i)).Sum(i => i.Valor),
            TicketMedio = itens.Count == 0 ? 0 : Math.Round(valor / itens.Count, 2)
        };
    }

    // ------------------------------------------------------------ faturamento por tipo / por convênio

    public async Task<RelatorioFaturamentoDto> FaturamentoPorTipoAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);
        var linhas = await CarregarFaturamentoAsync(con, j, f, null, ct);
        var geral = linhas.Sum(l => l.Valor);

        var itens = new List<FaturamentoLinhaDto>
        {
            Resumir("Particular", linhas.Where(l => l.Tipo == "PARTICULAR").ToList(), geral),
            Resumir("Convênio", linhas.Where(l => l.Tipo == "CONVENIO").ToList(), geral)
        };
        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_FATURAMENTO_TIPO", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);
        return new RelatorioFaturamentoDto { Filtros = j.Aplicados, GeradoEm = DateTime.UtcNow, Itens = itens, Totais = Resumir("Total", linhas, geral) };
    }

    public async Task<RelatorioFaturamentoDto> FaturamentoPorConvenioAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);
        var linhas = await CarregarFaturamentoAsync(con, j, f, "CONVENIO", ct);
        var geral = linhas.Sum(l => l.Valor);

        var itens = linhas
            .GroupBy(l => l.Convenio ?? "Convênio")
            .Select(g => Resumir(g.Key, g.ToList(), geral))
            .OrderByDescending(i => i.Valor).ThenBy(i => i.Nome)
            .ToList();
        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_FATURAMENTO_CONVENIO", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);
        return new RelatorioFaturamentoDto { Filtros = j.Aplicados, GeradoEm = DateTime.UtcNow, Itens = itens, Totais = Resumir("Total", linhas, geral) };
    }

    public async Task<RelatorioFaturamentoDto> FaturamentoParticularAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);
        var linhas = await CarregarFaturamentoAsync(con, j, f, "PARTICULAR", ct);
        var geral = linhas.Sum(l => l.Valor);

        // Uma linha por forma de pagamento (dinheiro, cartão, Pix...), do maior valor ao menor.
        var itens = linhas
            .GroupBy(l => l.Forma ?? "Sem forma de pagamento")
            .Select(g => Resumir(g.Key, g.ToList(), geral))
            .OrderByDescending(i => i.Valor).ThenBy(i => i.Nome)
            .ToList();
        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_FATURAMENTO_PARTICULAR", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);
        return new RelatorioFaturamentoDto { Filtros = j.Aplicados, GeradoEm = DateTime.UtcNow, Itens = itens, Totais = Resumir("Total", linhas, geral) };
    }

    // ------------------------------------------------------------ faturamento mensal

    public async Task<RelatorioFaturamentoMensalDto> FaturamentoMensalAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);
        var linhas = await CarregarFaturamentoAsync(con, j, f, null, ct);

        var porMes = linhas
            .GroupBy(l => DataBrasil.ParaBrasilia(DataBrasil.Utc(l.Inicio)).ToString("yyyy-MM"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var itens = new List<FaturamentoMensalItemDto>();
        decimal? anterior = null;
        for (var m = new DateOnly(j.Inicio.Year, j.Inicio.Month, 1); m <= j.Fim; m = m.AddMonths(1))
        {
            var chave = m.ToString("yyyy-MM");
            var doMes = porMes.TryGetValue(chave, out var doPeriodo) ? doPeriodo : new List<LinhaFatur>();
            var particular = doMes.Where(x => x.Tipo == "PARTICULAR").Sum(x => x.Valor);
            var convenio = doMes.Where(x => x.Tipo == "CONVENIO").Sum(x => x.Valor);
            var total = particular + convenio;
            itens.Add(new FaturamentoMensalItemDto
            {
                Mes = chave,
                Quantidade = doMes.Count,
                Particular = particular,
                Convenio = convenio,
                Total = total,
                VariacaoPercentual = anterior is > 0 ? Math.Round((total - anterior.Value) / anterior.Value * 100, 1) : null
            });
            anterior = total;
        }

        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_FATURAMENTO_MENSAL", null, null, usuario.Id, DetalhesLog(j, f), con, ct: ct);
        var part = itens.Sum(i => i.Particular);
        var conv = itens.Sum(i => i.Convenio);
        return new RelatorioFaturamentoMensalDto
        {
            Filtros = j.Aplicados,
            GeradoEm = DateTime.UtcNow,
            Itens = itens,
            Totais = new FaturamentoMensalItemDto { Mes = "Total", Quantidade = itens.Sum(i => i.Quantidade), Particular = part, Convenio = conv, Total = part + conv }
        };
    }

    // ------------------------------------------------------------ lista de espera

    private sealed class LinhaEspera
    {
        public int Id { get; set; }
        public Guid PacienteId { get; set; }
        public string Paciente { get; set; } = string.Empty;
        public int? ProfissionalId { get; set; }
        public string? Profissional { get; set; }
        public int? EspecialidadeId { get; set; }
        public string? Especialidade { get; set; }
        public int? UnidadeId { get; set; }
        public string? Unidade { get; set; }
        public string Situacao { get; set; } = string.Empty;
        public DateTime EntrouEm { get; set; }
        public DateTime? NotificadoEm { get; set; }
        public string? Observacao { get; set; }
    }

    private sealed class ContagemEspera
    {
        public string Situacao { get; set; } = string.Empty;
        public int Quantidade { get; set; }
    }

    private const string DeListaEspera = @"
FROM DBO.LISTA_ESPERA L
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = L.CODIGO_PACIENTE
LEFT JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = L.CODIGO_PROFISSIONAL
LEFT JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = L.CODIGO_ESPECIALIDADE
LEFT JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = L.CODIGO_UNIDADE";

    private const string OndeListaEspera = @"
WHERE L.DATA_CRIACAO >= @De AND L.DATA_CRIACAO < @Ate
  AND (@Prof IS NULL OR L.CODIGO_PROFISSIONAL = @Prof)
  AND (@Unidade IS NULL OR L.CODIGO_UNIDADE = @Unidade)";

    private static string? NormalizarSituacao(string? situacao)
    {
        if (string.IsNullOrWhiteSpace(situacao)) return null;
        var s = situacao.Trim().ToUpperInvariant();
        if (s is not ("AGUARDANDO" or "NOTIFICADO" or "ATENDIDO" or "CANCELADO"))
            throw Erro.Validacao("situacao", "Informe AGUARDANDO, NOTIFICADO, ATENDIDO ou CANCELADO");
        return s;
    }

    private static string NomeDaSituacao(string s) => s switch
    {
        "AGUARDANDO" => "Aguardando",
        "NOTIFICADO" => "Horário liberado",
        "ATENDIDO" => "Atendido",
        "CANCELADO" => "Cancelado",
        _ => s
    };

    private static ListaEsperaRelatorioItemDto ParaItemEspera(LinhaEspera l, DateTime agora)
    {
        var entrou = DataBrasil.Utc(l.EntrouEm);
        var aberto = l.Situacao is "AGUARDANDO" or "NOTIFICADO";
        return new ListaEsperaRelatorioItemDto
        {
            Id = l.Id,
            Paciente = new PacienteReferenciaDto { Id = l.PacienteId, Nome = l.Paciente },
            Profissional = l.ProfissionalId.HasValue ? new ReferenciaDto { Id = l.ProfissionalId.Value, Nome = l.Profissional ?? string.Empty } : null,
            Especialidade = l.EspecialidadeId.HasValue ? new ReferenciaDto { Id = l.EspecialidadeId.Value, Nome = l.Especialidade ?? string.Empty } : null,
            Unidade = l.UnidadeId.HasValue ? new ReferenciaDto { Id = l.UnidadeId.Value, Nome = l.Unidade ?? string.Empty } : null,
            Situacao = l.Situacao,
            EntrouEm = entrou,
            NotificadoEm = l.NotificadoEm.HasValue ? DataBrasil.Utc(l.NotificadoEm.Value) : null,
            DiasNaFila = aberto ? Math.Max(0, (int)(agora - entrou).TotalDays) : null,
            Observacao = l.Observacao
        };
    }

    private async Task<(List<LinhaEspera> Linhas, int Total)> DetalheListaEsperaAsync(SqlConnection con, Janela j, FiltroRelatorio f, string? situacao, int deslocamento, int tamanho, CancellationToken ct)
    {
        var onde = OndeListaEspera + " AND (@Situacao IS NULL OR L.STATUS = @Situacao)";
        var p = new DynamicParameters(new { De = j.De, Ate = j.Ate, Prof = f.ProfissionalId, Unidade = f.UnidadeId, Situacao = situacao });
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1)" + DeListaEspera + onde + ";", p, ct: ct));

        p.Add("Deslocamento", deslocamento);
        p.Add("Tamanho", tamanho);
        var linhas = (await con.QueryAsync<LinhaEspera>(Sql.Cmd(@"
SELECT L.CODIGO_LISTA AS Id, P.CODIGO_PUBLIC AS PacienteId, P.NOME AS Paciente,
       PR.CODIGO_PROFISSIONAL AS ProfissionalId, PR.NOME AS Profissional,
       E.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS Especialidade,
       U.CODIGO_UNIDADE AS UnidadeId, U.NOME_FANTASIA AS Unidade,
       L.STATUS AS Situacao, L.DATA_CRIACAO AS EntrouEm, L.DATA_NOTIFICACAO AS NotificadoEm, L.OBSERVACAO AS Observacao" + DeListaEspera + onde + @"
ORDER BY CASE L.STATUS WHEN 'NOTIFICADO' THEN 0 WHEN 'AGUARDANDO' THEN 1 ELSE 2 END, L.DATA_CRIACAO, L.CODIGO_LISTA
OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct))).ToList();
        return (linhas, total);
    }

    /// <summary>A lista de espera usa a data de entrada na fila; convênio e tipo não se aplicam a ela.</summary>
    private async Task<Janela> PrepararEsperaAsync(SqlConnection con, FiltroRelatorio f, CancellationToken ct)
    {
        var j = await PrepararAsync(con, new FiltroRelatorio
        {
            DataInicial = f.DataInicial,
            DataFinal = f.DataFinal,
            ProfissionalId = f.ProfissionalId,
            UnidadeId = f.UnidadeId
        }, ct);
        j.Aplicados.Situacao = NormalizarSituacao(f.Situacao);
        return j;
    }

    public async Task<RelatorioListaEsperaDto> ListaEsperaAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        if (f.Pagina < 1) throw Erro.Validacao("pagina", "Deve ser 1 ou mais");
        if (f.Tamanho is < 1 or > 100) throw Erro.Validacao("tamanho", "Deve estar entre 1 e 100");

        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararEsperaAsync(con, f, ct);
        var situacao = j.Aplicados.Situacao;

        // Os totais por situação não dependem do filtro de situação: mostram o quadro todo do período.
        var contagens = (await con.QueryAsync<ContagemEspera>(Sql.Cmd(
            "SELECT L.STATUS AS Situacao, CAST(COUNT(1) AS INT) AS Quantidade" + DeListaEspera + OndeListaEspera + " GROUP BY L.STATUS;",
            new { De = j.De, Ate = j.Ate, Prof = f.ProfissionalId, Unidade = f.UnidadeId }, ct: ct))).ToList();
        int Qtd(string s) => contagens.Where(c => c.Situacao == s).Sum(c => c.Quantidade);

        var agora = DateTime.UtcNow;
        var emAberto = (await con.QueryAsync<DateTime>(Sql.Cmd(
            "SELECT L.DATA_CRIACAO" + DeListaEspera + OndeListaEspera + " AND L.STATUS IN ('AGUARDANDO', 'NOTIFICADO');",
            new { De = j.De, Ate = j.Ate, Prof = f.ProfissionalId, Unidade = f.UnidadeId }, ct: ct))).ToList();
        var media = emAberto.Count == 0 ? 0m : Math.Round((decimal)emAberto.Average(d => Math.Max(0, (agora - DataBrasil.Utc(d)).TotalDays)), 1);

        var (linhas, total) = await DetalheListaEsperaAsync(con, j, f, situacao, (f.Pagina - 1) * f.Tamanho, f.Tamanho, ct);

        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_LISTA_ESPERA", null, null, usuario.Id,
            new { de = j.Inicio, ate = j.Fim, profissional = f.ProfissionalId, unidade = f.UnidadeId, situacao }, con, ct: ct);

        return new RelatorioListaEsperaDto
        {
            Filtros = j.Aplicados,
            GeradoEm = agora,
            Totais = new TotaisListaEsperaDto
            {
                Total = contagens.Sum(c => c.Quantidade),
                Aguardando = Qtd("AGUARDANDO"),
                Notificado = Qtd("NOTIFICADO"),
                Atendido = Qtd("ATENDIDO"),
                Cancelado = Qtd("CANCELADO"),
                MediaDiasEmAberto = media
            },
            Itens = linhas.Select(l => ParaItemEspera(l, agora)).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    // ------------------------------------------------------------ tabelas para exportação

    private static List<string> FiltrosFinanceiros(FiltrosAplicadosDto a, bool comConvenio, bool comTipo)
    {
        var l = new List<string> { $"Período: {Data(a.DataInicial)} a {Data(a.DataFinal)}", "Profissional: " + (a.Profissional?.Nome ?? "Todos"), "Unidade: " + (a.Unidade?.Nome ?? "Todas") };
        if (comConvenio) l.Add("Convênio: " + (a.Convenio?.Nome ?? "Todos"));
        if (comTipo) l.Add("Tipo: " + (a.TipoAtendimento switch { "PARTICULAR" => "Particular", "CONVENIO" => "Convênio", _ => "Todos" }));
        l.Add("Considera apenas consultas concluídas");
        return l;
    }

    private async Task<TabelaExportacao> TabelaFaturamentoAsync(string modo, FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        var porConvenio = modo == "convenio";
        var particular = modo == "particular";
        var r = porConvenio ? await FaturamentoPorConvenioAsync(f, usuario, ct) : particular ? await FaturamentoParticularAsync(f, usuario, ct) : await FaturamentoPorTipoAsync(f, usuario, ct);
        var confirmado = porConvenio ? "Autorizado" : particular ? "Recebido" : "Recebido / autorizado";
        var pendente = porConvenio ? "Guia pendente" : particular ? "A confirmar" : "A confirmar / pendente";
        var tabela = new TabelaExportacao
        {
            Titulo = porConvenio ? "Faturamento por convênio" : particular ? "Faturamento particular por forma de pagamento" : "Faturamento por tipo de atendimento",
            Filtros = FiltrosFinanceiros(r.Filtros, !particular, modo == "tipo"),
            Colunas =
            {
                ColunaTabela.Texto(porConvenio ? "Convênio" : particular ? "Forma de pagamento" : "Tipo"), ColunaTabela.Numero("Consultas"), ColunaTabela.Numero("Valor"), ColunaTabela.Numero("% do total"),
                ColunaTabela.Numero(confirmado), ColunaTabela.Numero(pendente), ColunaTabela.Numero("Negado"), ColunaTabela.Numero("Ticket médio")
            }
        };
        List<string> Linha(FaturamentoLinhaDto i) => new()
        {
            i.Nome, i.Quantidade.ToString(Pt), Moeda(i.Valor), Pct(i.Percentual), Moeda(i.Confirmado), Moeda(i.Pendente), Moeda(i.Negado), Moeda(i.TicketMedio)
        };
        foreach (var i in r.Itens) tabela.Linhas.Add(Linha(i));
        tabela.Totais = Linha(r.Totais);
        return tabela;
    }

    private async Task<TabelaExportacao> TabelaFaturamentoMensalAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        var r = await FaturamentoMensalAsync(f, usuario, ct);
        var tabela = new TabelaExportacao
        {
            Titulo = "Faturamento mensal",
            Filtros = FiltrosFinanceiros(r.Filtros, true, true),
            Colunas = { ColunaTabela.Texto("Mês"), ColunaTabela.Numero("Consultas"), ColunaTabela.Numero("Particular"), ColunaTabela.Numero("Convênio"), ColunaTabela.Numero("Total"), ColunaTabela.Numero("Variação") }
        };
        foreach (var i in r.Itens)
        {
            var mes = DateOnly.ParseExact(i.Mes + "-01", "yyyy-MM-dd").ToString("MM/yyyy", Pt);
            tabela.Linhas.Add(new List<string>
            {
                mes, i.Quantidade.ToString(Pt), Moeda(i.Particular), Moeda(i.Convenio), Moeda(i.Total),
                i.VariacaoPercentual.HasValue ? (i.VariacaoPercentual.Value > 0 ? "+" : "") + Pct(i.VariacaoPercentual.Value) : "—"
            });
        }
        var t = r.Totais;
        tabela.Totais = new List<string> { "Total", t.Quantidade.ToString(Pt), Moeda(t.Particular), Moeda(t.Convenio), Moeda(t.Total), "" };
        return tabela;
    }

    private async Task<TabelaExportacao> TabelaListaEsperaAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararEsperaAsync(con, f, ct);
        var (linhas, total) = await DetalheListaEsperaAsync(con, j, f, j.Aplicados.Situacao, 0, LimiteExportacao + 1, ct);
        if (total > LimiteExportacao) throw Erro.Regra("EXPORTACAO_MUITO_GRANDE", "O relatório passa de 10.000 linhas. Reduza o período ou use filtros.");

        var filtros = new List<string>
        {
            $"Entrada na fila: {Data(j.Inicio)} a {Data(j.Fim)}",
            "Profissional: " + (j.Aplicados.Profissional?.Nome ?? "Todos"),
            "Unidade: " + (j.Aplicados.Unidade?.Nome ?? "Todas"),
            "Situação: " + (j.Aplicados.Situacao is null ? "Todas" : NomeDaSituacao(j.Aplicados.Situacao))
        };
        var tabela = new TabelaExportacao
        {
            Titulo = "Lista de espera",
            Filtros = filtros,
            Colunas =
            {
                ColunaTabela.Texto("Paciente"), ColunaTabela.Texto("Profissional"), ColunaTabela.Texto("Especialidade"), ColunaTabela.Texto("Unidade"), ColunaTabela.Texto("Situação"),
                ColunaTabela.Texto("Entrou em"), ColunaTabela.Texto("Avisado em"), ColunaTabela.Numero("Dias na fila"), ColunaTabela.Texto("Observação")
            }
        };
        var agora = DateTime.UtcNow;
        foreach (var l in linhas)
        {
            var i = ParaItemEspera(l, agora);
            string Quando(DateTime? d) => d.HasValue ? DataBrasil.ParaBrasilia(d.Value).ToString("dd/MM/yyyy HH:mm", Pt) : string.Empty;
            tabela.Linhas.Add(new List<string>
            {
                i.Paciente.Nome, i.Profissional?.Nome ?? "Qualquer", i.Especialidade?.Nome ?? string.Empty, i.Unidade?.Nome ?? string.Empty, NomeDaSituacao(i.Situacao),
                Quando(i.EntrouEm), Quando(i.NotificadoEm), i.DiasNaFila?.ToString(Pt) ?? string.Empty, i.Observacao ?? string.Empty
            });
        }
        tabela.Totais = null;

        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_LISTA_ESPERA", null, null, usuario.Id,
            new { de = j.Inicio, ate = j.Fim, profissional = f.ProfissionalId, unidade = f.UnidadeId, situacao = j.Aplicados.Situacao }, con, ct: ct);
        return tabela;
    }
}
