using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Dashboards;

/// <summary>Dashboards (RF15): painel (dia e período) e painel financeiro.</summary>
[ApiController]
[Route("api/v1/dashboards")]
public sealed class DashboardController : ControllerBase
{
    private readonly DashboardService _servico;
    private readonly IPermissaoService _permissoes;

    public DashboardController(DashboardService servico, IPermissaoService permissoes)
    {
        _servico = servico;
        _permissoes = permissoes;
    }

    [HttpGet("painel-do-dia")]
    [Permissao("dashboard.painel:visualizar")]
    public async Task<ActionResult<PainelDoDiaDto>> PainelDoDia([FromQuery] FiltroPainel filtro, CancellationToken ct)
    {
        var u = User.ObterUsuarioAtual();
        // Guias e lista de espera só aparecem para quem tem acesso a essas telas.
        var verFaturamento = await _permissoes.TemAsync(u.PerfilId, u.Administrador, "faturamento.financeiro:visualizar", ct);
        var verListaEspera = await _permissoes.TemAsync(u.PerfilId, u.Administrador, "listaespera.cadastro:visualizar", ct);
        return Ok(await _servico.ObterPainelDoDiaAsync(filtro, u, verFaturamento, verListaEspera, ct));
    }

    [HttpGet("financeiro")]
    [Permissao("dashboard.financeiro:visualizar")]
    public async Task<ActionResult<PainelFinanceiroDto>> Financeiro([FromQuery] FiltroPainelFinanceiro filtro, CancellationToken ct) =>
        Ok(await _servico.ObterPainelFinanceiroAsync(filtro, User.ObterUsuarioAtual(), ct));
}
