using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 63: a IA cria e altera OS (pelo cartão, no programa; do Telegram, pela caixa): a OS fica ligada às tarefas, e um
// serviço que não está no cronograma vira uma tarefa nova, depois da predecessora, com a OS ligada a ela.
public sealed class OrdensPelaIATests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public OrdensPelaIATests()
    {
        _vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5).Valor = "3.000";
        Ambiente.AdicionarTarefa(_vm, "Reboco", 4, "1");
        Ambiente.AdicionarTarefa(_vm, "Telhado", 3, "2");
    }

    private IReadOnlyList<ItemDoCartao> Cartao(params AtualizacaoDaObra[] atualizacoes) =>
        _vm.ConferirAtualizacoes(atualizacoes, _vm.RetratoParaOAssistente());

    private async Task Aplicar(IReadOnlyList<ItemDoCartao> itens) =>
        await _vm.AplicarAtualizacoesAsync([.. itens.Where(i => i.Valido).Select(i => i.Operacao!)], "Atualizações do assistente");

    [Fact]
    public async Task Criar_ComAsTarefasDoCronograma_EOsCamposPedidos()
    {
        var cartao = Cartao(new AtualizacaoDaObra("os", Titulo: "Alvenaria e reboco", Executante: "Construtora Silva", TipoDeExecutante: "terceiro",
            Tarefas: "1, 2", Prazo: "2026-10-15", Pagamento: "medicao", Retencao: 5));

        cartao.Single().Should().Be(new ItemDoCartao("Nova OS: Alvenaria e reboco · Construtora Silva · terceiro (empreiteiro) · prazo 15/10/2026 · " +
            "por medição · retenção 5% · tarefas 1 Alvenaria, 2 Reboco", cartao[0].Operacao, null));
        await Aplicar(cartao);

        var os = _vm.Cronograma.OrdensDeServico.Todas.Single();
        (os.Codigo, os.Situacao, os.Dados.Executante, os.Dados.Prazo, os.Dados.Retencao, os.Dados.FormaDePagamento, os.Dados.Itens.Count)
            .Should().Be(("OS-001", SituacaoDaOrdem.Rascunho, "Construtora Silva", new DateOnly(2026, 10, 15), 5m, FormaDePagamentoDaOrdem.PorMedicao, 2));
        _vm.Tarefas[0].TemOrdens.Should().BeTrue("a marca aparece na grade");
    }

    [Fact]
    public async Task Criar_ServicoForaDoCronograma_ViraTarefaNovaDepoisDaPredecessora_EAOSLigadaAEla()
    {
        var cartao = Cartao(new AtualizacaoDaObra("os", Valor: 800, Titulo: "Reparo da calha", Executante: "Zé Calheiro", NovaTarefa: "Reparo da calha",
            Duracao: 2, Apos: "2"));

        cartao.Single().Texto.Should().Be("Nova OS: Reparo da calha · Zé Calheiro · R$ 800,00 · cria a tarefa \"Reparo da calha\" (2 dias, depois de 2 Reboco)");
        await Aplicar(cartao);

        _vm.Cronograma.Tarefas.Select(t => t.Nome).Should().Equal("Alvenaria", "Reboco", "Reparo da calha", "Telhado");
        var reparo = _vm.Cronograma.Tarefas[2];
        _vm.Cronograma.PredecessorasDe(reparo.Id).Single().TarefaOrigemId.Should().Be(_vm.Cronograma.Tarefas[1].Id);
        reparo.InicioMaisCedo.Should().Be(new DateTime(2026, 10, 9), "o reboco termina na quinta, 08/10: o reparo começa na sexta");
        var os = _vm.Cronograma.OrdensDeServico.Todas.Single();
        (os.Dados.Tarefas.Single(), os.Total, os.Dados.Prazo).Should().Be((reparo.Id, 800m, DateOnly.FromDateTime(reparo.TerminoMaisCedo!.Value)));
        _vm.TextoDesfazer.Should().Be("Desfazer Atualizações do assistente");
    }

    [Fact]
    public async Task Alterar_SoNoRascunho_EMudarASituacao()
    {
        await Aplicar(Cartao(new AtualizacaoDaObra("os", Titulo: "Alvenaria", Executante: "Equipe", Tarefas: "1")));

        var alterar = Cartao(new AtualizacaoDaObra("os", Os: 1, Prazo: "2026-10-20", Executante: "Construtora Silva"));
        alterar.Single().Texto.Should().Be("OS-001: alterar · Construtora Silva · prazo 20/10/2026");
        await Aplicar(alterar);
        await Aplicar(Cartao(new AtualizacaoDaObra("situacaoOs", Os: 1, Acao: "emitir")));

        var os = _vm.Cronograma.OrdensDeServico.Todas.Single();
        (os.Situacao, os.Dados.Executante, os.Historico[^1].Autor).Should().Be((SituacaoDaOrdem.Emitida, "Construtora Silva", MudarSituacaoDaOrdem.Autor));
        Cartao(new AtualizacaoDaObra("os", Os: 1, Prazo: "2026-10-30")).Single().Problema.Should().Be("a OS-001 já foi emitida: volte a rascunho antes");

        var cancelar = Cartao(new AtualizacaoDaObra("situacaoOs", Descricao: "empreiteiro saiu", Os: 1, Acao: "cancelar"));
        cancelar.Single().Texto.Should().Be("OS-001: cancelar (empreiteiro saiu)");
        await Aplicar(cancelar);
        _vm.Cronograma.OrdensDeServico.Todas.Single().Historico[^1].Descricao.Should().Be("Cancelada: empreiteiro saiu");
    }

    [Fact]
    public void Problemas_NoCartao()
    {
        Cartao(new AtualizacaoDaObra("os", Titulo: "x", Tarefas: "9")).Single().Problema.Should().Be("não há a tarefa nº 9 no cronograma");
        Cartao(new AtualizacaoDaObra("os", Titulo: "x")).Single().Problema.Should().Be("a OS precisa de tarefas do cronograma (ou de uma tarefa nova)");
        Cartao(new AtualizacaoDaObra("situacaoOs", Os: 5, Acao: "emitir")).Single().Problema.Should().Be("não há a OS-005");
        Cartao(new AtualizacaoDaObra("os", Valor: 100, Titulo: "x", Tarefas: "1, 2")).Single().Problema.Should()
            .Be("A OS tem vários itens: ajuste os preços de cada um na tela da OS.");
    }
}
