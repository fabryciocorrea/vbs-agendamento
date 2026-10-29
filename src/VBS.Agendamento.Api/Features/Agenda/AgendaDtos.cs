using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;

namespace VBS.Agendamento.Api.Features.Agenda;

/// <summary>Parâmetros da agenda (RN03, RN06, P27). Entrada e saída têm o mesmo formato.</summary>
public sealed class ParametrosAgendaDto
{
    public int? DuracaoPadraoConsultaMin { get; set; }
    public int? AntecedenciaCancelamentoHoras { get; set; }
    public int? LimiteEncaixesDia { get; set; }
    public DateOnly? RecessoFimAnoInicio { get; set; }
    public DateOnly? RecessoFimAnoFim { get; set; }
}

public sealed class FeriadoDto
{
    public DateOnly? Data { get; set; }
    public string? Nome { get; set; }
    /// <summary>Verdadeiro nos feriados nacionais calculados pelo sistema (não podem ser removidos).</summary>
    public bool Automatico { get; set; }
}

public sealed class SlotDto
{
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
}

public sealed class BloqueioDto
{
    public string Tipo { get; set; } = string.Empty;
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public string? Descricao { get; set; }
}

public sealed class PeriodoExpedienteDto
{
    public ReferenciaDto Unidade { get; set; } = new();
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
}

public sealed class AgendaSaidaDto
{
    public ReferenciaDto Profissional { get; set; } = new();
    public List<PeriodoExpedienteDto> Expediente { get; set; } = new();
    public List<BloqueioDto> Bloqueios { get; set; } = new();
    public List<ConsultaResumoDto> Consultas { get; set; } = new();
}

public sealed class FiltroHorarios
{
    public int? ProfissionalId { get; set; }
    public int? UnidadeId { get; set; }
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public int? DuracaoMin { get; set; }
}

public sealed class FiltroAgenda
{
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public int? UnidadeId { get; set; }
    public bool IncluirCanceladas { get; set; }
}
