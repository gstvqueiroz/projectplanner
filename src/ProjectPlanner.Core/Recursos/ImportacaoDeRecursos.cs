using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Recursos;

/// <summary>O que aconteceu ao importar recursos de uma planilha.</summary>
/// <param name="Novos">Recursos que não existiam no projeto (ou no catálogo).</param>
/// <param name="Atualizados">Recursos com o mesmo nome e tipo, que tiveram o custo, a unidade, o disponível e o fornecedor atualizados.</param>
/// <param name="Invalidas">Linhas que não deram um recurso: "linha 5: sem nome".</param>
public sealed record ResultadoDaImportacaoDeRecursos(IReadOnlyList<Recurso> Novos, IReadOnlyList<Recurso> Atualizados, IReadOnlyList<string> Invalidas)
{
    /// <summary>"3 recursos novos, 2 atualizados. 1 linha inválida: linha 5: sem nome."</summary>
    public string Resumo
    {
        get
        {
            var partes = new List<string>
            {
                (Novos.Count == 1 ? "1 recurso novo" : $"{Novos.Count} recursos novos") + ", " +
                (Atualizados.Count == 1 ? "1 atualizado." : $"{Atualizados.Count} atualizados."),
            };
            if (Invalidas.Count > 0)
                partes.Add((Invalidas.Count == 1 ? "1 linha inválida: " : $"{Invalidas.Count} linhas inválidas: ") +
                           string.Join(", ", Invalidas.Take(8)) + (Invalidas.Count > 8 ? $" e mais {Invalidas.Count - 8}" : "") + ".");
            return string.Join(" ", partes);
        }
    }
}

/// <summary>
/// Bloco 32: recursos de uma planilha (a de insumos da empresa, ou o catálogo exportado): as colunas Nome (ou Recurso,
/// Insumo, Descrição) e Custo (ou Preço, Custo unitário), e, se houver, Tipo, Unidade, Disponível (por dia) e Fornecedor.
/// Sem a coluna Tipo, o tipo vem da unidade ("dia" ou "h": profissional; outra: material). Um recurso com o mesmo nome e
/// tipo de um que já existe é atualizado (o custo de uma obra para outra muda).
/// </summary>
public static class ImportacaoDeRecursos
{
    private static readonly string[] NomesDoNome = ["nome", "recurso", "insumo", "descricao", "descricao do insumo"];
    private static readonly string[] NomesDoCusto = ["custo", "custo unitario", "preco", "preco unitario", "valor unitario", "custo (r$)", "preco (r$)"];
    private static readonly string[] NomesDoTipo = ["tipo", "categoria", "classe"];
    private static readonly string[] NomesDaUnidade = ["unidade", "un", "und", "unid"];
    private static readonly string[] NomesDoDisponivel = ["disponivel", "disponivel por dia", "quantidade disponivel", "limite"];
    private static readonly string[] NomesDoFornecedor = ["fornecedor"];

    /// <summary>Lê os recursos; os que já existem em <paramref name="existentes"/> (mesmo nome e tipo) voltam atualizados, com o mesmo Id.</summary>
    /// <exception cref="DomainValidationException">A planilha não tem as colunas Nome e Custo.</exception>
    public static ResultadoDaImportacaoDeRecursos Ler(IReadOnlyList<IReadOnlyList<string>> linhas, IReadOnlyList<Recurso> existentes)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        ArgumentNullException.ThrowIfNull(existentes);
        var colunas = AcharColunas(linhas)
                      ?? throw new DomainValidationException(nameof(linhas),
                          "A planilha precisa de um cabeçalho com as colunas Nome e Custo (e, se quiser, Tipo, Unidade, Disponível e Fornecedor).");
        var (novos, atualizados, invalidas) = (new List<Recurso>(), new List<Recurso>(), new List<string>());
        for (var i = colunas.Cabecalho + 1; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            var nome = Celula(linha, colunas.Nome);
            var textoDoCusto = Celula(linha, colunas.Custo);
            if (nome.Length == 0 && textoDoCusto.Length == 0)
                continue;
            if (nome.Length == 0)
            {
                invalidas.Add($"linha {i + 1}: sem nome");
                continue;
            }
            var unidade = Celula(linha, colunas.Unidade);
            var tipo = TiposDeRecurso.Ler(Celula(linha, colunas.Tipo))
                       ?? (TiposDeRecurso.Normalizar(unidade) is "dia" or "d" or "h" or "hora" or "mes" ? TipoDeRecurso.Profissional : TipoDeRecurso.Material);
            try
            {
                var existente = existentes.Concat(novos).FirstOrDefault(r => r.Tipo == tipo && string.Equals(r.Nome, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));
                var recurso = new Recurso(existente?.Id ?? Guid.NewGuid(), nome, tipo, unidade, Moeda.Ler(textoDoCusto) ?? 0,
                    Moeda.LerNumero(Celula(linha, colunas.Disponivel)), Celula(linha, colunas.Fornecedor));
                if (existente is null)
                    novos.Add(recurso);
                else if (novos.Contains(existente))
                    novos[novos.IndexOf(existente)] = recurso;
                else
                {
                    atualizados.RemoveAll(r => r.Id == recurso.Id);
                    atualizados.Add(recurso);
                }
            }
            catch (Exception ex) when (ex is DomainValidationException or FormatException)
            {
                invalidas.Add($"linha {i + 1}: {ex.Message.TrimEnd('.')}");
            }
        }
        return new ResultadoDaImportacaoDeRecursos(novos, atualizados, invalidas);
    }

    /// <summary>Os recursos numa tabela (para exportar em CSV): Nome, Tipo, Unidade, Custo, Disponível e Fornecedor.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Tabela(IEnumerable<Recurso> recursos) =>
    [
        ["Nome", "Tipo", "Unidade", "Custo unitário (R$)", "Disponível por dia", "Fornecedor"],
        .. recursos.Select(r => (IReadOnlyList<string>)[r.Nome, TiposDeRecurso.Texto(r.Tipo), r.Unidade, Moeda.Formatar(r.CustoUnitario),
            r.Disponivel is { } d ? Moeda.FormatarQuantidade(d) : "", r.Fornecedor ?? ""]),
    ];

    private sealed record Colunas(int Cabecalho, int Nome, int Custo, int Tipo, int Unidade, int Disponivel, int Fornecedor);

    private static Colunas? AcharColunas(IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        for (var i = 0; i < Math.Min(20, linhas.Count); i++)
        {
            var nomes = linhas[i].Select(TiposDeRecurso.Normalizar).ToList();
            var nome = nomes.FindIndex(n => NomesDoNome.Contains(n));
            var custo = nomes.FindIndex(n => NomesDoCusto.Contains(n));
            if (nome >= 0 && custo >= 0)
                return new Colunas(i, nome, custo, nomes.FindIndex(n => NomesDoTipo.Contains(n)), nomes.FindIndex(n => NomesDaUnidade.Contains(n)),
                    nomes.FindIndex(n => NomesDoDisponivel.Contains(n)), nomes.FindIndex(n => NomesDoFornecedor.Contains(n)));
        }
        return null;
    }

    private static string Celula(IReadOnlyList<string> linha, int coluna) => coluna >= 0 && coluna < linha.Count ? (linha[coluna] ?? "").Trim() : "";
}
