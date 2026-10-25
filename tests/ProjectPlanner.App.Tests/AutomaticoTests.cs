using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Bloco 51: o nível Automático no painel: a triagem antes de cada envio, a confirmação de custo e o "Refazer com um modelo melhor".
public sealed class AutomaticoTests
{
    private static readonly IReadOnlyList<ModeloDaConta> ModelosDoProfissional =
    [
        new(NiveisDaConta.Automatico, NiveisDaConta.Automatico, "Automático", true, true, 4),
        new("claude-haiku-5-5", "padrao", "Claude Haiku 5.5", true, true, 4),
        new("gemini-3-flash-preview", "intermediario", "Gemini 3 Flash", true, true, 19),
        new("claude-sonnet-5-5", "maximo", "Claude Sonnet 5.5", true, true, 70),
    ];

    private static readonly AvaliacaoDoPedido Simples =
        new("simples", "pedido curto", "padrao", "claude-haiku-5-5", "Claude Haiku 5.5", 4, false, false, "claude-haiku-5-5", "Claude Haiku 5.5", 4);

    private static readonly AvaliacaoDoPedido Complexa =
        new("complexa", "montar o cronograma inteiro", "maximo", "claude-sonnet-5-5", "Claude Sonnet 5.5", 120, false, true,
            "claude-haiku-5-5", "Claude Haiku 5.5", 9);

    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public AutomaticoTests()
    {
        _ambiente.Assistente.EstaConfigurado = true;
        _ambiente.Conta.ModelosDoServidor = ModelosDoProfissional;
        _vm = _ambiente.CriarViewModel();
    }

    private AssistenteViewModel Painel => _vm.Assistente;

    // Entra na conta com o painel aberto: ele busca os níveis (e o Automático é oferecido).
    private void Entrar()
    {
        Painel.Visivel = true;
        _ambiente.Conta.Conectar(plano: "profissional");
    }

    private Task Mandar(string texto)
    {
        Painel.TextoDigitado = texto;
        return Painel.EnviarCommand.ExecuteAsync(null);
    }

    [Fact]
    public void QuemEstavaNoPadraoOuNumModeloQueSaiu_PassaAoAutomatico_UmaVezSo()
    {
        _ambiente.ConfiguracaoAssistente.Modelo = "gemini-2.5-flash"; // o padrão antigo, que saiu da lista

        Entrar();

        (_ambiente.ConfiguracaoAssistente.Modelo, _ambiente.ConfiguracaoAssistente.AutomaticoOferecido, Painel.NivelEscolhido!.Id, Painel.UsaAutomatico)
            .Should().Be((NiveisDaConta.Automatico, true, NiveisDaConta.Automatico, true));
        Painel.NivelEscolhido.TextoCompleto.Should().Be("Automático: escolhe o modelo para cada pedido (a partir de 4 créditos; pergunta antes dos pedidos caros)");

        // Quem volta ao Padrão de propósito não é mudado de novo.
        Painel.NivelEscolhido = Painel.Niveis.Single(o => o.Id == "claude-haiku-5-5");
        _vm.Assistente.AtualizarConfiguracao();
        _ambiente.ConfiguracaoAssistente.Modelo.Should().Be("claude-haiku-5-5");
    }

    [Fact]
    public void QuemEscolheuUmNivelForte_ContinuaNele()
    {
        _ambiente.ConfiguracaoAssistente.Modelo = "claude-sonnet-5-5";

        Entrar();

        (_ambiente.ConfiguracaoAssistente.Modelo, _ambiente.ConfiguracaoAssistente.AutomaticoOferecido, Painel.UsaAutomatico)
            .Should().Be(("claude-sonnet-5-5", true, false));
    }

    [Fact]
    public async Task PedidoSimples_VaiNoModeloDaTriagem_SemPerguntar_EOfereceRefazerComOMelhor()
    {
        Entrar();
        _ambiente.Conta.Avaliacao = Simples;

        await Mandar("qual o prazo da alvenaria?");

        var pedido = _ambiente.Assistente.Pedidos.Single();
        pedido.Modelo.Should().Be("claude-haiku-5-5");
        var avaliado = _ambiente.Conta.Avaliados.Single();
        (avaliado.Mensagem, avaliado.Anexos).Should().Be(("qual o prazo da alvenaria?", 0));
        (Painel.TemConfirmacao, Painel.UltimaEscolha).Should().Be((false, "Último pedido: Claude Haiku 5.5 (pedido simples)"));
        (Painel.PodeRefazerMelhor, Painel.TextoDoRefazer).Should().Be((true, "Refazer com o Claude Sonnet 5.5 (~70 créditos)"));

        await Painel.RefazerMelhorCommand.ExecuteAsync(null);

        (_ambiente.Assistente.Pedidos[^1].Modelo, _ambiente.Assistente.Pedidos[^1].Mensagem).Should().Be(("claude-sonnet-5-5", "qual o prazo da alvenaria?"));
        _ambiente.Conta.Avaliados.Should().HaveCount(1, "refazer não passa pela triagem");
        Painel.PodeRefazerMelhor.Should().BeFalse("o Sonnet já é o mais forte");
    }

