namespace ProjectPlanner.Core.OrdensDeServico;

/// <summary>Bloco 65: um item no boletim: o contratado, o medido antes, nesta medição, o acumulado, o saldo e o %.</summary>
public sealed record LinhaDoBoletimDaOrdem(int Numero, ItemDaOrdem Item, decimal Anterior, decimal NestaMedicao)
{
    public decimal Acumulado => Anterior + NestaMedicao;

    public decimal Saldo => Math.Max(0, Item.Quantidade - Acumulado);

    /// <summary>O % acumulado do item (0 a 100).</summary>
    public decimal Percentual => Item.Quantidade == 0 ? 0 : Math.Round(Acumulado / Item.Quantidade * 100, 1, MidpointRounding.AwayFromZero);

    public decimal ValorNestaMedicao => Math.Round(NestaMedicao * Item.PrecoUnitario, 2, MidpointRounding.AwayFromZero);

    public decimal ValorAcumulado => Math.Round(Acumulado * Item.PrecoUnitario, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Bloco 65: o boletim de medição de uma OS (para pagar o executante): cada item com o contratado, o medido antes, nesta medição,
/// o acumulado, o saldo e o %; o valor desta medição, a retenção, o líquido a pagar e o acumulado da OS.
/// </summary>
public sealed record BoletimDaOrdem(OrdemDeServico Ordem, MedicaoDaOrdem Medicao, IReadOnlyList<LinhaDoBoletimDaOrdem> Linhas)
{
    public decimal ValorNestaMedicao => Medicao.Valor;

    public decimal Retencao => Ordem.RetencaoDe(Medicao);

    public decimal Liquido => ValorNestaMedicao - Retencao;

    /// <summary>O medido nas medições anteriores a esta (em valor).</summary>
    public decimal MedidoAnterior => Ordem.Medicoes.TakeWhile(m => m.Id != Medicao.Id).Sum(m => m.Valor);

    public decimal MedidoAcumulado => MedidoAnterior + ValorNestaMedicao;

    public decimal Saldo => Math.Max(0, Ordem.Total - MedidoAcumulado);

    public decimal PercentualAcumulado => Ordem.Total == 0 ? 0 : Math.Round(MedidoAcumulado / Ordem.Total * 100, 1, MidpointRounding.AwayFromZero);

    /// <summary>"OS-003 · Boletim de medição M2".</summary>
    public string Titulo => $"{Ordem.Codigo} · Boletim de medição {Medicao.Codigo}";

    /// <exception cref="DomainValidationException">A medição não é da OS.</exception>
    public static BoletimDaOrdem Montar(OrdemDeServico ordem, Guid medicaoId)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        var indice = ordem.Medicoes.ToList().FindIndex(m => m.Id == medicaoId);
        if (indice < 0)
            throw new DomainValidationException(nameof(medicaoId), "A medição não é desta OS.");
        var medicao = ordem.Medicoes[indice];
        var anteriores = ordem.Medicoes.Take(indice).ToList();
        return new BoletimDaOrdem(ordem, medicao, [.. ordem.Dados.Itens.Select((item, i) =>
            new LinhaDoBoletimDaOrdem(i + 1, item, anteriores.Sum(m => m.Quantidades[i]), medicao.Quantidades[i]))]);
    }
}
