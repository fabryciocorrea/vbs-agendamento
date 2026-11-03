using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Relatorios;

/// <summary>Relatórios operacionais, financeiros e de lista de espera (RF17 a RF19) e a tabela de exportação (RF23).</summary>
[ApiController]
[Route("api/v1/relatorios")]
public sealed class RelatoriosController : ControllerBase
{
    private readonly RelatorioService _servico;
    private readonly IPermissaoService _permissoes;

    public RelatoriosController(RelatorioService servico, IPermissaoService permissoes)
    {
        _servico = servico;
        _permissoes = permissoes;
    }

    [HttpGet("consultas-por-profissional")]
    [Permissao("relatorio.consultas_profissional:visualizar")]
    public async Task<ActionResult<RelatorioConsultasPorProfissionalDto>> ConsultasPorProfissional([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.ConsultasPorProfissionalAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("ocupacao-agenda")]
    [Permissao("relatorio.ocupacao:visualizar")]
    public async Task<ActionResult<RelatorioOcupacaoDto>> Ocupacao([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.OcupacaoAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("cancelamentos-faltas")]
    [Permissao("relatorio.cancelamentos_faltas:visualizar")]
    public async Task<ActionResult<RelatorioCancelamentosFaltasDto>> CancelamentosFaltas([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.CancelamentosFaltasAsync(filtro, User.ObterUsuarioAtual(), ct));

    /// <summary>
    /// Relatórios cadastrais (pacientes, profissionais, colaboradores, convênios e credenciamentos), com todos os dados e o CPF completo.
    /// Cada um tem a sua permissão de visualização.
    /// </summary>
    [HttpGet("cadastros/{tipo}")]
    public async Task<ActionResult<RelatorioCadastroDto>> Cadastro(string tipo, [FromQuery] FiltroRelatorio filtro, CancellationToken ct)
    {
        var u = User.ObterUsuarioAtual();
        if (!RelatorioService.PermissaoDeVisualizacao.TryGetValue("cadastro-" + tipo, out var exigencia))
            throw Erro.NaoEncontrado("Relatório não encontrado.", "RELATORIO_NAO_ENCONTRADO");
        await _permissoes.ExigirAsync(u, exigencia, ct);
        return Ok(await _servico.CadastroAsync(tipo, filtro, u, ct));
    }

    [HttpGet("faturamento-por-tipo")]
    [Permissao("relatorio.faturamento_tipo:visualizar")]
    public async Task<ActionResult<RelatorioFaturamentoDto>> FaturamentoPorTipo([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.FaturamentoPorTipoAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("faturamento-por-convenio")]
    [Permissao("relatorio.faturamento_convenio:visualizar")]
    public async Task<ActionResult<RelatorioFaturamentoDto>> FaturamentoPorConvenio([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.FaturamentoPorConvenioAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("faturamento-particular")]
    [Permissao("relatorio.faturamento_particular:visualizar")]
    public async Task<ActionResult<RelatorioFaturamentoDto>> FaturamentoParticular([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.FaturamentoParticularAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("faturamento-mensal")]
    [Permissao("relatorio.faturamento_mensal:visualizar")]
    public async Task<ActionResult<RelatorioFaturamentoMensalDto>> FaturamentoMensal([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.FaturamentoMensalAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("lista-espera")]
    [Permissao("relatorio.lista_espera:visualizar")]
    public async Task<ActionResult<RelatorioListaEsperaDto>> ListaEspera([FromQuery] FiltroRelatorio filtro, CancellationToken ct) =>
        Ok(await _servico.ListaEsperaAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpGet("prontuarios")]
    [Permissao("relatorio.prontuarios:visualizar")]
    public async Task<ActionResult<RelatorioProntuariosDto>> Prontuarios([FromQuery] FiltroRelatorio filtro, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await _servico.ProntuariosAsync(filtro, User.ObterUsuarioAtual(), ct));
    }

    /// <summary>
    /// Devolve a tabela do relatório já formatada; o navegador a transforma em PDF ou CSV.
    /// Exige a exportação e também a visualização do relatório escolhido.
    /// </summary>
    [HttpGet("{relatorio}/exportacao")]
    [Permissao("relatorio.exportacao:exportar")]
    public async Task<ActionResult<TabelaExportacao>> Exportar(string relatorio, [FromQuery] FiltroRelatorio filtro, CancellationToken ct)
    {
        var u = User.ObterUsuarioAtual();
        if (!RelatorioService.PermissaoDeVisualizacao.TryGetValue(relatorio, out var exigencia))
            throw Erro.NaoEncontrado("Relatório não disponível para exportação.", "RELATORIO_NAO_ENCONTRADO");
        await _permissoes.ExigirAsync(u, exigencia, ct);
        return Ok(await _servico.ExportarAsync(relatorio, filtro, u, ct));
    }
}
