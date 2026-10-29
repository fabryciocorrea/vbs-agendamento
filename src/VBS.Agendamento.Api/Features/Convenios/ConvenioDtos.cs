using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Convenios;

public sealed class ConvenioEntrada
{
    public string? RazaoSocial { get; set; }
    public string? NomeFantasia { get; set; }
    public string? Cnpj { get; set; }
    public string? CodigoAns { get; set; }
    public DateOnly? DataInscricao { get; set; }
    public string? InscricaoMunicipal { get; set; }
    public string? InscricaoEstadual { get; set; }
    public List<ConvenioEspecialidadeEntrada>? Especialidades { get; set; }
}

public sealed class ConvenioEspecialidadeEntrada
{
    public int? EspecialidadeId { get; set; }
    public decimal? Valor { get; set; }
}

public sealed class ConvenioEspecialidadeDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    /// <summary>Valor da consulta do convênio nesta especialidade (padrão; o credenciamento pode ter valor próprio por unidade).</summary>
    public decimal Valor { get; set; }
}

public sealed class FiltroConvenios
{
    public string? Busca { get; set; }
    public bool? Ativo { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class ConvenioDto
{
    public int Id { get; set; }
    public bool Ativo { get; set; }
    public string RazaoSocial { get; set; } = string.Empty;
    public string? NomeFantasia { get; set; }
    public string Cnpj { get; set; } = string.Empty;
    public string CodigoAns { get; set; } = string.Empty;
    public DateOnly DataInscricao { get; set; }
    public string? InscricaoMunicipal { get; set; }
    public string? InscricaoEstadual { get; set; }
    public List<ConvenioEspecialidadeDto> Especialidades { get; set; } = new();
    public int? FormaPagamentoId { get; set; }
    public DateTime CriadoEm { get; set; }
    public UsuarioReferenciaDto? CriadoPor { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public UsuarioReferenciaDto? AtualizadoPor { get; set; }
}

public sealed class ConvenioPaginaDto
{
    public List<ConvenioDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}
