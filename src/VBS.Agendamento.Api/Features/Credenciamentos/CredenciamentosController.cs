using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Credenciamentos;

/// <summary>Bloco 3A do contrato: credenciamento de convênios por unidade e especialidade.</summary>
[ApiController]
[Route("api/v1")]
public sealed class CredenciamentosController : ControllerBase
{
    private readonly CredenciamentoService _servico;
    public CredenciamentosController(CredenciamentoService servico) => _servico = servico;

    [HttpGet("credenciamentos")]
    [Permissao("convenio.credenciamento:visualizar")]
    public async Task<ActionResult<CredenciamentoPaginaDto>> Listar([FromQuery] FiltroCredenciamentos filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpGet("convenios/{convenioId:int}/credenciamentos")]
    [Permissao("convenio.credenciamento:visualizar")]
    public async Task<ActionResult<CredenciamentoPaginaDto>> ListarDoConvenio(int convenioId, [FromQuery] FiltroCredenciamentos filtro, CancellationToken ct) =>
        Ok(await _servico.ListarDoConvenioAsync(convenioId, filtro, ct));

    [HttpGet("convenios/{convenioId:int}/valor-consulta")]
    [Permissao("consulta.agendamento:visualizar")]
    public async Task<ActionResult<ValorConsultaDto>> ObterValor(int convenioId, [FromQuery] int? unidadeId, [FromQuery] int? especialidadeId, [FromQuery] DateOnly? data, CancellationToken ct) =>
        Ok(await _servico.ObterValorAsync(convenioId, unidadeId, especialidadeId, data, ct));

    [HttpPost("convenios/{convenioId:int}/credenciamentos")]
    [Permissao("convenio.credenciamento:incluir")]
    public async Task<ActionResult<CredenciamentoDto>> Incluir(int convenioId, [FromBody] CredenciamentoEntrada? entrada, CancellationToken ct)
    {
        var c = await _servico.IncluirAsync(convenioId, entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/convenios/{convenioId}/credenciamentos/{c.Id}", c);
    }

    [HttpPost("convenios/{convenioId:int}/credenciamentos/lote")]
    [Permissao("convenio.credenciamento:incluir")]
    public async Task<ActionResult<List<CredenciamentoDto>>> IncluirLote(int convenioId, [FromBody] CredenciamentoLoteEntrada? entrada, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await _servico.IncluirLoteAsync(convenioId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPut("convenios/{convenioId:int}/credenciamentos/{credenciamentoId:int}")]
    [Permissao("convenio.credenciamento:editar")]
    public async Task<ActionResult<CredenciamentoDto>> Atualizar(int convenioId, int credenciamentoId, [FromBody] CredenciamentoAtualizacao? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(convenioId, credenciamentoId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("convenios/{convenioId:int}/credenciamentos/{credenciamentoId:int}/status")]
    [Permissao("convenio.credenciamento:editar")]
    public async Task<ActionResult<CredenciamentoDto>> AlterarStatus(int convenioId, int credenciamentoId, [FromBody] CredenciamentoStatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(convenioId, credenciamentoId, entrada, User.ObterUsuarioAtual(), ct));
}
