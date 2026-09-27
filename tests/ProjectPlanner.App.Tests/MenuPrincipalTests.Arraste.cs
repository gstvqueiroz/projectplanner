using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 18, na janela de verdade: arrastar uma linha pela célula Nº com o mouse.
public partial class MenuPrincipalTests
{
    // O meio da célula de uma coluna (pelo cabeçalho) na linha visível indicada, em coordenadas da janela.
    private static Point Celula(Abertura a, string coluna, int linha, double fracaoDaAltura = 0.5)
    {
        Pintar(a);
        var cabecalho = a.Janela.Grade.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, coluna));
        var linhas = a.Janela.Grade.GetVisualDescendants().OfType<DataGridRowsPresenter>().Single();
        var x = cabecalho.TranslatePoint(new Point(cabecalho.Bounds.Width / 2, 0), a.Janela)!.Value.X;
        var y = linhas.TranslatePoint(new Point(0, (linha + fracaoDaAltura) * ViewModels.GanttViewModel.AlturaLinha), a.Janela)!.Value.Y;
        return new Point(x, y);
    }

    private static void Arrastar(Abertura a, Point de, Point para, bool soltar = true)
    {
        a.Janela.MouseDown(de, MouseButton.Left);
        Pintar(a);
        for (var i = 1; i <= 6; i++)
        {
            a.Janela.MouseMove(new Point(de.X + (para.X - de.X) * i / 6, de.Y + (para.Y - de.Y) * i / 6), RawInputModifiers.LeftMouseButton);
            Pintar(a);
        }
        if (soltar)
        {
            a.Janela.MouseUp(para, MouseButton.Left);
            Pintar(a);
        }
    }

    private static Abertura ComQuatroTarefas()
    {
        var a = Abrir();
        foreach (var nome in new[] { "A", "B", "C", "D" })
            Ambiente.AdicionarTarefa(a.Vm, nome, 1);
        Pintar(a);
        return a;
    }

    [Fact]
    public Task Arrastar_PeloNumero_ReordenaEMostraAGuiaDuranteOGesto() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        var linhaD = a.Vm.LinhasVisiveis[3];

        Arrastar(a, Celula(a, "Nº", 3), Celula(a, "Nº", 0, 0.25), soltar: false);

        linhaD.SendoArrastada.Should().BeTrue("a linha arrastada fica meio transparente");
        a.Janela.GuiaDeArraste.Linha.Should().NotBeNull();
        var linhas = a.Janela.Grade.GetVisualDescendants().OfType<DataGridRowsPresenter>().Single();
        // Nas coordenadas da camada da guia, que é onde ela é desenhada.
        a.Janela.GuiaDeArraste.Linha!.Value.Y.Should().BeApproximately(linhas.TranslatePoint(default, a.Janela.GuiaDeArraste)!.Value.Y, 1,
            "a guia fica em cima da primeira linha");
        a.Janela.GuiaDeArraste.Linha!.Value.Y.Should().BeInRange(0, a.Janela.GuiaDeArraste.Bounds.Height);

        a.Janela.MouseUp(Celula(a, "Nº", 0, 0.25), MouseButton.Left);
        Pintar(a);

        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("D", "A", "B", "C");
        (linhaD.SendoArrastada, a.Janela.GuiaDeArraste.Linha).Should().Be((false, ((double, double, double)?)null));
    });

    [Fact]
    public Task Arrastar_SobreAsLinhasVazias_NaoMostraGuia_ESoltarNaoMudaNada() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();

        Arrastar(a, Celula(a, "Nº", 1), Celula(a, "Nº", 7), soltar: false); // linha 7: preenchimento
        a.Janela.GuiaDeArraste.Linha.Should().BeNull();
        a.Janela.MouseUp(Celula(a, "Nº", 7), MouseButton.Left);
        Pintar(a);

        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");
        a.Vm.Mensagem.Should().Be(MainWindowViewModel.MensagemReordenacaoInvalida);
    });

    [Fact]
    public Task Arrastar_ForaDaColunaNumero_NaoReordena() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();

        Arrastar(a, Celula(a, "Duração\n(dias)", 3), Celula(a, "Duração\n(dias)", 0, 0.25));

        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");
        a.Janela.GuiaDeArraste.Linha.Should().BeNull();
    });

    [Fact]
    public Task Arrastar_EscCancela() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();

        Arrastar(a, Celula(a, "Nº", 3), Celula(a, "Nº", 0, 0.25), soltar: false);
        a.Janela.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pintar(a);
        a.Janela.MouseUp(Celula(a, "Nº", 0, 0.25), MouseButton.Left);
        Pintar(a);

        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");
        a.Janela.GuiaDeArraste.Linha.Should().BeNull();
    });

    [Fact]
    public Task CliqueSimplesNoNumero_SoSeleciona() => Tela.Executar(() =>
    {
        var a = ComQuatroTarefas();
        var ponto = Celula(a, "Nº", 2);

        a.Janela.MouseDown(ponto, MouseButton.Left);
        a.Janela.MouseUp(ponto, MouseButton.Left);
        Pintar(a);

        (a.Vm.TarefaSelecionada?.Nome, a.Vm.PossuiAlteracoesNaoSalvas).Should().Be(("C", true));
        a.Vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");
    });
}
