using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>Bloco 2 do contrato: pacientes. Cada operação exige a liberação do x-permissao correspondente.</summary>
[ApiController]
[Route("api/v1/pacientes")]
public sealed class PacientesController : ControllerBase
{
    private readonly PacienteService _servico;
    public PacientesController(PacienteService servico) => _servico = servico;

    [HttpGet]
    [Permissao("paciente.cadastro:visualizar")]
    public async Task<ActionResult<PacientePaginaDto>> Listar([FromQuery] FiltroPacientes filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, User.ObterUsuarioAtual(), ct));

    [HttpPost]
    [Permissao("paciente.cadastro:incluir")]
    public async Task<ActionResult<PacienteDto>> Criar([FromBody] PacienteEntrada? entrada, CancellationToken ct)
    {
        var paciente = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/pacientes/{paciente.Id}", paciente);
    }

    [HttpPost("busca-por-cpf")]
    [Permissao("paciente.cadastro:visualizar")]
    public async Task<ActionResult<PacienteResumoDto>> BuscarPorCpf([FromBody] BuscaCpfEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.BuscarPorCpfAsync(entrada, User.ObterUsuarioAtual(), ct));

    [HttpGet("{pacienteId:guid}")]
    [Permissao("paciente.cadastro:visualizar")]
    public async Task<ActionResult<PacienteDto>> Obter(Guid pacienteId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(pacienteId, User.ObterUsuarioAtual(), ct));

    [HttpPut("{pacienteId:guid}")]
    [Permissao("paciente.cadastro:editar")]
    public async Task<ActionResult<PacienteDto>> Atualizar(Guid pacienteId, [FromBody] PacienteBaseEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(pacienteId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{pacienteId:guid}/status")]
    [Permissao("paciente.cadastro:editar")]
    public async Task<ActionResult<PacienteDto>> AlterarStatus(Guid pacienteId, [FromBody] StatusPacienteEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(pacienteId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPut("{pacienteId:guid}/profissional-responsavel")]
    [Permissao("paciente.cadastro:editar")]
    public async Task<ActionResult<PacienteDto>> TrocarProfissional(Guid pacienteId, [FromBody] TrocaProfissionalEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.TrocarProfissionalAsync(pacienteId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpGet("{pacienteId:guid}/profissionais-historico")]
    [Permissao("paciente.cadastro:visualizar")]
    public async Task<ActionResult<List<HistoricoProfissionalDto>>> HistoricoProfissionais(Guid pacienteId, CancellationToken ct) =>
        Ok(await _servico.ListarHistoricoAsync(pacienteId, User.ObterUsuarioAtual(), ct));

    [HttpGet("{pacienteId:guid}/consentimentos")]
    [Permissao("paciente.cadastro:visualizar")]
    public async Task<ActionResult<List<ConsentimentoDto>>> ListarConsentimentos(Guid pacienteId, CancellationToken ct) =>
        Ok(await _servico.ListarConsentimentosAsync(pacienteId, User.ObterUsuarioAtual(), ct));

    [HttpPost("{pacienteId:guid}/consentimentos")]
    [Permissao("paciente.cadastro:editar")]
    public async Task<ActionResult<ConsentimentoDto>> RegistrarConsentimento(Guid pacienteId, [FromBody] ConsentimentoEntrada? entrada, CancellationToken ct)
    {
        var criado = await _servico.RegistrarConsentimentoAsync(pacienteId, entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/pacientes/{pacienteId}/consentimentos", criado);
    }
}
