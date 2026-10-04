using System.Reflection;

namespace ProjectPlanner.App.Services;

/// <summary>
/// As novidades de uma versão, lidas do CHANGELOG.md que vai embutido no programa (Bloco 25): a tela Sobre e o arquivo
/// nunca divergem. Cada novidade é um item "- " da seção "## &lt;versão&gt;"; uma linha recuada continua o item de cima.
/// </summary>
public static class NovidadesDaVersao
{
    private const string Recurso = "ProjectPlanner.App.CHANGELOG.md";

    /// <summary>O CHANGELOG.md embutido; vazio se não estiver lá.</summary>
    public static string Changelog()
    {
        using var fluxo = Assembly.GetExecutingAssembly().GetManifestResourceStream(Recurso);
        if (fluxo is null)
            return "";
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }

    /// <summary>As novidades da versão no texto do changelog; vazia se a versão não aparece nele.</summary>
    public static IReadOnlyList<string> Ler(string changelog, string versao)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        var itens = new List<string>();
        var naSecao = false;
        foreach (var linha in changelog.Replace("\r\n", "\n").Split('\n'))
        {
            if (linha.StartsWith("## ", StringComparison.Ordinal))
            {
                if (naSecao)
                    break;
                naSecao = linha[3..].Trim() == versao;
                continue;
            }
            if (!naSecao)
                continue;
            if (linha.StartsWith("- ", StringComparison.Ordinal))
                itens.Add(linha[2..].Trim());
            else if (linha.StartsWith("  ", StringComparison.Ordinal) && itens.Count > 0 && linha.Trim().Length > 0)
                itens[^1] += " " + linha.Trim();
        }
        return itens;
    }
}
