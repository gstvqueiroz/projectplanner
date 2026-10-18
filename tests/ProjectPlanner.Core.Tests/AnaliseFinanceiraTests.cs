using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 43: os indicadores ampliados. A obra do Bloco 37 (seg 28/09/2026): Fundação > Escavação (5 d, R$ 5.000) → Sapatas
// (10 d, R$ 10.000); Estrutura (10 d, R$ 20.000); Entrega (marco). Custo orçado: a escavação pelos recursos (pedreiro
// R$ 200/dia × 2 por dia × 5 dias = R$ 2.000) e as sapatas pelo orçamento (10 m³ × R$ 600 = R$ 6.000); a estrutura sem.
// Em 09/10: escavação 100%, sapatas 30%; custos R$ 4.000 (01/10, escavação) e R$ 6.000 (08/10, sapatas), mais um geral de
// R$ 1.000 em 20/10; a medição 1 (28/09 a 09/10) mede 100% da escavação e 3 dos 10 m³ das sapatas (30%).
public class AnaliseFinanceiraTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static DateOnly D(int dia, int mes = 10) => new(2026, mes, dia);

    private static (Cronograma C, Dictionary<string, Tarefa> T) Obra(bool emAndamento = true)
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
        if (!emAndamento)
            return (c, t);

        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, null, 200);
        c.Recursos.Adicionar(pedreiro);
        c.Recursos.Atribuir(t["Escavação"], [new AtribuicaoDeRecurso(pedreiro.Id, 2)]);
        t["Sapatas"].DefinirQuantidade(10);
        c.Orcamento.Associar(t["Sapatas"], new ComposicaoAberta(new BaseDeReferencia("SINAPI"), "1", "CONCRETO", "M3",
            [new InsumoNaComposicao("1", "CONCRETO", "M3", TipoDeInsumo.Material, 1, 600)]));
        t["Escavação"].AtualizarPercentualConcluido(100);
        t["Sapatas"].AtualizarPercentualConcluido(30);
        c.Custos.Adicionar(Custo(D(1), t["Escavação"].Id, 4000));
        c.Custos.Adicionar(Custo(D(8), t["Sapatas"].Id, 6000));
        c.Custos.Adicionar(Custo(D(20), null, 1000));
        c.Contrato.NovaMedicao(D(28, 9), D(9));
        c.Contrato.Medir(1, t["Escavação"].Id, 100);
        c.Contrato.Medir(1, t["Sapatas"].Id, 3); // 3 de 10 m³ (com quantidade, mede-se a quantidade)
        c.Contrato.Aprovar(1, new DateTime(2026, 10, 9, 18, 0, 0));
        return (c, t);
    }

    private static Indicadores.LancamentoDeCusto Custo(DateOnly data, Guid? tarefa, decimal valor) =>
        new(Guid.NewGuid(), data, tarefa, CategoriaDeCusto.Material, "Custo", null, valor);

    private static AnaliseFinanceira Analise(Cronograma c, DateOnly data) =>
        AnaliseFinanceira.Montar(c, PainelDeIndicadores.Montar(c, data, Periodicidade.Mensal), Periodicidade.Mensal);

    [Fact]
    public void CustoOrcado_PeloOrcamento_SenaoPelosRecursos_SenaoNulo()
    {
        var (c, t) = Obra();
        c.Recursos.Atribuir(t["Sapatas"], [new AtribuicaoDeRecurso(c.Recursos.Lista[0].Id, 1)]);

        AnaliseFinanceira.CustoOrcado(c, t["Sapatas"]).Should().Be(6000, "o orçamento vale mais que os recursos");
        AnaliseFinanceira.CustoOrcado(c, t["Escavação"]).Should().Be(2000);
        AnaliseFinanceira.CustoOrcado(c, t["Estrutura"]).Should().BeNull();
    }

    [Fact]
    public void Etapas_DoOrcadoAoRealizado_ComAsProjecoes()
    {
        var (c, _) = Obra();

        var a = Analise(c, D(9));

        a.Etapas.Select(e => e.Nome).Should().Equal(["Fundação", "Estrutura"], "a Entrega não tem valor, custo nem medição");
        var f = a.Etapas[0];
        (f.Contratado, f.CustoOrcado, f.MargemPrevista, f.CustoOrcadoDoFeito, f.CustoReal, f.DesvioDeCusto)
            .Should().Be((15000m, 8000m, 7000m, 3800m, 10000m, -6200m), "orçado do feito: 2.000 × 100% + 6.000 × 30%");
        f.MargemPrevistaPercentual.Should().BeApproximately(46.67, 0.01);
        f.IndiceDeCusto.Should().BeApproximately(0.38, 0.0001);
        (f.CustoFinalProjetado, f.MargemProjetada, f.FaltaGastar).Should().Be((21052.63m, -6052.63m, 11052.63m));
        f.Fisico.Should().BeApproximately(53.33, 0.01, "pesado pelo Valor: (5.000 × 100% + 10.000 × 30%) ÷ 15.000");
        (f.Medido, f.DiferencaDaMedicao, f.AMedir).Should().Be((8000m, f.PercentualMedido - f.Fisico, 0m));
        f.DiferencaDaMedicao.Should().BeApproximately(0, 0.001);

        var e = a.Etapas[1];
        (e.Contratado, e.CustoOrcado, e.MargemPrevista, e.DesvioDeCusto, e.CustoFinalProjetado, e.Fisico).Should().Be((20000m, null, null, null, null, 0d));

        (a.Total.Contratado, a.Total.CustoOrcado, a.Total.CustoReal, a.CustosGerais, a.CustoRealTotal).Should().Be((35000m, 8000m, 10000m, 0m, 10000m),
            "o custo geral de 20/10 é depois da data");
        (a.Total.ContratadoComCusto, a.Total.CustoParcial, a.Total.MargemPrevista, a.Total.MargemProjetada).Should().Be((15000m, true, 7000m, -6052.63m),
            "a estrutura, sem custo orçado, fica fora da margem (senão ela pareceria 100% de lucro)");
        a.MaioresDesvios.Should().ContainSingle().Which.Nome.Should().Be("Fundação");
    }

    [Fact]
    public void Medicoes_AFrenteEAMedir_EContratoComAditivo()
    {
        var (c, t) = Obra();
        t["Sapatas"].AtualizarPercentualConcluido(60); // executado 60%, medido 30%
        c.Contrato.RegistrarAditivo(D(8), "Ampliação", [new AlteracaoDoAditivo(t["Estrutura"].Id, 0, 2000)]);
        c.Contrato.RegistrarAditivo(D(25), "Depois da data", [new AlteracaoDoAditivo(t["Estrutura"].Id, 0, 1000)]);

        var a = Analise(c, D(9));

        var f = a.Etapas[0];
        f.Fisico.Should().BeApproximately(73.33, 0.01);
        f.DiferencaDaMedicao.Should().BeApproximately(-20, 0.01, "executado a medir");
        f.AMedir.Should().Be(3000m);
        var alerta = a.Alertas.Single(x => x.Area == "Medição");
        (alerta.Nivel, alerta.Titulo).Should().Be((NivelDoAlerta.Atencao, "R$ 3.000,00 executado a medir"));
        alerta.Detalhe.Should().StartWith("Há serviço executado ainda não medido");
        a.Contrato.Should().Be(new ContratoNaData(35000m, 1, 2000m, 37000m, 8000m, 8000m, 0m, 1));
        a.Contrato.Saldo.Should().Be(29000m);
        a.Contrato.AditivosPercentual.Should().BeApproximately(5.71, 0.01);
    }

    [Fact]
    public void Caixa_PorMes_MedidoLiquidoMenosCusto_EOPrevistoAteOFim()
    {
        var (c, _) = Obra();

        var caixa = Analise(c, D(9)).Caixa;

        caixa.Select(p => (p.Periodo.Inicio, p.Realizado)).Should().Equal((D(28, 9), true), (D(1), true));
        caixa.Sum(p => p.ReceitaPrevista).Should().Be(35000m, "o previsto distribui todo o contratado");
        (caixa[0].Medido, caixa[0].Custo, caixa[0].SaldoAcumulado).Should().Be((0m, 0m, 0m));
        (caixa[1].Medido, caixa[1].Custo, caixa[1].Saldo, caixa[1].SaldoAcumulado).Should().Be((8000m, 10000m, -2000m, -2000m));

        var depois = Analise(c, D(31)).Caixa;
        depois[1].Custo.Should().Be(11000m, "o custo geral de 20/10 entra até 31/10");
    }

    [Fact]
    public void Alertas_PrazoECustoCriticos_MedicaoRecursosEFotosOk()
    {
        var (c, _) = Obra();

        var alertas = Analise(c, D(9)).Alertas;

        alertas.Select(x => (x.Area, x.Nivel)).Should().Equal(
            ("Prazo", NivelDoAlerta.Critico), ("Custo", NivelDoAlerta.Critico), ("Medição", NivelDoAlerta.Ok),
            ("Recursos", NivelDoAlerta.Ok), ("Fotos", NivelDoAlerta.Ok));
        alertas[0].Titulo.Should().StartWith("IDP 0,80");
        alertas[1].Titulo.Should().Be("Índice de custo 0,38");
        alertas[1].Detalhe.Should().Contain("margem projetada R$ -6.052,63 (sobre os R$ 15.000,00 com custo orçado)").And.Contain("1 Fundação");
        alertas[2].Titulo.Should().Be("Medição em dia");
    }

    [Fact]
    public void Alertas_SemDados_DizemOQueFalta()
    {
        var (c, t) = Obra(emAndamento: false);
        c.ConfiguracaoDasFotos.Should().NotBeNull();

        var alertas = Analise(c, D(9)).Alertas;

        alertas.Where(x => x.Area is "Custo" or "Medição" or "Recursos").Should().OnlyContain(x => x.Nivel == NivelDoAlerta.SemDados);
        alertas.Single(x => x.Area == "Custo").Titulo.Should().Be("Sem custo orçado");
        alertas.Single(x => x.Area == "Fotos").Titulo.Should().MatchRegex("tarefa\\(s\\) sem foto recente|Fotos em dia");

        t["Sapatas"].DefinirQuantidade(10);
        c.Orcamento.Associar(t["Sapatas"], new ComposicaoAberta(new BaseDeReferencia("SINAPI"), "1", "C", "M3",
            [new InsumoNaComposicao("1", "C", "M3", TipoDeInsumo.Material, 1, 600)]));
        Analise(c, D(9)).Alertas.Single(x => x.Area == "Custo").Titulo.Should().Be("Sem custo real");
    }

    [Fact]
    public void Alertas_MedidoAFrente_ESuperalocacao()
    {
        var (c, t) = Obra();
        t["Sapatas"].AtualizarPercentualConcluido(10); // medido 30%, executado 10%: medido à frente
        var pedreiro = c.Recursos.Lista[0];
        c.Recursos.Substituir(new Recurso(pedreiro.Id, pedreiro.Nome, pedreiro.Tipo, null, pedreiro.CustoUnitario, 1));
        c.Recursos.Atribuir(t["Estrutura"], [new AtribuicaoDeRecurso(pedreiro.Id, 3)]);

        var alertas = Analise(c, D(9)).Alertas;

        var medicao = alertas.Single(x => x.Area == "Medição");
        (medicao.Nivel, medicao.Titulo).Should().Be((NivelDoAlerta.Critico, "Medido à frente em 1 etapa(s)"));
        medicao.Detalhe.Should().Contain("Medido à frente do executado em 1 etapa(s), como 1 Fundação");
        var recursos = alertas.Single(x => x.Area == "Recursos");
        (recursos.Nivel, recursos.Titulo).Should().Be((NivelDoAlerta.Atencao, "1 superalocação(ões)"));
        recursos.Detalhe.Should().Contain("Pedreiro de 19/10 a 30/10");
    }
}
