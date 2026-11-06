using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Documentos;

/// <summary>Documento interno da empresa (folha timbrada, termo, contrato, política, formulário, modelo...).</summary>
public sealed class DocumentoInternoDto
{
    public Guid Id { get; set; }
    /// <summary>FOLHA_TIMBRADA, TERMO, CONTRATO, POLITICA, FORMULARIO, MODELO ou OUTRO.</summary>
    public string Tipo { get; set; } = string.Empty;
    public string TipoNome { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string TipoConteudo { get; set; } = string.Empty;
    public int TamanhoBytes { get; set; }
    public DateTime EnviadoEm { get; set; }
    public UsuarioReferenciaDto? EnviadoPor { get; set; }
}
