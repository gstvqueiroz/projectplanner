using System.Globalization;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Indicadores;

/// <summary>O que aconteceu ao importar custos de uma planilha.</summary>
/// <param name="EdtsNaoEncontradas">EDTs que não existem no projeto: os custos delas entraram como custo geral da obra.</param>
/// <param name="Invalidas">Linhas sem data ou com valor que não serve: "linha 7: 31/02/2026".</param>
public sealed record ResultadoDaImportacaoDeCustos(int Importados, decimal Total, IReadOnlyList<string> EdtsNaoEncontradas, IReadOnlyList<string> Invalidas)
{
    /// <summary>"12 custos importados (R$ 34.500,00). 1 EDT não encontrada (lançada como custo geral): 9. 1 linha inválida: linha 7: x."</summary>
    public string Resumo
    {
        get
        {
            var partes = new List<string> { (Importados == 1 ? "1 custo importado" : $"{Importados} custos importados") + $" ({Moeda.ComSimbolo(Total)})." };
            if (EdtsNaoEncontradas.Count > 0)
                partes.Add((EdtsNaoEncontradas.Count == 1 ? "1 EDT não encontrada" : $"{EdtsNaoEncontradas.Count} EDTs não encontradas") +
                           $" (lançada{(EdtsNaoEncontradas.Count == 1 ? "" : "s")} como custo geral): {Lista(EdtsNaoEncontradas)}.");
            if (Invalidas.Count > 0)
                partes.Add((Invalidas.Count == 1 ? "1 linha inválida: " : $"{Invalidas.Count} linhas inválidas: ") + Lista(Invalidas) + ".");
            return string.Join(" ", partes);
        }
    }

    private static string Lista(IReadOnlyList<string> itens) => string.Join(", ", itens.Take(8)) + (itens.Count > 8 ? $" e mais {itens.Count - 8}" : "");
}

