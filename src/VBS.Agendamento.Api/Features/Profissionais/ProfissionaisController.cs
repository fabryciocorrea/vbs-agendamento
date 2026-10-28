using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Profissionais;

/// <summary>Bloco 3 do contrato: profissionais e expediente.</summary>
[ApiController]
[Route("api/v1/profissionais")]
public sealed class ProfissionaisController : ControllerBase
{
    private readonly ProfissionalService _servico;
    public ProfissionaisController(ProfissionalService servico) => _servico = servico;

    [HttpGet]
    [Permissao("profissional.cadastro:visualizar")]
    public async Task<ActionResult<ProfissionalPaginaDto>> Listar([FromQuery] FiltroProfissionais filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    [HttpPost]
    [Permissao("profissional.cadastro:incluir")]
    public async Task<ActionResult<ProfissionalDto>> Criar([FromBody] ProfissionalEntrada? entrada, CancellationToken ct)
    {
        var p = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/profissionais/{p.Id}", p);
    }

    [HttpGet("{profissionalId:int}")]
    [Permissao("profissional.cadastro:visualizar")]
    public async Task<ActionResult<ProfissionalDto>> Obter(int profissionalId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(profissionalId, ct));

    [HttpPut("{profissionalId:int}")]
    [Permissao("profissional.cadastro:editar")]
    public async Task<ActionResult<ProfissionalDto>> Atualizar(int profissionalId, [FromBody] ProfissionalAtualizacao? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(profissionalId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{profissionalId:int}/status")]
    [Permissao("profissional.cadastro:editar")]
    public async Task<ActionResult<ProfissionalDto>> AlterarStatus(int profissionalId, [FromBody] StatusEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(profissionalId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpGet("{profissionalId:int}/horarios")]
    [Permissao("profissional.cadastro:visualizar")]
    public async Task<ActionResult<List<HorarioDto>>> ObterHorarios(int profissionalId, CancellationToken ct) =>
        Ok(await _servico.ObterHorariosAsync(profissionalId, ct));

    [HttpPut("{profissionalId:int}/horarios")]
    [Permissao("profissional.cadastro:editar")]
    public async Task<ActionResult<HorariosSaidaDto>> SubstituirHorarios(int profissionalId, [FromBody] HorariosEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.SubstituirHorariosAsync(profissionalId, entrada, User.ObterUsuarioAtual(), ct));
}
