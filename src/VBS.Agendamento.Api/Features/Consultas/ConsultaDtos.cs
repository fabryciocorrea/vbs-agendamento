using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Consultas;

public sealed class PacienteReferenciaDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}

public sealed class SalaReferenciaDto
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string Andar { get; set; } = string.Empty;
}

public sealed class AvisoDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
}

public sealed class CredenciamentoLiberadoDto
{
    public UsuarioReferenciaDto LiberadoPor { get; set; } = new();
    public string Justificativa { get; set; } = string.Empty;
}

public sealed class LiberacaoCredenciamentoEntrada
{
    public string? Justificativa { get; set; }
}

public sealed class LiberacaoAdministradorEntrada
{
    public string? Justificativa { get; set; }
}

public sealed class LiberacaoAdministradorDto
{
    public UsuarioReferenciaDto LiberadoPor { get; set; } = new();
    public string Justificativa { get; set; } = string.Empty;
    public DateTime Em { get; set; }
}

public sealed class StatusEntrada
{
    public LiberacaoAdministradorEntrada? LiberacaoAdministrador { get; set; }
    public string? Status { get; set; }
    public string? Motivo { get; set; }
    public string? CanceladoPor { get; set; }
    public string? Versao { get; set; }
}

public sealed class PagamentoConfirmacaoEntrada
{
    public LiberacaoAdministradorEntrada? LiberacaoAdministrador { get; set; }
    public int? FormaPagamentoId { get; set; }
    public string? Versao { get; set; }
}

public sealed class RemarcacaoEntrada
{
    public LiberacaoAdministradorEntrada? LiberacaoAdministrador { get; set; }
    public DateTime? NovoInicio { get; set; }
    public DateTime? NovoFim { get; set; }
    public string? Motivo { get; set; }
    public int? ProfissionalId { get; set; }
    public int? EspecialidadeId { get; set; }
    public int? UnidadeId { get; set; }
    public int? SalaId { get; set; }
    public bool Encaixe { get; set; }
    public LiberacaoCredenciamentoEntrada? LiberacaoCredenciamento { get; set; }
    public string? Versao { get; set; }
}

public sealed class GuiaConvenioDto
{
    /// <summary>PENDENTE, AUTORIZADA, NEGADA ou CANCELADA. Quem confirma é o faturamento.</summary>
    public string Status { get; set; } = "PENDENTE";
    public string? Numero { get; set; }
    public string? Observacao { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class ConsultaEntrada
{
    public Guid? PacienteId { get; set; }
    public int? ProfissionalId { get; set; }
    public int? EspecialidadeId { get; set; }
    public int? UnidadeId { get; set; }
    public int? SalaId { get; set; }
    public string? Modalidade { get; set; }
    public string? TipoAtendimento { get; set; }
    public int? ConvenioId { get; set; }
    public decimal? Valor { get; set; }
    public int? FormaPagamentoId { get; set; }
    public DateTime? Inicio { get; set; }
    public DateTime? Fim { get; set; }
    public bool Encaixe { get; set; }
    public bool PagamentoConfirmado { get; set; }
    public LiberacaoCredenciamentoEntrada? LiberacaoCredenciamento { get; set; }
    public LiberacaoAdministradorEntrada? LiberacaoAdministrador { get; set; }
}

public class ConsultaResumoDto
{
    public Guid Id { get; set; }
    public string Versao { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public PacienteReferenciaDto Paciente { get; set; } = new();
    public ReferenciaDto Profissional { get; set; } = new();
    public ReferenciaDto Especialidade { get; set; } = new();
    public ReferenciaDto Unidade { get; set; } = new();
    public SalaReferenciaDto? Sala { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public string Modalidade { get; set; } = string.Empty;
    public string TipoAtendimento { get; set; } = string.Empty;
    public ReferenciaDto? Convenio { get; set; }
    public decimal? Valor { get; set; }
    public bool Encaixe { get; set; }
    /// <summary>Verdadeiro quando esta consulta nasceu de uma remarcação (a original ficou REMARCADA no horário antigo).</summary>
    public bool Remarcada { get; set; }
}

public sealed class ConsultaDto : ConsultaResumoDto
{
    public string? LinkOnline { get; set; }
    public string? NumeroCarteirinha { get; set; }
    public bool CarteirinhaVencida { get; set; }
    public ReferenciaDto FormaPagamento { get; set; } = new();
    public bool PagamentoConfirmado { get; set; }
    public DateTime? PagamentoConfirmadoEm { get; set; }
    public UsuarioReferenciaDto? PagamentoConfirmadoPor { get; set; }
    public Guid? ConsultaOrigemId { get; set; }
    public CredenciamentoLiberadoDto? CredenciamentoLiberado { get; set; }
    /// <summary>Liberação do administrador de uma regra operacional (guia, pagamento, horário etc.), com justificativa.</summary>
    public LiberacaoAdministradorDto? LiberacaoAdministrador { get; set; }
    /// <summary>Guia do convênio (só em consulta por convênio).</summary>
    public GuiaConvenioDto? Guia { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public List<AvisoDto>? Avisos { get; set; }
}

public sealed class ConsultaPaginaDto
{
    public List<ConsultaResumoDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

public sealed class FiltroConsultas
{
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public Guid? PacienteId { get; set; }
    public int? ProfissionalId { get; set; }
    public int? UnidadeId { get; set; }
    public string? Status { get; set; }
    public bool? Encaixe { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}
