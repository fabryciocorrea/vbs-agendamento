namespace VBS.Agendamento.Api.Features.Apoio;

public sealed class FiltroApoio
{
    public bool IncluirInativos { get; set; }
    public int? EspecialidadeId { get; set; }
    public int? UnidadeId { get; set; }
}

public sealed class ItemApoioDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public bool Ativo { get; set; }
    public string? Complemento { get; set; }
}
