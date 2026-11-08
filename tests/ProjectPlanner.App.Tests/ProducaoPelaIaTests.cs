using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 84: o andamento por quantidade pela IA (no programa e pelo Telegram, que chegam ao mesmo cartão): "fizemos 20 m² de
// reboco hoje" vira o tipo producao; o cartão mostra a conta (o quanto é do total e o % de antes e de depois) e aplica somando
// ao executado de então; a produção do dia no RDO; as quantidades no retrato; a coluna Executado na grade. "Hoje" é segunda
// 28/09/2026. Tarefas: 1 Reboco (200 m², 30%), 2 Limpeza (sem quantidade), 3 Acabamentos (resumo) com 3.1 Pintura.
public sealed class ProducaoPelaIaTests
{
    private static readonly DateOnly D28 = new(2026, 9, 28);

    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _reboco;

    public ProducaoPelaIaTests()
    {
        _vm = _ambiente.CriarViewModel();
        _reboco = Ambiente.AdicionarTarefa(_vm, "Reboco", 10);
        _reboco.Unidade = "m²";
        _reboco.Quantidade = "200";
        _reboco.PercentualConcluido = 30;
        Ambiente.AdicionarTarefa(_vm, "Limpeza", 2);
        Ambiente.AdicionarTarefa(_vm, "Acabamentos", 5);
        _vm.TarefaSelecionada = Ambiente.AdicionarTarefa(_vm, "Pintura", 5);
        _vm.RebaixarCommand.Execute(null);
    }

    private static AtualizacaoDaObra P(string tarefa, decimal quantidade, string? unidade = null, string? acao = null) =>
        new(TiposDeAtualizacao.Producao, tarefa, Quantidade: quantidade, Unidade: unidade, Acao: acao);

    private IReadOnlyList<ItemDoCartao> Conferir(params AtualizacaoDaObra[] atualizacoes) => _vm.ConferirAtualizacoes(atualizacoes, _vm.RetratoParaOAssistente());

    private async Task Aplicar(IReadOnlyList<ItemDoCartao> cartao)
    {
        cartao.Should().OnlyContain(i => i.Valido);
        (await _vm.AplicarAtualizacoesAsync([.. cartao.Select(i => i.Operacao!)], "Produção")).Situacao.Should().Be(SituacaoDaAplicacao.Aplicada);
    }

    private Tarefa Reboco => _vm.Cronograma.Tarefas.Single(t => t.Nome == "Reboco");

    [Fact]
    public async Task OCartaoMostraAConta_EDoisCartoesAplicadosDepoisSomamOsDois()
    {
        var primeiro = Conferir(P("1", 20));
        var segundo = Conferir(P("1", 20, "m2")); // chegou pelo Telegram antes de o primeiro ser aplicado

        primeiro.Single().Texto.Should().Be("1 Reboco: +20 m² executados (10% de 200 m²): 30% → 40% concluído");
        await Aplicar(primeiro);
        (Reboco.PercentualConcluido, Reboco.QuantidadeExecutada).Should().Be((40, (decimal?)80m));
        await Aplicar(segundo);
        (Reboco.PercentualConcluido, Reboco.QuantidadeExecutada).Should().Be((50, (decimal?)100m), "a conta é feita ao aplicar, sobre o executado de então");
        _vm.TextoDesfazer.Should().Be("Desfazer Atualizações do assistente");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        (Reboco.PercentualConcluido, Reboco.QuantidadeExecutada).Should().Be((40, (decimal?)80m), "cada cartão é um passo do Desfazer");
    }

    [Fact]
    public void Total_EOQueOCartaoRecusa()
    {
        var cartao = Conferir(
            P("1", 120, acao: "total"),
            P("1", 5, "m³"),
            P("1", 0),
            P("2", 10, "un"),
            P("3", 10),
            P("1", 10, acao: "metade"),
            P("9", 10));

        cartao.Select(i => (i.Texto, i.Problema)).Should().Equal(
            ("1 Reboco: 120 m² executados no total (60% de 200 m²): 30% → 60% concluído", null),
            ("1 Reboco: +5 m³ executados", "a tarefa é medida em m²: confira a unidade (m³)"),
            ("1 Reboco: 0 m² executados", "informe a quantidade executada"),
            ("2 Limpeza: +10 un executados", Tarefa.SemQuantidadeTotal),
            ("3 Acabamentos: +10 executados", "o andamento de um resumo vem das tarefas dentro dele (informe uma delas)"),
            ("1 Reboco: +10 m² executados", "a ação é somar (o executado no período) ou total (o acumulado até agora)"),
            ("Tarefa 9: produção", "não há a tarefa nº 9 no cronograma"));
    }