    [Fact]
    public async Task PedidoCaro_PedeConfirmacao_ETentarNoPadraoMandaNoHaiku()
    {
        Entrar();
        _ambiente.Conta.Avaliacao = Complexa;
        _ambiente.Conta.AtualizarSaldo(6000);

        var envio = Mandar("monte o cronograma completo de um prédio de 4 pavimentos");

        Painel.TemConfirmacao.Should().BeTrue();
        _ambiente.Assistente.Pedidos.Should().BeEmpty("espera a escolha");
        var c = Painel.Confirmacao!;
        (c.Titulo, c.Estimativa, c.TextoDoRecomendado, c.TextoDoPadrao, c.MostrarPadrao).Should().Be((
            "Este pedido parece complexo: montar o cronograma inteiro.", "Cerca de 120 créditos no Claude Sonnet 5.5 (2% do seu saldo).",
            "Usar Claude Sonnet 5.5 (~120)", "Tentar no Claude Haiku 5.5 (~9)", true));
        Painel.Aguardando.Should().BeTrue();

        Painel.UsarPadraoCommand.Execute(null);
        await envio;

        (_ambiente.Assistente.Pedidos.Single().Modelo, Painel.TemConfirmacao, Painel.UltimaEscolha)
            .Should().Be(("claude-haiku-5-5", false, "Último pedido: Claude Haiku 5.5 (você escolheu)"));
    }

    [Fact]
    public async Task Confirmacao_Cancelar_NaoManda_ENaoPerguntarDeNovoValeAteLimparAConversa()
    {
        Entrar();
        _ambiente.Conta.Avaliacao = Complexa;

        var cancelado = Mandar("monte o cronograma");
        Painel.CancelarConfirmacaoCommand.Execute(null);
        await cancelado;
        (_ambiente.Assistente.Pedidos.Count, Painel.Mensagens[^1].Texto, Painel.Mensagens[^1].EhErro).Should().Be((0, "Resposta cancelada.", true));

        var primeiro = Mandar("monte o cronograma");
        Painel.Confirmacao!.NaoPerguntarDeNovo = true;
        Painel.UsarRecomendadoCommand.Execute(null);
        await primeiro;
        _ambiente.Assistente.Pedidos[^1].Modelo.Should().Be("claude-sonnet-5-5");

        await Mandar("monte o cronograma de novo");
        (Painel.TemConfirmacao, _ambiente.Assistente.Pedidos[^1].Modelo).Should().Be((false, "claude-sonnet-5-5"), "não pergunta de novo nesta conversa");

        Painel.LimparConversaCommand.Execute(null);
        var depois = Mandar("monte outro cronograma");
        Painel.TemConfirmacao.Should().BeTrue("a conversa recomeçou");
        Painel.CancelarCommand.Execute(null); // o Cancelar do painel também desiste
        await depois;
        Painel.TemConfirmacao.Should().BeFalse();
    }

    [Fact]
    public async Task TriagemQueFalha_MandaSemModelo_EForaDoAutomaticoNaoHaTriagem()
    {
        Entrar();

        await Mandar("oi"); // sem Avaliacao: a rota falha

        (_ambiente.Assistente.Pedidos.Single().Modelo, Painel.UltimaEscolha, Painel.Mensagens[^1].EhErro).Should().Be((null, "", false),
            "o servidor manda o automatico ao Padrão");

        Painel.NivelEscolhido = Painel.Niveis.Single(o => o.Id == "gemini-3-flash-preview");
        _ambiente.Conta.Avaliacao = Simples;
        await Mandar("oi de novo");
        (_ambiente.Assistente.Pedidos[^1].Modelo, _ambiente.Conta.Avaliados.Count, Painel.PodeRefazerMelhor).Should().Be((null, 1, false),
            "com um nível escolhido à mão, vale o configurado");
    }

    [Fact]
    public void OpcaoDoAutomatico_IndisponivelEForaDoPlano()
    {
        new OpcaoDeNivel(new ModeloDaConta(NiveisDaConta.Automatico, NiveisDaConta.Automatico, "Automático", true, false, 4)).Detalhe
            .Should().Be("indisponível no momento");
        new ModeloDaConta(NiveisDaConta.Automatico, NiveisDaConta.Automatico, "Automático", true, true, 4).NomeDoNivel.Should().Be("Automático");
    }
}
