using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.IA;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 19, na janela de verdade: o painel do assistente à direita, pelo menu, pela faixa e pelo ×.
public partial class MenuPrincipalTests
{
    private static AssistenteView Painel(Abertura a) => a.Janela.PainelAssistente;

    [Fact]
    public Task Assistente_ComecaOculto_SemOcuparLargura() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);

        (Painel(a).IsVisible, a.Janela.Paineis.ColumnDefinitions[4].Width, a.Janela.Paineis.ColumnDefinitions[3].Width)
            .Should().Be((false, new GridLength(0), new GridLength(0)));
    });

    [Fact]
    public Task Assistente_PeloMenuExibir_PelaFaixaEPeloX() => Tela.Executar(() =>
    {
        var a = Abrir();

        a.Clicar("Exibir", "Assistente (IA)");
        Pintar(a);
        (Painel(a).IsVisible, a.Janela.Paineis.ColumnDefinitions[4].Width).Should().Be((true, new GridLength(340)));
        Painel(a).Bounds.Width.Should().BeApproximately(340, 1);
        a.Item("Exibir", "Assistente (IA)").IsChecked.Should().BeTrue();
        Botao(a, "Assistente").Classes.Should().Contain("ativo");
        a.Janela.MenuPrincipal.Close();

        a.ClicarComOMouse(Painel(a).FindControl<Button>("FecharPainel")!);
        Pintar(a);
        (Painel(a).IsVisible, a.Vm.Assistente.Visivel).Should().Be((false, false));

        a.ClicarComOMouse(Botao(a, "Assistente"));
        Pintar(a);
        Painel(a).IsVisible.Should().BeTrue();
        a.Ambiente.PreferenciasInterface.Salvo!.MostrarAssistente.Should().BeTrue();
    });

    [Fact]
    public Task Assistente_EmCadaModoDeVisualizacao_FicaADireita() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.Assistente.Visivel = true;
        foreach (var modo in new[] { ViewModels.ModoVisualizacao.SomenteTabela, ViewModels.ModoVisualizacao.SomenteGantt, ViewModels.ModoVisualizacao.Dividido })
        {
            a.Vm.ModoVisualizacao = modo;
            Pintar(a);

            Painel(a).IsVisible.Should().BeTrue();
            var direita = Painel(a).TranslatePoint(new Point(Painel(a).Bounds.Width, 0), a.Janela)!.Value.X;
            (a.Janela.Bounds.Width - direita).Should().BeInRange(0, 12, $"no modo {modo} o painel encosta na direita");
        }
    });

    [Fact]
    public Task Assistente_EnterEnvia_ShiftEnterNao_EAConversaAparece() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.Assistente.Visivel = true;
        Pintar(a);
        var entrada = Painel(a).FindControl<TextBox>("Entrada")!;
        entrada.Focus();
        a.Vm.Assistente.TextoDigitado = "Olá";
        Pintar(a);

        a.Janela.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, "\r");
        Pintar(a);
        a.Vm.Assistente.Mensagens.Should().BeEmpty("Shift+Enter só quebra a linha");

        a.Janela.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Pintar(a);

        a.Vm.Assistente.Mensagens.Should().HaveCount(2);
        Painel(a).FindControl<ItemsControl>("Historico")!.ItemCount.Should().Be(2);
        (Painel(a).FindControl<TextBlock>("BoasVindas")!.IsVisible, Painel(a).FindControl<Border>("AvisoNaoConfigurado")!.IsVisible)
            .Should().Be((false, true));
    });

    [Fact]
    public Task Assistente_DigitandoEBotaoCancelar_DuranteAEspera() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.Assistente.Visivel = true;
        a.Ambiente.Assistente.Segurar();
        a.Vm.Assistente.TextoDigitado = "Demora";
        Pintar(a);

        a.ClicarComOMouse(Painel(a).FindControl<Button>("Enviar")!);
        Pintar(a);

        (Painel(a).FindControl<TextBlock>("Digitando")!.IsVisible, Painel(a).FindControl<Button>("Cancelar")!.IsVisible,
            Painel(a).FindControl<Button>("Enviar")!.IsVisible).Should().Be((true, true, false));

        a.ClicarComOMouse(Painel(a).FindControl<Button>("Cancelar")!);
        Pintar(a);
        (Painel(a).FindControl<TextBlock>("Digitando")!.IsVisible, a.Vm.Assistente.Mensagens[^1].Texto)
            .Should().Be((false, "Resposta cancelada."));
    });

    // ---- Bloco 23: configurações do assistente ----

    [Fact]
    public Task Assistente_NaoConfigurado_OAvisoTemOLinkParaEntrarNaConta() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.Assistente.Visivel = true;
        Pintar(a);
        var link = Painel(a).FindControl<Button>("EntrarNaConta")!;
        (link.IsEffectivelyVisible, link.Content, Painel(a).FindControl<TextBlock>("Modelo")!.IsVisible).Should().Be((true, "Entrar ou criar conta (grátis)…", false));

        a.ClicarComOMouse(link);
        a.ClicarComOMouse(Painel(a).FindControl<Button>("Configurar")!);

        (a.Ambiente.Dialogos.Contas.Count, a.Ambiente.Dialogos.ConfiguracoesDoAssistente.Count).Should().Be((1, 1),
            "o link do aviso abre a conta; a engrenagem, as configurações");
    });

    [Fact]
    public Task Assistente_Configurado_MostraOModeloNoCabecalho_SemOAviso() => Tela.Executar(() =>
    {
        var a = Abrir(ambiente => ambiente.Assistente.EstaConfigurado = true);
        a.Vm.Assistente.Visivel = true;
        Pintar(a);

        var modelo = Painel(a).FindControl<TextBlock>("Modelo")!;
        (modelo.IsVisible, modelo.Text).Should().Be((true, "Conta ProjectPlanner · gemini-2.5-flash"));
        Painel(a).FindControl<Border>("AvisoNaoConfigurado")!.IsVisible.Should().BeFalse();
    });

    [Fact]
    public Task ConfiguracaoDoAssistente_AContaEOsNiveis_SemCampoDeChave() => Tela.Executar(() =>
    {
        var ambiente = new Ambiente();
        ambiente.Conta.Conectar();
        var tela = new ViewModels.ConfiguracaoAssistenteViewModel(ambiente.ConfiguracaoAssistente, ambiente.Dialogos, ambiente.Conta);
        var janela = new ConfiguracaoAssistenteWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs(); // ao abrir, a tela busca os níveis da conta

        janela.FindControl<TextBox>("Chave").Should().BeNull("a chave própria saiu da tela no Bloco 46");
        janela.FindControl<TextBlock>("SituacaoDaConta")!.Text.Should().Be("Conectado como Ana (ana@exemplo.com), plano Grátis.");
        var niveis = janela.FindControl<ListBox>("Niveis")!;
        (niveis.IsEffectivelyVisible, niveis.ItemCount, janela.FindControl<TextBlock>("ChaveDeDesenvolvimento")!.IsVisible).Should().Be((true, 3, false));
        var itens = niveis.GetRealizedContainers().OfType<ListBoxItem>().ToList();
        itens.Select(i => i.IsEnabled).Should().Equal(true, false, false);
        niveis.SelectedIndex.Should().Be(0);
        var testar = janela.FindControl<Button>("Testar")!;
        var ponto = testar.TranslatePoint(new Point(testar.Bounds.Width / 2, testar.Bounds.Height / 2), janela)!.Value;
        janela.Bounds.Contains(ponto).Should().BeTrue("os botões cabem na janela");
        janela.Close();
    });

    // ---- Bloco 24: alterações do cronograma pelo chat ----

    [Fact]
    public Task Assistente_RespostaComAlteracoes_MostraOQueMudou_EDesfazerVolta() => Tela.Executar(() =>
    {
        var a = Abrir(ambiente => ambiente.Assistente.ResponderComAlteracoes = _ => new RespostaIA("Criei a fundação.")
        {
            Alteracoes = [new AlteracaoDoCronograma(AcoesNoCronograma.Criar, Ref: "n1", Nome: "Fundação", Duracao: 3)],
        });
        a.Vm.Assistente.Visivel = true;
        a.Vm.Assistente.TextoDigitado = "Crie a fundação";
        Pintar(a);

        a.ClicarComOMouse(Painel(a).FindControl<Button>("Enviar")!);
        Pintar(a);

        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("Fundação");
        Painel(a).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("Cronograma alterado: 1 tarefa criada");
        var desfazer = Painel(a).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Desfazer") && b.IsEffectivelyVisible);

        a.ClicarComOMouse(desfazer);
        Pintar(a);

        a.Vm.Tarefas.Should().BeEmpty();
        desfazer.IsVisible.Should().BeFalse("só dá para desfazer uma vez");
        Painel(a).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("Desfeito: 1 tarefa criada");
    });
}
