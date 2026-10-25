using ProjectPlanner.Core.Indicadores;

namespace ProjectPlanner.Core.OrdensDeServico;

/// <summary>Bloco 62: a retenção de garantia paga ao executante no fim: a data, o documento, o valor e os custos lançados.</summary>
public sealed record LiberacaoDaRetencao(DateOnly Data, string? Documento, decimal Valor, IReadOnlyList<Guid> Lancamentos);

/// <summary>Bloco 60: o pagamento de uma medição: a data, o documento e os custos realizados que ele lançou.</summary>
/// <param name="Lancamentos">Os ids dos lançamentos de custo (um por tarefa), para desfazer o pagamento.</param>
public sealed record PagamentoDaMedicao(DateOnly Data, string? Documento, IReadOnlyList<Guid> Lancamentos);

/// <summary>
/// Bloco 60: uma medição da OS: o número (M1, M2…), a data, a quantidade medida de cada item nesta medição (na ordem dos
/// itens), o valor e, depois, o pagamento.
/// </summary>
public sealed record MedicaoDaOrdem
{
    /// <exception cref="DomainValidationException">Id vazio, número menor que 1, quantidade negativa ou valor negativo.</exception>
    public MedicaoDaOrdem(Guid id, int numero, DateOnly data, IReadOnlyList<decimal> quantidades, decimal valor, PagamentoDaMedicao? pagamento = null)
    {
        ArgumentNullException.ThrowIfNull(quantidades);
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id da medição é obrigatório.");
        if (numero < 1)
            throw new DomainValidationException(nameof(Numero), "O número da medição começa em 1.");
        if (quantidades.Any(q => q < 0))
            throw new DomainValidationException(nameof(Quantidades), "Uma quantidade medida não pode ser negativa.");
        if (valor < 0)
            throw new DomainValidationException(nameof(Valor), "O valor da medição não pode ser negativo.");
        (Id, Numero, Data, Quantidades, Valor, Pagamento) = (id, numero, data, [.. quantidades], valor, pagamento);
    }

    public Guid Id { get; }

    public int Numero { get; }

    /// <summary>"M2".</summary>
    public string Codigo => $"M{Numero}";

    public DateOnly Data { get; }

    public IReadOnlyList<decimal> Quantidades { get; }

    public decimal Valor { get; }

    public PagamentoDaMedicao? Pagamento { get; init; }

    public bool Paga => Pagamento is not null;
}

/// <summary>Bloco 60: os custos realizados de um pagamento e o avanço das tarefas pelo que foi medido.</summary>
public static class PagamentoDasOrdens
{
    /// <summary>
    /// Os lançamentos de custo do pagamento de uma medição: um por tarefa (o que foi medido nos itens dela; os avulsos, como custo
    /// geral da obra), na categoria do executante (equipe própria: mão de obra; terceiro: serviços de terceiros).
    /// </summary>
    /// <remarks>Bloco 62: com retenção, lança o líquido (o valor menos a retenção), repartido pelas tarefas.</remarks>
    public static IReadOnlyList<LancamentoDeCusto> Lancamentos(OrdemDeServico ordem, MedicaoDaOrdem medicao, DateOnly data, string? documento)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        ArgumentNullException.ThrowIfNull(medicao);
        var porTarefa = ordem.Dados.Itens.Select((item, i) => (item.TarefaId, Valor: Math.Round(medicao.Quantidades[i] * item.PrecoUnitario, 2, MidpointRounding.AwayFromZero)))
            .GroupBy(x => x.TarefaId).Select(g => (Tarefa: g.Key, Valor: g.Sum(x => x.Valor))).ToList();
        return Repartir(ordem, porTarefa, medicao.Valor - ordem.RetencaoDe(medicao), data, documento ?? "", $"{ordem.Codigo}/{medicao.Codigo}", medicao.Codigo);
    }

    /// <summary>Bloco 62: os lançamentos da retenção liberada, repartida pelas tarefas como o medido.</summary>
    public static IReadOnlyList<LancamentoDeCusto> LancamentosDaRetencao(OrdemDeServico ordem, DateOnly data, string? documento)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        var porTarefa = ordem.Dados.Itens.Select((item, i) => (item.TarefaId, Valor: ordem.Medicoes.Sum(m => Math.Round(m.Quantidades[i] * item.PrecoUnitario, 2, MidpointRounding.AwayFromZero))))
            .GroupBy(x => x.TarefaId).Select(g => (Tarefa: g.Key, Valor: g.Sum(x => x.Valor))).ToList();
        return Repartir(ordem, porTarefa, ordem.Retido, data, documento ?? "", $"{ordem.Codigo}/retenção", "retenção");
    }

    // Reparte o total pelas tarefas na proporção do valor de cada uma (o último leva a diferença dos centavos).
    private static List<LancamentoDeCusto> Repartir(OrdemDeServico ordem, List<(Guid? Tarefa, decimal Valor)> porTarefa, decimal total, DateOnly data,
        string documento, string documentoPadrao, string rotulo)
    {
        var categoria = ordem.Dados.Tipo == TipoDeExecutante.EquipePropria ? CategoriaDeCusto.MaoDeObra : CategoriaDeCusto.Terceiros;
        var doc = string.IsNullOrWhiteSpace(documento) ? documentoPadrao : documento.Trim();
        if (doc.Length > LancamentoDeCusto.TamanhoMaximoDoDocumento)
            doc = doc[..LancamentoDeCusto.TamanhoMaximoDoDocumento];
        var descricao = $"{ordem.Codigo} {rotulo} · {ordem.Dados.Executante ?? "executante"}: {ordem.Dados.Titulo}";
        if (descricao.Length > LancamentoDeCusto.TamanhoMaximoDaDescricao)
            descricao = descricao[..(LancamentoDeCusto.TamanhoMaximoDaDescricao - 1)] + "…";
        var partes = porTarefa.Where(x => x.Valor > 0).ToList();
        var bruto = partes.Sum(x => x.Valor);
        if (bruto <= 0 || total <= 0)
            return [];
        var valores = partes.Select(x => Math.Round(total * x.Valor / bruto, 2, MidpointRounding.AwayFromZero)).ToList();
        valores[^1] += total - valores.Sum();
        return [.. partes.Select((x, i) => (x.Tarefa, Valor: valores[i])).Where(x => x.Valor > 0)
            .Select(x => new LancamentoDeCusto(Guid.NewGuid(), data, x.Tarefa, categoria, descricao, doc, x.Valor))];
    }

    /// <summary>
    /// O % de cada tarefa da OS pelo que foi medido nos itens dela, pesado pelo valor (itens de unidades diferentes somam); sem
    /// preços, pela quantidade.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> PercentualDasTarefas(OrdemDeServico ordem)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        return ordem.Dados.Itens.Select((item, i) => (item, i)).Where(x => x.item.TarefaId is not null)
            .GroupBy(x => x.item.TarefaId!.Value)
            .ToDictionary(g => g.Key, g =>
            {
                var porValor = g.Any(x => x.item.PrecoUnitario > 0);
                decimal Peso(ItemDaOrdem item) => porValor ? item.PrecoUnitario : 1;
                var contratado = g.Sum(x => x.item.Quantidade * Peso(x.item));
                var medido = g.Sum(x => ordem.MedidoDoItem(x.i) * Peso(x.item));
                return contratado <= 0 ? 0 : (int)Math.Clamp(Math.Floor(medido / contratado * 100), 0, 100);
            });
    }
}
