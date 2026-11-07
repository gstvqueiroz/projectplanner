using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

// Bloco 72, na janela de verdade: Ctrl+clique (⌘ no Mac) na grade seleciona várias, a tecla Delete (⌫ no Mac) as exclui e o
// aviso "Desfazer" aparece; editando um texto, a tecla é do texto.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task Delete_NaGrade_ExcluiAsSelecionadas_EOAvisoDesfaz() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        a.Janela.MouseDown(Celula(a, "Nº", 1), MouseButton.Left);
        a.Janela.MouseUp(Celula(a, "Nº", 1), MouseButton.Left);
        // A tecla da seleção múltipla da grade é a da plataforma (⌘ no Mac de verdade; Ctrl na janela de teste).
        var ctrl = a.Janela.GetPlatformSettings()!.HotkeyConfiguration.CommandModifiers.HasFlag(KeyModifiers.Meta)
            ? RawInputModifiers.Meta : RawInputModifiers.Control;
        a.Janela.MouseDown(Celula(a, "Nº", 3), MouseButton.Left, ctrl);
        a.Janela.MouseUp(Celula(a, "Nº", 3), MouseButton.Left, ctrl);
        Pintar(a);

        a.Vm.Selecionadas.Select(l => l.Nome).Should().Equal("B", "D");

        a.Janela.Grade.Focus();
        Apertar(a, Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);

        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("A", "C");
        a.Janela.FindControl<Border>("AvisoComDesfazer")!.IsVisible.Should().BeTrue();
        a.ClicarComOMouse(a.Janela.FindControl<Button>("DesfazerDoAviso")!);
        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("A", "B", "C", "D");
        a.Janela.FindControl<Border>("AvisoComDesfazer")!.IsVisible.Should().BeFalse();
    });

    [Fact]
    public Task Delete_EditandoUmTexto_NaoExclui() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        var texto = a.Janela.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
        texto.Focus();

        Apertar(a, Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);

        a.Vm.Tarefas.Should().HaveCount(4);
    });
}

public partial class MenuPrincipalTests
{
    [Fact]
    public Task Delete_NoGantt_DepoisDeClicarNaBarra() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        var gantt = a.Janela.FindControl<ProjectPlanner.App.Views.GanttChartView>("Gantt")!;
        var barra = a.Vm.Gantt.Layout!.Barras[2];
        var corpo = gantt.FindControl<Control>("Corpo")!;
        var ponto = corpo.TranslatePoint(new Point(barra.X + barra.Largura / 2, barra.Linha * ViewModels.GanttViewModel.AlturaLinha + ViewModels.GanttViewModel.AlturaLinha / 2), a.Janela)!.Value;
        a.Janela.MouseDown(ponto, MouseButton.Left);
        a.Janela.MouseUp(ponto, MouseButton.Left);
        Pintar(a);
        a.Vm.TarefaSelecionada!.Nome.Should().Be("C");

        Apertar(a, Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);

        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("A", "B", "D");
    });
}
