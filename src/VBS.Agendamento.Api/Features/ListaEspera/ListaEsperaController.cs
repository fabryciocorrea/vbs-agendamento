using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.ListaEspera;

/// <summary>Lista de espera (RN08, G9): fila de pacientes por profissional ou especialidade.</summary>
[ApiController]
[Route("api/v1/lista-espera")]
public sealed class ListaEsperaController : ControllerBase
{
    private readonly ListaEsperaService _servico;
    private readonly IPermissaoService _permissoes;
    public ListaEsperaController(ListaEsperaService servico, IPermissaoService permissoes)
    {
        _servico = servico;
        _permissoes = permissoes;
    }

    [HttpGet]
    [Permissao("listaespera.cadastro:visualizar")]
    public async Task<ActionResult<ListaEsperaPaginaDto>> Listar([FromQuery] FiltroListaEspera filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpPost]
    [Permissao("listaespera.cadastro:incluir")]
    public async Task<ActionResult<ListaEsperaDto>> Incluir([FromBody] ListaEsperaEntrada? entrada, CancellationToken ct)
    {
        var item = await _servico.IncluirAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/lista-espera/{item.Id}", item);
    }

    [HttpPatch("{listaId:int}/status")]
    [Permissao("listaespera.cadastro:editar", "listaespera.cadastro:cancelar")]
    public async Task<ActionResult<ListaEsperaDto>> AlterarStatus(int listaId, [FromBody] ListaEsperaStatusEntrada? entrada, CancellationToken ct)
    {
        // Cancelar o item da fila tem liberação própria; encerrar como atendido ou voltar para a fila usam "editar".
        var usuario = User.ObterUsuarioAtual();
        await _permissoes.ExigirAsync(usuario, entrada?.Status == "CANCELADO" ? "listaespera.cadastro:cancelar" : "listaespera.cadastro:editar", ct);
        return Ok(await _servico.AlterarStatusAsync(listaId, entrada, usuario, ct));
    }
}
