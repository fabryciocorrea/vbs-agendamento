using VBS.Agendamento.Api.Features.Auth;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Usuarios;

// ---------- Entrada ----------

public class UsuarioAtualizacao
{
    public string? NomeUsuario { get; set; }
    public string? NomeCompleto { get; set; }
    public string? Email { get; set; }
    public string? Cargo { get; set; }
    public int? PerfilId { get; set; }
    public int? ProfissionalId { get; set; }
}

public sealed class UsuarioEntrada : UsuarioAtualizacao
{
    public string? Senha { get; set; }
    public bool? Ativo { get; set; }
}

public sealed class SenhaEntrada
{
    public string? NovaSenha { get; set; }
}

public sealed class FiltroUsuarios
{
    public string? Busca { get; set; }
    public int? PerfilId { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

// ---------- Saída ----------

public sealed class UsuarioDto
{
    public int Id { get; set; }
    public string NomeUsuario { get; set; } = string.Empty;
    public string NomeCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public bool Ativo { get; set; }
    public PerfilReferenciaDto Perfil { get; set; } = new();
    public int? ProfissionalId { get; set; }
    public string? ProfissionalNome { get; set; }
    public bool Bloqueado { get; set; }
    public DateTime? BloqueadoEm { get; set; }
    public DateTime? UltimoLoginEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class UsuarioPaginaDto
{
    public List<UsuarioDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
