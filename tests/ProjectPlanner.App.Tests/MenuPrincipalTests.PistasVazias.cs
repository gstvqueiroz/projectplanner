using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 20, na janela de verdade: o Gantt desenha as divisórias das pistas vazias, como a grid ao lado.
public partial class MenuPrincipalTests
{
    // Divisórias horizontais das pistas no corpo do Gantt (de largura toda, na altura exata de uma pista).
    private static int DivisoriasDePista(Abertura a)
    {
        var corpo = a.Janela.Gantt.FindControl<Canvas>("Corpo")!;
        return corpo.Children.OfType<Line>().Count(l => l.StartPoint.Y == l.EndPoint.Y && l.StartPoint.X == 0
            && l.StartPoint.Y > 0 && l.StartPoint.Y % GanttViewModel.AlturaLinha == 0);
    }

    [Fact]
    public Task Gantt_SemTarefas_PistasVaziasAteOFim_EmSintoniaComAGrid() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        var layout = a.Vm.Gantt.Layout!;

        layout.PistasVazias.Should().BeGreaterThan(10);
        DivisoriasDePista(a).Should().Be(layout.Pistas);
        (layout.Pistas * GanttViewModel.AlturaLinha).Should().BeGreaterThanOrEqualTo(layout.Altura, "cobrem a altura toda");
        Math.Abs(layout.PistasVazias - a.Janela.LinhasVazias.Quantidade).Should().BeLessThanOrEqualTo(1,
            "a grid e o Gantt fazem a mesma conta; a área útil deles só difere pela barra de rolagem horizontal");
    });

    [Fact]
    public Task Gantt_PoucasTarefas_PistasContinuamAbaixo_EAcompanhamTarefasETamanho() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        var semTarefas = a.Vm.Gantt.Layout!.PistasVazias;

        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        Pintar(a);
        (a.Vm.Gantt.Layout!.PistasVazias, DivisoriasDePista(a)).Should().Be((semTarefas - 2, semTarefas));

        a.Janela.Height = 400;
        Pintar(a);
        a.Vm.Gantt.Layout!.PistasVazias.Should().BeLessThan(semTarefas - 2, "a janela ficou mais baixa");
        DivisoriasDePista(a).Should().Be(a.Vm.Gantt.Layout!.Pistas);

        a.Vm.Gantt.PixelsPorDia = 50;
        Pintar(a);
        DivisoriasDePista(a).Should().Be(a.Vm.Gantt.Layout!.Pistas, "o zoom só muda a largura");
    });
}
