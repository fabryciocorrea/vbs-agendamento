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
    // As listas de apoio (profissionais, unidades, formas de pagamento etc.) alimentam as telas de agenda, pacientes, convênios e faturamento:
    // quem tem acesso a uma dessas telas consegue ler as listas, mesmo sem a tela "Listas de apoio".
    [Permissao("apoio.listas:visualizar", "agenda.profissional:visualizar", "consulta.agendamento:visualizar", "consulta.agendamento:incluir",
        "paciente.cadastro:visualizar", "convenio.cadastro:visualizar", "convenio.credenciamento:visualizar", "faturamento.financeiro:visualizar")]
    public async Task<ActionResult<List<ItemApoioDto>>> Listar(string tipo, [FromQuery] FiltroApoio filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(tipo, filtro, ct));
}
