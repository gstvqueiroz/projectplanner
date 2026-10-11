using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Tests;

// Bloco 35: períodos, cronograma físico-financeiro, Curva S e importação de valores. Projeto na seg 28/09/2026:
// Fundação (resumo) > Escavação (5 d: 28/09–02/10, R$ 5.000) → Sapatas (10 d: 05/10–16/10, R$ 10.000);
// Estrutura (10 d: 19/10–30/10, R$ 20.000), depois das Sapatas; Entrega (marco, depois da Estrutura, R$ 1.000).
public class FisicoFinanceiroTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static DateOnly D(int dia, int mes = 10) => new(2026, mes, dia);

    private static (Cronograma C, Dictionary<string, Tarefa> T) Obra(bool comValores = true)
    {
        var c = new Cronograma("Residência", Seg);
        var t = new Dictionary<string, Tarefa>
        {
            ["Fundação"] = new("Fundação", Seg, 1),
            ["Escavação"] = new("Escavação", Seg, 5),
            ["Sapatas"] = new("Sapatas", Seg, 10),
            ["Estrutura"] = new("Estrutura", Seg, 10),
            ["Entrega"] = new("Entrega", Seg, 0),
        };
        c.AdicionarTarefa(t["Fundação"]);
        c.AdicionarTarefa(t["Escavação"], t["Fundação"].Id);
        c.AdicionarTarefa(t["Sapatas"], t["Fundação"].Id);
        c.AdicionarTarefa(t["Estrutura"]);
        c.AdicionarTarefa(t["Entrega"]);
        c.AdicionarPredecessora(new Predecessora(t["Escavação"].Id, t["Sapatas"].Id));
        c.AdicionarPredecessora(new Predecessora(t["Sapatas"].Id, t["Estrutura"].Id));
        c.AdicionarPredecessora(new Predecessora(t["Estrutura"].Id, t["Entrega"].Id));
        if (comValores)
        {
            t["Escavação"].DefinirValor(5000);
            t["Sapatas"].DefinirValor(10000);
            t["Estrutura"].DefinirValor(20000);
            t["Entrega"].DefinirValor(1000);
        }
        CalculadoraCronograma.Calcular(c);
        return (c, t);
    }

    private static IReadOnlyList<PeriodoFinanceiro> Semanas() => PeriodosFinanceiros.Gerar(D(28, 9), D(30), Periodicidade.Semanal);

    // ---- Períodos ----

    [Fact]
    public void Periodos_Semanais_DeSegundaADomingo_OPrimeiroEOUltimoParciais()
    {
        var periodos = PeriodosFinanceiros.Gerar(D(30, 9), D(14), Periodicidade.Semanal);

        periodos.Select(p => (p.Numero, p.Inicio, p.Fim)).Should().Equal(
            (1, D(30, 9), D(4)), (2, D(5), D(11)), (3, D(12), D(14)));
        periodos[0].Rotulo.Should().Be("30/09–04/10");
    }

    [Fact]
    public void Periodos_QuinzenaisEMensais()
    {
        PeriodosFinanceiros.Gerar(D(10), D(20, 11), Periodicidade.Quinzenal).Select(p => (p.Inicio, p.Fim)).Should().Equal(
            (D(10), D(15)), (D(16), D(31)), (D(1, 11), D(15, 11)), (D(16, 11), D(20, 11)));
        var meses = PeriodosFinanceiros.Gerar(D(1), D(15, 12), Periodicidade.Mensal);
        meses.Select(p => p.Rotulo).Should().Equal("out/26", "nov/26", "01/12–15/12");
    }

    [Fact]
    public void Periodos_DatasDeCorte_ForaDoIntervaloIgnoradas()
    {
        var periodos = PeriodosFinanceiros.Gerar(D(1), D(30), Periodicidade.DatasDeCorte, [D(20), D(10), D(10), D(30), new DateOnly(2027, 1, 1)]);

        periodos.Select(p => (p.Inicio, p.Fim)).Should().Equal((D(1), D(10)), (D(11), D(20)), (D(21), D(30)));
        PeriodosFinanceiros.Gerar(D(1), D(30), Periodicidade.DatasDeCorte).Should().ContainSingle();
    }

    [Fact]
    public void Periodos_Recusas_ELerCortes()
    {
        ((Action)(() => PeriodosFinanceiros.Gerar(D(2), D(1), Periodicidade.Mensal))).Should().Throw<DomainValidationException>();
        ((Action)(() => PeriodosFinanceiros.Gerar(D(1), new DateOnly(2040, 1, 1), Periodicidade.Semanal))).Should().Throw<DomainValidationException>()
            .WithMessage("*periodicidade maior*");
        PeriodosFinanceiros.LerCortes("31/10/2026, 30/11/2026\n15/12/26").Should().Equal(D(31), D(30, 11), D(15, 12));
        ((Action)(() => PeriodosFinanceiros.LerCortes("31/13/2026"))).Should().Throw<FormatException>().WithMessage("\"31/13/2026\" não é uma data*");
        PeriodosFinanceiros.LerCortes("  ").Should().BeEmpty();
    }

    // ---- Distribuição ----

    [Fact]
    public void PorEtapa_ValorDistribuidoPelosDiasUteis()
    {
        var (c, _) = Obra();

        var t = CalculadoraFisicoFinanceira.Calcular(c, BaseDoFisicoFinanceiro.Atual, Semanas(), LinhasDoFisicoFinanceiro.Etapas);

        t.PorValor.Should().BeTrue();
        t.Linhas.Select(l => (l.Edt, l.Nome, l.Valor)).Should().Equal(("1", "Fundação", 15000m), ("2", "Estrutura", 20000m), ("3", "Entrega", 1000m));
        t.Linhas[0].ValorPorPeriodo.Take(3).Should().Equal(5000m, 5000m, 5000m);
        t.Linhas[1].ValorPorPeriodo.Should().Equal(0m, 0m, 0m, 10000m, 10000m);
        t.Linhas[2].ValorPorPeriodo.Should().Equal(0m, 0m, 0m, 0m, 1000m);
        (t.ValorTotal, t.ValorDoPeriodo[0], t.ValorAcumulado[^1]).Should().Be((36000m, 5000m, 36000m));
        t.PercentualDoPeriodo[0].Should().BeApproximately(100 * 5000 / 36000.0, 1e-9);
        t.PercentualAcumulado[^1].Should().BeApproximately(100, 1e-9);
        t.Linhas[0].PercentualPorPeriodo.Take(3).Should().AllSatisfy(p => p.Should().BeApproximately(100 / 3.0, 1e-9));
    }

    [Fact]
    public void PorTarefa_ETarefaQueAtravessaOMes_DividePelosDiasUteis()
    {
        var (c, _) = Obra();
        var meses = PeriodosFinanceiros.Gerar(D(28, 9), D(30), Periodicidade.Mensal);

        var t = CalculadoraFisicoFinanceira.Calcular(c, BaseDoFisicoFinanceiro.Atual, meses, LinhasDoFisicoFinanceiro.Tarefas);

        t.Linhas.Select(l => l.Nome).Should().Equal("Escavação", "Sapatas", "Estrutura", "Entrega");
        t.Linhas[0].ValorPorPeriodo.Should().Equal([3000m, 2000m], "3 dos 5 dias em setembro");
        t.ValorDoPeriodo.Should().Equal(3000m, 33000m);
    }

    [Fact]
    public void Centavos_ASomaDaLinhaFechaComOValor()
    {
        var (c, t) = Obra();
        t["Escavação"].DefinirValor(100m);
        t["Sapatas"].DefinirValor(100m); // 10 dias (05/10–16/10): 2 até o corte de 06/10, 5 até 13/10, 3 depois
        t["Estrutura"].DefinirValor(0.10m);
        CalculadoraCronograma.Calcular(c);
        var cortes = PeriodosFinanceiros.Gerar(D(28, 9), D(30), Periodicidade.DatasDeCorte, [D(6), D(13)]);

        var tabela = CalculadoraFisicoFinanceira.Calcular(c, BaseDoFisicoFinanceiro.Atual, cortes, LinhasDoFisicoFinanceiro.Tarefas);

        tabela.Linhas.Should().OnlyContain(l => l.ValorPorPeriodo.Sum() == l.Valor);
        tabela.Linhas[1].ValorPorPeriodo.Should().Equal(20m, 50m, 30m);
    }

    [Fact]
    public void SemValores_PesaPelaDuracao_EMarcoNaoPesa()
    {
        var (c, _) = Obra(comValores: false);

        var t = CalculadoraFisicoFinanceira.Calcular(c, BaseDoFisicoFinanceiro.Atual, Semanas(), LinhasDoFisicoFinanceiro.Etapas);

        (t.PorValor, t.ValorTotal, t.PesoTotal).Should().Be((false, 0m, 25.0));
        t.PercentualDoPeriodo[0].Should().BeApproximately(20, 1e-9, "5 dos 25 dias");
        t.Linhas[2].Peso.Should().Be(0);
    }

    [Fact]
    public void LinhaDeBase_UsaAsDatasEValoresGuardados_EAsRemovidas()
    {
        var (c, t) = Obra();
        c.SalvarLinhaDeBase(null, new DateTime(2026, 9, 28));
        t["Escavação"].AlterarDuracao(10); // tudo anda uma semana
        t["Estrutura"].DefinirValor(30000);
        c.RemoverTarefa(t["Entrega"].Id);
        CalculadoraCronograma.Calcular(c);

        var prevista = CalculadoraFisicoFinanceira.Calcular(c, BaseDoFisicoFinanceiro.LinhaDeBase, Semanas(), LinhasDoFisicoFinanceiro.Etapas);
        var atual = CalculadoraFisicoFinanceira.Calcular(c, BaseDoFisicoFinanceiro.Atual, Semanas(), LinhasDoFisicoFinanceiro.Etapas);

        prevista.Linhas.Select(l => (l.Nome, l.Valor)).Should().Equal(("Fundação", 15000m), ("Estrutura", 20000m), ("Tarefas removidas depois da linha de base", 1000m));
        prevista.ValorDoPeriodo[0].Should().Be(5000m);
        (atual.ValorTotal, atual.ValorDoPeriodo[0]).Should().Be((45000m, 2500m));
        ((Action)(() => CalculadoraFisicoFinanceira.Calcular(new Cronograma("x", Seg), BaseDoFisicoFinanceiro.LinhaDeBase, Semanas(), LinhasDoFisicoFinanceiro.Etapas)))
            .Should().Throw<DomainValidationException>().WithMessage("Não há linha de base em uso para comparar.");
    }

    [Fact]
    public void LinhaDeBaseDa031_SemAMarcaDeResumo_UsaAEstruturaAtual()
    {
        var (c, t) = Obra();
        var lb = c.SalvarLinhaDeBase(null, new DateTime(2026, 9, 28));
        var antiga = new LinhaDeBase(5, null, new DateTime(2026, 9, 28),
            lb.Tarefas.ToDictionary(p => p.Key, p => p.Value with { EhResumo = null }));
        var outro = c.Copiar();
        outro.RemoverLinhaDeBase(0);
        outro.AdicionarLinhaDeBase(antiga, emUso: true);

        var tabela = CalculadoraFisicoFinanceira.Calcular(outro, BaseDoFisicoFinanceiro.LinhaDeBase, Semanas(), LinhasDoFisicoFinanceiro.Etapas);

        tabela.ValorTotal.Should().Be(36000m, "o resumo Fundação não soma de novo");
        _ = t;
    }

    [Fact]
    public void Realizado_PesadoPeloValor_EIntervalo()
    {
        var (c, t) = Obra();
        t["Escavação"].AtualizarPercentualConcluido(100);
        t["Sapatas"].AtualizarPercentualConcluido(50);

        var realizado = CalculadoraFisicoFinanceira.Realizado(c, D(9));

        (realizado.Data, realizado.Valor).Should().Be((D(9), 10000m));
        realizado.Percentual.Should().BeApproximately(100 * 10000 / 36000.0, 1e-9);
        CalculadoraFisicoFinanceira.Intervalo(c).Should().Be((D(28, 9), D(30)));
        CalculadoraFisicoFinanceira.Realizado(new Cronograma("x", Seg), D(1)).Percentual.Should().Be(0);
    }

    [Fact]
    public void CurvaS_PrevistaAtualERealizado()
    {
        var (c, t) = Obra();
        c.SalvarLinhaDeBase(null, new DateTime(2026, 9, 28));
        t["Escavação"].AlterarDuracao(10);
        t["Escavação"].AtualizarPercentualConcluido(40);
        CalculadoraCronograma.Calcular(c);
        var periodos = PeriodosFinanceiros.Gerar(D(28, 9), D(6, 11), Periodicidade.Semanal);

        var curva = CurvaS.Montar(c, periodos, D(7));

        curva.Series.Select(s => s.Nome).Should().Equal("Linha de base (LB0)", "Cronograma atual");
        curva.Series[0].PercentualAcumulado[0].Should().BeGreaterThan(curva.Series[1].PercentualAcumulado[0], "atrasou");
        curva.Series.Should().OnlyContain(s => Math.Abs(s.PercentualAcumulado.Last() - 100) < 1e-9);
        (curva.Realizado!.Data, curva.Realizado.Valor, curva.PorValor).Should().Be((D(7), 2000m, true)); // 40% de R$ 5.000
        curva.PosicaoDe(D(28, 9)).Should().BeApproximately(1 / 7.0, 1e-9);
        curva.PosicaoDe(D(4)).Should().BeApproximately(1, 1e-9);
        curva.PosicaoDe(D(1, 12)).Should().BeNull();
        CurvaS.Montar(c, periodos, D(1, 12)).Realizado.Should().BeNull("hoje fora dos períodos");
        c.UsarLinhaDeBase(null);
        CurvaS.Montar(c, periodos, D(7)).Series.Should().ContainSingle();
    }

    // ---- Importação de valores ----

    private static IReadOnlyList<IReadOnlyList<string>> Planilha(params string[] linhas) =>
        [.. linhas.Select(l => (IReadOnlyList<string>)l.Split(';'))];

    [Fact]
    public void Importar_ComCabecalho_AplicaNasTarefas_EAvisaOResto()
    {
        var (c, t) = Obra(comValores: false);

        var resultado = ImportacaoDeValores.Aplicar(c, Planilha(
            "Planilha orçamentária;;",
            "Item;Descrição;Unid.;Preço total",
            "1;Fundação;;15.000,00",
            "1.1.;Escavação;m³;R$ 4.800,50",
            "1.2;Sapatas;un;9.900",
            "2;Estrutura;;abc",
            "9.9;Outra;;10,00",
            ";;;",
            "3;Entrega;;1000.5"));

        (t["Escavação"].Valor, t["Sapatas"].Valor, t["Entrega"].Valor, t["Estrutura"].Valor).Should().Be((4800.50m, 9900m, 1000.50m, null));
        (resultado.Aplicados, resultado.NaoEncontradas.Single(), resultado.Resumos.Single(), resultado.Invalidas.Single())
            .Should().Be((3, "9.9", "1", "linha 6: abc"));
        resultado.Resumo.Should().Be("3 valores importados. 1 EDT não encontrada: 9.9. 1 resumo ignorado (o valor dele é a soma): 1. 1 valor inválido: linha 6: abc.");
    }

    [Fact]
    public void Importar_SemCabecalho_PrimeiraEUltimaColuna()
    {
        var (c, t) = Obra(comValores: false);

        ImportacaoDeValores.Aplicar(c, Planilha("1.1;Escavação;5.000,00", "2;Estrutura;20000"));

        (t["Escavação"].Valor, t["Estrutura"].Valor).Should().Be((5000m, 20000m));
    }

    [Fact]
    public void Importar_PlanilhaSemNada_Recusada()
    {
        var (c, _) = Obra(comValores: false);

        ((Action)(() => ImportacaoDeValores.Aplicar(c, Planilha("só texto")))).Should().Throw<DomainValidationException>()
            .WithMessage("A planilha não tem linhas com EDT e valor.*");
        ((Action)(() => ImportacaoDeValores.Aplicar(c, []))).Should().Throw<DomainValidationException>();
    }
}
