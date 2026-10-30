using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Faturamento;

/// <summary>Faturamento das guias de convênio: o plano aprova a guia e o faturamento confirma aqui.</summary>
[ApiController]
[Route("api/v1")]
public sealed class GuiaController : ControllerBase
{
    private readonly GuiaService _servico;
    private readonly IPermissaoService _permissoes;
    public GuiaController(GuiaService servico, IPermissaoService permissoes)
    {
        _servico = servico;
        _permissoes = permissoes;
    }

    [HttpGet("faturamento/guias")]
    [Permissao("faturamento.financeiro:visualizar")]
    public async Task<ActionResult<GuiaPaginaDto>> Listar([FromQuery] FiltroGuias filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPatch("consultas/{consultaId:guid}/guia")]
    [Permissao("faturamento.financeiro:editar", "faturamento.financeiro:cancelar")]
    public async Task<ActionResult<GuiaItemDto>> Atualizar(Guid consultaId, [FromBody] GuiaEntrada? entrada, CancellationToken ct)
    {
        // Cancelar a guia tem liberação própria; as demais situações usam "editar".
        var usuario = User.ObterUsuarioAtual();
        await _permissoes.ExigirAsync(usuario, entrada?.Status == "CANCELADA" ? "faturamento.financeiro:cancelar" : "faturamento.financeiro:editar", ct);
        return Ok(await _servico.AtualizarAsync(consultaId, entrada, usuario, ct));
    }
}
