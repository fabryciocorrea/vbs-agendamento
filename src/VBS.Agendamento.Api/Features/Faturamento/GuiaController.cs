using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Faturamento;

/// <summary>Faturamento das guias de convênio: o plano aprova a guia e o faturamento confirma aqui.</summary>
[ApiController]
[Route("api/v1")]
public sealed class GuiaController : ControllerBase
{
    private readonly GuiaService _servico;
    public GuiaController(GuiaService servico) => _servico = servico;

    [HttpGet("faturamento/guias")]
    [Permissao("faturamento.financeiro:visualizar")]
    public async Task<ActionResult<GuiaPaginaDto>> Listar([FromQuery] FiltroGuias filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPatch("consultas/{consultaId:guid}/guia")]
    [Permissao("faturamento.financeiro:editar")]
    public async Task<ActionResult<GuiaItemDto>> Atualizar(Guid consultaId, [FromBody] GuiaEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(consultaId, entrada, User.ObterUsuarioAtual(), ct));
}
