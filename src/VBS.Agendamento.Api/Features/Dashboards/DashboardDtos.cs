using VBS.Agendamento.Api.Common;

namespace VBS.Agendamento.Api.Features.Dashboards;

public sealed class FiltroPainel
{
    /// <summary>Um só dia (AAAA-MM-DD, horário de Brasília). Vale quando o período não é informado. Vazia: hoje.</summary>
    public DateOnly? Data { get; set; }
    /// <summary>Início do período. Sem a data final, vale só esse dia.</summary>
    public DateOnly? DataInicial { get; set; }
    /// <summary>Fim do período (inclusive), no máximo 366 dias depois do início.</summary>
    public DateOnly? DataFinal { get; set; }
    public int? UnidadeId { get; set; }
    public int? ProfissionalId { get; set; }
    /// <summary>PARTICULAR ou CONVENIO. Vazio: todos.</summary>
    public string? Tipo { get; set; }
}

public sealed class PainelTotaisDto
{
    public int Agendadas { get; set; }
    public int Confirmadas { get; set; }
    public int CheckIn { get; set; }
    public int EmAtendimento { get; set; }
    public int Concluidas { get; set; }
    public int Canceladas { get; set; }
    public int Faltas { get; set; }
    public int Remarcadas { get; set; }
    /// <summary>Encaixes do dia, já contados nos demais status.</summary>
    public int Encaixes { get; set; }
    public int Total { get; set; }
}

public sealed class PainelPendenciasDto
{
    /// <summary>Consultas agendadas ou confirmadas cujo horário já passou sem check-in.</summary>
    public int EmAtraso { get; set; }
    /// <summary>Particulares com check-in ou em atendimento e pagamento ainda não confirmado.</summary>
    public int PagamentoParticularPendente { get; set; }
    /// <summary>Consultas de convênio, do dia em diante, com a guia ainda pendente. Só para quem vê o faturamento.</summary>
    public int? GuiasPendentes { get; set; }
    /// <summary>Consultas canceladas cuja guia continua autorizada: o faturamento precisa cancelá-la. Só para quem vê o faturamento.</summary>
    public int? GuiasParaCancelar { get; set; }
}

public sealed class PainelConsultaDto
{
    public Guid ConsultaId { get; set; }
    public DateTime Inicio { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Paciente { get; set; } = string.Empty;
    public string Profissional { get; set; } = string.Empty;
    public string Especialidade { get; set; } = string.Empty;
    public string Unidade { get; set; } = string.Empty;
    public string TipoAtendimento { get; set; } = string.Empty;
    public string? Convenio { get; set; }
    public bool Encaixe { get; set; }
    /// <summary>Avisos curtos: Atrasada, Pagamento pendente, Guia pendente.</summary>
    public List<string> Alertas { get; set; } = new();
}

public sealed class PainelListaEsperaDto
{
    public int Aguardando { get; set; }
    public int HorarioLiberado { get; set; }
}

public sealed class PainelDistribuicaoDto
{
    /// <summary>PARTICULAR ou CONVENIO.</summary>
    public string Tipo { get; set; } = string.Empty;
    /// <summary>"Particular" ou o nome do convênio.</summary>
    public string Nome { get; set; } = string.Empty;
    public int Quantidade { get; set; }
}

public sealed class PainelDoDiaDto
{
    /// <summary>Igual a DataInicial (mantido pelo contrato).</summary>
    public DateOnly Data { get; set; }
    public DateOnly DataInicial { get; set; }
    public DateOnly DataFinal { get; set; }
    public ReferenciaDto? Unidade { get; set; }
    public ReferenciaDto? Profissional { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public string? Tipo { get; set; }
    public PainelTotaisDto Totais { get; set; } = new();
    /// <summary>Atendimentos do dia (sem canceladas, remarcadas e faltas) por convênio e particular, do maior para o menor.</summary>
    public List<PainelDistribuicaoDto> Distribuicao { get; set; } = new();
    public PainelPendenciasDto Pendencias { get; set; } = new();
    /// <summary>Consultas do dia ainda em andamento (agendada, confirmada, check-in ou em atendimento), por horário.</summary>
    public List<PainelConsultaDto> Proximas { get; set; } = new();
    /// <summary>Nulo para quem não tem acesso à lista de espera.</summary>
    public PainelListaEsperaDto? ListaEspera { get; set; }
}

// ---------------------------------------------------------------- painel financeiro

public sealed class FiltroPainelFinanceiro
{
    /// <summary>Início do período (horário de Brasília). Vazio: primeiro dia do mês atual.</summary>
    public DateOnly? DataInicial { get; set; }
    /// <summary>Fim do período, inclusive, no máximo 366 dias depois do início. Vazio: hoje.</summary>
    public DateOnly? DataFinal { get; set; }
    public int? UnidadeId { get; set; }
    public int? ProfissionalId { get; set; }
    /// <summary>PARTICULAR ou CONVENIO. Vazio: todos.</summary>
    public string? Tipo { get; set; }
}

public sealed class PainelValorDto
{
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
}

public sealed class PainelFinanceiroTotaisDto
{
    /// <summary>Consultas concluídas no período.</summary>
    public PainelValorDto Faturado { get; set; } = new();
    /// <summary>Valor faturado dividido pelas consultas concluídas.</summary>
    public decimal TicketMedio { get; set; }
    public PainelValorDto ParticularRecebido { get; set; } = new();
    public PainelValorDto ParticularAConfirmar { get; set; } = new();
    public PainelValorDto ConvenioAutorizado { get; set; } = new();
    public PainelValorDto ConvenioPendente { get; set; } = new();
    public PainelValorDto ConvenioNegado { get; set; } = new();
    /// <summary>Consultas ainda não concluídas (agendada, confirmada, check-in e em atendimento): o que está previsto.</summary>
    public PainelValorDto Previsto { get; set; } = new();
}

public sealed class PainelEvolucaoDto
{
    /// <summary>AAAA-MM-DD (agrupamento DIA) ou AAAA-MM (agrupamento MES).</summary>
    public string Periodo { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
}

public sealed class PainelFinanceiroLinhaDto
{
    /// <summary>PARTICULAR ou CONVENIO nas linhas por convênio; vazio nas demais.</summary>
    public string? Tipo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
    /// <summary>Participação no valor faturado (0 a 100).</summary>
    public decimal Percentual { get; set; }
    /// <summary>Só nas linhas de convênio: parte do valor por situação da guia.</summary>
    public decimal? Autorizado { get; set; }
    public decimal? Pendente { get; set; }
    public decimal? Negado { get; set; }
}

public sealed class PainelFinanceiroDto
{
    public DateOnly DataInicial { get; set; }
    public DateOnly DataFinal { get; set; }
    public ReferenciaDto? Unidade { get; set; }
    public ReferenciaDto? Profissional { get; set; }
    public string? Tipo { get; set; }
    public DateTime AtualizadoEm { get; set; }
    /// <summary>DIA (períodos de até 62 dias) ou MES.</summary>
    public string Agrupamento { get; set; } = "DIA";
    public PainelFinanceiroTotaisDto Totais { get; set; } = new();
    public List<PainelEvolucaoDto> Evolucao { get; set; } = new();
    public List<PainelFinanceiroLinhaDto> PorConvenio { get; set; } = new();
    public List<PainelFinanceiroLinhaDto> PorEspecialidade { get; set; } = new();
    public List<PainelFinanceiroLinhaDto> PorProfissional { get; set; } = new();
}
