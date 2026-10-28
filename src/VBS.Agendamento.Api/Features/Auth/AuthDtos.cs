namespace VBS.Agendamento.Api.Features.Auth;

// ---------- Entrada ----------

public sealed class LoginEntrada
{
    public string? NomeUsuario { get; set; }
    public string? Senha { get; set; }
}

public sealed class RecuperarSenhaEntrada
{
    public string? Email { get; set; }
}

public sealed class RedefinirSenhaEntrada
{
    public string? Token { get; set; }
    public string? NovaSenha { get; set; }
}

// ---------- Saída (nomes e formatos do contrato) ----------

public sealed class PerfilReferenciaDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}

public sealed class UsuarioResumoDto
{
    public int Id { get; set; }
    public string NomeUsuario { get; set; } = string.Empty;
    public string NomeCompleto { get; set; } = string.Empty;
    public PerfilReferenciaDto Perfil { get; set; } = new();
}

public sealed class SessaoSaida
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public UsuarioResumoDto Usuario { get; set; } = new();
}

public sealed class PermissaoTelaDto
{
    public int TelaId { get; set; }
    public string TelaCodigo { get; set; } = string.Empty;
    public bool PodeVisualizar { get; set; }
    public bool PodeIncluir { get; set; }
    public bool PodeEditar { get; set; }
    public bool PodeExcluir { get; set; }
    public bool PodeExportar { get; set; }
}

public sealed class PermissaoCampoDto
{
    public int CampoId { get; set; }
    public string CampoCodigo { get; set; } = string.Empty;
    public bool PodeVisualizar { get; set; }
    public bool PodeEditar { get; set; }
}

public sealed class PermissoesSaida
{
    public List<PermissaoTelaDto> Telas { get; set; } = new();
    public List<PermissaoCampoDto> Campos { get; set; } = new();
}

public sealed class UsuarioAtualSaida
{
    public UsuarioResumoDto Usuario { get; set; } = new();
    public int? ProfissionalId { get; set; }
    public PermissoesSaida Permissoes { get; set; } = new();
}
