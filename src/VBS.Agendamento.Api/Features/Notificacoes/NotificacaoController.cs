using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Notificacoes;

/// <summary>Avisos internos da recepção: o sino da barra do sistema.</summary>
[ApiController]
[Route("api/v1/notificacoes-internas")]
public sealed class NotificacaoController : ControllerBase
{
    private readonly NotificacaoService _servico;
    public NotificacaoController(NotificacaoService servico) => _servico = servico;

    [HttpGet]
    [Permissao("notificacao.interna:visualizar")]
    public async Task<ActionResult<NotificacaoInternaPaginaDto>> Listar([FromQuery] FiltroNotificacao filtro, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await _servico.ListarAsync(filtro, User.ObterUsuarioAtual(), ct));
    }

    [HttpGet("nao-lidas/contagem")]
    [Permissao("notificacao.interna:visualizar")]
    public async Task<ActionResult<ContagemNaoLidasDto>> Contar(CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await _servico.ContarNaoLidasAsync(User.ObterUsuarioAtual(), ct));
    }

    [HttpPatch("{notificacaoId:int}/leitura")]
    [Permissao("notificacao.interna:editar")]
    public async Task<ActionResult<NotificacaoInternaDto>> MarcarLida(int notificacaoId, CancellationToken ct) =>
        Ok(await _servico.MarcarLidaAsync(notificacaoId, User.ObterUsuarioAtual(), ct));
}
