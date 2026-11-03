using VBS.Agendamento.Api.Features.Pacientes;

namespace VBS.Agendamento.Api.Features.Notificacoes;

public sealed class FiltroNotificacao
{
    public string? Tipo { get; set; }
    public bool? Lida { get; set; }
    public Guid? ConsultaId { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamanho { get; set; } = 20;
}

public sealed class PacienteNotificacaoDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}

public sealed class ConsultaNotificacaoDto
{
    public Guid Id { get; set; }
    public DateTime Inicio { get; set; }
    public PacienteNotificacaoDto Paciente { get; set; } = new();
}

public sealed class NotificacaoInternaDto
{
    public int Id { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public ConsultaNotificacaoDto? Consulta { get; set; }
    public DateTime CriadaEm { get; set; }
    public bool Lida { get; set; }
    public DateTime? LidaEm { get; set; }
    public UsuarioReferenciaDto? LidaPor { get; set; }
}

public sealed class NotificacaoInternaPaginaDto
{
    public List<NotificacaoInternaDto> Itens { get; set; } = new();
    public int Pagina { get; set; }
    public int Tamanho { get; set; }
    public int Total { get; set; }
}

public sealed class ContagemTipoDto
{
    public string Tipo { get; set; } = string.Empty;
    public int Total { get; set; }
}

public sealed class ContagemNaoLidasDto
{
    public int Total { get; set; }
    public List<ContagemTipoDto> PorTipo { get; set; } = new();
}
