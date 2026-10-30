using Microsoft.AspNetCore.Mvc.Filters;
using VBS.Agendamento.Api.Common;

namespace VBS.Agendamento.Api.Infra.Seguranca;

public static class PermissaoExtensoes
{
    /// <summary>Confere uma exigência "tela:ação" dentro da ação do controller (403 quando o perfil não tem).</summary>
    public static async Task ExigirAsync(this IPermissaoService servico, UsuarioAtual usuario, string exigencia, CancellationToken ct)
    {
        if (!await servico.TemAsync(usuario.PerfilId, usuario.Administrador, exigencia, ct)) throw Erro.SemPermissao();
    }
}

/// <summary>
/// Exige a liberação do contrato (x-permissao) em cada requisição (RNF08).
/// Uso: [Permissao("paciente.cadastro:visualizar")].
/// Com mais de uma exigência, basta uma delas: [Permissao("a.tela:incluir", "a.tela:editar")].
/// A autenticação (401) já foi conferida antes; aqui só decide 403.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class PermissaoAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _exigencias;

    public PermissaoAttribute(params string[] exigencias) => _exigencias = exigencias;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexto)
    {
        var http = contexto.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true) return;

        var usuario = http.User.ObterUsuarioAtual();
        var servico = http.RequestServices.GetRequiredService<IPermissaoService>();

        foreach (var exigencia in _exigencias)
        {
            if (await servico.TemAsync(usuario.PerfilId, usuario.Administrador, exigencia, http.RequestAborted))
                return;
        }

        throw Erro.SemPermissao();
    }
}
