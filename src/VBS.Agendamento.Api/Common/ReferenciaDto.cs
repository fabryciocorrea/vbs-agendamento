namespace VBS.Agendamento.Api.Common;

/// <summary>Referência curta a outro cadastro: { id, nome } (schema Referencia do contrato).</summary>
public sealed class ReferenciaDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}
