namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>Documento anexado ao cadastro do paciente (carteira de identidade, encaminhamento, autorização...).</summary>
public sealed class DocumentoPacienteDto
{
    public Guid Id { get; set; }
    /// <summary>IDENTIDADE, ENCAMINHAMENTO, AUTORIZACAO, CARTEIRINHA, EXAME ou OUTRO.</summary>
    public string Tipo { get; set; } = string.Empty;
    public string TipoNome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string TipoConteudo { get; set; } = string.Empty;
    public int TamanhoBytes { get; set; }
    public DateTime EnviadoEm { get; set; }
    public UsuarioReferenciaDto? EnviadoPor { get; set; }
}
