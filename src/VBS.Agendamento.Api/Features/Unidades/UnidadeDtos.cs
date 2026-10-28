using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Unidades;

// ---------- Entrada ----------

public sealed class UnidadeEntrada
{
    public string? RazaoSocial { get; set; }
    public string? NomeFantasia { get; set; }
    public string? Cnpj { get; set; }
    public string? Cep { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
}

/// <summary>Corpo do PATCH .../status (usado por vários cadastros).</summary>
public sealed class StatusEntrada
{
    public bool? Ativo { get; set; }
}

public sealed class FiltroUnidades
{
    public string? Busca { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

// ---------- Saída ----------

public sealed class UnidadeDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string RazaoSocial { get; set; } = string.Empty;
    public string NomeFantasia { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public string Cep { get; set; } = string.Empty;
    public string Endereco { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Complemento { get; set; }
    public string Cidade { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class UnidadePaginaDto
{
    public List<UnidadeDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
