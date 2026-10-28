using System.Security.Claims;

namespace VBS.Agendamento.Api.Infra.Seguranca;

/// <summary>Quem está fazendo a requisição, montado a partir da sessão validada.</summary>
public sealed record UsuarioAtual(int Id, long SessaoId, int PerfilId, bool Administrador, int? ProfissionalId)
{
    /// <summary>RN20: usuário vinculado a um profissional é tratado como médico.</summary>
    public bool EhMedico => ProfissionalId.HasValue;
}

public static class UsuarioAtualExtensoes
{
    public const string ClaimUsuario = "vbs:uid";
    public const string ClaimSessao = "vbs:sid";
    public const string ClaimPerfil = "vbs:pid";
    public const string ClaimAdministrador = "vbs:adm";
    public const string ClaimProfissional = "vbs:prof";

    public static UsuarioAtual ObterUsuarioAtual(this ClaimsPrincipal principal)
    {
        string? Valor(string tipo) => principal.FindFirst(tipo)?.Value;

        if (!int.TryParse(Valor(ClaimUsuario), out var id) ||
            !long.TryParse(Valor(ClaimSessao), out var sessao) ||
            !int.TryParse(Valor(ClaimPerfil), out var perfil))
        {
            throw new InvalidOperationException("Requisição sem usuário autenticado.");
        }

        int? profissional = int.TryParse(Valor(ClaimProfissional), out var p) ? p : null;
        return new UsuarioAtual(id, sessao, perfil, Valor(ClaimAdministrador) == "1", profissional);
    }
}
