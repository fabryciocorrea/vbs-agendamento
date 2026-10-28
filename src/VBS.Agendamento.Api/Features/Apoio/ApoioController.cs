using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Apoio;

/// <summary>Listas de apoio para as caixas de seleção (profissionais, unidades, salas, especialidades, convênios, formas de pagamento).</summary>
[ApiController]
[Route("api/v1/apoio")]
public sealed class ApoioController : ControllerBase
{
    private readonly ApoioService _servico;
    public ApoioController(ApoioService servico) => _servico = servico;

    [HttpGet("{tipo}")]
    [Permissao("apoio.listas:visualizar")]
    public async Task<ActionResult<List<ItemApoioDto>>> Listar(string tipo, [FromQuery] FiltroApoio filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(tipo, filtro, ct));
}
