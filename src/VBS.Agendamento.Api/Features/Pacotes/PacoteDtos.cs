using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Pacotes;

// ---------------------------------------------------------------- modelos de pacote (cadastro)
public sealed class PacoteEntrada
{
    public string? Nome { get; set; }
    public int? EspecialidadeId { get; set; }
    public int? QuantidadeSessoes { get; set; }
    public decimal? Valor { get; set; }
    public int? ValidadeDias { get; set; }
}

public sealed class PacoteAtivoEntrada
{
    public bool? Ativo { get; set; }
}

public sealed class FiltroPacotes
{
    public string? Busca { get; set; }
    public int? EspecialidadeId { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class PacoteDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public ReferenciaDto Especialidade { get; set; } = new();
    public int QuantidadeSessoes { get; set; }
    /// <summary>Valor total do pacote.</summary>
    public decimal Valor { get; set; }
    /// <summary>Valor de cada sessão (valor total dividido pela quantidade, em centavos).</summary>
    public decimal ValorSessao { get; set; }
    public int ValidadeDias { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class PacotePaginaDto
{
    public List<PacoteDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

// ---------------------------------------------------------------- pacote vendido ao paciente
public sealed class VendaPacoteEntrada
{
    public int? PacoteId { get; set; }
    public int? FormaPagamentoId { get; set; }
    /// <summary>Verdadeiro quando o pagamento do pacote já foi recebido (por exemplo, PIX adiantado).</summary>
    public bool PagamentoConfirmado { get; set; }
}

public sealed class CancelamentoPacoteEntrada
{
    public string? Motivo { get; set; }
}

public sealed class FiltroPacotesVendidos
{
    public string? Busca { get; set; }
    /// <summary>DISPONIVEL (ativo, no prazo e com sessões), ENCERRADO (sem sessões ou vencido) ou CANCELADO.</summary>
    public string? Situacao { get; set; }
    public bool? PagamentoPendente { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public class PacotePacienteDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public PacienteReferenciaDto Paciente { get; set; } = new();
    public ReferenciaDto Especialidade { get; set; } = new();
    public int QuantidadeSessoes { get; set; }
    public int SessoesUsadas { get; set; }
    public int SessoesRestantes { get; set; }
    public decimal Valor { get; set; }
    public decimal ValorSessao { get; set; }
    public DateOnly DataInicio { get; set; }
    public DateOnly DataValidade { get; set; }
    public ReferenciaDto FormaPagamento { get; set; } = new();
    public bool PagamentoConfirmado { get; set; }
    public DateTime? PagamentoConfirmadoEm { get; set; }
    /// <summary>ATIVO ou CANCELADO.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Calculado: DISPONIVEL, ENCERRADO ou CANCELADO.</summary>
    public string Situacao { get; set; } = string.Empty;
    public DateTime VendidoEm { get; set; }
    public UsuarioReferenciaDto? VendidoPor { get; set; }
    public DateTime? CanceladoEm { get; set; }
    public string? MotivoCancelamento { get; set; }
}

public sealed class SessaoPacoteDto
{
    public Guid ConsultaId { get; set; }
    public DateTime Inicio { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Profissional { get; set; } = string.Empty;
    /// <summary>Verdadeiro quando a consulta consome uma sessão do pacote (não está cancelada nem remarcada).</summary>
    public bool Consome { get; set; }
}

public sealed class PacotePacienteDetalheDto : PacotePacienteDto
{
    public List<SessaoPacoteDto> Sessoes { get; set; } = new();
}

public sealed class PacotePacientePaginaDto
{
    public List<PacotePacienteDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
