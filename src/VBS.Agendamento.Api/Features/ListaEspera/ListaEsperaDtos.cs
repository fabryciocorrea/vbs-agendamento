using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.ListaEspera;

public sealed class ListaEsperaEntrada
{
    public Guid? PacienteId { get; set; }
    public int? ProfissionalId { get; set; }
    public int? EspecialidadeId { get; set; }
    public int? UnidadeId { get; set; }
    public string? Observacao { get; set; }
}

public sealed class ListaEsperaStatusEntrada
{
    /// <summary>ATENDIDO ou CANCELADO encerram o item; AGUARDANDO devolve à fila um item NOTIFICADO.</summary>
    public string? Status { get; set; }
}

public sealed class FiltroListaEspera
{
    public string? Status { get; set; }
    public int? ProfissionalId { get; set; }
    public int? EspecialidadeId { get; set; }
    public string? Busca { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class ListaEsperaDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public PacienteReferenciaDto Paciente { get; set; } = new();
    public ReferenciaDto? Profissional { get; set; }
    public ReferenciaDto? Especialidade { get; set; }
    public ReferenciaDto? Unidade { get; set; }
    public string? Observacao { get; set; }
    public DateTime? NotificadoEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
}

public sealed class ListaEsperaPaginaDto
{
    public List<ListaEsperaDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
