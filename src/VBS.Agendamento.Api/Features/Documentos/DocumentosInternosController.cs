using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Documentos;

/// <summary>Documentos internos da empresa (folha timbrada, termos...). Tela de permissão: documento.interno.</summary>
[ApiController]
[Route("api/v1/documentos-internos")]
public sealed class DocumentosInternosController : ControllerBase
{
    private readonly DocumentoInternoService _servico;
    public DocumentosInternosController(DocumentoInternoService servico) => _servico = servico;

    private void SemCache() => Response.Headers["Cache-Control"] = "no-store";

    [HttpGet]
    [Permissao("documento.interno:visualizar")]
    public async Task<ActionResult<List<DocumentoInternoDto>>> Listar([FromQuery] string? tipo, [FromQuery] string? busca, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ListarAsync(tipo, busca, ct));
    }

    [HttpPost]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [Permissao("documento.interno:incluir")]
    public async Task<ActionResult<DocumentoInternoDto>> Enviar(
        IFormFile? arquivo, [FromForm] string? tipo, [FromForm] string? titulo, [FromForm] string? descricao, CancellationToken ct)
    {
        SemCache();
        if (arquivo is null || arquivo.Length == 0) throw Erro.Validacao("arquivo", "Anexe o documento");
        if (arquivo.Length > DocumentoInternoService.TamanhoMaximo) throw Erro.Validacao("arquivo", "O documento deve ter no máximo 10 MB");
        byte[] conteudo;
        await using (var fluxo = arquivo.OpenReadStream())
        using (var memoria = new MemoryStream())
        {
            await fluxo.CopyToAsync(memoria, ct);
            conteudo = memoria.ToArray();
        }
        var doc = await _servico.EnviarAsync(tipo, titulo, descricao, arquivo.FileName, conteudo, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/documentos-internos/{doc.Id}", doc);
    }

    [HttpGet("{documentoId:guid}")]
    [Permissao("documento.interno:visualizar")]
    public async Task<IActionResult> Baixar(Guid documentoId, CancellationToken ct)
    {
        SemCache();
        var (conteudo, nome, tipo) = await _servico.BaixarAsync(documentoId, User.ObterUsuarioAtual(), ct);
        return File(conteudo, tipo, nome);
    }

    [HttpDelete("{documentoId:guid}")]
    [Permissao("documento.interno:excluir")]
    public async Task<IActionResult> Excluir(Guid documentoId, CancellationToken ct)
    {
        SemCache();
        await _servico.ExcluirAsync(documentoId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }
}
