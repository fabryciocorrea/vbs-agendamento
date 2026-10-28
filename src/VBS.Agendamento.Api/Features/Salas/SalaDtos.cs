using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Salas;

public sealed class SalaEntrada
{
    public int? UnidadeId { get; set; }
    public string? Numero { get; set; }
    public string? Andar { get; set; }
}

public sealed class FiltroSalas
{
    public string? Busca { get; set; }
    public int? UnidadeId { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class UnidadeSalaDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}

public sealed class SalaDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public UnidadeSalaDto Unidade { get; set; } = new();
    public string Numero { get; set; } = string.Empty;
    public string Andar { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class SalaPaginaDto
{
    public List<SalaDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
