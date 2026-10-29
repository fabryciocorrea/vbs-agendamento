namespace VBS.Agendamento.Api.Features.Agenda;

/// <summary>
/// Dias em que a clínica fecha, calculados pelo sistema para qualquer ano: feriados nacionais (fixos e Sexta-feira Santa)
/// e os pontos facultativos que a clínica adota (segunda e terça de Carnaval, e a quinta-feira de Corpus Christi).
/// Outros facultativos a clínica inclui pela tela.
/// </summary>
public static class FeriadosNacionais
{
    public static Dictionary<DateOnly, string> Do(int ano)
    {
        var l = new Dictionary<DateOnly, string>
        {
            [new DateOnly(ano, 1, 1)] = "Confraternização Universal",
            [Pascoa(ano).AddDays(-48)] = "Carnaval (segunda-feira)",
            [Pascoa(ano).AddDays(-47)] = "Carnaval (terça-feira)",
            [Pascoa(ano).AddDays(-2)] = "Sexta-feira Santa",
            [Pascoa(ano).AddDays(60)] = "Corpus Christi",
            [new DateOnly(ano, 4, 21)] = "Tiradentes",
            [new DateOnly(ano, 5, 1)] = "Dia do Trabalho",
            [new DateOnly(ano, 9, 7)] = "Independência do Brasil",
            [new DateOnly(ano, 10, 12)] = "Nossa Senhora Aparecida",
            [new DateOnly(ano, 11, 2)] = "Finados",
            [new DateOnly(ano, 11, 15)] = "Proclamação da República",
            [new DateOnly(ano, 12, 25)] = "Natal"
        };
        // Dia da Consciência Negra é feriado nacional desde 2024 (Lei 14.759/2023).
        if (ano >= 2024) l[new DateOnly(ano, 11, 20)] = "Dia da Consciência Negra";
        return l;
    }

    public static bool Eh(DateOnly data) => Do(data.Year).ContainsKey(data);

    /// <summary>Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher).</summary>
    private static DateOnly Pascoa(int ano)
    {
        var a = ano % 19; var b = ano / 100; var c = ano % 100;
        var d = b / 4; var e = b % 4; var f = (b + 8) / 25; var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4; var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var mes = (h + l - 7 * m + 114) / 31;
        var dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia);
    }
}
