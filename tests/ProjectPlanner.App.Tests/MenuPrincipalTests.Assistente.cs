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
}
