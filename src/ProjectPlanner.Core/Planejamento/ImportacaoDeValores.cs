using System.Globalization;
using System.Text;

namespace ProjectPlanner.Core.Planejamento;

/// <summary>O que aconteceu ao importar valores de uma planilha.</summary>
/// <param name="Aplicados">Tarefas que receberam valor.</param>
/// <param name="NaoEncontradas">EDTs da planilha que não existem no projeto.</param>
/// <param name="Resumos">EDTs de resumos (o valor deles é a soma: ficam de fora).</param>
/// <param name="Invalidas">Linhas com valor que não é número: "linha 7: abc".</param>
public sealed record ResultadoDaImportacaoDeValores(int Aplicados, IReadOnlyList<string> NaoEncontradas, IReadOnlyList<string> Resumos,
    IReadOnlyList<string> Invalidas)
{
    /// <summary>"12 valores importados. 2 EDTs não encontradas: 9.1, 9.2. 1 resumo ignorado (o valor dele é a soma): 1."</summary>
    public string Resumo
    {
        get
        {
            var partes = new List<string> { Aplicados == 1 ? "1 valor importado." : $"{Aplicados} valores importados." };
            if (NaoEncontradas.Count > 0)
                partes.Add((NaoEncontradas.Count == 1 ? "1 EDT não encontrada: " : $"{NaoEncontradas.Count} EDTs não encontradas: ") + Lista(NaoEncontradas) + ".");
            if (Resumos.Count > 0)
                partes.Add((Resumos.Count == 1 ? "1 resumo ignorado" : $"{Resumos.Count} resumos ignorados") + " (o valor dele é a soma): " + Lista(Resumos) + ".");
            if (Invalidas.Count > 0)
                partes.Add((Invalidas.Count == 1 ? "1 valor inválido: " : $"{Invalidas.Count} valores inválidos: ") + Lista(Invalidas) + ".");
            return string.Join(" ", partes);
        }
    }

    private static string Lista(IReadOnlyList<string> itens) =>
        string.Join(", ", itens.Take(8)) + (itens.Count > 8 ? $" e mais {itens.Count - 8}" : "");
}

/// <summary>
/// Bloco 35: os valores das tarefas vindos de uma planilha (a do contrato, ou um orçamento): uma coluna com a EDT e outra com
/// o valor. O cabeçalho é achado nas primeiras linhas pelos nomes (EDT, Item ou Código; Valor, Total ou Preço total); sem
/// cabeçalho, a 1ª coluna é a EDT e a última é o valor.
/// </summary>
public static class ImportacaoDeValores
{
    private static readonly string[] NomesDaEdt = ["edt", "item", "codigo", "cod", "n", "no"];
    private static readonly string[] NomesDoValor = ["valor", "valor total", "total", "preco total", "valor (r$)", "valor r$", "total (r$)", "preco total (r$)"];

    /// <summary>Lê as linhas da planilha e põe os valores nas tarefas (substituindo os que havia).</summary>
    /// <exception cref="DomainValidationException">A planilha não tem nenhuma linha com EDT e valor.</exception>
    public static ResultadoDaImportacaoDeValores Aplicar(Cronograma cronograma, IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(linhas);
        var (cabecalho, colunaEdt, colunaValor) = AcharColunas(linhas);
        var edt = cronograma.NumeracaoEdt().ToDictionary(p => p.Value, p => p.Key);
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var (naoEncontradas, resumos, invalidas) = (new List<string>(), new List<string>(), new List<string>());
        var valores = new Dictionary<Tarefa, decimal>();
        for (var i = cabecalho + 1; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            var codigo = Celula(linha, colunaEdt).TrimEnd('.');
            var texto = Celula(linha, colunaValor);
            if (codigo.Length == 0 || texto.Length == 0)
                continue;
            decimal? valor;
            try
            {
                valor = Moeda.Ler(texto);
            }
            catch (FormatException)
            {
                invalidas.Add($"linha {i + 1}: {texto}");
                continue;
            }
            if (!edt.TryGetValue(codigo, out var id))
                naoEncontradas.Add(codigo);
            else if (porId[id].EhResumo)
                resumos.Add(codigo);
            else if (valor is < 0 or > Tarefa.ValorMaximo)
                invalidas.Add($"linha {i + 1}: {texto}");
            else
                valores[porId[id]] = valor ?? 0;
        }
        if (valores.Count == 0 && naoEncontradas.Count == 0 && resumos.Count == 0 && invalidas.Count == 0)
            throw new DomainValidationException(nameof(linhas),
                "A planilha não tem linhas com EDT e valor. Use uma coluna EDT (ou Item) e uma coluna Valor (ou Total).");
        foreach (var (tarefa, valor) in valores)
            tarefa.DefinirValor(valor);
        return new ResultadoDaImportacaoDeValores(valores.Count, naoEncontradas, resumos, invalidas);
    }

    // O cabeçalho é a primeira linha (até a 20ª) com uma coluna de EDT e uma de valor; sem ele, 1ª e última coluna.
    private static (int Cabecalho, int Edt, int Valor) AcharColunas(IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        for (var i = 0; i < Math.Min(20, linhas.Count); i++)
        {
            var nomes = linhas[i].Select(Normalizar).ToList();
            var colunaEdt = nomes.FindIndex(n => NomesDaEdt.Contains(n));
            var colunaValor = nomes.FindLastIndex(n => NomesDoValor.Contains(n));
            if (colunaEdt >= 0 && colunaValor >= 0 && colunaEdt != colunaValor)
                return (i, colunaEdt, colunaValor);
        }
        var largura = linhas.Count == 0 ? 0 : linhas.Max(l => l.Count);
        return (-1, 0, Math.Max(1, largura - 1));
    }

    private static string Celula(IReadOnlyList<string> linha, int coluna) => coluna < linha.Count ? (linha[coluna] ?? "").Trim() : "";

    private static string Normalizar(string texto)
    {
        var decomposto = (texto ?? "").Trim().ToLowerInvariant().Replace("º", "").Replace("°", "").Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder();
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                limpo.Append(c);
        return limpo.ToString().TrimEnd('.', ':').Trim();
    }
}
