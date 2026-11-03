using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Prontuarios;

/// <summary>Autorização exigida quando o administrador inclui ou edita um registro (H3).</summary>
public sealed class AutorizacaoProntuarioEntrada
{
    /// <summary>Profissional que solicitou (escolhido na lista de profissionais ativos).</summary>
    public int? SolicitadoPorProfissionalId { get; set; }
    /// <summary>Responsável legal (ou o próprio paciente) que deu o OK, por extenso.</summary>
    public string? AutorizadoPor { get; set; }
    public string? Observacao { get; set; }
    /// <summary>Documento anexado antes (POST .../prontuario/autorizacoes), obrigatório.</summary>
    public Guid? DocumentoId { get; set; }
}

public sealed class DocumentoAutorizacaoDto
{
    public Guid Id { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public int TamanhoBytes { get; set; }
}

public sealed class RegistroProntuarioEntrada
{
    public string? Observacao { get; set; }
    public Guid? ConsultaId { get; set; }
    /// <summary>Só o administrador envia: profissional em nome de quem o registro é incluído.</summary>
    public int? ProfissionalId { get; set; }
    public AutorizacaoProntuarioEntrada? Autorizacao { get; set; }
}

public sealed class RegistroProntuarioEdicao
{
    public string? Observacao { get; set; }
    public AutorizacaoProntuarioEntrada? Autorizacao { get; set; }
}

public sealed class FiltroProntuario
{
    public DateOnly? DataInicial { get; set; }
    public DateOnly? DataFinal { get; set; }
    public int? ProfissionalId { get; set; }
    public Guid? ConsultaId { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class TransferenciaProntuarioEntrada
{
    public int? ProfissionalId { get; set; }
    public string? Motivo { get; set; }
}

public sealed class TransferenciaProntuarioDto
{
    public ReferenciaDto ProfissionalResponsavel { get; set; } = new();
    public DateTime TransferidoEm { get; set; }
    /// <summary>Consultas futuras (agendadas ou confirmadas) do paciente que seguem com o profissional anterior; a recepção decide se remarca.</summary>
    public int ConsultasFuturasComProfissionalAnterior { get; set; }
}

public sealed class ProfissionalRegistroDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string RegistroProfissional { get; set; } = string.Empty;
    public string NumeroConselho { get; set; } = string.Empty;
    public string UfConselho { get; set; } = string.Empty;
}

public sealed class InclusaoAdministrativaDto
{
    public UsuarioReferenciaDto IncluidoPor { get; set; } = new();
    public string SolicitadoPor { get; set; } = string.Empty;
    public string AutorizadoPor { get; set; } = string.Empty;
    public string ObservacaoAutorizacao { get; set; } = string.Empty;
    public DocumentoAutorizacaoDto? Documento { get; set; }
}

public sealed class RegistroProntuarioDto
{
    public Guid Id { get; set; }
    public PacienteReferenciaDto Paciente { get; set; } = new();
    public Guid? ConsultaId { get; set; }
    public ProfissionalRegistroDto Profissional { get; set; } = new();
    public DateTime RegistradoEm { get; set; }
    public string Observacao { get; set; } = string.Empty;
    public InclusaoAdministrativaDto? InclusaoAdministrativa { get; set; }
    public bool Corrigido { get; set; }
    public DateTime? CorrigidoEm { get; set; }
}

public sealed class RegistroProntuarioPaginaDto
{
    public List<RegistroProntuarioDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

public sealed class CorrecaoProntuarioDto
{
    public string Origem { get; set; } = string.Empty;
    public string ObservacaoAnterior { get; set; } = string.Empty;
    public string ObservacaoNova { get; set; } = string.Empty;
    public string SolicitadoPor { get; set; } = string.Empty;
    public string AutorizadoPor { get; set; } = string.Empty;
    public string ObservacaoAutorizacao { get; set; } = string.Empty;
    public string ExecutadoPor { get; set; } = string.Empty;
    public DateTime CorrigidoEm { get; set; }
    public DocumentoAutorizacaoDto? Documento { get; set; }
}
