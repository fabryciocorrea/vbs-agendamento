using System.Text.RegularExpressions;

namespace VBS.Agendamento.Api.Common;

public static class CpfUtil
{
    /// <summary>Somente os 11 dígitos, com dígitos verificadores corretos.</summary>
    public static bool Valido(string? cpf)
    {
        if (cpf is null || cpf.Length != 11 || !cpf.All(char.IsAsciiDigit)) return false;
        if (cpf.Distinct().Count() == 1) return false;

        int Digito(int quantidade)
        {
            var soma = 0;
            for (var i = 0; i < quantidade; i++) soma += (cpf[i] - '0') * (quantidade + 1 - i);
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(9) == cpf[9] - '0' && Digito(10) == cpf[10] - '0';
    }

    /// <summary>Mostra só os dígitos do meio (E1): ***.982.247-**</summary>
    public static string Mascarar(string cpf) => $"***.{cpf.Substring(3, 3)}.{cpf.Substring(6, 3)}-**";
}

public static class CnpjUtil
{
    /// <summary>Somente os 14 dígitos, com dígitos verificadores corretos.</summary>
    public static bool Valido(string? cnpj)
    {
        if (cnpj is null || cnpj.Length != 14 || !cnpj.All(char.IsAsciiDigit)) return false;
        if (cnpj.Distinct().Count() == 1) return false;

        int Digito(int quantidade)
        {
            var pesos = quantidade == 12
                ? new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }
                : new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            var soma = 0;
            for (var i = 0; i < quantidade; i++) soma += (cnpj[i] - '0') * pesos[i];
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(12) == cnpj[12] - '0' && Digito(13) == cnpj[13] - '0';
    }
}

public static class Formatos
{
    public static readonly Regex Cep = new(@"^[0-9]{8}$", RegexOptions.Compiled);
    public static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    public static readonly Regex Telefone = new(@"^[0-9]{10,13}$", RegexOptions.Compiled);

    /// <summary>D4: mínimo de 10 caracteres, com letras, números e pelo menos um caractere especial.</summary>
    public static readonly Regex Senha = new(@"^(?=.*[A-Za-z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{10,128}$", RegexOptions.Compiled);

    public static readonly HashSet<string> Ufs = new(StringComparer.Ordinal)
    {
        "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO"
    };

    /// <summary>Escapa % _ [ para uso em LIKE sem cláusula ESCAPE.</summary>
    public static string EscaparLike(string texto) =>
        texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}

/// <summary>Datas de regra de negócio valem no horário de Brasília.</summary>
public static class DataBrasil
{
    private static readonly TimeZoneInfo Fuso = ObterFuso();

    private static TimeZoneInfo ObterFuso()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // O Brasil não tem horário de verão desde 2019: UTC-3 fixo serve de reserva.
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
    }

    public static DateOnly Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Fuso));

    public static int Idade(DateOnly nascimento, DateOnly hoje)
    {
        var idade = hoje.Year - nascimento.Year;
        if (nascimento > hoje.AddYears(-idade)) idade--;
        return idade;
    }

    /// <summary>O SQL Server devolve DATETIME2 sem informação de fuso; tudo no banco é UTC.</summary>
    public static DateTime Utc(DateTime valor) => DateTime.SpecifyKind(valor, DateTimeKind.Utc);
    public static DateTime? Utc(DateTime? valor) => valor.HasValue ? DateTime.SpecifyKind(valor.Value, DateTimeKind.Utc) : null;
}
