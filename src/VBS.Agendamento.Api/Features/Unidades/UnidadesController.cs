using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Unidades;

/// <summary>Bloco 3 do contrato: unidades.</summary>
[ApiController]
[Route("api/v1/unidades")]
public sealed class UnidadesController : ControllerBase
{
    private readonly UnidadeService _servico;
    public UnidadesController(UnidadeService servico) => _servico = servico;

    [HttpGet]
    [Permissao("unidade.cadastro:visualizar")]
    public async Task<ActionResult<UnidadePaginaDto>> Listar([FromQuery] FiltroUnidades filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("unidade.cadastro:incluir")]
    public async Task<ActionResult<UnidadeDto>> Criar([FromBody] UnidadeEntrada? entrada, CancellationToken ct)
    {
        var unidade = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/unidades/{unidade.Id}", unidade);
    }

    [HttpGet("{unidadeId:int}")]
    [Permissao("unidade.cadastro:visualizar")]
    public async Task<ActionResult<UnidadeDto>> Obter(int unidadeId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(unidadeId, ct));

    [HttpPut("{unidadeId:int}")]
    [Permissao("unidade.cadastro:editar")]
    public async Task<ActionResult<UnidadeDto>> Atualizar(int unidadeId, [FromBody] UnidadeEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(unidadeId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{unidadeId:int}/status")]
    [Permissao("unidade.cadastro:editar")]
    public async Task<ActionResult<UnidadeDto>> AlterarStatus(int unidadeId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(unidadeId, entrada, User.ObterUsuarioAtual(), ct));
}
