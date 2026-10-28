using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.FormasPagamento;

/// <summary>Bloco 3 do contrato: formas de pagamento.</summary>
[ApiController]
[Route("api/v1/formas-pagamento")]
public sealed class FormasPagamentoController : ControllerBase
{
    private readonly FormaPagamentoService _servico;
    public FormasPagamentoController(FormaPagamentoService servico) => _servico = servico;

    [HttpGet]
    [Permissao("formapagamento.cadastro:visualizar")]
    public async Task<ActionResult<FormaPagamentoPaginaDto>> Listar([FromQuery] FiltroFormasPagamento filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("formapagamento.cadastro:incluir")]
    public async Task<ActionResult<FormaPagamentoDto>> Criar([FromBody] FormaPagamentoEntrada? entrada, CancellationToken ct)
    {
        var f = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/formas-pagamento/{f.Id}", f);
    }

    [HttpGet("{formaPagamentoId:int}")]
    [Permissao("formapagamento.cadastro:visualizar")]
    public async Task<ActionResult<FormaPagamentoDto>> Obter(int formaPagamentoId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(formaPagamentoId, ct));

    [HttpPut("{formaPagamentoId:int}")]
    [Permissao("formapagamento.cadastro:editar")]
    public async Task<ActionResult<FormaPagamentoDto>> Atualizar(int formaPagamentoId, [FromBody] FormaPagamentoEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(formaPagamentoId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{formaPagamentoId:int}/status")]
    [Permissao("formapagamento.cadastro:editar")]
    public async Task<ActionResult<FormaPagamentoDto>> AlterarStatus(int formaPagamentoId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(formaPagamentoId, entrada, User.ObterUsuarioAtual(), ct));
}
