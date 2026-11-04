using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>
/// P73: CPF e RG do paciente (e do responsável legal) saem mascarados da API para quem só visualiza cadastros.
/// Administrador e quem pode incluir ou editar pacientes (recepção) recebem os dados completos, porque precisam deles
/// para cadastrar e corrigir. O banco continua guardando tudo completo: o mascaramento é só na resposta.
/// </summary>
public static class MascaraDados
{
    /// <summary>Completo para o administrador e para quem pode incluir ou editar pacientes; os demais recebem mascarado.</summary>
    public static async Task<bool> PodeVerCompletoAsync(IPermissaoService permissoes, UsuarioAtual usuario, CancellationToken ct)
    {
        if (usuario.Administrador) return true;
        return await permissoes.TemAsync(usuario.PerfilId, usuario.Administrador, "paciente.cadastro:editar", ct)
            || await permissoes.TemAsync(usuario.PerfilId, usuario.Administrador, "paciente.cadastro:incluir", ct);
    }

    /// <summary>CPF no formato ***.123.456-** (mesmo da lista). Valor fora do padrão vira ***.</summary>
    public static string? Cpf(string? cpf)
    {
        var s = cpf?.Trim();
        if (string.IsNullOrEmpty(s)) return cpf;
        var digitos = new string(s.Where(char.IsAsciiDigit).ToArray());
        return digitos.Length == 11 ? CpfUtil.Mascarar(digitos) : "***";
    }

    /// <summary>RG: só os 3 últimos caracteres ficam visíveis (ex.: ******789).</summary>
    public static string? Rg(string? rg)
    {
        var s = rg?.Trim();
        if (string.IsNullOrEmpty(s)) return rg;
        return s.Length <= 3 ? "***" : new string('*', s.Length - 3) + s[^3..];
    }

    public static void Aplicar(ResponsavelLegalDto? r)
    {
        if (r is null) return;
        r.Cpf = Cpf(r.Cpf) ?? string.Empty;
        r.Rg = Rg(r.Rg);
        r.DadosMascarados = true;
    }

    public static void Aplicar(PacienteDto p)
    {
        p.Cpf = Cpf(p.Cpf);
        p.Rg = Rg(p.Rg);
        p.NumeroDocumentoEstrangeiro = Rg(p.NumeroDocumentoEstrangeiro); // RNE / CRNM: mesma regra do RG
        p.DadosMascarados = true;
        Aplicar(p.ResponsavelLegal);
    }
}
