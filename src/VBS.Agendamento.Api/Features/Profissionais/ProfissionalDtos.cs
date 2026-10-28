using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Profissionais;

// ---------- Entrada ----------

public sealed class HorarioDto
{
    public int? UnidadeId { get; set; }
    public int? DiaSemana { get; set; }
    public string? Tipo { get; set; }
    public string? HoraInicio { get; set; }
    public string? HoraFim { get; set; }
}

/// <summary>Dados do profissional (PUT). O expediente e o status têm operações próprias.</summary>
public class ProfissionalAtualizacao
{
    public string? Nome { get; set; }
    public string? Cpf { get; set; }
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
    public string? RegistroProfissional { get; set; }
    public string? NumeroConselho { get; set; }
    public string? UfConselho { get; set; }
    public List<int>? EspecialidadeIds { get; set; }
    public List<int>? UnidadeIds { get; set; }
}

/// <summary>Cadastro (POST): os mesmos dados mais o expediente, obrigatório (RF04).</summary>
public sealed class ProfissionalEntrada : ProfissionalAtualizacao
{
    public List<HorarioDto>? Horarios { get; set; }
}

public sealed class HorariosEntrada
{
    public List<HorarioDto>? Horarios { get; set; }
}

public sealed class FiltroProfissionais
{
    public string? Busca { get; set; }
    public int? EspecialidadeId { get; set; }
    public int? UnidadeId { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

// ---------- Saída ----------

public sealed class ProfissionalDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string? Rg { get; set; }
    public DateOnly DataNascimento { get; set; }
    public string Telefone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Endereco { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Complemento { get; set; }
    public string Cidade { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string? Escolaridade { get; set; }
    public string RegistroProfissional { get; set; } = string.Empty;
    public string NumeroConselho { get; set; } = string.Empty;
    public string UfConselho { get; set; } = string.Empty;
    public List<ReferenciaDto> Especialidades { get; set; } = new();
    public List<ReferenciaDto> Unidades { get; set; } = new();
    public int QuantidadePacientes { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class ProfissionalResumoDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Registro { get; set; } = string.Empty;
    public string RegistroProfissional { get; set; } = string.Empty;
    public string NumeroConselho { get; set; } = string.Empty;
    public string UfConselho { get; set; } = string.Empty;
    public List<ReferenciaDto> Especialidades { get; set; } = new();
    public List<ReferenciaDto> Unidades { get; set; } = new();
}

public sealed class ProfissionalPaginaDto
{
    public List<ProfissionalResumoDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

public sealed class ConsultaForaExpedienteDto
{
    public Guid ConsultaId { get; set; }
    public DateTime DataInicio { get; set; }
}

public sealed class ForaDoExpedienteDto
{
    public int Quantidade { get; set; }
    public List<ConsultaForaExpedienteDto> Itens { get; set; } = new();
}

public sealed class HorariosSaidaDto
{
    public List<HorarioDto> Horarios { get; set; } = new();
    public ForaDoExpedienteDto ConsultasForaDoExpediente { get; set; } = new();
}
