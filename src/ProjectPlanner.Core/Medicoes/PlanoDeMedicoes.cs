using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Medicoes;

/// <summary>Um período do plano de medições: o previsto (do físico-financeiro) e o que as medições aprovadas mediram nele.</summary>
/// <param name="Medicoes">Os números das medições aprovadas que terminam neste período.</param>
public sealed record PeriodoDoPlano(PeriodoFinanceiro Periodo, decimal Previsto, decimal PrevistoAcumulado, IReadOnlyList<int> Medicoes,
    decimal Medido, decimal MedidoAcumulado)
{
    /// <summary>Medido acumulado menos previsto acumulado (negativo: medindo menos que o previsto).</summary>
    public decimal Diferenca => MedidoAcumulado - PrevistoAcumulado;
}

/// <summary>
/// Bloco 36: o plano de medições: os períodos do contrato (semanal, quinzenal, mensal ou datas de corte), com o previsto em
/// cada um (o físico-financeiro da linha de base em uso; sem ela, do cronograma atual) e o medido pelas medições aprovadas
/// (cada uma conta no período em que termina).
/// </summary>
/// <param name="Base">"Linha de base (LB0)" ou "Cronograma atual".</param>
public sealed record PlanoDeMedicoes(string Base, IReadOnlyList<PeriodoDoPlano> Periodos)
{
    /// <exception cref="DomainValidationException">Cronograma não calculado (sem linha de base), ou períodos demais.</exception>
    public static PlanoDeMedicoes Montar(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var contrato = cronograma.Contrato;
        var (inicio, fim) = CalculadoraFisicoFinanceira.Intervalo(cronograma);
        foreach (var medicao in contrato.Medicoes)
            (inicio, fim) = (medicao.De < inicio ? medicao.De : inicio, medicao.Ate > fim ? medicao.Ate : fim);
        var periodos = PeriodosFinanceiros.Gerar(inicio, fim, contrato.Configuracao.Periodicidade, contrato.Configuracao.Cortes);
        var linhaDeBase = cronograma.LinhaDeBaseEmUso;
        var tabela = CalculadoraFisicoFinanceira.Calcular(cronograma,
            linhaDeBase is null ? BaseDoFisicoFinanceiro.Atual : BaseDoFisicoFinanceiro.LinhaDeBase, periodos, LinhasDoFisicoFinanceiro.Etapas);

        var medidoPorPeriodo = new decimal[periodos.Count];
        var medicoesPorPeriodo = periodos.Select(_ => new List<int>()).ToArray();
        foreach (var medicao in contrato.Medicoes.Where(m => m.Travada))
        {
            var i = periodos.ToList().FindIndex(p => medicao.Ate >= p.Inicio && medicao.Ate <= p.Fim);
            medidoPorPeriodo[i] += medicao.Resumo!.MedidoNoPeriodo;
            medicoesPorPeriodo[i].Add(medicao.Numero);
        }
        var (previstoAcumulado, medidoAcumulado) = (0m, 0m);
        var linhas = periodos.Select((p, i) => new PeriodoDoPlano(p, tabela.ValorDoPeriodo[i], previstoAcumulado += tabela.ValorDoPeriodo[i],
            medicoesPorPeriodo[i], medidoPorPeriodo[i], medidoAcumulado += medidoPorPeriodo[i])).ToList();
        return new PlanoDeMedicoes(linhaDeBase is null ? "Cronograma atual" : $"Linha de base (LB{linhaDeBase.Numero})", linhas);
    }
}
