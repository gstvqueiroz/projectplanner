using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.Views;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.Tests;

// Bloco 17, na janela de verdade: menu de contexto da grid e do Gantt, câmera na coluna Nome e a galeria.
public partial class MenuPrincipalTests
{
    private static Button? CameraDaLinha(Abertura a, int indice)
    {
        var linha = a.Janela.Grade.GetVisualDescendants().OfType<DataGridRow>()
            .SingleOrDefault(r => r.DataContext == a.Vm.LinhasVisiveis[indice]);
        return linha?.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Classes.Contains("indicadorDeFotos"));
    }

    [Fact]
    public Task Grade_IndicadorDeFotos_SoNasTarefasComFotos_EAbreAGaleria() => Tela.Executar(() =>
    {
        var a = Abrir();
        var semFotos = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var comFotos = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        Pintar(a);
        CameraDaLinha(a, 0)!.IsVisible.Should().BeFalse();
        CameraDaLinha(a, 1)!.IsVisible.Should().BeFalse();

        a.Vm.GerenciarFotosCommand.Execute(comFotos);
        a.Ambiente.Dialogos.Galerias.Last().AdicionarFotoCommand.Execute(null);
        Pintar(a);

        CameraDaLinha(a, 0)!.IsVisible.Should().BeFalse();
        var camera = CameraDaLinha(a, 1)!;
        camera.IsVisible.Should().BeTrue();
        ToolTip.GetTip(camera).Should().Be("1 foto — abrir a galeria");
        camera.GetLogicalDescendants().OfType<TextBlock>().Single().Text.Should().Be("1");

        a.Vm.TarefaSelecionada = semFotos;
        a.ClicarComOMouse(camera);
        a.Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — Estrutura");
    });

    [Fact]
    public Task Grade_CliqueDireitoNumaLinha_SelecionaEOMenuAbreAGaleria() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var segunda = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        Pintar(a);
        var linha = a.Janela.Grade.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext == segunda);
        var celula = linha.GetVisualDescendants().OfType<DataGridCell>().First();

        var pedido = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = celula };
        celula.RaiseEvent(pedido);

        a.Vm.TarefaSelecionada.Should().Be(segunda);
        pedido.Handled.Should().BeTrue("o menu de contexto abriu");
        var item = a.Janela.MenuDaGrade.Items.OfType<MenuItem>().Single();
        item.Header.Should().Be("Gerenciar fotos…");
        item.Command!.Execute(item.CommandParameter);
        a.Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — Estrutura");
        a.Janela.MenuDaGrade.Close();
    });

    [Fact]
    public Task Grade_CliqueDireitoForaDasLinhas_SemMenu() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        var pedido = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = a.Janela.Grade };

        a.Janela.Grade.RaiseEvent(pedido);

        (pedido.Handled, a.Janela.MenuDaGrade.IsOpen).Should().Be((true, false));
    });

    [Fact]
    public Task Gantt_CliqueDireitoNaBarra_SelecionaEOMenuAbreAGaleria_ForaDaBarraSemMenu() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var segunda = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        Pintar(a);
        var superficie = a.Janela.Gantt.FindControl<Panel>("Superficie")!;
        var barra = a.Vm.Gantt.Layout!.Barras.Single(b => b.TarefaId == segunda.Tarefa.Id);
        var menu = superficie.ContextMenu!;

        // Clique direito de verdade sobre a barra da Estrutura.
        var ponto = superficie.TranslatePoint(new Point(barra.X + barra.Largura / 2, barra.CentroY), a.Janela)!.Value;
        a.Janela.MouseDown(ponto, MouseButton.Right);
        a.Janela.MouseUp(ponto, MouseButton.Right);
        Pintar(a);

        a.Vm.TarefaSelecionada.Should().Be(segunda);
        menu.IsOpen.Should().BeTrue();
        var item = menu.Items.OfType<MenuItem>().Single();
        item.Header.Should().Be("Gerenciar fotos…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        a.Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — Estrutura");
        menu.Close();
        Pintar(a);

        // Fora das barras: nada.
        var vazio = superficie.TranslatePoint(new Point(5, barra.CentroY + 5 * GanttViewModelAltura), a.Janela)!.Value;
        a.Janela.MouseDown(vazio, MouseButton.Right);
        a.Janela.MouseUp(vazio, MouseButton.Right);
        Pintar(a);
        menu.IsOpen.Should().BeFalse();
    });

    [Fact]
    public Task Gantt_PedidoDeMenuSemPosicao_UsaOMouseSobreABarra() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var segunda = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        Pintar(a);
        var superficie = a.Janela.Gantt.FindControl<Panel>("Superficie")!;
        var barra = a.Vm.Gantt.Layout!.Barras.Single(b => b.TarefaId == segunda.Tarefa.Id);
        a.Janela.MouseMove(superficie.TranslatePoint(new Point(barra.X + barra.Largura / 2, barra.CentroY), a.Janela)!.Value);

        // Pedido do menu sem a posição do ponteiro.
        var pedido = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = superficie };
        superficie.RaiseEvent(pedido);
        Pintar(a);

        (a.Vm.TarefaSelecionada, superficie.ContextMenu!.IsOpen).Should().Be((segunda, true));
        superficie.ContextMenu.Close();

        // Com o mouse fora do Gantt, um pedido sem posição (teclado) não abre nada.
        a.Janela.MouseMove(new Point(5, 5));
        var outro = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = superficie };
        superficie.RaiseEvent(outro);
        Pintar(a);
        superficie.ContextMenu.IsOpen.Should().BeFalse();
    });

    private const double GanttViewModelAltura = ViewModels.GanttViewModel.AlturaLinha;

    [Fact]
    public Task Galeria_Vazia_MostraAMensagemEOBotaoAdicionar_ComOAviso() => Tela.Executar(() =>
    {
        var a = Abrir();
        var tarefa = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Vm.GerenciarFotosCommand.Execute(tarefa);
        var janela = new GaleriaFotosWindow { DataContext = a.Ambiente.Dialogos.Galerias.Last() };
        janela.Show();
        Dispatcher.UIThread.RunJobs();

        var adicionar = janela.FindControl<Button>("AdicionarFoto")!;
        var vazia = janela.FindControl<TextBlock>("MensagemVazia")!;
        (adicionar.IsEffectivelyVisible, adicionar.IsEffectivelyEnabled, adicionar.Content).Should().Be((true, true, "Adicionar foto"));
        (vazia.IsVisible, vazia.Text).Should().Be((true, "Nenhuma foto adicionada ainda."));
        janela.FindControl<TextBlock>("Aviso")!.Text.Should().Contain("ainda será implementado");

        var ponto = adicionar.TranslatePoint(new Point(adicionar.Bounds.Width / 2, adicionar.Bounds.Height / 2), janela)!.Value;
        janela.MouseDown(ponto, MouseButton.Left);
        janela.MouseUp(ponto, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        tarefa.Tarefa.Fotos.Should().ContainSingle();
        vazia.IsVisible.Should().BeFalse();
        adicionar.IsEffectivelyVisible.Should().BeTrue("o botão continua lá para as próximas");
        janela.Close();
    });
}
