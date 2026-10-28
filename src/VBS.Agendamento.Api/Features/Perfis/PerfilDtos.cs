using VBS.Agendamento.Api.Features.Auth;

namespace VBS.Agendamento.Api.Features.Perfis;

// ---------- Entrada ----------

/// <summary>Liberações enviadas pela tela. Reaproveita os DTOs de /auth/eu (o código da tela/campo é ignorado na entrada).</summary>
public sealed class PermissoesEntrada
{
    public List<PermissaoTelaDto>? Telas { get; set; }
    public List<PermissaoCampoDto>? Campos { get; set; }
}

public sealed class PerfilEntrada
{
    public string? Nome { get; set; }
    public string? Descricao { get; set; }
    public PermissoesEntrada? Permissoes { get; set; }
}

// ---------- Saída ----------

public class PerfilResumoDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public bool Protegido { get; set; }
    public int QuantidadeUsuarios { get; set; }
}

public class PerfilDto : PerfilResumoDto
{
    public PermissoesSaida Permissoes { get; set; } = new();
}

public sealed class PerfilComAvisosDto : PerfilDto
{
    public List<AvisoPerfilDto> Avisos { get; set; } = new();
}

public sealed class AvisoPerfilDto
{
    public int TelaId { get; set; }
    public int CampoId { get; set; }
    public string Mensagem { get; set; } = string.Empty;
}

public sealed class ValidacaoPerfilDto
{
    public List<AvisoPerfilDto> Avisos { get; set; } = new();
}

public sealed class CampoDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public bool Obrigatorio { get; set; }
}

public sealed class TelaDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Modulo { get; set; } = string.Empty;
    public List<CampoDto> Campos { get; set; } = new();
}
