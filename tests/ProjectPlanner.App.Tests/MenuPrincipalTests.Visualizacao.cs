using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 16, na janela de verdade: modos de visualização, linhas vazias na grid, zoom flutuante e folga.
public partial class MenuPrincipalTests
{
    private static void Pintar(Abertura a)
    {
        for (var i = 0; i < 2; i++)
        {
            Dispatcher.UIThread.RunJobs();
            a.Janela.UpdateLayout();
        }
    }

    private static (GridLength Tabela, GridLength Divisor, GridLength Gantt) Colunas(Abertura a) =>
        (a.Janela.Paineis.ColumnDefinitions[0].Width, a.Janela.Paineis.ColumnDefinitions[1].Width,
         a.Janela.Paineis.ColumnDefinitions[2].Width);

    [Fact]
    public Task Modos_PeloMenuExibir_MudamOsPaineisEMarcamOItem() => Tela.Executar(() =>
    {
        var a = Abrir();

        a.Clicar("Exibir", "Somente Tabela");
        (a.Vm.ModoVisualizacao, a.Janela.PainelGantt.IsVisible, a.Janela.Grade.Opacity).Should().Be((ModoVisualizacao.SomenteTabela, false, 1.0));
        Colunas(a).Should().Be((GridLength.Star, new GridLength(0), new GridLength(0)));
        (a.Item("Exibir", "Somente Tabela").IsChecked, a.Item("Exibir", "Dividido").IsChecked).Should().Be((true, false));

        a.Clicar("Exibir", "Somente Gantt");
        (a.Vm.ModoVisualizacao, a.Janela.PainelGantt.IsVisible, a.Janela.Grade.Opacity, a.Janela.Grade.IsHitTestVisible)
            .Should().Be((ModoVisualizacao.SomenteGantt, true, 0.0, false));
        Colunas(a).Should().Be((new GridLength(0), new GridLength(0), GridLength.Star));
        a.Janela.Divisor.IsVisible.Should().BeFalse();

        a.Clicar("Exibir", "Dividido");
        (a.Vm.ModoVisualizacao, a.Janela.PainelGantt.IsVisible, a.Janela.Grade.Opacity, a.Janela.Divisor.IsVisible)
            .Should().Be((ModoVisualizacao.Dividido, true, 1.0, true));
        Colunas(a).Should().Be((new GridLength(620), new GridLength(6), GridLength.Star));
    });

    [Fact]
    public Task Modos_PelaFaixa_OBotaoDoModoAtualFicaMarcado() => Tela.Executar(() =>
    {
        var a = Abrir();

        Botao(a, "Dividido").Classes.Should().Contain("ativo");
        a.ClicarComOMouse(Botao(a, "Somente Gantt"));

        a.Vm.ModoVisualizacao.Should().Be(ModoVisualizacao.SomenteGantt);
        new[] { "Dividido", "Somente Tabela", "Somente Gantt" }.Select(n => Botao(a, n).Classes.Contains("ativo"))
            .Should().Equal(false, false, true);
        a.ClicarComOMouse(Botao(a, "Somente Tabela"));
        a.Vm.ModoVisualizacao.Should().Be(ModoVisualizacao.SomenteTabela);
    });

    [Fact]
    public Task Modos_VoltarAoDividido_DevolveALarguraQueOUsuarioDeuATabela() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Janela.Paineis.ColumnDefinitions[0].Width = new GridLength(480); // como o divisor faz ao ser arrastado

        a.Vm.ModoVisualizacao = ModoVisualizacao.SomenteGantt;
        a.Vm.ModoVisualizacao = ModoVisualizacao.SomenteTabela;
        a.Vm.ModoVisualizacao = ModoVisualizacao.Dividido;

