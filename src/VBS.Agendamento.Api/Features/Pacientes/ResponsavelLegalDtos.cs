namespace VBS.Agendamento.Api.Features.Pacientes;

public sealed class ResponsavelLegalEntrada
{
    public string? Nome { get; set; }
    public string? Cpf { get; set; }
    public string? Rg { get; set; }
    public DateOnly? DataNascimento { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Escolaridade { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    /// <summary>Obrigatório no cadastro; na alteração, só quando o responsável ainda não tem o consentimento de tratamento de dados.</summary>
    public List<ConsentimentoEntrada>? Consentimentos { get; set; }
}

public sealed class TrocaProfissionalEntrada
{
    public int? ProfissionalId { get; set; }
}

public sealed class HistoricoProfissionalDto
{
    public ProfissionalReferenciaDto Profissional { get; set; } = new();
    public DateTime DataInicio { get; set; }
    public DateTime? DataFim { get; set; }
    public UsuarioReferenciaDto? AlteradoPor { get; set; }
}
