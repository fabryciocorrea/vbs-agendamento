using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.FormasPagamento;

public sealed class FormaPagamentoEntrada
{
    public string? Descricao { get; set; }
    public string? Tipo { get; set; }
    public string? Condicao { get; set; }
    public int? ConvenioId { get; set; }
}

public sealed class FiltroFormasPagamento
{
    public string? Busca { get; set; }
    public string? Tipo { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class FormaPagamentoDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Condicao { get; set; } = string.Empty;
    public ReferenciaDto? Convenio { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class FormaPagamentoPaginaDto
{
    public List<FormaPagamentoDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
