using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Especialidades;

/// <summary>Bloco 3 do contrato: especialidades.</summary>
[ApiController]
[Route("api/v1/especialidades")]
public sealed class EspecialidadesController : ControllerBase
{
    private readonly EspecialidadeService _servico;
    public EspecialidadesController(EspecialidadeService servico) => _servico = servico;

    [HttpGet]
    [Permissao("especialidade.cadastro:visualizar")]
    public async Task<ActionResult<EspecialidadePaginaDto>> Listar([FromQuery] FiltroEspecialidades filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("especialidade.cadastro:incluir")]
    public async Task<ActionResult<EspecialidadeDto>> Criar([FromBody] EspecialidadeEntrada? entrada, CancellationToken ct)
    {
        var e = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/especialidades/{e.Id}", e);
    }

    [HttpGet("{especialidadeId:int}")]
    [Permissao("especialidade.cadastro:visualizar")]
    public async Task<ActionResult<EspecialidadeDto>> Obter(int especialidadeId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(especialidadeId, ct));

    [HttpPut("{especialidadeId:int}")]
    [Permissao("especialidade.cadastro:editar")]
    public async Task<ActionResult<EspecialidadeDto>> Atualizar(int especialidadeId, [FromBody] EspecialidadeEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(especialidadeId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{especialidadeId:int}/status")]
    [Permissao("especialidade.cadastro:editar")]
    public async Task<ActionResult<EspecialidadeDto>> AlterarStatus(int especialidadeId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(especialidadeId, entrada, User.ObterUsuarioAtual(), ct));
}
