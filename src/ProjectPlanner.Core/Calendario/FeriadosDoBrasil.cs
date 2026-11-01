namespace ProjectPlanner.Core;

/// <summary>Bloco 71: um feriado nacional (ou ponto facultativo) num ano.</summary>
public sealed record FeriadoNacional(DateOnly Data, string Nome, bool Facultativo);

/// <summary>
/// Bloco 71: os feriados nacionais do Brasil (Lei 662/1949 e as seguintes, com a Lei 14.759/2023, que fez do 20 de novembro
/// feriado nacional a partir de 2024) e os pontos facultativos que as obras costumam parar: Carnaval (segunda e terça) e
/// Corpus Christi. Os móveis saem da Páscoa (o cálculo gregoriano de Meeus/Jones/Butcher).
/// </summary>
public static class FeriadosDoBrasil
{
    /// <summary>O domingo de Páscoa do ano (calendário gregoriano).</summary>
    public static DateOnly Pascoa(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia);
    }

    /// <summary>Os feriados do ano, em ordem; os que cairiam fora das datas que o .NET representa ficam de fora.</summary>
    public static IReadOnlyList<FeriadoNacional> Do(int ano)
    {
        if (ano is < 1 or > 9999)
            return [];
        var lista = new List<FeriadoNacional>
        {
            new(new DateOnly(ano, 1, 1), "Confraternização Universal", false),
            new(new DateOnly(ano, 4, 21), "Tiradentes", false),
            new(new DateOnly(ano, 5, 1), "Dia do Trabalho", false),
            new(new DateOnly(ano, 9, 7), "Independência do Brasil", false),
            new(new DateOnly(ano, 10, 12), "Nossa Senhora Aparecida", false),
            new(new DateOnly(ano, 11, 2), "Finados", false),
            new(new DateOnly(ano, 11, 15), "Proclamação da República", false),
            new(new DateOnly(ano, 12, 25), "Natal", false),
        };
        if (ano >= 2024)
            lista.Add(new(new DateOnly(ano, 11, 20), "Dia Nacional de Zumbi e da Consciência Negra", false));
        if (ano >= 1583) // a Páscoa gregoriana
        {
            var pascoa = Pascoa(ano);
            lista.Add(new(pascoa.AddDays(-48), "Carnaval (segunda-feira)", true));
            lista.Add(new(pascoa.AddDays(-47), "Carnaval (terça-feira)", true));
            lista.Add(new(pascoa.AddDays(-2), "Sexta-feira Santa", false));
            lista.Add(new(pascoa.AddDays(60), "Corpus Christi", true));
        }
        return [.. lista.OrderBy(f => f.Data)];
    }
}
