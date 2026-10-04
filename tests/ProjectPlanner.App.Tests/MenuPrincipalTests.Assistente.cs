using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
    public Task Assistente_NaoConfigurado_OAvisoTemOLinkParaConfigurar() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.Assistente.Visivel = true;
        Pintar(a);
        var link = Painel(a).FindControl<Button>("ConfigurarChave")!;
        (link.IsEffectivelyVisible, Painel(a).FindControl<TextBlock>("Modelo")!.IsVisible).Should().Be((true, false));

        a.ClicarComOMouse(link);
        a.ClicarComOMouse(Painel(a).FindControl<Button>("Configurar")!);

        a.Ambiente.Dialogos.ConfiguracoesDoAssistente.Should().HaveCount(2, "pelo link do aviso e pela engrenagem do cabeçalho");
    });

    [Fact]
    public Task Assistente_Configurado_MostraOModeloNoCabecalho_SemOAviso() => Tela.Executar(() =>
    {
        var a = Abrir(ambiente => ambiente.Assistente.EstaConfigurado = true);
        a.Vm.Assistente.Visivel = true;
        Pintar(a);

        var modelo = Painel(a).FindControl<TextBlock>("Modelo")!;
        (modelo.IsVisible, modelo.Text).Should().Be((true, "Gemini (Google) · gemini-2.5-flash"));
        Painel(a).FindControl<Border>("AvisoNaoConfigurado")!.IsVisible.Should().BeFalse();
    });

    [Fact]
    public Task ConfiguracaoDoAssistente_ChaveEscondida_SalvarMostraSoOFim() => Tela.Executar(() =>
    {
        var ambiente = new Ambiente();
        var tela = new ViewModels.ConfiguracaoAssistenteViewModel(ambiente.ConfiguracaoAssistente, ambiente.Dialogos);
        var janela = new ConfiguracaoAssistenteWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        var chave = janela.FindControl<TextBox>("Chave")!;
        chave.PasswordChar.Should().Be('●');
        janela.FindControl<TextBlock>("Situacao")!.Text.Should().Be("Nenhuma chave configurada: o assistente dá respostas simuladas.");
        janela.FindControl<Button>("Remover")!.IsVisible.Should().BeFalse("sem chave salva, nada a remover");

        chave.Focus();
        janela.KeyTextInput("AIzaChaveDigitadaNaJanela000000004321");
        Dispatcher.UIThread.RunJobs();
        tela.ChaveDigitada.Should().Be("AIzaChaveDigitadaNaJanela000000004321");
        var salvar = janela.FindControl<Button>("Salvar")!;
        var ponto = salvar.TranslatePoint(new Point(salvar.Bounds.Width / 2, salvar.Bounds.Height / 2), janela)!.Value;
        janela.Bounds.Contains(ponto).Should().BeTrue("os botões cabem na janela");
        janela.MouseDown(ponto, MouseButton.Left);
        janela.MouseUp(ponto, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        ambiente.ConfiguracaoAssistente.Chave.Should().Be("AIzaChaveDigitadaNaJanela000000004321");
        chave.Text.Should().BeEmpty("a chave salva não volta para o campo");
        janela.FindControl<TextBlock>("Situacao")!.Text.Should().Be("Chave guardada no cofre do sistema (Chaves do macOS), terminada em …4321.");
        janela.FindControl<TextBlock>("Mensagem")!.Text.Should().Be("Chave salva no cofre do sistema.");
        (janela.FindControl<Button>("Remover")!.IsVisible, janela.FindControl<Button>("Remover")!.IsEffectivelyEnabled).Should().Be((true, true));
        janela.Close();
    });
}
