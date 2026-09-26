using System.Globalization;
using System.Text.RegularExpressions;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Models;

/// <summary>Um item da coluna Predecessoras: a linha da predecessora, o tipo do vínculo e o lag.</summary>
public readonly record struct ItemPredecessora(int Linha, TipoDependencia Tipo, int Lag);

/// <summary>
/// Notação de texto da coluna Predecessoras, no estilo do MS Project em português.
/// </summary>
/// <remarks>
/// <para>
/// Cada item é o número da linha da predecessora, seguido opcionalmente do tipo e do lag em dias úteis:
/// </para>
/// <list type="bullet">
/// <item>tipo: TI (término-início, o padrão), II (início-início), TT (término-término) ou IT (início-término);</item>
/// <item>lag: +n para atraso ou -n para antecipação (lead), com um "d" opcional no fim.</item>
/// </list>
/// <para>
/// Os itens são separados por ";" ou ","; maiúsculas, minúsculas e espaços não importam. Exemplos:
/// "2", "2TI", "3II+2", "4TT-1", "5IT+3d", "2; 3II+2". Texto vazio remove todas as predecessoras.
/// </para>
/// <para>
/// A exibição usa sempre a forma canônica: TI sem lag aparece só como o número ("2"), os demais com
/// o código ("3II+2", "2TI-1"), e os itens vêm separados por "; ".
/// </para>
/// </remarks>
public static partial class NotacaoPredecessoras
{
    private static readonly Dictionary<string, TipoDependencia> TipoPorCodigo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TI"] = TipoDependencia.FS,
        ["II"] = TipoDependencia.SS,
        ["TT"] = TipoDependencia.FF,
        ["IT"] = TipoDependencia.SF,
    };

    /// <exception cref="FormatException">Algum item não segue a notação.</exception>
    public static IReadOnlyList<ItemPredecessora> Interpretar(string? texto)
    {
        var itens = new List<ItemPredecessora>();
        var partes = (texto ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var parte in partes)
        {
            var item = Item().Match(parte);
            if (!item.Success
                || !int.TryParse(item.Groups["linha"].Value, CultureInfo.InvariantCulture, out var linha)
                || !TryLerLag(item, out var lag))
                throw new FormatException(
                    $"Não entendi \"{parte}\" nas predecessoras. Use o número da linha, com tipo e lag " +
                    "opcionais, separados por \";\": 2, 3II, 4TT+2, 5IT-1.");

            var tipo = item.Groups["tipo"].Success ? TipoPorCodigo[item.Groups["tipo"].Value] : TipoDependencia.FS;
            itens.Add(new ItemPredecessora(linha, tipo, lag));
        }
        return itens;
    }

    /// <summary>A sigla do tipo na notação: TI, II, TT ou IT.</summary>
    public static string Sigla(TipoDependencia tipo) => TipoPorCodigo.First(p => p.Value == tipo).Key;

    public static string Formatar(IEnumerable<ItemPredecessora> itens) => string.Join("; ", itens.Select(Formatar));

    public static string Formatar(ItemPredecessora item)
    {
        if (item is { Tipo: TipoDependencia.FS, Lag: 0 })
            return item.Linha.ToString(CultureInfo.InvariantCulture);

        var codigo = TipoPorCodigo.First(p => p.Value == item.Tipo).Key;
        var lag = item.Lag switch
        {
            0 => "",
            > 0 => $"+{item.Lag}",
            _ => item.Lag.ToString(CultureInfo.InvariantCulture),
        };
        return $"{item.Linha}{codigo}{lag}";
    }

    private static bool TryLerLag(Match item, out int lag)
    {
        lag = 0;
        if (!item.Groups["lag"].Success)
            return true;
        if (!int.TryParse(item.Groups["lag"].Value, CultureInfo.InvariantCulture, out lag))
            return false;
        if (item.Groups["sinal"].Value is "-" or "−")
            lag = -lag;
        return true;
    }

    [GeneratedRegex(@"^(?<linha>\d+)\s*(?<tipo>TI|II|TT|IT)?\s*(?:(?<sinal>[+\-−])\s*(?<lag>\d+)\s*d?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Item();
}