    [Fact]
    public async Task OTotalDaTarefaNoMesmoCartao_VemAntes_EAProducaoValeDepoisDele()
    {
        var cartao = Conferir(new AtualizacaoDaObra(TiposDeAtualizacao.Tarefa, "2", Quantidade: 50, Unidade: "m²"), P("2", 10));

        cartao.Select(i => i.Problema).Should().Equal(null, null);
        await Aplicar(cartao);
        var limpeza = _vm.Cronograma.Tarefas.Single(t => t.Nome == "Limpeza");
        (limpeza.PercentualConcluido, limpeza.QuantidadeExecutada).Should().Be((20, (decimal?)10m));
    }

    [Fact]
    public async Task PassarDoTotal_Avisa_ESo100NoTotal()
    {
        await Aplicar(Conferir(P("1", 190, acao: "total")));

        Conferir(P("1", 20)).Single().Texto.Should().Be("1 Reboco: +20 m² executados (10% de 200 m²): 95% → 100% concluído; passa do total em 10 m²");
        Conferir(P("1", 9)).Single().Texto.Should().EndWith("95% → 99% concluído", "199 de 200 ainda não terminou");
    }

    [Fact]
    public async Task OServicoDoRdo_ComAProducaoDoDia()
    {
        var cartao = Conferir(new AtualizacaoDaObra(TiposDeAtualizacao.Diario, "1", Quantidade: 20, Acao: "servico"), P("1", 20));

        cartao.Select(i => (i.Texto, i.Problema)).Should().Equal(
            ("RDO de 28/09/2026 (dia novo): serviço 1 Reboco, 20 m² executados", null),
            ("1 Reboco: +20 m² executados (10% de 200 m²): 30% → 40% concluído", null));
        await Aplicar(cartao);
        _vm.Cronograma.Diario.Dia(D28)!.Atividades.Should().Equal(new AtividadeNoDia(Reboco.Id, "1 Reboco", null, 20m, "m²"));
        _vm.RetratoParaOAssistente().Diario!.Last().Should().EndWith("serviços: 1 Reboco 20 m²");

        Conferir(new AtualizacaoDaObra(TiposDeAtualizacao.Diario, "1", Quantidade: 5, Unidade: "m³", Acao: "servico")).Single().Problema
            .Should().Be("a tarefa é medida em m²: confira a unidade");
        Conferir(new AtualizacaoDaObra(TiposDeAtualizacao.Diario, "1", Quantidade: -5, Acao: "servico")).Single().Problema
            .Should().Be("a quantidade executada não pode ser negativa");
    }

    [Fact]
    public async Task ORetrato_LevaAsQuantidadesEOExecutado()
    {
        await Aplicar(Conferir(P("1", 21))); // 81 de 200: 40,5% → 41%

        var retrato = _vm.RetratoParaOAssistente();

        retrato.Tarefas[0].Should().Match<TarefaNoContexto>(t => t.Quantidade == 200m && t.Unidade == "m²" && t.Executado == 81m && t.PercentualConcluido == 41);
        (retrato.Tarefas[1].Quantidade, retrato.Tarefas[2].Executado).Should().Be(((decimal?)null, (decimal?)null));
        retrato.ComoTexto().Should().Contain("Quantidades das tarefas (Nº: o total na unidade e o executado até agora; o % concluído é o executado ÷ o total):\n" +
                                             "- 1 (1 Reboco): 200 m²; executado 81\n");
    }

    [Fact]
    public void AColunaExecutado_NaGrade_LevaOPercentual_EVoltaComODesfazer()
    {
        _reboco.QuantidadeExecutada.Should().Be("60,00", "sem executado guardado, o % de agora sobre a quantidade");

        _reboco.QuantidadeExecutada = "120";

        (Reboco.QuantidadeExecutada, Reboco.PercentualConcluido).Should().Be(((decimal?)120m, 60));
        _vm.TextoDesfazer.Should().Be("Desfazer Executado de \"Reboco\"");
        _vm.DesfazerAlteracaoCommand.Execute(null);
        (Reboco.QuantidadeExecutada, Reboco.PercentualConcluido).Should().Be(((decimal?)null, 30));

        var limpeza = _vm.Tarefas.Single(t => t.Nome == "Limpeza");
        limpeza.QuantidadeExecutada = "10";
        (limpeza.ErroDe(nameof(TarefaViewModel.QuantidadeExecutada)), _vm.Cronograma.Tarefas.Single(t => t.Nome == "Limpeza").QuantidadeExecutada)
            .Should().Be((Tarefa.SemQuantidadeTotal, (decimal?)null));
        _vm.Tarefas.Single(t => t.Nome == "Acabamentos").QuantidadeExecutada.Should().BeEmpty("um resumo não tem executado");
    }
}
