using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Orcamento;

/// <summary>O que uma planilha de referência trouxe: insumos, composições e avisos (linhas puladas).</summary>
public sealed record ResultadoDaImportacaoDeReferencia(IReadOnlyList<Insumo> Insumos, IReadOnlyList<Composicao> Composicoes, IReadOnlyList<string> Avisos)
{
    /// <summary>"5.012 insumos e 0 composições. 3 linhas puladas: …".</summary>
    public string Resumo
    {
        get
        {
            var texto = $"{Insumos.Count.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} insumos e " +
                        $"{Composicoes.Count.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} composições.";
            return Avisos.Count == 0 ? texto
                : $"{texto} {(Avisos.Count == 1 ? "1 linha pulada" : $"{Avisos.Count} linhas puladas")}: {string.Join("; ", Avisos.Take(5))}{(Avisos.Count > 5 ? "…" : "")}.";
        }
    }
}

/// <summary>
/// Bloco 33: lê as planilhas de uma base de preços (as da Caixa para a SINAPI, ou uma base própria no mesmo jeito). O
/// cabeçalho é achado nas primeiras 40 linhas pelos nomes das colunas, então os títulos acima dele e a ordem das colunas não
/// importam:
/// <list type="bullet">
/// <item>Insumos: Código, Descrição, Unidade e Preço (ou Custo, Valor; "preço mediano" na SINAPI) e, se houver, Classe (ou Tipo).</item>
/// <item>Composições (analítico): Código da composição, Tipo do item (INSUMO ou COMPOSIÇÃO), Código do item e Coeficiente; a
/// descrição e a unidade da composição vêm da linha dela ou das colunas Descrição/Unidade da composição. Com Descrição,
/// Unidade e Preço do item, os insumos que faltam na planilha de insumos também entram.</item>
/// </list>
/// </summary>
public static class ImportacaoDeReferencia
{
    /// <exception cref="DomainValidationException">A planilha não tem as colunas de insumos nem as de composições.</exception>
    public static ResultadoDaImportacaoDeReferencia Ler(IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        for (var i = 0; i < Math.Min(40, linhas.Count); i++)
        {
            var nomes = linhas[i].Select(TiposDeInsumo.Normalizar).ToList();
            var composicao = nomes.FindIndex(n => n.Contains("codigo") && n.Contains("composic"));
            var itemCodigo = nomes.FindIndex(n => n.Contains("codigo") && n.Contains("item"));
            var coeficiente = nomes.FindIndex(n => n.Contains("coeficiente"));
            if (composicao >= 0 && itemCodigo >= 0 && coeficiente >= 0)
                return LerComposicoes(linhas, i, nomes, composicao, itemCodigo, coeficiente);
            var codigo = nomes.FindIndex(n => n.StartsWith("codigo") || n is "cod" or "cod." || n.StartsWith("cod "));
            var descricao = nomes.FindIndex(n => n.StartsWith("descricao"));
            var preco = nomes.FindIndex(n => n.Contains("preco mediano"));
            if (preco < 0)
                preco = nomes.FindIndex(n => n.StartsWith("preco") || n.StartsWith("custo") || n.StartsWith("valor"));
            if (codigo >= 0 && descricao >= 0 && preco >= 0)
                return LerInsumos(linhas, i, nomes, codigo, descricao, preco);
        }
        throw new DomainValidationException(nameof(linhas),
            "A planilha não parece uma base de preços: faltam as colunas Código, Descrição, Unidade e Preço (insumos) ou Código da composição, Código do item e Coeficiente (composições analíticas).");
    }

    private static ResultadoDaImportacaoDeReferencia LerInsumos(IReadOnlyList<IReadOnlyList<string>> linhas, int cabecalho, List<string> nomes,
        int codigo, int descricao, int preco)
    {
        var unidade = nomes.FindIndex(n => n.StartsWith("unid") || n is "un" or "und");
        var classe = nomes.FindIndex(n => n.StartsWith("classe") || n.StartsWith("tipo") || n.StartsWith("categoria") || n.StartsWith("grupo"));
        var (insumos, avisos) = (new List<Insumo>(), new List<string>());
        for (var i = cabecalho + 1; i < linhas.Count; i++)
        {
            var cod = Celula(linhas[i], codigo);
            if (cod.Length == 0)
                continue;
            if (Numero(Celula(linhas[i], preco)) is not { } valor)
            {
                avisos.Add($"linha {i + 1} ({cod}): preço inválido");
                continue;
            }
            var und = Celula(linhas[i], unidade);
            insumos.Add(new Insumo(cod, Celula(linhas[i], descricao), und, TiposDeInsumo.Inferir(Celula(linhas[i], classe), und), valor));
        }
        return new ResultadoDaImportacaoDeReferencia(insumos, [], avisos);
    }

