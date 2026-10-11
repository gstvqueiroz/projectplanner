using System.Globalization;
using System.Text;

namespace ProjectPlanner.Core.Planejamento;

/// <summary>O que aconteceu ao importar valores de uma planilha.</summary>
/// <param name="Aplicados">Tarefas que receberam valor.</param>
/// <param name="NaoEncontradas">EDTs da planilha que não existem no projeto.</param>
/// <param name="Resumos">EDTs de resumos (o valor deles é a soma: ficam de fora).</param>
/// <param name="Invalidas">Linhas com valor que não é número: "linha 7: abc".</param>
/// <param name="ComQuantidades">Bloco 36: a planilha tinha as colunas de unidade e quantidade, e elas também foram importadas.</param>
public sealed record ResultadoDaImportacaoDeValores(int Aplicados, IReadOnlyList<string> NaoEncontradas, IReadOnlyList<string> Resumos,
    IReadOnlyList<string> Invalidas, bool ComQuantidades = false)
{
    /// <summary>"12 valores importados. 2 EDTs não encontradas: 9.1, 9.2. 1 resumo ignorado (o valor dele é a soma): 1."</summary>
    public string Resumo
    {
        get
        {
            var comQuantidades = ComQuantidades && Aplicados > 0 ? " (com unidade e quantidade)" : "";
            var partes = new List<string> { (Aplicados == 1 ? "1 valor importado" : $"{Aplicados} valores importados") + comQuantidades + "." };
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
/// cabeçalho, a 1ª coluna é a EDT e a última é o valor. Bloco 36: com as colunas Unidade (Un, Unid) e Quantidade (Quant, Qtd)
/// no cabeçalho, elas também vão para as tarefas (para medir por quantidade).
/// </summary>
public static class ImportacaoDeValores
{
    private static readonly string[] NomesDaEdt = ["edt", "item", "codigo", "cod", "n", "no"];
    private static readonly string[] NomesDoValor = ["valor", "valor total", "total", "preco total", "valor (r$)", "valor r$", "total (r$)", "preco total (r$)"];
    private static readonly string[] NomesDaUnidade = ["un", "und", "unid", "unidade"];
    private static readonly string[] NomesDaQuantidade = ["quant", "quantidade", "qtd", "qtde", "qt"];

    /// <summary>Lê as linhas da planilha e põe os valores nas tarefas (substituindo os que havia).</summary>
    /// <exception cref="DomainValidationException">
    /// A planilha não tem nenhuma linha com EDT e valor, ou o contrato está travado (Bloco 36: há medição aprovada).
    /// </exception>
    public static ResultadoDaImportacaoDeValores Aplicar(Cronograma cronograma, IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(linhas);
        if (cronograma.Contrato.Travado)
            throw new DomainValidationException(nameof(Cronograma.Contrato), Medicoes.ContratoDoProjeto.MotivoDoTravamento);
        var (cabecalho, colunaEdt, colunaValor, colunaUnidade, colunaQuantidade) = AcharColunas(linhas);
        var comQuantidades = colunaUnidade >= 0 && colunaQuantidade >= 0;
        var edt = cronograma.NumeracaoEdt().ToDictionary(p => p.Value, p => p.Key);
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var (naoEncontradas, resumos, invalidas) = (new List<string>(), new List<string>(), new List<string>());
        var valores = new Dictionary<Tarefa, (decimal Valor, string? Unidade, decimal? Quantidade)>();
        for (var i = cabecalho + 1; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            var codigo = Celula(linha, colunaEdt).TrimEnd('.');
            var texto = Celula(linha, colunaValor);
            if (codigo.Length == 0 || texto.Length == 0)
                continue;
            decimal? valor, quantidade = null;
            var unidade = comQuantidades ? Celula(linha, colunaUnidade) : "";
            try
            {
                valor = Moeda.Ler(texto);
                if (comQuantidades)
                    quantidade = Moeda.Ler(Celula(linha, colunaQuantidade));
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
            else if (valor is < 0 or > Tarefa.ValorMaximo || quantidade is < 0 or > Tarefa.QuantidadeMaxima || unidade.Length > Tarefa.TamanhoMaximoDaUnidade)
                invalidas.Add($"linha {i + 1}: {texto}");
            else
                valores[porId[id]] = (valor ?? 0, unidade.Length == 0 ? null : unidade, quantidade is 0 ? null : quantidade);
        }
        if (valores.Count == 0 && naoEncontradas.Count == 0 && resumos.Count == 0 && invalidas.Count == 0)
            throw new DomainValidationException(nameof(linhas),
                "A planilha não tem linhas com EDT e valor. Use uma coluna EDT (ou Item) e uma coluna Valor (ou Total).");
        foreach (var (tarefa, (valor, unidade, quantidade)) in valores)
        {
            tarefa.DefinirValor(valor);
            if (!comQuantidades)
                continue;
            tarefa.DefinirUnidade(unidade);
            tarefa.DefinirQuantidade(quantidade);
        }
        return new ResultadoDaImportacaoDeValores(valores.Count, naoEncontradas, resumos, invalidas, comQuantidades);
    }

    // O cabeçalho é a primeira linha (até a 20ª) com uma coluna de EDT e uma de valor; sem ele, 1ª e última coluna (e sem
    // unidade nem quantidade: −1).
    private static (int Cabecalho, int Edt, int Valor, int Unidade, int Quantidade) AcharColunas(IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        for (var i = 0; i < Math.Min(20, linhas.Count); i++)
        {
            var nomes = linhas[i].Select(Normalizar).ToList();
            var colunaEdt = nomes.FindIndex(n => NomesDaEdt.Contains(n));
            var colunaValor = nomes.FindLastIndex(n => NomesDoValor.Contains(n));
            if (colunaEdt >= 0 && colunaValor >= 0 && colunaEdt != colunaValor)
                return (i, colunaEdt, colunaValor, nomes.FindIndex(n => NomesDaUnidade.Contains(n)), nomes.FindIndex(n => NomesDaQuantidade.Contains(n)));
        }
        var largura = linhas.Count == 0 ? 0 : linhas.Max(l => l.Count);
        return (-1, 0, Math.Max(1, largura - 1), -1, -1);
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
