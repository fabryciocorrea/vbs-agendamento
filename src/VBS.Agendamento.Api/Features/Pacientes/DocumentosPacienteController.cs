using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>Documentos do paciente (carteira de identidade, encaminhamento, autorização...). Tela de permissão: paciente.documento.</summary>
[ApiController]
[Route("api/v1/pacientes/{pacienteId:guid}/documentos")]
public sealed class DocumentosPacienteController : ControllerBase
{
    private readonly DocumentoPacienteService _servico;
    public DocumentosPacienteController(DocumentoPacienteService servico) => _servico = servico;

    private void SemCache() => Response.Headers["Cache-Control"] = "no-store";

    [HttpGet]
    [Permissao("paciente.documento:visualizar")]
    public async Task<ActionResult<List<DocumentoPacienteDto>>> Listar(Guid pacienteId, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ListarAsync(pacienteId, User.ObterUsuarioAtual(), ct));
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Permissao("paciente.documento:incluir")]
    public async Task<ActionResult<DocumentoPacienteDto>> Enviar(
        Guid pacienteId, IFormFile? arquivo, [FromForm] string? tipo, [FromForm] string? descricao, CancellationToken ct)
    {
        SemCache();
        if (arquivo is null || arquivo.Length == 0) throw Erro.Validacao("arquivo", "Anexe o documento");
        if (arquivo.Length > DocumentoPacienteService.TamanhoMaximo) throw Erro.Validacao("arquivo", "O documento deve ter no máximo 5 MB");
        byte[] conteudo;
        await using (var fluxo = arquivo.OpenReadStream())
        using (var memoria = new MemoryStream())
        {
            await fluxo.CopyToAsync(memoria, ct);
            conteudo = memoria.ToArray();
        }
        var doc = await _servico.EnviarAsync(pacienteId, tipo, descricao, arquivo.FileName, conteudo, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/pacientes/{pacienteId}/documentos/{doc.Id}", doc);
    }

    [HttpGet("{documentoId:guid}")]
    [Permissao("paciente.documento:visualizar")]
    public async Task<IActionResult> Baixar(Guid pacienteId, Guid documentoId, CancellationToken ct)
    {
        SemCache();
        var (conteudo, nome, tipo) = await _servico.BaixarAsync(pacienteId, documentoId, User.ObterUsuarioAtual(), ct);
        return File(conteudo, tipo, nome);
    }

    [HttpDelete("{documentoId:guid}")]
    [Permissao("paciente.documento:excluir")]
    public async Task<IActionResult> Excluir(Guid pacienteId, Guid documentoId, CancellationToken ct)
    {
        SemCache();
        await _servico.ExcluirAsync(pacienteId, documentoId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }
}
