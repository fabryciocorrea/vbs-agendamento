using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Colaboradores;

/// <summary>Bloco 4 do contrato: colaboradores (CLT) e prestadores (MEI).</summary>
[ApiController]
[Route("api/v1/colaboradores")]
public sealed class ColaboradoresController : ControllerBase
{
    private readonly ColaboradorService _servico;
    public ColaboradoresController(ColaboradorService servico) => _servico = servico;

    [HttpGet]
    [Permissao("colaborador.cadastro:visualizar")]
    public async Task<ActionResult<ColaboradorPaginaDto>> Listar([FromQuery] FiltroColaboradores filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("colaborador.cadastro:incluir")]
    public async Task<ActionResult<ColaboradorDto>> Criar([FromBody] ColaboradorEntrada? entrada, CancellationToken ct)
    {
        var c = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/colaboradores/{c.Id}", c);
    }

    [HttpGet("{colaboradorId:int}")]
    [Permissao("colaborador.cadastro:visualizar")]
    public async Task<ActionResult<ColaboradorDto>> Obter(int colaboradorId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(colaboradorId, ct));

    [HttpPut("{colaboradorId:int}")]
    [Permissao("colaborador.cadastro:editar")]
    public async Task<ActionResult<ColaboradorDto>> Atualizar(int colaboradorId, [FromBody] ColaboradorEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(colaboradorId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{colaboradorId:int}/status")]
    [Permissao("colaborador.cadastro:editar")]
    public async Task<ActionResult<ColaboradorDto>> AlterarStatus(int colaboradorId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(colaboradorId, entrada, User.ObterUsuarioAtual(), ct));
}
