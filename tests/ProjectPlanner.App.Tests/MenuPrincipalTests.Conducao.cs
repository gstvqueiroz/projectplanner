using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 26, na janela de verdade: o cartão do modo, as perguntas com respostas para clicar, o plano e a ficha no painel.
public partial class MenuPrincipalTests
{
    private static Abertura AbrirComAssistente(params RespostaIA[] respostas)
    {
        var fila = new Queue<RespostaIA>(respostas);
        var a = Abrir(ambiente =>
        {
            ambiente.PreferenciasInterface.Salvo = new Services.PreferenciasInterface(MostrarAssistente: true);
            ambiente.Assistente.ResponderComAlteracoes = p => fila.Count > 0 ? fila.Dequeue() : new RespostaIA($"Resposta a: {p.Mensagem}");
        });
        Pintar(a);
        return a;
    }

    private static Button BotaoNoPainel(Abertura a, string nome) =>
        a.Janela.PainelAssistente.GetVisualDescendants().OfType<Button>()
            .Last(b => AutomationProperties.GetName(b) == nome && b.IsEffectivelyVisible);

    private static void PedirNoPainel(Abertura a, string texto)
    {
        a.Vm.Assistente.TextoDigitado = texto;
        a.Vm.Assistente.EnviarCommand.Execute(null);
        Pintar(a);
    }

    [Fact]
    public Task Conducao_CartaoDoModo_CliqueEmMontarAgora() => Tela.Executar(() =>
    {
        var a = AbrirComAssistente(new RespostaIA("Posso montar agora ou estudar antes.") { Tipo = TipoDeResposta.EscolherModo });
        PedirNoPainel(a, "Monte o cronograma da casa");

        var montar = BotaoNoPainel(a, "Montar o cronograma agora");
        BotaoNoPainel(a, "Estudar e conversar antes").IsEffectivelyEnabled.Should().BeTrue();

        a.ClicarComOMouse(montar);
        Pintar(a);

        a.Ambiente.Assistente.Pedidos[^1].Mensagem.Should().Be(AssistenteViewModel.TextoMontarAgora);
        a.Vm.Conversa.Modo.Should().Be(Core.Assistente.ModoDaConversa.Direto);
        montar.IsEffectivelyEnabled.Should().BeFalse("o cartão já não é a última mensagem");
        a.Janela.PainelAssistente.FindControl<TextBlock>("EtapaDoEstudo")!.Text.Should().Be("Modo: montar direto");
    });

    [Fact]
    public Task Conducao_Perguntas_PilulasParaClicar_EResponder() => Tela.Executar(() =>
    {
        var a = AbrirComAssistente(new RespostaIA("Algumas perguntas:")
        {
            Tipo = TipoDeResposta.Perguntas,
            Perguntas = [new PerguntaIA("Qual a estrutura?", ["Concreto armado", "Alvenaria estrutural"])],
            Ficha = [new ItemDeFicha("Área construída", "100 m²")],
        });
        PedirNoPainel(a, "Estude a planta");

        var responder = BotaoNoPainel(a, "Responder as perguntas");
        responder.IsEffectivelyEnabled.Should().BeFalse("nada escolhido");
        var opcoes = a.Janela.PainelAssistente.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        opcoes.Select(o => o.Content).Should().Equal("Concreto armado", "Alvenaria estrutural", PerguntaNaTela.NaoSei);

        a.ClicarComOMouse(opcoes[1]);
        Pintar(a);
        a.Vm.Assistente.Ultima!.Perguntas[0].Escolhida.Should().Be("Alvenaria estrutural");
        a.ClicarComOMouse(responder);
        Pintar(a);

        a.Ambiente.Assistente.Pedidos[^1].Mensagem.Should().Be("Respostas:\n- Qual a estrutura? Alvenaria estrutural");
        var ficha = a.Janela.PainelAssistente.FindControl<Expander>("FichaDoProjeto")!;
        (ficha.IsVisible, ficha.Header).Should().Be((true, "Ficha do projeto (1)"));
        a.Janela.PainelAssistente.FindControl<TextBlock>("EtapaDoEstudo")!.Text.Should().Be("Estudo · perguntas (1 de 4)");
        a.Janela.PainelAssistente.FindControl<Button>("PularParaOPlano")!.IsVisible.Should().BeTrue();
    });

    [Fact]
    public Task Conducao_Plano_AprovarPeloBotao() => Tela.Executar(() =>
    {
        var a = AbrirComAssistente(
            new RespostaIA("O plano:")
            {
                Tipo = TipoDeResposta.Plano,
                Plano = new PlanoIA([new EtapaIA("Fundação", 15), new EtapaIA("Estrutura", 25)], "2027-02-26", ["Sábado sem obra"]),
            },
            new RespostaIA("Montei.") { Alteracoes = [new AlteracaoDoCronograma(AcoesNoCronograma.Criar, Ref: "n1", Nome: "Fundação", Duracao: 15)] });
        PedirNoPainel(a, "Estude a planta");

        a.Janela.PainelAssistente.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Should().Contain(["Plano do cronograma", "Fundação", "15 dias", "Término estimado: 26/02/2027", "– Sábado sem obra"]);

        a.ClicarComOMouse(BotaoNoPainel(a, "Aprovar o plano e montar"));
        Pintar(a);

        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("Fundação");
        a.Vm.Conversa.Modo.Should().Be(Core.Assistente.ModoDaConversa.Direto);
    });

    [Fact]
    public Task Conducao_FichaNoPainel_AcrescentarPelaTela() => Tela.Executar(() =>
    {
        var a = AbrirComAssistente();
        var ficha = a.Janela.PainelAssistente.FindControl<Expander>("FichaDoProjeto")!;
        (ficha.IsVisible, ficha.IsExpanded, ficha.Header).Should().Be((true, false, "Ficha do projeto"), "sempre à mão, recolhida");

        a.ClicarComOMouse(ficha.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First());
        Pintar(a);
        ficha.IsExpanded.Should().BeTrue();
        a.Vm.Assistente.NovoCampo = "Prazo desejado";
        a.Vm.Assistente.NovoValor = "6 meses";
        a.ClicarComOMouse(BotaoNoPainel(a, "Acrescentar o campo à ficha"));
        Pintar(a);

        (ficha.Header, a.Vm.Conversa.Ficha["Prazo desejado"], a.Vm.PossuiAlteracoesNaoSalvas).Should().Be(("Ficha do projeto (1)", "6 meses", true));
        a.Janela.PainelAssistente.GetLogicalDescendants().OfType<TextBox>().Select(t => t.Text).Should().Contain("6 meses");
    });
}
