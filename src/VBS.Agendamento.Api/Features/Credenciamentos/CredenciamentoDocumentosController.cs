using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Credenciamentos;

/// <summary>Documentos anexados ao credenciamento de convênios. Tela de permissão: credenciamento.documento.</summary>
[ApiController]
[Route("api/v1/convenios/{convenioId:int}/credenciamentos/{credenciamentoId:int}/documentos")]
public sealed class CredenciamentoDocumentosController : ControllerBase
{
    private readonly CredenciamentoDocumentoService _servico;
    public CredenciamentoDocumentosController(CredenciamentoDocumentoService servico) => _servico = servico;

    private void SemCache() => Response.Headers["Cache-Control"] = "no-store";

    [HttpGet]
    [Permissao("credenciamento.documento:visualizar")]
    public async Task<ActionResult<List<CredenciamentoDocumentoDto>>> Listar(int convenioId, int credenciamentoId, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ListarAsync(convenioId, credenciamentoId, ct));
    }

    [HttpPost]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [Permissao("credenciamento.documento:incluir")]
    public async Task<ActionResult<CredenciamentoDocumentoDto>> Enviar(
        int convenioId, int credenciamentoId, IFormFile? arquivo, [FromForm] string? tipo, [FromForm] string? descricao, CancellationToken ct)
    {
        SemCache();
        if (arquivo is null || arquivo.Length == 0) throw Erro.Validacao("arquivo", "Anexe o documento");
        if (arquivo.Length > CredenciamentoDocumentoService.TamanhoMaximo) throw Erro.Validacao("arquivo", "O documento deve ter no máximo 10 MB");
        byte[] conteudo;
        await using (var fluxo = arquivo.OpenReadStream())
        using (var memoria = new MemoryStream())
        {
            await fluxo.CopyToAsync(memoria, ct);
            conteudo = memoria.ToArray();
        }
        var doc = await _servico.EnviarAsync(convenioId, credenciamentoId, tipo, descricao, arquivo.FileName, conteudo, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/convenios/{convenioId}/credenciamentos/{credenciamentoId}/documentos/{doc.Id}", doc);
    }

    [HttpGet("{documentoId:guid}")]
    [Permissao("credenciamento.documento:visualizar")]
    public async Task<IActionResult> Baixar(int convenioId, int credenciamentoId, Guid documentoId, CancellationToken ct)
    {
        SemCache();
        var (conteudo, nome, tipo) = await _servico.BaixarAsync(convenioId, credenciamentoId, documentoId, User.ObterUsuarioAtual(), ct);
        return File(conteudo, tipo, nome);
    }

    [HttpDelete("{documentoId:guid}")]
    [Permissao("credenciamento.documento:excluir")]
    public async Task<IActionResult> Excluir(int convenioId, int credenciamentoId, Guid documentoId, CancellationToken ct)
    {
        SemCache();
        await _servico.ExcluirAsync(convenioId, credenciamentoId, documentoId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }
}
