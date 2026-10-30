using VBS.Agendamento.Api.Common;

namespace VBS.Agendamento.Api.Features.Credenciamentos;

public sealed class CredenciamentoEntrada
{
    public int? UnidadeId { get; set; }
    public int? EspecialidadeId { get; set; }
    public string? Situacao { get; set; }
    public DateOnly? DataInicio { get; set; }
    public DateOnly? DataFim { get; set; }
    public string? Protocolo { get; set; }
    public string? Observacao { get; set; }
    public decimal? Valor { get; set; }
}

public sealed class CredenciamentoAtualizacao
{
    /// <summary>Opcionais: quando informados e diferentes dos atuais, trocam convênio, unidade ou especialidade do credenciamento.</summary>
    public int? ConvenioId { get; set; }
    public int? UnidadeId { get; set; }
    public int? EspecialidadeId { get; set; }
    public string? Situacao { get; set; }
    public DateOnly? DataInicio { get; set; }
    public DateOnly? DataFim { get; set; }
    public string? Protocolo { get; set; }
    public string? Observacao { get; set; }
    public decimal? Valor { get; set; }
}

public sealed class CredenciamentoLoteEntrada
{
    public int? UnidadeId { get; set; }
    public List<int>? EspecialidadeIds { get; set; }
    public string? Situacao { get; set; }
    public DateOnly? DataInicio { get; set; }
    public DateOnly? DataFim { get; set; }
    public string? Protocolo { get; set; }
    public string? Observacao { get; set; }
    public decimal? Valor { get; set; }
}

public sealed class FiltroCredenciamentos
{
    public int? ConvenioId { get; set; }
    public int? UnidadeId { get; set; }
    public int? EspecialidadeId { get; set; }
    public string? Situacao { get; set; }
    public bool? Vigente { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class UnidadeCredenciamentoDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Cidade { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
}

public sealed class CredenciamentoDto
{
    public int Id { get; set; }
    public ReferenciaDto Convenio { get; set; } = new();
    public UnidadeCredenciamentoDto Unidade { get; set; } = new();
    public ReferenciaDto Especialidade { get; set; } = new();
    public string Situacao { get; set; } = string.Empty;
    public bool Ativo { get; set; }
    public DateOnly? DataInicio { get; set; }
    public DateOnly? DataFim { get; set; }
    public string? Protocolo { get; set; }
    public string? Observacao { get; set; }
    /// <summary>Valor que vale na consulta: o próprio do credenciamento ou, se vazio, o da especialidade no cadastro do convênio.</summary>
    public decimal Valor { get; set; }
    /// <summary>Valor próprio deste credenciamento (por unidade); vazio quando vale o do convênio.</summary>
    public decimal? ValorProprio { get; set; }
    public bool VigenteHoje { get; set; }
    public DateTime AtualizadoEm { get; set; }
}

public sealed class CredenciamentoStatusEntrada
{
    public bool? Ativo { get; set; }
}

public sealed class CredenciamentoPaginaDto
{
    public List<CredenciamentoDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

public sealed class ValorConsultaDto
{
    /// <summary>Valor da consulta por convênio para a unidade e a especialidade; vazio quando não há credenciamento.</summary>
    public decimal? Valor { get; set; }
    public bool Credenciado { get; set; }
}
