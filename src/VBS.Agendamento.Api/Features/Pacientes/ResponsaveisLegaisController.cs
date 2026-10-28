using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>Responsável legal do paciente menor de idade (contrato: /responsaveis-legais).</summary>
[ApiController]
[Route("api/v1/responsaveis-legais")]
public sealed class ResponsaveisLegaisController : ControllerBase
{
    private readonly ResponsavelLegalService _servico;
    public ResponsaveisLegaisController(ResponsavelLegalService servico) => _servico = servico;

    [HttpPost]
    [Permissao("paciente.cadastro:incluir")]
    public async Task<ActionResult<ResponsavelLegalDto>> Criar([FromBody] ResponsavelLegalEntrada? entrada, CancellationToken ct)
    {
        var criado = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/responsaveis-legais/{criado.Id}", criado);
    }

    [HttpPost("busca-por-cpf")]
    [Permissao("paciente.cadastro:incluir", "paciente.cadastro:editar")]
    public async Task<ActionResult<ResponsavelLegalDto>> BuscarPorCpf([FromBody] BuscaCpfEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.BuscarPorCpfAsync(entrada, User.ObterUsuarioAtual(), ct));

    [HttpGet("{responsavelId:int}")]
    [Permissao("paciente.cadastro:visualizar")]
    public async Task<ActionResult<ResponsavelLegalDto>> Obter(int responsavelId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(responsavelId, User.ObterUsuarioAtual(), ct));

    [HttpPut("{responsavelId:int}")]
    [Permissao("paciente.cadastro:editar")]
    public async Task<ActionResult<ResponsavelLegalDto>> Atualizar(int responsavelId, [FromBody] ResponsavelLegalEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(responsavelId, entrada, User.ObterUsuarioAtual(), ct));
}
