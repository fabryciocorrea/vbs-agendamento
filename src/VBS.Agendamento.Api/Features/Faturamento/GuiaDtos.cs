using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Faturamento;

public sealed class GuiaEntrada
{
    /// <summary>PENDENTE, AUTORIZADA ou NEGADA.</summary>
    public string? Status { get; set; }
    public string? Numero { get; set; }
    public string? Observacao { get; set; }
}

public sealed class FiltroGuias
{
    public string? Status { get; set; }
    public int? ConvenioId { get; set; }
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public string? Busca { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class GuiaItemDto
{
    public Guid ConsultaId { get; set; }
    public DateTime Inicio { get; set; }
    public string StatusConsulta { get; set; } = string.Empty;
    public string Paciente { get; set; } = string.Empty;
    public ReferenciaDto Convenio { get; set; } = new();
    public string? NumeroCarteirinha { get; set; }
    public ReferenciaDto Especialidade { get; set; } = new();
    public ReferenciaDto Profissional { get; set; } = new();
    public decimal Valor { get; set; }
    public string GuiaStatus { get; set; } = "PENDENTE";
    public string? GuiaNumero { get; set; }
    public string? GuiaObservacao { get; set; }
    public DateTime? GuiaEm { get; set; }
    public UsuarioReferenciaDto? GuiaPor { get; set; }
}

public sealed class GuiaPaginaDto
{
    public List<GuiaItemDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
