using System.Globalization;
using System.Text;

namespace ProjectPlanner.Core.Planejamento;

/// <summary>
/// Bloco 84: as unidades de medida como o canteiro escreve ("m2", "M²", "metros quadrados", "pç"): a forma normal, para conferir que
/// duas são a mesma (a produção apontada em m³ numa tarefa medida em m² é um engano).
/// </summary>
public static class Unidades
{
    private static readonly Dictionary<string, string> Sinonimos = new(StringComparer.Ordinal)
    {
        ["m2"] = "m2", ["mq"] = "m2", ["metroquadrado"] = "m2", ["metrosquadrados"] = "m2",
        ["m3"] = "m3", ["metrocubico"] = "m3", ["metroscubicos"] = "m3",
        ["m"] = "m", ["metro"] = "m", ["metros"] = "m", ["ml"] = "m", ["metrolinear"] = "m", ["metroslineares"] = "m",
        ["un"] = "un", ["und"] = "un", ["unid"] = "un", ["unidade"] = "un", ["unidades"] = "un", ["u"] = "un", ["pc"] = "un", ["peca"] = "un",
        ["pecas"] = "un",
        ["kg"] = "kg", ["quilo"] = "kg", ["quilos"] = "kg", ["quilograma"] = "kg", ["quilogramas"] = "kg",
        ["t"] = "t", ["ton"] = "t", ["tonelada"] = "t", ["toneladas"] = "t",
        ["l"] = "l", ["litro"] = "l", ["litros"] = "l",
        ["sc"] = "sc", ["saco"] = "sc", ["sacos"] = "sc",
        ["h"] = "h", ["hora"] = "h", ["horas"] = "h",
        ["dia"] = "dia", ["dias"] = "dia", ["d"] = "dia",
        ["vb"] = "vb", ["verba"] = "vb",
        ["cj"] = "cj", ["conjunto"] = "cj", ["conjuntos"] = "cj",
        ["pt"] = "pt", ["ponto"] = "pt", ["pontos"] = "pt",
    };

    /// <summary>"M²" e "metros quadrados" → "m2"; "pç" → "un". Uma unidade desconhecida fica só sem acentos, espaços e pontos.</summary>
    public static string Normal(string? unidade)
    {
        var limpa = new StringBuilder();
        foreach (var ch in (unidade ?? "").Trim().ToLowerInvariant().Replace("²", "2").Replace("³", "3").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && ch is not (' ' or '.'))
                limpa.Append(ch);
        var texto = limpa.ToString();
        return Sinonimos.GetValueOrDefault(texto, texto);
    }

    /// <summary>Se as duas são a mesma unidade; nulo se uma está vazia (não dá para conferir).</summary>
    public static bool? Mesma(string? a, string? b) =>
        string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) ? null : Normal(a) == Normal(b);
}