    private static ResultadoDaImportacaoDeReferencia LerComposicoes(IReadOnlyList<IReadOnlyList<string>> linhas, int cabecalho, List<string> nomes,
        int composicao, int itemCodigo, int coeficiente)
    {
        var tipoItem = nomes.FindIndex(n => n.Contains("tipo") && n.Contains("item"));
        var descricaoComposicao = nomes.FindIndex(n => n.StartsWith("descricao") && n.Contains("composic"));
        var unidadeComposicao = nomes.FindIndex(n => n.StartsWith("unid") && n.Contains("composic"));
        var descricaoItem = nomes.FindIndex(n => n.StartsWith("descricao") && n.Contains("item"));
        var unidadeItem = nomes.FindIndex(n => n.StartsWith("unid") && n.Contains("item"));
        var precoItem = nomes.FindIndex(n => (n.StartsWith("preco") || n.StartsWith("custo unit")) && !n.Contains("total"));
        // Sem "unidade da composição": a primeira coluna "unidade" que não é a do item.
        if (unidadeComposicao < 0)
            unidadeComposicao = nomes.FindIndex(n => n.StartsWith("unid") && !n.Contains("item"));
        if (descricaoComposicao < 0)
            descricaoComposicao = nomes.FindIndex(n => n.StartsWith("descricao") && !n.Contains("item") && !n.Contains("classe") && !n.Contains("tipo"));

        var dados = new Dictionary<string, (string Descricao, string Unidade, List<ItemDeComposicao> Itens)>();
        var insumos = new Dictionary<string, Insumo>();
        var avisos = new List<string>();
        for (var i = cabecalho + 1; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            var cod = Celula(linha, composicao);
            if (cod.Length == 0)
                continue;
            if (!dados.TryGetValue(cod, out var atual))
                dados[cod] = atual = ("", "", []);
            var descricao = Celula(linha, descricaoComposicao);
            var unidade = Celula(linha, unidadeComposicao);
            if (atual.Descricao.Length == 0 && descricao.Length > 0)
                dados[cod] = atual = (descricao, unidade.Length > 0 ? unidade : atual.Unidade, atual.Itens);
            else if (atual.Unidade.Length == 0 && unidade.Length > 0)
                dados[cod] = atual = (atual.Descricao, unidade, atual.Itens);
            var codItem = Celula(linha, itemCodigo);
            if (codItem.Length == 0)
                continue; // a linha da própria composição
            if (Numero(Celula(linha, coeficiente)) is not { } coef)
            {
                avisos.Add($"linha {i + 1} ({cod}): coeficiente inválido");
                continue;
            }
            var ehComposicao = TiposDeInsumo.Normalizar(Celula(linha, tipoItem)).StartsWith("composic");
            atual.Itens.Add(new ItemDeComposicao(ehComposicao, codItem, coef));
            if (!ehComposicao && Numero(Celula(linha, precoItem)) is { } preco && !insumos.ContainsKey(codItem))
            {
                var und = Celula(linha, unidadeItem);
                insumos[codItem] = new Insumo(codItem, Celula(linha, descricaoItem), und, TiposDeInsumo.Inferir(null, und), preco);
            }
        }
        return new ResultadoDaImportacaoDeReferencia([.. insumos.Values],
            [.. dados.Where(d => d.Value.Itens.Count > 0).Select(d => new Composicao(d.Key, d.Value.Descricao, d.Value.Unidade, d.Value.Itens))], avisos);
    }

    private static string Celula(IReadOnlyList<string> linha, int coluna) => coluna >= 0 && coluna < linha.Count ? (linha[coluna] ?? "").Trim() : "";

    private static decimal? Numero(string texto)
    {
        try
        {
            return Moeda.Ler(texto);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Junta o que veio de várias planilhas da mesma base: os insumos da planilha de insumos valem mais que os achados no
    /// analítico; as composições se somam.
    /// </summary>
    public static TabelaDeReferencia Montar(BaseDeReferencia @base, IEnumerable<ResultadoDaImportacaoDeReferencia> partes)
    {
        ArgumentNullException.ThrowIfNull(partes);
        var lista = partes.ToList();
        var insumos = new Dictionary<string, Insumo>();
        foreach (var insumo in lista.Where(p => p.Composicoes.Count > 0).SelectMany(p => p.Insumos))
            insumos[insumo.Codigo] = insumo;
        foreach (var insumo in lista.Where(p => p.Composicoes.Count == 0).SelectMany(p => p.Insumos))
            insumos[insumo.Codigo] = insumo; // a planilha de insumos (com a classe) vale mais
        return new TabelaDeReferencia(@base, insumos.Values, lista.SelectMany(p => p.Composicoes));
    }
}
