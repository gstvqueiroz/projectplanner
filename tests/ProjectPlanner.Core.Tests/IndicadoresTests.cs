using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Tests;

// Bloco 37: custos realizados, importação de custos, valor agregado, painel de indicadores e a Curva S com o medido e o
// custo real. Projeto na seg 28/09/2026: Fundação (resumo) > Escavação (5 d: 28/09–02/10, R$ 5.000) → Sapatas (10 d:
// 05/10–16/10, R$ 10.000); Estrutura (10 d: 19/10–30/10, R$ 20.000); Entrega (marco, 30/10, sem valor). Orçamento R$ 35.000.
public class IndicadoresTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static DateOnly D(int dia, int mes = 10) => new(2026, mes, dia);

    private static (Cronograma C, Dictionary<string, Tarefa> T) Obra()
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
        t["Escavação"].DefinirValor(5000);
        t["Sapatas"].DefinirValor(10000);
        t["Estrutura"].DefinirValor(20000);
        CalculadoraCronograma.Calcular(c);
        return (c, t);
    }

    private static LancamentoDeCusto Custo(DateOnly data, Guid? tarefa, decimal valor, CategoriaDeCusto categoria = CategoriaDeCusto.Material) =>
        new(Guid.NewGuid(), data, tarefa, categoria, "Custo", null, valor);

    // Na sex 09/10: a escavação feita (100%) e 30% das sapatas; custos de R$ 4.000 (01/10) e R$ 6.000 (08/10), e um geral
    // de R$ 1.000 em 20/10.
    private static (Cronograma C, Dictionary<string, Tarefa> T) EmAndamento()
    {
        var (c, t) = Obra();
        t["Escavação"].AtualizarPercentualConcluido(100);
        t["Sapatas"].AtualizarPercentualConcluido(30);
        c.Custos.Adicionar(Custo(D(1), t["Escavação"].Id, 4000, CategoriaDeCusto.MaoDeObra));
        c.Custos.Adicionar(Custo(D(8), t["Sapatas"].Id, 6000));
        c.Custos.Adicionar(Custo(D(20), null, 1000));
        return (c, t);
    }

    // ---- Custos realizados ----

    [Fact]
    public void Lancamento_ValidaELimpa()
    {
        var l = new LancamentoDeCusto(Guid.NewGuid(), D(1), null, CategoriaDeCusto.Equipamento, "  Betoneira ", " ", 123.456m);

        (l.Descricao, l.Documento, l.Valor).Should().Be(("Betoneira", null, 123.46m));
        ((Action)(() => new LancamentoDeCusto(Guid.NewGuid(), D(1), null, CategoriaDeCusto.Outros, " ", null, 1)))
            .Should().Throw<DomainValidationException>().WithMessage("Escreva a descrição do custo.");
        ((Action)(() => new LancamentoDeCusto(Guid.NewGuid(), D(1), null, CategoriaDeCusto.Outros, "x", null, 0)))
            .Should().Throw<DomainValidationException>().WithMessage("O valor do custo precisa ser maior que zero.");
        ((Action)(() => new LancamentoDeCusto(Guid.Empty, D(1), null, CategoriaDeCusto.Outros, "x", null, 1))).Should().Throw<DomainValidationException>();
        ((Action)(() => new LancamentoDeCusto(Guid.NewGuid(), D(1), null, (CategoriaDeCusto)99, "x", null, 1))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Custos_NaOrdemDaData_AteUmaData_SubstituirERemover()
    {
        var (c, t) = EmAndamento();
        var primeiro = c.Custos.Lancamentos[0];

        c.Custos.Lancamentos.Select(l => l.Data).Should().BeInAscendingOrder();
        (c.Custos.Total, c.Custos.Ate(D(8)), c.Custos.Ate(D(30, 9))).Should().Be((11000m, 10000m, 0m));
        c.Custos.Substituir(new LancamentoDeCusto(primeiro.Id, D(2), t["Escavação"].Id, CategoriaDeCusto.MaoDeObra, "Folha", "FP 10", 4500));
        c.Custos.Total.Should().Be(11500);
        ((Action)(() => c.Custos.Adicionar(c.Custos.Lancamentos[0]))).Should().Throw<DomainValidationException>().WithMessage("*já está na lista*");
        c.Custos.Remover(primeiro.Id);
        ((Action)(() => c.Custos.Remover(primeiro.Id))).Should().Throw<DomainValidationException>();
        var copia = c.Copiar();
        copia.Custos.Remover(copia.Custos.Lancamentos[0].Id);
        (c.Custos.Lancamentos.Count, copia.Custos.Lancamentos.Count).Should().Be((2, 1), "a cópia é independente (Desfazer)");
    }

    [Theory]
    [InlineData("Folha de pagamento", CategoriaDeCusto.MaoDeObra)]
    [InlineData("MÃO DE OBRA", CategoriaDeCusto.MaoDeObra)]
    [InlineData("materiais", CategoriaDeCusto.Material)]
    [InlineData("Locação", CategoriaDeCusto.Equipamento)]
    [InlineData("Serviços de terceiros", CategoriaDeCusto.Terceiros)]
    [InlineData("Adm.", CategoriaDeCusto.Administracao)]
    [InlineData("Outros", CategoriaDeCusto.Outros)]
    [InlineData("xyz", null)]
    [InlineData("", null)]
    public void Categorias_LidasDeUmaPlanilha(string texto, CategoriaDeCusto? esperada) => Categorias.Ler(texto).Should().Be(esperada);

    [Fact]
    public void ImportarCustos_ColunasPeloCabecalho_DatasDoExcel_EProblemas()
    {
        var (c, t) = Obra();

        var resultado = ImportacaoDeCustos.Aplicar(c, [
            ["Data", "EDT", "Categoria", "Descrição", "NF", "Valor"],
            ["01/10/2026", "1.1", "Folha", "Pedreiros", "FP 09", "4.000,00"],
            ["46303", "", "materiais", "", "NF 12", "1.500"],
            ["31/02/2026", "1", "x", "Errada", "", "10"],
            ["02/10/2026", "9.9", "equipamento", "Betoneira", "", "300"],
            ["03/10/2026", "", "", "Sem valor", "", ""],
        ]);

        resultado.Resumo.Should().Be("3 custos importados (R$ 5.800,00). 1 EDT não encontrada (lançada como custo geral): 9.9. " +
                                     "2 linhas inválidas: linha 4: 31/02/2026, linha 6: sem valor.");
        c.Custos.Lancamentos.Select(l => (l.Data, l.TarefaId, l.Categoria, l.Descricao, l.Documento, l.Valor)).Should().Equal(
            (D(1), t["Escavação"].Id, CategoriaDeCusto.MaoDeObra, "Pedreiros", "FP 09", 4000m),
            (D(2), null, CategoriaDeCusto.Equipamento, "Betoneira", null, 300m),
            (D(8), null, CategoriaDeCusto.Material, "Material", "NF 12", 1500m));
        ((Action)(() => ImportacaoDeCustos.Aplicar(c, [["1", "2"]]))).Should().Throw<DomainValidationException>().WithMessage("*Data e Valor*");
        ((Action)(() => ImportacaoDeCustos.Aplicar(c, [["Data", "Valor"], ["", ""]]))).Should().Throw<DomainValidationException>()
            .WithMessage("A planilha não tem nenhuma linha com data e valor.");
    }

    // ---- Valor agregado ----

    [Fact]
    public void ValorAgregado_PlanejadoAgregadoECusto_IndicesEProjecoes()
    {
        var (c, _) = EmAndamento();

        var v = PainelDeIndicadores.Montar(c, D(9), Periodicidade.Semanal).ValorAgregado;

        (v.Base, v.Orcamento, v.ValorPlanejado, v.ValorAgregado, v.CustoReal).Should().Be(("Cronograma atual", 35000m, 10000m, 8000m, 10000m));
        (v.Idp, v.Idc).Should().Be((0.8, 0.8));
        (v.VariacaoDePrazo, v.VariacaoDeCusto, v.EstimativaNoTermino, v.EstimativaParaTerminar, v.VariacaoNoTermino)
            .Should().Be((-2000m, -2000m, 43750m, 33750m, -8750m));
        v.FisicoPrevisto.Should().BeApproximately(28.571, 0.001);
        v.FisicoRealizado.Should().BeApproximately(22.857, 0.001);
        (v.TerminoPrevisto, v.TerminoAtual, v.TerminoPelaTendencia).Should().Be((null, D(30), D(10, 11)),
            "25 dias úteis previstos ÷ 0,8 = 32: termina em 10/11");
    }

    [Fact]
    public void ValorAgregado_SemCustoNemPlanejado_SemIndices()
    {
        var (c, _) = Obra();

        var v = PainelDeIndicadores.Montar(c, D(25, 9), Periodicidade.Mensal).ValorAgregado;

        (v.ValorPlanejado, v.Idp, v.Idc, v.EstimativaNoTermino, v.TerminoPelaTendencia).Should().Be((0m, null, null, null, null));
    }

    [Fact]
    public void ValorAgregado_ComLinhaDeBase_PlanejadoPelasDatasDela()
    {
        var (c, t) = EmAndamento();
        c.SalvarLinhaDeBase(null, Seg);
        t["Sapatas"].AlterarDuracao(15); // atrasou: no atual, metade do planejado em 09/10 cai
        CalculadoraCronograma.Calcular(c);

        var v = PainelDeIndicadores.Montar(c, D(9), Periodicidade.Semanal).ValorAgregado;

        (v.Base, v.ValorPlanejado, v.TerminoPrevisto, v.TerminoAtual).Should().Be(("Linha de base (LB0)", 10000m, D(30), D(6, 11)));
    }

    [Fact]
    public void Montar_SemCalculo_Recusa()
    {
        var c = new Cronograma("P", Seg);
        c.AdicionarTarefa(new Tarefa("A", Seg, 2));

        ((Action)(() => PainelDeIndicadores.Montar(c, D(1), Periodicidade.Mensal))).Should().Throw<DomainValidationException>()
            .WithMessage("Calcule o cronograma antes dos indicadores.");
    }

    // ---- Painel ----

    [Fact]
    public void Painel_EtapasAtrasosEContrato()
    {
        var (c, t) = EmAndamento();
        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { PercentualDeRetencao = 10 });
        c.Contrato.NovaMedicao(D(28, 9), D(9));
        c.Contrato.Medir(1, t["Escavação"].Id, 100);
        c.Contrato.Medir(1, t["Sapatas"].Id, 30);
        c.Contrato.Aprovar(1, new DateTime(2026, 10, 9, 18, 0, 0));

        var painel = PainelDeIndicadores.Montar(c, D(9), Periodicidade.Semanal);

        painel.Etapas.Select(e => (e.Edt, e.Orcamento, e.ValorPlanejado, e.ValorAgregado, e.Medido, e.CustoReal)).Should().Equal(
            ("1", 15000m, 10000m, 8000m, 8000m, 10000m), ("2", 20000m, 0m, 0m, 0m, 0m));
        painel.Etapas[0].Desvio.Should().Be(-2000);
        painel.CustosGerais.Should().Be(0, "o custo geral é de 20/10");
        painel.Atrasos.Should().Equal(new AtrasoNoPainel("1.2", "Sapatas", 40, 30, D(16), null));
        (painel.TotalDeAtrasadas, painel.ValorContratado, painel.Medido, painel.Saldo, painel.Retido, painel.MedicoesAprovadas)
            .Should().Be((1, 35000m, 8000m, 27000m, 800m, 1));
        painel.FinanceiroMedido.Should().BeApproximately(22.857, 0.001);
        PainelDeIndicadores.Montar(c, D(31), Periodicidade.Semanal).CustosGerais.Should().Be(1000);
    }

    [Fact]
    public void CurvaS_ComMedidoCustoRealEHistorico()
    {
        var (c, t) = EmAndamento();
        c.Contrato.NovaMedicao(D(28, 9), D(9));
        c.Contrato.Medir(1, t["Escavação"].Id, 100);
        c.Contrato.Medir(1, t["Sapatas"].Id, 30);
        c.Contrato.Aprovar(1, new DateTime(2026, 10, 9, 18, 0, 0));

        var curva = PainelDeIndicadores.Montar(c, D(12), Periodicidade.Semanal).Curva;

        curva.Series.Select(s => (s.Nome, s.Tipo)).Should().Equal(("Cronograma atual", TipoDaSerie.Atual), ("Medido", TipoDaSerie.Medida),
            ("Custo real", TipoDaSerie.CustoReal));
        curva.Series[1].ValorAcumulado.Should().Equal(0m, 8000m);
        curva.Series[2].ValorAcumulado.Should().Equal([4000m, 10000m, 10000m], "até a semana de hoje (12/10)");
        curva.Historico.Should().ContainSingle().Which.Should().Be(new RealizadoNaData(D(9), 22.857142857142858, 0));
    }

    [Fact]
    public void CurvaS_SemMedicoesNemCustos_ComoNoBloco35()
    {
        var (c, _) = Obra();

        var curva = CurvaS.Montar(c, PeriodosFinanceiros.Gerar(D(28, 9), D(30), Periodicidade.Mensal), D(1));

        curva.Series.Should().ContainSingle().Which.Tipo.Should().Be(TipoDaSerie.Atual);
        curva.Historico.Should().BeEmpty();
    }
}
