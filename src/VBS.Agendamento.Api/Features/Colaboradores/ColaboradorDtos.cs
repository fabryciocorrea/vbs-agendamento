using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Colaboradores;

public sealed class ColaboradorEntrada
{
    public string? TipoVinculo { get; set; }
    public string? Nome { get; set; }
    public string? Cpf { get; set; }
    public string? RazaoSocial { get; set; }
    public string? NomeFantasia { get; set; }
    public string? Cnpj { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Cargo { get; set; }
    public string? CtpsNumero { get; set; }
    public string? CtpsSerie { get; set; }
    public string? CtpsUf { get; set; }
    public DateOnly? CtpsDataEmissao { get; set; }
    public string? PisPasep { get; set; }
    public DateOnly? DataAdmissao { get; set; }
}

public sealed class FiltroColaboradores
{
    public string? Busca { get; set; }
    public string? TipoVinculo { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class ColaboradorDto
{
    /// <summary>Matrícula: é o CODIGO_CLB_PRT, gerado pelo banco e só alterável por UPDATE direto no banco.</summary>
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string TipoVinculo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string? Cpf { get; set; }
    public string? RazaoSocial { get; set; }
    public string? NomeFantasia { get; set; }
    public string? Cnpj { get; set; }
    public string Telefone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Endereco { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Complemento { get; set; }
    public string Cidade { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string? CtpsNumero { get; set; }
    public string? CtpsSerie { get; set; }
    public string? CtpsUf { get; set; }
    public DateOnly? CtpsDataEmissao { get; set; }
    public string? PisPasep { get; set; }
    public DateOnly? DataAdmissao { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class ColaboradorPaginaDto
{
    public List<ColaboradorDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
