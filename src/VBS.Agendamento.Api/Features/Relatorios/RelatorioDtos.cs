using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;

namespace VBS.Agendamento.Api.Features.Relatorios;

public sealed class FiltroRelatorio
{
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public int? ProfissionalId { get; set; }
    public int? UnidadeId { get; set; }
    public int? ConvenioId { get; set; }
    /// <summary>PARTICULAR ou CONVENIO. Vazio: todos.</summary>
    public string? TipoAtendimento { get; set; }
    /// <summary>Só em cancelamentos e faltas: RESUMO (padrão) ou DETALHE.</summary>
    public string? Visao { get; set; }
    /// <summary>Só na lista de espera: AGUARDANDO, NOTIFICADO, ATENDIDO ou CANCELADO. Vazio: todas.</summary>
    public string? Situacao { get; set; }
    /// <summary>Só nos relatórios cadastrais: true (ativos), false (inativos). Vazio: todos.</summary>
    public bool? Ativo { get; set; }
    /// <summary>Só no relatório de colaboradores e prestadores: CLT ou MEI.</summary>
    public string? Vinculo { get; set; }
    /// <summary>Só no relatório de pacientes: false (nacionais) ou true (estrangeiros). Vazio: todos.</summary>
    public bool? Estrangeiro { get; set; }
    /// <summary>Só no relatório de prontuários: o administrador pede o texto dos registros; os demais recebem 403.</summary>
    public bool IncluirTexto { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class FiltrosAplicadosDto
{
    public DateOnly DataInicial { get; set; }
    public DateOnly DataFinal { get; set; }
    public ReferenciaDto? Profissional { get; set; }
    public ReferenciaDto? Unidade { get; set; }
    public ReferenciaDto? Convenio { get; set; }
    public string? TipoAtendimento { get; set; }
    /// <summary>Só na lista de espera.</summary>
    public string? Situacao { get; set; }
}

// ------------------------------------------------------------ consultas por profissional

public sealed class ConsultasPorProfissionalItemDto
{
    public ReferenciaDto Profissional { get; set; } = new();
    /// <summary>Consultas concluídas no período.</summary>
    public int Atendimentos { get; set; }
    /// <summary>Todas as consultas marcadas no período, exceto remarcadas.</summary>
    public int Agendamentos { get; set; }
}

public sealed class TotaisConsultasPorProfissionalDto
{
    public int Atendimentos { get; set; }
    public int Agendamentos { get; set; }
}

public sealed class RelatorioConsultasPorProfissionalDto
{
    public FiltrosAplicadosDto Filtros { get; set; } = new();
    public DateTime GeradoEm { get; set; }
    public List<ConsultasPorProfissionalItemDto> Itens { get; set; } = new();
    public TotaisConsultasPorProfissionalDto Totais { get; set; } = new();
}

// ------------------------------------------------------------ ocupação da agenda

public sealed class OcupacaoItemDto
{
    public ReferenciaDto Profissional { get; set; } = new();
    /// <summary>Expediente do período, sem intervalos, feriados nacionais e recesso.</summary>
    public int MinutosDisponiveis { get; set; }
    /// <summary>Duração das consultas dentro do expediente, exceto canceladas, remarcadas e encaixes.</summary>
    public int MinutosOcupados { get; set; }
    public decimal OcupacaoPercentual { get; set; }
    /// <summary>Encaixes do período, fora do cálculo.</summary>
    public int Encaixes { get; set; }
}

public sealed class TotaisOcupacaoDto
{
    public int MinutosDisponiveis { get; set; }
    public int MinutosOcupados { get; set; }
    public decimal OcupacaoPercentual { get; set; }
    public int Encaixes { get; set; }
}

public sealed class RelatorioOcupacaoDto
{
    public FiltrosAplicadosDto Filtros { get; set; } = new();
    public DateTime GeradoEm { get; set; }
    public List<OcupacaoItemDto> Itens { get; set; } = new();
    public TotaisOcupacaoDto Totais { get; set; } = new();
}

// ------------------------------------------------------------ cancelamentos e faltas

public sealed class CancelamentoFaltaResumoDto
{
    public ReferenciaDto Profissional { get; set; } = new();
    public int TotalConsultas { get; set; }
    public int CanceladasPaciente { get; set; }
    public int CanceladasClinica { get; set; }
    public int Faltas { get; set; }
    public decimal PercentualCancelamento { get; set; }
    public decimal PercentualFaltas { get; set; }
}

public sealed class CancelamentoFaltaDetalheDto
{
    public Guid ConsultaId { get; set; }
    public PacienteReferenciaDto Paciente { get; set; } = new();
    public ReferenciaDto Profissional { get; set; } = new();
    public DateTime Inicio { get; set; }
    /// <summary>CANCELADA ou FALTA.</summary>
    public string Situacao { get; set; } = string.Empty;
    /// <summary>PACIENTE ou CLINICA, só nas canceladas.</summary>
    public string? CanceladoPor { get; set; }
    public string? Motivo { get; set; }
}

public sealed class CancelamentoFaltaDetalhePaginaDto
{
    public List<CancelamentoFaltaDetalheDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

public sealed class TotaisCancelamentosFaltasDto
{
    public int TotalConsultas { get; set; }
    public int CanceladasPaciente { get; set; }
    public int CanceladasClinica { get; set; }
    public int Faltas { get; set; }
}

public sealed class RelatorioCancelamentosFaltasDto
{
    public FiltrosAplicadosDto Filtros { get; set; } = new();
    public DateTime GeradoEm { get; set; }
    public List<CancelamentoFaltaResumoDto> Itens { get; set; } = new();
    public TotaisCancelamentosFaltasDto Totais { get; set; } = new();
    /// <summary>Só quando a visão é DETALHE.</summary>
    public CancelamentoFaltaDetalhePaginaDto? Detalhe { get; set; }
}

// ------------------------------------------------------------ faturamento (por tipo e por convênio)

/// <summary>
/// Uma linha do faturamento (só consultas concluídas). Particular: Confirmado = recebido, Pendente = a confirmar.
/// Convênio: Confirmado = guia autorizada, Pendente = guia pendente, Negado = guia negada.
/// </summary>
public sealed class FaturamentoLinhaDto
{
    public string Nome { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
    /// <summary>Parte do faturado total do relatório.</summary>
    public decimal Percentual { get; set; }
    public decimal Confirmado { get; set; }
    public decimal Pendente { get; set; }
    public decimal Negado { get; set; }
    public decimal TicketMedio { get; set; }
}

public sealed class RelatorioFaturamentoDto
{
    public FiltrosAplicadosDto Filtros { get; set; } = new();
    public DateTime GeradoEm { get; set; }
    public List<FaturamentoLinhaDto> Itens { get; set; } = new();
    public FaturamentoLinhaDto Totais { get; set; } = new();
}

// ------------------------------------------------------------ faturamento mensal

public sealed class FaturamentoMensalItemDto
{
    /// <summary>AAAA-MM.</summary>
    public string Mes { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public decimal Particular { get; set; }
    public decimal Convenio { get; set; }
    public decimal Total { get; set; }
    /// <summary>Variação do total em relação ao mês anterior do relatório; vazia no primeiro mês ou quando o anterior é zero.</summary>
    public decimal? VariacaoPercentual { get; set; }
}

public sealed class RelatorioFaturamentoMensalDto
{
    public FiltrosAplicadosDto Filtros { get; set; } = new();
    public DateTime GeradoEm { get; set; }
    public List<FaturamentoMensalItemDto> Itens { get; set; } = new();
    public FaturamentoMensalItemDto Totais { get; set; } = new();
}

// ------------------------------------------------------------ lista de espera

public sealed class ListaEsperaRelatorioItemDto
{
    public int Id { get; set; }
    public PacienteReferenciaDto Paciente { get; set; } = new();
    public ReferenciaDto? Profissional { get; set; }
    public ReferenciaDto? Especialidade { get; set; }
    public ReferenciaDto? Unidade { get; set; }
    public string Situacao { get; set; } = string.Empty;
    public DateTime EntrouEm { get; set; }
    public DateTime? NotificadoEm { get; set; }
    /// <summary>Dias na fila: até hoje, para quem ainda espera; só informativo para os encerrados (vazio).</summary>
    public int? DiasNaFila { get; set; }
    public string? Observacao { get; set; }
}

public sealed class TotaisListaEsperaDto
{
    public int Total { get; set; }
    public int Aguardando { get; set; }
    public int Notificado { get; set; }
    public int Atendido { get; set; }
    public int Cancelado { get; set; }
    /// <summary>Média de dias na fila de quem ainda espera (aguardando e notificado).</summary>
    public decimal MediaDiasEmAberto { get; set; }
}

public sealed class RelatorioListaEsperaDto
{
    public FiltrosAplicadosDto Filtros { get; set; } = new();
    public DateTime GeradoEm { get; set; }
    public TotaisListaEsperaDto Totais { get; set; } = new();
    public List<ListaEsperaRelatorioItemDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

// ------------------------------------------------------------ prontuários

public sealed class ProfissionalProntuarioDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string RegistroProfissional { get; set; } = string.Empty;
    public string NumeroConselho { get; set; } = string.Empty;
    public string UfConselho { get; set; } = string.Empty;
}

public sealed class ProntuarioRelatorioItemDto
{
    public Guid RegistroId { get; set; }
    public VBS.Agendamento.Api.Features.Consultas.PacienteReferenciaDto Paciente { get; set; } = new();
    public ProfissionalProntuarioDto Profissional { get; set; } = new();
    public DateTime RegistradoEm { get; set; }
    public bool Corrigido { get; set; }
    public bool OrigemAdministrativa { get; set; }
    /// <summary>Nome do administrador que fez a inclusão; vazio quando o registro não é administrativo.</summary>
    public string? IncluidoPorNome { get; set; }
    /// <summary>Só vem quando o administrador pede o texto.</summary>
    public string? Observacao { get; set; }
}

public sealed class ProntuarioRelatorioPaginaDto
{
    public List<ProntuarioRelatorioItemDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

public sealed class RelatorioProntuariosDto
{
    public FiltrosAplicadosDto Filtros { get; set; } = new();
    public DateTime GeradoEm { get; set; }
    public ProntuarioRelatorioPaginaDto Resultado { get; set; } = new();
    public int TotalPacientes { get; set; }
}

// ------------------------------------------------------------ relatórios cadastrais

/// <summary>Relatório cadastral em forma de tabela (colunas e linhas já formatadas em português), com paginação.</summary>
public sealed class RelatorioCadastroDto
{
    public string Titulo { get; set; } = string.Empty;
    public List<string> Filtros { get; set; } = new();
    public List<ColunaTabela> Colunas { get; set; } = new();
    public List<List<string>> Linhas { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
    public DateTime GeradoEm { get; set; }
}
