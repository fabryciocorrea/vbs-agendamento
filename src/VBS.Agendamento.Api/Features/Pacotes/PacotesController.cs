using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacotes;

/// <summary>Modelos de pacote de consultas particulares. Tela de permissão: pacote.cadastro.</summary>
[ApiController]
[Route("api/v1/pacotes")]
public sealed class PacotesController : ControllerBase
{
    private readonly PacoteService _servico;
    public PacotesController(PacoteService servico) => _servico = servico;

    [HttpGet]
    [Permissao("pacote.cadastro:visualizar")]
    public async Task<ActionResult<PacotePaginaDto>> Listar([FromQuery] FiltroPacotes filtro, CancellationToken ct) =>
        Ok(await _servico.ListarAsync(filtro, ct));

    /// <summary>Modelos ativos para vender (usado ao vender um pacote na Nova consulta ou na tela Pacotes vendidos). Quem vende não precisa ver o cadastro.</summary>
    [HttpGet("para-venda")]
    [Permissao("pacote.venda:incluir")]
    public async Task<ActionResult<List<PacoteDto>>> ParaVenda([FromQuery] int? especialidadeId, CancellationToken ct) =>
        Ok((await _servico.ListarAsync(new FiltroPacotes { EspecialidadeId = especialidadeId, Ativo = true, Tamanho = 100 }, ct)).Itens);

    [HttpPost]
    [Permissao("pacote.cadastro:incluir")]
    public async Task<ActionResult<PacoteDto>> Criar([FromBody] PacoteEntrada? entrada, CancellationToken ct)
    {
        var p = await _servico.CriarAsync(entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/pacotes/{p.Id}", p);
    }

    [HttpGet("{pacoteId:int}")]
    [Permissao("pacote.cadastro:visualizar")]
    public async Task<ActionResult<PacoteDto>> Obter(int pacoteId, CancellationToken ct) =>
        Ok(await _servico.ObterAsync(pacoteId, ct));

    [HttpPut("{pacoteId:int}")]
    [Permissao("pacote.cadastro:editar")]
    public async Task<ActionResult<PacoteDto>> Atualizar(int pacoteId, [FromBody] PacoteEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(pacoteId, entrada, User.ObterUsuarioAtual(), ct));

    [HttpPatch("{pacoteId:int}/status")]
    [Permissao("pacote.cadastro:editar")]
    public async Task<ActionResult<PacoteDto>> AlterarStatus(int pacoteId, [FromBody] PacoteAtivoEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.AlterarStatusAsync(pacoteId, entrada, User.ObterUsuarioAtual(), ct));
}

/// <summary>Pacotes vendidos a pacientes: venda, saldo, pagamento e cancelamento. Tela de permissão: pacote.venda.</summary>
[ApiController]
[Route("api/v1")]
public sealed class PacotesVendidosController : ControllerBase
{
    private readonly PacotePacienteService _servico;
    public PacotesVendidosController(PacotePacienteService servico) => _servico = servico;

    private void SemCache() => Response.Headers["Cache-Control"] = "no-store";

    [HttpGet("pacientes/{pacienteId:guid}/pacotes")]
    [Permissao("pacote.venda:visualizar")]
    public async Task<ActionResult<List<PacotePacienteDto>>> ListarDoPaciente(Guid pacienteId, [FromQuery] bool somenteDisponiveis, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ListarDoPacienteAsync(pacienteId, somenteDisponiveis, User.ObterUsuarioAtual(), ct));
    }

    [HttpPost("pacientes/{pacienteId:guid}/pacotes")]
    [Permissao("pacote.venda:incluir")]
    public async Task<ActionResult<PacotePacienteDto>> Vender(Guid pacienteId, [FromBody] VendaPacoteEntrada? entrada, CancellationToken ct)
    {
        var p = await _servico.VenderAsync(pacienteId, entrada, User.ObterUsuarioAtual(), ct);
        return Created($"/api/v1/pacotes-vendidos/{p.Id}", p);
    }

    [HttpGet("pacotes-vendidos")]
    [Permissao("pacote.venda:visualizar")]
    public async Task<ActionResult<PacotePacientePaginaDto>> Listar([FromQuery] FiltroPacotesVendidos filtro, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ListarAsync(filtro, User.ObterUsuarioAtual(), ct));
    }

    [HttpGet("pacotes-vendidos/{pacoteId:guid}")]
    [Permissao("pacote.venda:visualizar")]
    public async Task<ActionResult<PacotePacienteDetalheDto>> Obter(Guid pacoteId, CancellationToken ct)
    {
        SemCache();
        return Ok(await _servico.ObterAsync(pacoteId, User.ObterUsuarioAtual(), ct));
    }

    [HttpPost("pacotes-vendidos/{pacoteId:guid}/pagamento")]
    [Permissao("pacote.venda:editar")]
    public async Task<ActionResult<PacotePacienteDto>> ConfirmarPagamento(Guid pacoteId, CancellationToken ct) =>
        Ok(await _servico.ConfirmarPagamentoAsync(pacoteId, User.ObterUsuarioAtual(), ct));

    [HttpPost("pacotes-vendidos/{pacoteId:guid}/cancelamento")]
    [Permissao("pacote.venda:excluir")]
    public async Task<ActionResult<PacotePacienteDto>> Cancelar(Guid pacoteId, [FromBody] CancelamentoPacoteEntrada? entrada, CancellationToken ct) =>
        Ok(await _servico.CancelarAsync(pacoteId, entrada, User.ObterUsuarioAtual(), ct));
}
