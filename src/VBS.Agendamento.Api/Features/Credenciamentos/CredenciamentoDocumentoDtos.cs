using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Credenciamentos;

/// <summary>Documento anexado a um credenciamento de convênio (protocolo, contrato, carta de aprovação, tabela de valores...).</summary>
public sealed class CredenciamentoDocumentoDto
{
    public Guid Id { get; set; }
    /// <summary>PROTOCOLO, CONTRATO, APROVACAO, TABELA_VALORES, COMUNICADO ou OUTRO.</summary>
    public string Tipo { get; set; } = string.Empty;
    public string TipoNome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string TipoConteudo { get; set; } = string.Empty;
    public int TamanhoBytes { get; set; }
    public DateTime EnviadoEm { get; set; }
    public UsuarioReferenciaDto? EnviadoPor { get; set; }
}
