using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Consultas;

/// <summary>Consultas: agendar, listar e consultar (1ª fatia).</summary>
[ApiController]
[Route("api/v1/consultas")]
public sealed class ConsultasController : ControllerBase
{
    private readonly ConsultaService _servico;
    public ConsultasController(ConsultaService servico) => _servico = servico;

    [HttpGet]
    [Permissao("consulta.agendamento:visualizar")]
    public async Task<ActionResult<ConsultaPaginaDto>> Listar([FromQuery] FiltroConsultas filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpPost]
    [Permissao("consulta.agendamento:incluir")]
    public async Task<ActionResult<ConsultaDto>> Agendar([FromBody] ConsultaEntrada? entrada, CancellationToken ct)
    {
        var consulta = await _servico.AgendarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/consultas/{consulta.Id}", consulta);
    }

    [HttpGet("{consultaId:guid}")]
    [Permissao("consulta.agendamento:visualizar")]
    public async Task<ActionResult<ConsultaDto>> Obter(Guid consultaId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(consultaId, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{consultaId:guid}/status")]
    [Permissao("consulta.status:editar")]
    public async Task<ActionResult<ConsultaDto>> AlterarStatus(Guid consultaId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(consultaId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPost("{consultaId:guid}/pagamento/confirmacao")]
    [Permissao("consulta.agendamento:editar")]
    public async Task<ActionResult<ConsultaDto>> ConfirmarPagamento(Guid consultaId, [FromBody] PagamentoConfirmacaoEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.ConfirmarPagamentoAsync(consultaId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPost("{consultaId:guid}/remarcacao")]
    [Permissao("consulta.agendamento:editar")]
    public async Task<ActionResult<ConsultaDto>> Remarcar(Guid consultaId, [FromBody] RemarcacaoEntrada? entrada, CancellationToken ct)
    {
        var nova = await _servico.RemarcarAsync(consultaId, entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/consultas/{nova.Id}", nova);
    }
}
