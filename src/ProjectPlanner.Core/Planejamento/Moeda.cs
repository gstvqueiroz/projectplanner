using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectPlanner.Core.Planejamento;

/// <summary>Bloco 35: valores em reais, como o usuário digita e lê ("1.234,56", "R$ 1.234,56", "1234.5").</summary>
public static partial class Moeda
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"1.234,56" (sem o "R$").</summary>
    public static string Formatar(decimal valor) => valor.ToString("#,##0.00", Brasil);

    /// <summary>"R$ 1.234,56".</summary>
    public static string ComSimbolo(decimal valor) => "R$ " + Formatar(valor);

    /// <summary>
    /// Lê um valor em reais: com vírgula decimal e ponto de milhar ("1.234,56"), com ou sem "R$" e espaços; sem vírgula,
    /// um ponto seguido de 1 ou 2 algarismos no fim é a casa decimal ("1234.5", como sai de uma planilha em inglês), e
    /// os demais pontos são de milhar ("1.234"). Vazio é nulo.
    /// </summary>
    /// <exception cref="FormatException">Não é um número.</exception>
    public static decimal? Ler(string? texto)
    {
        var limpo = (texto ?? "").Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace(" ", "").Trim();
        if (limpo.Length == 0)
            return null;
        if (!limpo.Contains(',') && DecimalComPonto().IsMatch(limpo))
            limpo = limpo.Replace('.', ',');
        if (!NumeroBrasileiro().IsMatch(limpo))
            throw new FormatException($"\"{texto?.Trim()}\" não é um valor em reais (use, por exemplo, 1.234,56).");
        return decimal.Parse(limpo.Replace(".", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Brasil);
    }

    [GeneratedRegex(@"^-?\d+\.\d{1,2}$")]
    private static partial Regex DecimalComPonto();

    [GeneratedRegex(@"^-?(\d{1,3}(\.\d{3})+|\d+)(,\d+)?$")]
    private static partial Regex NumeroBrasileiro();
}