/// <summary>
/// Bloco 37: custos realizados vindos de uma planilha (a do financeiro da obra): uma linha por custo, com as colunas Data e
/// Valor e, se houver, Descrição (ou Histórico), EDT, Categoria (ou Tipo) e Documento (ou NF). O cabeçalho é achado nas
/// primeiras linhas pelos nomes. Uma categoria que não é reconhecida vira "Outros".
/// </summary>
public static class ImportacaoDeCustos
{
    private static readonly string[] NomesDaData = ["data", "dt", "data de emissao", "emissao", "data do pagamento", "pagamento"];
    private static readonly string[] NomesDoValor = ["valor", "valor (r$)", "valor r$", "total", "valor total", "total (r$)"];
    private static readonly string[] NomesDaDescricao = ["descricao", "historico", "discriminacao", "fornecedor"];
    private static readonly string[] NomesDaEdt = ["edt", "codigo", "tarefa", "item"];
    private static readonly string[] NomesDaCategoria = ["categoria", "tipo", "classe", "natureza"];
    private static readonly string[] NomesDoDocumento = ["documento", "doc", "nf", "nota", "nota fiscal", "n da nf"];

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Lê as linhas da planilha e acrescenta os custos (os que já havia ficam).</summary>
    /// <exception cref="DomainValidationException">A planilha não tem as colunas Data e Valor, ou nenhuma linha com custo.</exception>
    public static ResultadoDaImportacaoDeCustos Aplicar(Cronograma cronograma, IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(linhas);
        var colunas = AcharColunas(linhas)
                      ?? throw new DomainValidationException(nameof(linhas),
                          "A planilha precisa de um cabeçalho com as colunas Data e Valor (e, se quiser, Descrição, EDT, Categoria e Documento).");
        var edt = cronograma.NumeracaoEdt().ToDictionary(p => p.Value, p => p.Key);
        var (novos, naoEncontradas, invalidas) = (new List<LancamentoDeCusto>(), new List<string>(), new List<string>());
        for (var i = colunas.Cabecalho + 1; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            var textoDaData = Celula(linha, colunas.Data);
            var textoDoValor = Celula(linha, colunas.Valor);
            if (textoDaData.Length == 0 && textoDoValor.Length == 0)
                continue;
            if (LerData(textoDaData) is not { } data)
            {
                invalidas.Add($"linha {i + 1}: {(textoDaData.Length == 0 ? "sem data" : textoDaData)}");
                continue;
            }
            decimal? valor;
            try
            {
                valor = Moeda.Ler(textoDoValor);
            }
            catch (FormatException)
            {
                valor = null;
            }
            if (valor is not > 0 || valor > Tarefa.ValorMaximo)
            {
                invalidas.Add($"linha {i + 1}: {(textoDoValor.Length == 0 ? "sem valor" : textoDoValor)}");
                continue;
            }
            Guid? tarefa = null;
            var codigo = Celula(linha, colunas.Edt).TrimEnd('.');
            if (codigo.Length > 0)
            {
                if (edt.TryGetValue(codigo, out var id))
                    tarefa = id;
                else if (!naoEncontradas.Contains(codigo))
                    naoEncontradas.Add(codigo);
            }
            var categoria = Categorias.Ler(Celula(linha, colunas.Categoria)) ?? CategoriaDeCusto.Outros;
            var descricao = Celula(linha, colunas.Descricao);
            var documento = Celula(linha, colunas.Documento);
            try
            {
                novos.Add(new LancamentoDeCusto(Guid.NewGuid(), data, tarefa, categoria,
                    descricao.Length > 0 ? Cortar(descricao, LancamentoDeCusto.TamanhoMaximoDaDescricao) : Categorias.Texto(categoria),
                    Cortar(documento, LancamentoDeCusto.TamanhoMaximoDoDocumento), valor.Value));
            }
            catch (DomainValidationException)
            {
                invalidas.Add($"linha {i + 1}");
            }
        }
        if (novos.Count == 0 && invalidas.Count == 0)
            throw new DomainValidationException(nameof(linhas), "A planilha não tem nenhuma linha com data e valor.");
        foreach (var lancamento in novos)
            cronograma.Custos.Adicionar(lancamento);
        return new ResultadoDaImportacaoDeCustos(novos.Count, novos.Sum(l => l.Valor), naoEncontradas, invalidas);
    }

    /// <summary>Uma data de planilha: dd/mm/aaaa, aaaa-mm-dd, ou o número de série do Excel (como sai de uma célula de data).</summary>
    public static DateOnly? LerData(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (DateOnly.TryParseExact(t, ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd.MM.yyyy", "dd-MM-yyyy"], Brasil, DateTimeStyles.None, out var data))
            return data;
        if (double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var serie) && serie is >= 20000 and <= 80000)
            return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serie)));
        return null;
    }

    private sealed record Colunas(int Cabecalho, int Data, int Valor, int Descricao, int Edt, int Categoria, int Documento);

    private static Colunas? AcharColunas(IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        for (var i = 0; i < Math.Min(20, linhas.Count); i++)
        {
            var nomes = linhas[i].Select(Categorias.Normalizar).ToList();
            var data = nomes.FindIndex(n => NomesDaData.Contains(n));
            var valor = nomes.FindLastIndex(n => NomesDoValor.Contains(n));
            if (data >= 0 && valor >= 0 && data != valor)
                return new Colunas(i, data, valor, nomes.FindIndex(n => NomesDaDescricao.Contains(n)), nomes.FindIndex(n => NomesDaEdt.Contains(n)),
                    nomes.FindIndex(n => NomesDaCategoria.Contains(n)), nomes.FindIndex(n => NomesDoDocumento.Contains(n)));
        }
        return null;
    }

    private static string Celula(IReadOnlyList<string> linha, int coluna) => coluna >= 0 && coluna < linha.Count ? (linha[coluna] ?? "").Trim() : "";

    private static string Cortar(string texto, int tamanho) => texto.Length <= tamanho ? texto : texto[..tamanho];
}
