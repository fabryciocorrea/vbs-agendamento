namespace VBS.Agendamento.Api.Features.Auth;

/// <summary>
/// Envio de e-mail do sistema. Ainda não há provedor definido: em desenvolvimento a mensagem só
/// aparece no console do servidor (com o link de redefinição de senha), sem sair do computador.
/// Quando houver servidor de e-mail, troque a implementação registrada no Program.cs.
/// </summary>
public interface IEnviadorEmail
{
    Task EnviarAsync(string para, string assunto, string corpo, CancellationToken ct);
}

public sealed class EnviadorEmailConsole : IEnviadorEmail
{
    private readonly ILogger<EnviadorEmailConsole> _log;
    public EnviadorEmailConsole(ILogger<EnviadorEmailConsole> log) => _log = log;

    public Task EnviarAsync(string para, string assunto, string corpo, CancellationToken ct)
    {
        _log.LogWarning("E-MAIL (SIMULADO, NÃO ENVIADO) para {Para} | {Assunto} | {Corpo}", para, assunto, corpo);
        return Task.CompletedTask;
    }
}
