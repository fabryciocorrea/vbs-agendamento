using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Prontuarios;

/// <summary>Prontuário do paciente (RF12, RN16, RN19). O acesso também depende de ser o profissional responsável ou o administrador (H1).</summary>
[ApiController]
[Route("api/v1/pacientes/{pacienteId:guid}/prontuario")]
public sealed class ProntuarioController : ControllerBase
{
    private readonly ProntuarioService _servico;
    public ProntuarioController(ProntuarioService servico) => _servico = servico;

    private void SemCache() => Response.Headers["Cache-Control"] = "no-store";

    [HttpGet]
    [Permissao("prontuario.registro:visualizar")]
    public async Task<ActionResult<RegistroProntuarioPaginaDto>> Listar(Guid pacienteId, [FromQuery] FiltroProntuario filtro, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ListarAsync(pacienteId, filtro, User.ObterUsuarioAtual(), ct));
    }

    [HttpPost]
    [Permissao("prontuario.registro:incluir")]
    public async Task<ActionResult<RegistroProntuarioDto>> Incluir(Guid pacienteId, [FromBody] RegistroProntuarioEntrada? entrada, CancellationToken ct)
    {
        SemCache();
        var registro = await _servico.IncluirAsync(pacienteId, entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/pacientes/{pacienteId}/prontuario/{registro.Id}", registro);
    }

    [HttpPost("transferencia")]
    [Permissao("prontuario.transferencia:editar")]
    public async Task<ActionResult<TransferenciaProntuarioDto>> Transferir(Guid pacienteId, [FromBody] TransferenciaProntuarioEntrada? entrada, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.TransferirAsync(pacienteId, entrada, User.ObterUsuarioAtual(), ct));
    }

    [HttpPost("autorizacoes")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Permissao("prontuario.registro:editar")]
    public async Task<ActionResult<DocumentoAutorizacaoDto>> EnviarDocumento(Guid pacienteId, IFormFile? arquivo, CancellationToken ct)
    {
        SemCache();
        if (arquivo is null || arquivo.Length == 0) throw VBS.Agendamento.Api.Common.Erro.Validacao("arquivo", "Anexe o documento de autorização");
        if (arquivo.Length > ProntuarioService.TamanhoMaximoDocumento) throw VBS.Agendamento.Api.Common.Erro.Validacao("arquivo", "O documento deve ter no máximo 5 MB");
        byte[] conteudo;
        await using (var fluxo = arquivo.OpenReadStream())
        using (var memoria = new MemoryStream())
        {
            await fluxo.CopyToAsync(memoria, ct);
            conteudo = memoria.ToArray();
        }
        var doc = await _servico.EnviarDocumentoAsync(pacienteId, arquivo.FileName, conteudo, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/pacientes/{pacienteId}/prontuario/autorizacoes/{doc.Id}", doc);
    }

    [HttpGet("autorizacoes/{documentoId:guid}")]
    [Permissao("prontuario.correcao:visualizar")]
    public async Task<IActionResult> BaixarDocumento(Guid pacienteId, Guid documentoId, CancellationToken ct)
    {
        SemCache();
        var (conteudo, nome, tipo) = await _servico.BaixarDocumentoAsync(pacienteId, documentoId, User.ObterUsuarioAtual(), ct);
        return File(conteudo, tipo, nome);
    }

    [HttpGet("{registroId:guid}")]
    [Permissao("prontuario.registro:visualizar")]
    public async Task<ActionResult<RegistroProntuarioDto>> Obter(Guid pacienteId, Guid registroId, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ObterAsync(pacienteId, registroId, User.ObterUsuarioAtual(), ct));
    }

    [HttpPut("{registroId:guid}")]
    [Permissao("prontuario.registro:editar")]
    public async Task<ActionResult<RegistroProntuarioDto>> Editar(Guid pacienteId, Guid registroId, [FromBody] RegistroProntuarioEdicao? entrada, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.EditarAsync(pacienteId, registroId, entrada, User.ObterUsuarioAtual(), ct));
    }

    [HttpGet("{registroId:guid}/correcoes")]
    [Permissao("prontuario.correcao:visualizar")]
    public async Task<ActionResult<List<CorrecaoProntuarioDto>>> Correcoes(Guid pacienteId, Guid registroId, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.CorrecoesAsync(pacienteId, registroId, User.ObterUsuarioAtual(), ct));
    }
}
