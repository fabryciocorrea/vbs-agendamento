using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Convenios;

/// <summary>Bloco 3 do contrato: convênios.</summary>
[ApiController]
[Route("api/v1/convenios")]
public sealed class ConveniosController : ControllerBase
{
    private readonly ConvenioService _servico;
    public ConveniosController(ConvenioService servico) => _servico = servico;

    [HttpGet]
    [Permissao("convenio.cadastro:visualizar")]
    public async Task<ActionResult<ConvenioPaginaDto>> Listar([FromQuery] FiltroConvenios filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("convenio.cadastro:incluir")]
    public async Task<ActionResult<ConvenioDto>> Criar([FromBody] ConvenioEntrada? entrada, CancellationToken ct)
    {
        var c = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/convenios/{c.Id}", c);
    }

    [HttpGet("{convenioId:int}")]
    [Permissao("convenio.cadastro:visualizar")]
    public async Task<ActionResult<ConvenioDto>> Obter(int convenioId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(convenioId, ct));

    [HttpPut("{convenioId:int}")]
    [Permissao("convenio.cadastro:editar")]
    public async Task<ActionResult<ConvenioDto>> Atualizar(int convenioId, [FromBody] ConvenioEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(convenioId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{convenioId:int}/status")]
    [Permissao("convenio.cadastro:editar")]
    public async Task<ActionResult<ConvenioDto>> AlterarStatus(int convenioId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(convenioId, entrada, User.ObterUsuarioAtual(), ct));
}
