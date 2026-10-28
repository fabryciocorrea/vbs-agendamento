using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Auth;

/// <summary>Bloco 1 do contrato: autenticação (x-permissao: pública ou logado).</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _servico;
    public AuthController(AuthService servico) => _servico = servico;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<SessaoSaida>> Login([FromBody] LoginEntrada? entrada, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        return Ok(await _servico.LoginAsync(entrada, ip, ct));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _servico.LogoutAsync(User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpGet("eu")]
    public async Task<ActionResult<UsuarioAtualSaida>> ObterUsuarioAtual(CancellationToken ct) =>
        Ok(await _servico.ObterAtualAsync(User.ObterUsuarioAtual(), ct));

    [HttpPost("recuperar-senha")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> RecuperarSenha([FromBody] RecuperarSenhaEntrada? entrada, CancellationToken ct)
    {
        await _servico.RecuperarSenhaAsync(entrada, ct);
        return Accepted();
    }

    [HttpPost("redefinir-senha")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> RedefinirSenha([FromBody] RedefinirSenhaEntrada? entrada, CancellationToken ct)
    {
        await _servico.RedefinirSenhaAsync(entrada, ct);
        return NoContent();
    }
}
