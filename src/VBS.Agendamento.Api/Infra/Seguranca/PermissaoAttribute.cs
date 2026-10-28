using Microsoft.AspNetCore.Mvc.Filters;
using VBS.Agendamento.Api.Common;

namespace VBS.Agendamento.Api.Infra.Seguranca;

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
