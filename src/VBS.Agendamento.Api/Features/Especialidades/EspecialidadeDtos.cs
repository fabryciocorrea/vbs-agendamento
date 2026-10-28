using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Especialidades;

public sealed class EspecialidadeEntrada
{
    public string? Nome { get; set; }
}

public sealed class FiltroEspecialidades
{
    public string? Busca { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class EspecialidadeDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class EspecialidadePaginaDto
{
    public List<EspecialidadeDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
