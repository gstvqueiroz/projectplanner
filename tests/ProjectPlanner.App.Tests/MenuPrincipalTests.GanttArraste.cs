using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ProjectPlanner.App.Tests;

// Bloco 74, na janela de verdade (o plano "Regras no gráfico de Gantt", G1 a G5): arrastar o corpo de uma barra na vertical
// reordena pelas regras da grade, com a guia nos dois painéis; a trava de eixo (8 px) impede mudar posição e data no mesmo
// gesto; a horizontal não muda nada (as datas mudam na grade); Esc cancela.
public partial class MenuPrincipalTests
{
    // O meio da barra da linha visível indicada, em coordenadas da janela (com um deslocamento em X opcional).
    private static Point NaBarra(Abertura a, int linha, double frac = 0.5)
    {
        Pintar(a);
        var gantt = a.Janela.FindControl<ProjectPlanner.App.Views.GanttChartView>("Gantt")!;
        var corpo = gantt.FindControl<Control>("Corpo")!;
        var barra = a.Vm.Gantt.Layout!.Barras.Single(b => b.Linha == linha);
        return corpo.TranslatePoint(new Point(barra.X + barra.Largura * frac, (linha + 0.5) * ViewModels.GanttViewModel.AlturaLinha), a.Janela)!.Value;
    }

    private static Point NaLinhaDoGantt(Abertura a, int linha, double fracaoDaAltura, double x)
    {
        var gantt = a.Janela.FindControl<ProjectPlanner.App.Views.GanttChartView>("Gantt")!;
        var corpo = gantt.FindControl<Control>("Corpo")!;
        return corpo.TranslatePoint(new Point(x, (linha + fracaoDaAltura) * ViewModels.GanttViewModel.AlturaLinha), a.Janela)!.Value;
    }

    private static Canvas Interacao(Abertura a) =>
        a.Janela.FindControl<ProjectPlanner.App.Views.GanttChartView>("Gantt")!.FindControl<Canvas>("Interacao")!;

    [Fact]
    public Task Gantt_ArrastarABarraNaVertical_Reordena_ComAGuiaNaGradeENoGantt() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        var de = NaBarra(a, 2);
        var x = de.X;

        Arrastar(a, de, new Point(x, NaLinhaDoGantt(a, 0, 0.25, 0).Y), soltar: false);

        Interacao(a).Children.OfType<Line>().Should().ContainSingle("a guia azul no Gantt");
        a.Janela.GuiaDeArraste.Linha.Should().NotBeNull("a mesma guia na grade, na mesma altura");
        a.Vm.Gantt.MovimentoEmCurso!.Lacuna.Should().Be(0);
        a.Janela.MouseUp(new Point(x, NaLinhaDoGantt(a, 0, 0.25, 0).Y), MouseButton.Left);
        Pintar(a);

        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("C", "A", "B", "D");
        (a.Janela.GuiaDeArraste.Linha, a.Vm.Gantt.MovimentoEmCurso).Should().Be((((double, double, double)?)null, (ViewModels.MovimentoNoGantt?)null));
    });

    [Fact]
    public Task Gantt_ArrastarNaHorizontal_EODiagonalTravado_NaoMudamNada() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        var de = NaBarra(a, 2);

        Arrastar(a, de, new Point(de.X + 120, de.Y), soltar: false);
        Interacao(a).Children.OfType<Border>().Single().Child.Should().BeOfType<TextBlock>()
            .Which.Text.Should().Be("As datas mudam na grade (Início, Duração e Predecessoras)");
        a.Janela.MouseUp(new Point(de.X + 120, de.Y), MouseButton.Left);
        Pintar(a);
        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");

        // Nos primeiros 8 px andou mais para o lado: o eixo fica horizontal até soltar, mesmo descendo depois.
        a.Janela.MouseDown(de, MouseButton.Left);
        a.Janela.MouseMove(new Point(de.X + 12, de.Y + 2), RawInputModifiers.LeftMouseButton);
        a.Janela.MouseMove(new Point(de.X + 14, NaLinhaDoGantt(a, 0, 0.25, 0).Y), RawInputModifiers.LeftMouseButton);
        a.Janela.MouseUp(new Point(de.X + 14, NaLinhaDoGantt(a, 0, 0.25, 0).Y), MouseButton.Left);
        Pintar(a);
        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");
    });

    [Fact]
    public Task Gantt_EscCancela_EVariasSelecionadasVaoJuntas() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        Arrastar(a, NaBarra(a, 3), NaLinhaDoGantt(a, 0, 0.25, NaBarra(a, 3).X - 0), soltar: false);
        a.Janela.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pintar(a);
        a.Janela.MouseUp(NaLinhaDoGantt(a, 0, 0.25, 0), MouseButton.Left);
        Pintar(a);
        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");

        a.Vm.DefinirSelecao([a.Vm.LinhasVisiveis[2], a.Vm.LinhasVisiveis[3]]);
        Pintar(a);
        var de = NaBarra(a, 3);
        Arrastar(a, de, new Point(de.X, NaLinhaDoGantt(a, 0, 0.25, 0).Y));

        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("C", "D", "A", "B");
    });

    [Fact]
    public Task Gantt_CliqueNumaDasSelecionadas_FicaSoEla() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        a.Vm.DefinirSelecao([a.Vm.LinhasVisiveis[1], a.Vm.LinhasVisiveis[2]]);
        var ponto = NaBarra(a, 2);

        a.Janela.MouseDown(ponto, MouseButton.Left);
        a.Janela.MouseUp(ponto, MouseButton.Left);
        Pintar(a);

        a.Vm.Selecionadas.Select(l => l.Nome).Should().Equal("C");
    });
}
