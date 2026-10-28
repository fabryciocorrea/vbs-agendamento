using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Perfis;

/// <summary>Perfis de acesso e liberações por tela e por campo (RF26).</summary>
[ApiController]
[Route("api/v1/perfis")]
public sealed class PerfisController : ControllerBase
{
    private readonly PerfilService _servico;
    public PerfisController(PerfilService servico) => _servico = servico;

    // O cadastro de usuários também precisa da lista de perfis para a caixa de seleção.
    [HttpGet]
    [Permissao("perfil.cadastro:visualizar", "usuario.cadastro:visualizar")]
    public async Task<ActionResult<List<PerfilResumoDto>>> Listar(CancellationToken ct) =>
        Ok(await _servico.ListarAsync(ct));

    [HttpPost]
    [Permissao("perfil.cadastro:incluir")]
    public async Task<ActionResult<PerfilComAvisosDto>> Criar([FromBody] PerfilEntrada? entrada, CancellationToken ct)
    {
        var p = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/perfis/{p.Id}", p);
    }

    [HttpPost("validacao")]
    [Permissao("perfil.cadastro:incluir")]
    public async Task<ActionResult<ValidacaoPerfilDto>> Validar([FromBody] PerfilEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.ValidarAsync(entrada, ct));

    [HttpGet("{perfilId:int}")]
    [Permissao("perfil.cadastro:visualizar")]
    public async Task<ActionResult<PerfilDto>> Obter(int perfilId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(perfilId, ct));

    [HttpPut("{perfilId:int}")]
    [Permissao("perfil.cadastro:editar")]
    public async Task<ActionResult<PerfilComAvisosDto>> Atualizar(int perfilId, [FromBody] PerfilEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(perfilId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpDelete("{perfilId:int}")]
    [Permissao("perfil.cadastro:excluir")]
    public async Task<IActionResult> Excluir(int perfilId, CancellationToken ct)
    {
        await _servico.ExcluirAsync(perfilId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }
}

/// <summary>Catálogo de telas e campos disponíveis para liberação.</summary>
[ApiController]
[Route("api/v1/telas")]
public sealed class TelasController : ControllerBase
{
    private readonly PerfilService _servico;
    public TelasController(PerfilService servico) => _servico = servico;

    [HttpGet]
    [Permissao("perfil.cadastro:visualizar")]
    public async Task<ActionResult<List<TelaDto>>> Listar(CancellationToken ct) =>
        Ok(await _servico.ListarTelasAsync(ct));
}
