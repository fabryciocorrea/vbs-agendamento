namespace VBS.Agendamento.Api.Features.Faturamento;

public sealed class FiltroFinanceiro
{
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public int? UnidadeId { get; set; }
}

public sealed class QuantidadeValorDto
{
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
}

public sealed class ResumoParticularDto
{
    public int Quantidade { get; set; }
    public decimal Total { get; set; }
    /// <summary>Pagamento confirmado pela recepção.</summary>
    public QuantidadeValorDto Recebido { get; set; } = new();
    /// <summary>Concluídas sem pagamento confirmado.</summary>
    public QuantidadeValorDto NaoConfirmado { get; set; } = new();
}

public sealed class ResumoConvenioDto
{
    public int Quantidade { get; set; }
    public decimal Total { get; set; }
    public QuantidadeValorDto Autorizado { get; set; } = new();
    public QuantidadeValorDto Pendente { get; set; } = new();
    public QuantidadeValorDto Negado { get; set; } = new();
}

public sealed class ResumoFinanceiroDto
{
    public DateOnly DataInicial { get; set; }
    public DateOnly DataFinal { get; set; }
    public int? UnidadeId { get; set; }
    public int Concluidas { get; set; }
    public decimal ValorTotal { get; set; }
    public ResumoParticularDto Particular { get; set; } = new();
    public ResumoConvenioDto Convenio { get; set; } = new();
}

public sealed class FiltroParticulares
{
    /// <summary>PENDENTE (sem pagamento confirmado) ou CONFIRMADO.</summary>
    public string? Pagamento { get; set; }
    public int? UnidadeId { get; set; }
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public string? Busca { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class ParticularItemDto
{
    public Guid ConsultaId { get; set; }
    public DateTime Inicio { get; set; }
    public string StatusConsulta { get; set; } = string.Empty;
    public Guid PacienteId { get; set; }
    public string Paciente { get; set; } = string.Empty;
    public string Profissional { get; set; } = string.Empty;
    public string Especialidade { get; set; } = string.Empty;
    public string Unidade { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string FormaPagamento { get; set; } = string.Empty;
    public bool PagamentoConfirmado { get; set; }
    public DateTime? PagamentoConfirmadoEm { get; set; }
    public string? PagamentoConfirmadoPor { get; set; }
}

public sealed class ParticularPaginaDto
{
    public List<ParticularItemDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