        Colunas(a).Tabela.Should().Be(new GridLength(480));
    });

    [Fact]
    public Task SomenteGantt_ARodaDoMouseContinuaRolandoOGantt() => Tela.Executar(() =>
    {
        var a = Abrir();
        for (var i = 0; i < 40; i++)
            Ambiente.AdicionarTarefa(a.Vm, $"T{i}", 1);
        a.Vm.ModoVisualizacao = ModoVisualizacao.SomenteGantt;
        Pintar(a);
        var rolagem = a.Janela.Gantt.FindControl<ScrollViewer>("RolagemCorpo")!;
        var ponto = a.Janela.Gantt.TranslatePoint(new Point(300, 200), a.Janela)!.Value;

        a.Janela.MouseWheel(ponto, new Vector(0, -5));
        Pintar(a);

        rolagem.Offset.Y.Should().BeGreaterThan(0, "a grid, mesmo com largura zero, ainda comanda a rolagem vertical");
    });

    [Fact]
    public Task Grade_SemTarefas_PreencheComLinhasVazias_EAcompanhaTarefasETamanho() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        var semTarefas = a.Janela.LinhasVazias.Quantidade;
        semTarefas.Should().BeGreaterThan(10, "uma janela de 700 px tem espaço para bem mais de 10 linhas");

        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        Pintar(a);
        a.Janela.LinhasVazias.Quantidade.Should().Be(semTarefas - 2);

        a.Janela.Height = 400;
        Pintar(a);
        a.Janela.LinhasVazias.Quantidade.Should().BeLessThan(semTarefas - 2, "a janela ficou mais baixa");

        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        a.Clicar("Tarefa", "Remover tarefa");
        Pintar(a);
        var comUma = a.Janela.LinhasVazias.Quantidade;
        comUma.Should().BeGreaterThan(0);
        a.Janela.LinhasVazias.IsHitTestVisible.Should().BeFalse("são só desenho: não recebem o mouse");
    });

    [Fact]
    public Task Grade_ComMaisTarefasQueAAltura_NenhumaLinhaVazia() => Tela.Executar(() =>
    {
        var a = Abrir();
        for (var i = 0; i < 40; i++)
            Ambiente.AdicionarTarefa(a.Vm, $"T{i}", 1);
        Pintar(a);

        a.Janela.LinhasVazias.Quantidade.Should().Be(0);
    });

    [Fact]
    public Task Grade_LinhasVaziasNaoViramTarefas() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        var abaixo = a.Janela.Grade.TranslatePoint(new Point(200, 300), a.Janela)!.Value;

        a.Janela.MouseDown(abaixo, Avalonia.Input.MouseButton.Left);
        a.Janela.MouseUp(abaixo, Avalonia.Input.MouseButton.Left);
        Pintar(a);

        (a.Vm.Tarefas.Count, a.Vm.TarefaSelecionada).Should().Be((0, (TarefaViewModel?)null));
    });

    [Fact]
    public Task ZoomFlutuante_NoCantoInferiorDireitoDoGantt_ForaDaFaixa() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        var painel = a.Janela.PainelGantt;
        var zoom = a.Janela.ControleDeZoom;
        var cantoDoZoom = zoom.TranslatePoint(new Point(zoom.Bounds.Width, zoom.Bounds.Height), painel)!.Value;

        a.Janela.AcessoRapido.Children.OfType<Slider>().Should().BeEmpty();
        (painel.Bounds.Width - cantoDoZoom.X).Should().BeInRange(8, 30, "fica encostado à direita, com margem");
        (painel.Bounds.Height - cantoDoZoom.Y).Should().BeInRange(16, 40, "fica embaixo, acima da barra de rolagem horizontal");
        zoom.Background.Should().BeAssignableTo<ISolidColorBrush>().Which.Color.A.Should().BeInRange((byte)128, (byte)254, "fundo semitransparente");
    });

    [Fact]
    public Task ZoomFlutuante_BotoesMaisEMenos() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        Button Botao(string nome) => a.Janela.ControleDeZoom.GetLogicalDescendants().OfType<Button>()
            .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == nome);

        a.ClicarComOMouse(Botao("Aumentar zoom"));
        a.Vm.Gantt.PixelsPorDia.Should().Be(35);
        a.ClicarComOMouse(Botao("Diminuir zoom"));
        a.ClicarComOMouse(Botao("Diminuir zoom"));
        (a.Vm.Gantt.PixelsPorDia, a.Janela.Zoom.Value).Should().Be((25.0, 25.0));

        a.Vm.Gantt.PixelsPorDia = GanttViewModel.EscalaMaxima;
        Pintar(a);
        Botao("Aumentar zoom").IsEffectivelyEnabled.Should().BeFalse();
    });

    [Fact]
    public Task Folga_DesligadaNaoDesenhaALinha_LigadaPeloMenuDesenha() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Longa", 5);
        Ambiente.AdicionarTarefa(a.Vm, "Curta", 1);
        Pintar(a);
        var corpo = a.Janela.Gantt.FindControl<Canvas>("Corpo")!;
        int LinhasDeFolga() => corpo.Children.OfType<Line>()
            .Count(l => l.Stroke is ISolidColorBrush c && c.Color == Color.Parse("#9E9E9E"));
        a.Vm.Gantt.Layout!.Barras.Should().Contain(b => b.FimDaFolga > b.Direita, "a Curta tem folga");

        LinhasDeFolga().Should().Be(0, "desligada por padrão, com ou sem caminho crítico");
        a.Vm.Gantt.MostrarCaminhoCritico = false;
        Pintar(a);
        LinhasDeFolga().Should().Be(0);

        a.Clicar("Exibir", "Folga");
        Pintar(a);
        (a.Vm.Gantt.MostrarFolga, LinhasDeFolga()).Should().Be((true, 1));
    });
}
