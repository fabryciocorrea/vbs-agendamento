using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Faturamento;

/// <summary>Informações financeiras do faturamento: resumo do período e exportação.</summary>
[ApiController]
[Route("api/v1/faturamento")]
public sealed class FinanceiroController : ControllerBase
{
    private readonly FinanceiroService _servico;
    public FinanceiroController(FinanceiroService servico) => _servico = servico;

    [HttpGet("resumo")]
    [Permissao("faturamento.financeiro:visualizar")]
    public async Task<ActionResult<ResumoFinanceiroDto>> Resumo([FromQuery] FiltroFinanceiro filtro, CancellationToken ct) =>
        Ok(await _servico.ResumoAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("particulares")]
    [Permissao("faturamento.financeiro:visualizar")]
    public async Task<ActionResult<ParticularPaginaDto>> Particulares([FromQuery] FiltroParticulares filtro, CancellationToken ct) =>
        Ok(await _servico.ListarParticularesAsync(filtro, ct));

    [HttpGet("exportacao")]
    [Permissao("faturamento.financeiro:exportar")]
    public async Task<IActionResult> Exportar([FromQuery] FiltroFinanceiro filtro, CancellationToken ct)
    {
        var bytes = await _servico.ExportarAsync(filtro, User.ObterUsuarioAtual(), ct);
        return File(bytes, "text/csv; charset=utf-8", $"faturamento_{filtro.DataInicial:yyyyMMdd}_{filtro.DataFinal:yyyyMMdd}.csv");
    }
}
