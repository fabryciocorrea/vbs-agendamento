using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Agenda;

/// <summary>Parâmetros da agenda, feriados, horários disponíveis e agenda do profissional.</summary>
[ApiController]
[Route("api/v1")]
public sealed class AgendaController : ControllerBase
{
    private readonly AgendaService _servico;
    public AgendaController(AgendaService servico) => _servico = servico;

    [HttpGet("parametros-agenda")]
    [Permissao("agenda.parametros:visualizar")]
    public async Task<ActionResult<ParametrosAgendaDto>> ObterParametros(CancellationToken ct) =>
        Ok(await _servico.ObterParametrosAsync(ct));

    [HttpPut("parametros-agenda")]
    [Permissao("agenda.parametros:editar")]
    public async Task<ActionResult<ParametrosAgendaDto>> AtualizarParametros([FromBody] ParametrosAgendaDto? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarParametrosAsync(entrada, User.ObterUsuarioAtual(), ct));

    [HttpGet("feriados")]
    [Permissao("agenda.parametros:visualizar")]
    public async Task<ActionResult> ListarFeriados([FromQuery] int? ano, CancellationToken ct) =>
        Ok(await _servico.ListarFeriadosAsync(ano, ct));

    [HttpPost("feriados")]
    [Permissao("agenda.parametros:editar")]
    public async Task<ActionResult<FeriadoDto>> IncluirFeriado([FromBody] FeriadoDto? entrada, CancellationToken ct)
    {
        var feriado = await _servico.IncluirFeriadoAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/feriados/{feriado.Data:yyyy-MM-dd}", feriado);
    }

    [HttpDelete("feriados/{data}")]
    [Permissao("agenda.parametros:editar")]
    public async Task<IActionResult> RemoverFeriado(string data, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(data, "yyyy-MM-dd", out var d))
            throw Common.Erro.Validacao("data", "Use o formato aaaa-mm-dd");
        await _servico.RemoverFeriadoAsync(d, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpGet("agenda/horarios-disponiveis")]
    [Permissao("consulta.agendamento:visualizar")]
    public async Task<ActionResult> ListarHorarios([FromQuery] FiltroHorarios filtro, CancellationToken ct) =>
        Ok(await _servico.ListarHorariosAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("agenda/geral")]
    [Permissao("agenda.profissional:visualizar")]
    public async Task<ActionResult<AgendaSaidaDto>> ObterAgendaGeral([FromQuery] FiltroAgenda filtro, CancellationToken ct) =>
        Ok(await _servico.ObterAgendaGeralAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("agenda/profissionais/{profissionalId:int}")]
    [Permissao("agenda.profissional:visualizar")]
    public async Task<ActionResult<AgendaSaidaDto>> ObterAgenda(int profissionalId, [FromQuery] FiltroAgenda filtro, CancellationToken ct) =>
        Ok(await _servico.ObterAgendaAsync(profissionalId, filtro, User.ObterUsuarioAtual(), ct));
}
