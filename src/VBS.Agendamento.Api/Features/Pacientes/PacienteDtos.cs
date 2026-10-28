namespace VBS.Agendamento.Api.Features.Pacientes;

// ---------- Entrada ----------

public sealed class ConvenioPacienteEntrada
{
    public int? ConvenioId { get; set; }
    public string? NumeroCarteirinha { get; set; }
    public DateOnly? DataValidadeCarteirinha { get; set; }
}

/// <summary>Campos comuns ao cadastro e à alteração (PacienteBase do contrato).</summary>
public class PacienteBaseEntrada
{
    public string? Nome { get; set; }
    public bool? Estrangeiro { get; set; }
    public string? Cpf { get; set; }
    public string? TipoDocumentoEstrangeiro { get; set; }
    public string? NumeroDocumentoEstrangeiro { get; set; }
    public string? Rg { get; set; }
    public DateOnly? DataNascimento { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Escolaridade { get; set; }
    public ConvenioPacienteEntrada? Convenio { get; set; }
    public int? ResponsavelLegalId { get; set; }
}

public sealed class ConsentimentoEntrada
{
    public string? Finalidade { get; set; }
    public bool? Concedido { get; set; }
    public string? Origem { get; set; }
}

public sealed class PacienteEntrada : PacienteBaseEntrada
{
    public int? ProfissionalResponsavelId { get; set; }
    public List<ConsentimentoEntrada>? Consentimentos { get; set; }
}

public sealed class StatusPacienteEntrada
{
    public bool? Ativo { get; set; }
}

public sealed class BuscaCpfEntrada
{
    public string? Cpf { get; set; }
}

public sealed class FiltroPacientes
{
    public string? Busca { get; set; }
    public bool? Ativo { get; set; }
    public int? ProfissionalId { get; set; }
    public int? ConvenioId { get; set; }
    public bool? MenorDeIdade { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

// ---------- Saída ----------

public sealed class ProfissionalReferenciaDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}

public sealed class ConvenioReferenciaDto
{
    public int Id { get; set; }
    public string NomeFantasia { get; set; } = string.Empty;
}

public sealed class ConvenioPacienteDto
{
    public ConvenioReferenciaDto Convenio { get; set; } = new();
    public string NumeroCarteirinha { get; set; } = string.Empty;
    public DateOnly DataValidadeCarteirinha { get; set; }
    public bool CarteirinhaVencida { get; set; }
}

public sealed class UsuarioReferenciaDto
{
    public int Id { get; set; }
    public string NomeCompleto { get; set; } = string.Empty;
}

public sealed class ResponsavelLegalDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string? Rg { get; set; }
    public DateOnly DataNascimento { get; set; }
    public string Telefone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Escolaridade { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public List<ConsentimentoDto> ConsentimentosAtuais { get; set; } = new();
    public int QuantidadePacientes { get; set; }
}

public sealed class ConsentimentoDto
{
    public string Finalidade { get; set; } = string.Empty;
    public bool Concedido { get; set; }
    public string Origem { get; set; } = string.Empty;
    public DateTime DataRegistro { get; set; }
    public UsuarioReferenciaDto? RegistradoPor { get; set; }
}

public sealed class PacienteDto
{
    public Guid Id { get; set; }
    public bool Ativo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public bool Estrangeiro { get; set; }
    public string? Cpf { get; set; }
    public string? TipoDocumentoEstrangeiro { get; set; }
    public string? NumeroDocumentoEstrangeiro { get; set; }
    public string? Rg { get; set; }
    public DateOnly DataNascimento { get; set; }
    public bool MenorDeIdade { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string Endereco { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Complemento { get; set; }
    public string Cidade { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string? Escolaridade { get; set; }
    public ConvenioPacienteDto? Convenio { get; set; }
    public ProfissionalReferenciaDto ProfissionalResponsavel { get; set; } = new();
    public ResponsavelLegalDto? ResponsavelLegal { get; set; }
    public List<ConsentimentoDto> ConsentimentosAtuais { get; set; } = new();
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class PacienteResumoDto
{
    public Guid Id { get; set; }
    public bool Ativo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? CpfMascarado { get; set; }
    public DateOnly DataNascimento { get; set; }
    public bool MenorDeIdade { get; set; }
    public string? Telefone { get; set; }
    public ConvenioReferenciaDto? Convenio { get; set; }
    public ProfissionalReferenciaDto ProfissionalResponsavel { get; set; } = new();
}

public sealed class PacientePaginaDto
{
    public List<PacienteResumoDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
