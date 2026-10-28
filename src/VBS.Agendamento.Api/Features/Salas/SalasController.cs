using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Salas;

/// <summary>Bloco 4 do contrato: salas.</summary>
[ApiController]
[Route("api/v1/salas")]
public sealed class SalasController : ControllerBase
{
    private readonly SalaService _servico;
    public SalasController(SalaService servico) => _servico = servico;

    [HttpGet]
    [Permissao("sala.cadastro:visualizar")]
    public async Task<ActionResult<SalaPaginaDto>> Listar([FromQuery] FiltroSalas filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("sala.cadastro:incluir")]
    public async Task<ActionResult<SalaDto>> Criar([FromBody] SalaEntrada? entrada, CancellationToken ct)
    {
        var sala = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/salas/{sala.Id}", sala);
    }

    [HttpGet("{salaId:int}")]
    [Permissao("sala.cadastro:visualizar")]
    public async Task<ActionResult<SalaDto>> Obter(int salaId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(salaId, ct));

    [HttpPut("{salaId:int}")]
    [Permissao("sala.cadastro:editar")]
    public async Task<ActionResult<SalaDto>> Atualizar(int salaId, [FromBody] SalaEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(salaId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{salaId:int}/status")]
    [Permissao("sala.cadastro:editar")]
    public async Task<ActionResult<SalaDto>> AlterarStatus(int salaId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(salaId, entrada, User.ObterUsuarioAtual(), ct));
}
