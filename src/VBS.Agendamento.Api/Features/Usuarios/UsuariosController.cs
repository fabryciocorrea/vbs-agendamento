using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Usuarios;

/// <summary>Cadastro de usuários do sistema.</summary>
[ApiController]
[Route("api/v1/usuarios")]
public sealed class UsuariosController : ControllerBase
{
    private readonly UsuarioService _servico;
    public UsuariosController(UsuarioService servico) => _servico = servico;

    [HttpGet]
    [Permissao("usuario.cadastro:visualizar")]
    public async Task<ActionResult<UsuarioPaginaDto>> Listar([FromQuery] FiltroUsuarios filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("usuario.cadastro:incluir")]
    public async Task<ActionResult<UsuarioDto>> Criar([FromBody] UsuarioEntrada? entrada, CancellationToken ct)
    {
        var u = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/usuarios/{u.Id}", u);
    }

    [HttpGet("{usuarioId:int}")]
    [Permissao("usuario.cadastro:visualizar")]
    public async Task<ActionResult<UsuarioDto>> Obter(int usuarioId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(usuarioId, ct));

    [HttpPut("{usuarioId:int}")]
    [Permissao("usuario.cadastro:editar")]
    public async Task<ActionResult<UsuarioDto>> Atualizar(int usuarioId, [FromBody] UsuarioAtualizacao? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(usuarioId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{usuarioId:int}/status")]
    [Permissao("usuario.cadastro:editar")]
    public async Task<ActionResult<UsuarioDto>> AlterarStatus(int usuarioId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(usuarioId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPost("{usuarioId:int}/desbloquear")]
    [Permissao("usuario.cadastro:editar")]
    public async Task<IActionResult> Desbloquear(int usuarioId, CancellationToken ct)
    {
        await _servico.DesbloquearAsync(usuarioId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpPut("{usuarioId:int}/senha")]
    [Permissao("usuario.cadastro:editar")]
    public async Task<IActionResult> RedefinirSenha(int usuarioId, [FromBody] SenhaEntrada? entrada, CancellationToken ct)
    {
        await _servico.RedefinirSenhaAsync(usuarioId, entrada, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }
}
