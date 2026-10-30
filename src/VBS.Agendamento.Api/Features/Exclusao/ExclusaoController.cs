using Microsoft.AspNetCore.Mvc;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Exclusao;

/// <summary>Exclusão definitiva de cadastros (só o administrador). Sem histórico: apaga; com histórico: 409 e o caminho é Inativar.</summary>
[ApiController]
[Route("api/v1")]
public sealed class ExclusaoController : ControllerBase
{
    private readonly ExclusaoService _servico;
    public ExclusaoController(ExclusaoService servico) => _servico = servico;

    [HttpDelete("pacientes/{pacienteId:guid}")]
    [Permissao("paciente.cadastro:excluir")]
    public async Task<IActionResult> Paciente(Guid pacienteId, CancellationToken ct)
    {
        await _servico.ExcluirPacienteAsync(pacienteId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("convenios/{convenioId:int}")]
    [Permissao("convenio.cadastro:excluir")]
    public async Task<IActionResult> Convenio(int convenioId, CancellationToken ct)
    {
        await _servico.ExcluirConvenioAsync(convenioId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("convenios/{convenioId:int}/credenciamentos/{credenciamentoId:int}")]
    [Permissao("convenio.credenciamento:excluir")]
    public async Task<IActionResult> Credenciamento(int convenioId, int credenciamentoId, CancellationToken ct)
    {
        await _servico.ExcluirCredenciamentoAsync(convenioId, credenciamentoId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("profissionais/{profissionalId:int}")]
    [Permissao("profissional.cadastro:excluir")]
    public async Task<IActionResult> Profissional(int profissionalId, CancellationToken ct)
    {
        await _servico.ExcluirProfissionalAsync(profissionalId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("colaboradores/{colaboradorId:int}")]
    [Permissao("colaborador.cadastro:excluir")]
    public async Task<IActionResult> Colaborador(int colaboradorId, CancellationToken ct)
    {
        await _servico.ExcluirColaboradorAsync(colaboradorId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("especialidades/{especialidadeId:int}")]
    [Permissao("especialidade.cadastro:excluir")]
    public async Task<IActionResult> Especialidade(int especialidadeId, CancellationToken ct)
    {
        await _servico.ExcluirEspecialidadeAsync(especialidadeId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("formas-pagamento/{formaPagamentoId:int}")]
    [Permissao("formapagamento.cadastro:excluir")]
    public async Task<IActionResult> FormaPagamento(int formaPagamentoId, CancellationToken ct)
    {
        await _servico.ExcluirFormaPagamentoAsync(formaPagamentoId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("unidades/{unidadeId:int}")]
    [Permissao("unidade.cadastro:excluir")]
    public async Task<IActionResult> Unidade(int unidadeId, CancellationToken ct)
    {
        await _servico.ExcluirUnidadeAsync(unidadeId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("salas/{salaId:int}")]
    [Permissao("sala.cadastro:excluir")]
    public async Task<IActionResult> Sala(int salaId, CancellationToken ct)
    {
        await _servico.ExcluirSalaAsync(salaId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }

    [HttpDelete("usuarios/{usuarioId:int}")]
    [Permissao("usuario.cadastro:excluir")]
    public async Task<IActionResult> Usuario(int usuarioId, CancellationToken ct)
    {
        await _servico.ExcluirUsuarioAsync(usuarioId, User.ObterUsuarioAtual(), ct);
        return NoContent();
    }
}
