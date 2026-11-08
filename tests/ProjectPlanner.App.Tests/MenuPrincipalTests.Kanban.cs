using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 75, na janela de verdade (o plano "Delete, arrastar e Kanban", K5): Exibir > Kanban troca a grade e o Gantt pelo
// quadro; arrastar um card para Concluída põe 100%, com a guia e a dica durante o gesto; Esc cancela; clicar seleciona (Ctrl
// acrescenta) e os selecionados vão juntos; o Delete num card exclui a tarefa.
public partial class MenuPrincipalTests
{
    private static Border CardNaTela(Abertura a, string nome)
    {
        Pintar(a);
        return a.Janela.QuadroKanban.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("card") && (b.DataContext as CardNoKanban)?.Nome == nome);
    }

    private static Border ColunaNaTela(Abertura a, string nome) =>
        a.Janela.QuadroKanban.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("coluna") && (b.DataContext as ColunaNoKanban)?.Nome == nome);

    private static Point Meio(Abertura a, Control controle) =>
        controle.TranslatePoint(new Point(controle.Bounds.Width / 2, controle.Bounds.Height / 2), a.Janela)!.Value;

    private static IEnumerable<string> CardsDa(Abertura a, string coluna) =>
        a.Vm.Kanban.Colunas.Single(c => c.Nome == coluna).Cards.Select(k => k.Nome);

    private static Abertura NoKanban()
    {
        var a = ComQuatroTarefas();
        a.Clicar("Exibir", "Kanban");
        Pintar(a);
        return a;
    }

    [Fact]
    public Task Kanban_PeloMenu_MostraOQuadroNoLugarDaGradeEDoGantt() => Tela.Executar(() =>
    {
        var a = NoKanban();

        (a.Vm.ModoVisualizacao, a.Janela.QuadroKanban.IsVisible, a.Janela.PainelGantt.IsVisible, a.Janela.Grade.Opacity, a.Janela.Divisor.IsVisible)
            .Should().Be((ModoVisualizacao.Kanban, true, false, 0.0, false));
        (a.Item("Exibir", "Kanban").IsChecked, Botao(a, "Kanban").Classes.Contains("ativo")).Should().Be((true, true));
        a.Vm.Kanban.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Impedida", "Concluída");
        CardsDa(a, "A fazer").Should().Equal("A", "B", "C", "D");
        ColunaNaTela(a, "Concluída").TranslatePoint(new Point(ColunaNaTela(a, "Concluída").Bounds.Width, 0), a.Janela.QuadroKanban)!.Value.X
            .Should().BeLessThanOrEqualTo(a.Janela.QuadroKanban.Bounds.Width, "as quatro colunas cabem na janela");

        a.Clicar("Exibir", "Dividido");
        (a.Janela.QuadroKanban.IsVisible, a.Janela.PainelGantt.IsVisible, a.Janela.Grade.Opacity).Should().Be((false, true, 1.0));
    });

    [Fact]
    public Task Kanban_ArrastarCardParaConcluida_Poe100_ComAGuiaEADicaDuranteOGesto() => Tela.Executar(() =>
    {
        var a = NoKanban();
        var concluida = ColunaNaTela(a, "Concluída");

        Arrastar(a, Meio(a, CardNaTela(a, "B")), Meio(a, concluida), soltar: false);

        a.Vm.Kanban.MovimentoEmCurso.Should().Match<MovimentoNoKanban>(m => m.ColunaId == a.Vm.Kanban.Colunas[3].Id && m.Lacuna == 0);
        var sobreposicao = a.Janela.QuadroKanban.FindControl<Canvas>("Sobreposicao")!;
        sobreposicao.Children.OfType<Rectangle>().Should().ContainSingle("a guia azul onde o card vai cair");
        sobreposicao.Children.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text)
            .Should().Contain("Mover para \"Concluída\" · 100%");
        a.Vm.Kanban.CardDe(a.Vm.Tarefas[1].Tarefa.Id)!.Arrastado.Should().BeTrue("o card de origem fica meio transparente");

        a.Janela.MouseUp(Meio(a, concluida), MouseButton.Left);
        Pintar(a);

        a.Vm.Tarefas[1].PercentualConcluido.Should().Be(100);
        (CardsDa(a, "A fazer"), CardsDa(a, "Concluída")).Should().BeEquivalentTo((new[] { "A", "C", "D" }, new[] { "B" }));
        sobreposicao.Children.Should().BeEmpty();
        a.Vm.Kanban.MovimentoEmCurso.Should().BeNull();
        a.Vm.TextoDesfazer.Should().Be("Desfazer Mover \"B\" para Concluída");
    });

    [Fact]
    public Task Kanban_EscCancela_ESoltarForaDoQuadroNaoMudaNada() => Tela.Executar(() =>
    {
        var a = NoKanban();
        var de = Meio(a, CardNaTela(a, "A"));

        Arrastar(a, de, Meio(a, ColunaNaTela(a, "Em andamento")), soltar: false);
        Apertar(a, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        a.Janela.MouseUp(Meio(a, ColunaNaTela(a, "Em andamento")), MouseButton.Left);
        Pintar(a);

        CardsDa(a, "A fazer").Should().Equal("A", "B", "C", "D");
        a.Vm.Kanban.Colunas.SelectMany(c => c.Cards).Should().OnlyContain(k => !k.Arrastado);
        a.Vm.Kanban.MovimentoEmCurso.Should().BeNull();

        // Soltar em cima da barra de menus (fora do quadro) cancela.
        Arrastar(a, Meio(a, CardNaTela(a, "A")), new Point(de.X, 5));
        CardsDa(a, "A fazer").Should().Equal("A", "B", "C", "D");
        a.Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue("as tarefas novas; o arraste não registrou nada");
        a.Vm.TextoDesfazer.Should().NotContain("Mover");
    });

    [Fact]
    public Task Kanban_CtrlCliqueSelecionaVarios_QueVaoJuntos_EReordenaNaColuna() => Tela.Executar(() =>
    {
        var a = NoKanban();
        var ctrl = a.Janela.GetPlatformSettings()!.HotkeyConfiguration.CommandModifiers.HasFlag(KeyModifiers.Meta)
            ? RawInputModifiers.Meta : RawInputModifiers.Control;
        a.Janela.MouseDown(Meio(a, CardNaTela(a, "C")), MouseButton.Left);
        a.Janela.MouseUp(Meio(a, CardNaTela(a, "C")), MouseButton.Left);
        a.Janela.MouseDown(Meio(a, CardNaTela(a, "D")), MouseButton.Left, ctrl);
        a.Janela.MouseUp(Meio(a, CardNaTela(a, "D")), MouseButton.Left, ctrl);
        Pintar(a);
        a.Vm.Selecionadas.Select(l => l.Nome).Should().Equal("C", "D");
        a.Janela.Grade.SelectedItems.OfType<TarefaViewModel>().Select(l => l.Nome).Should().BeEquivalentTo(["C", "D"], "a mesma seleção na grade");

        // Os dois para o topo da própria coluna: antes de A.
        var a1 = CardNaTela(a, "A");
        Arrastar(a, Meio(a, CardNaTela(a, "D")), a1.TranslatePoint(new Point(a1.Bounds.Width / 2, 3), a.Janela)!.Value);

        CardsDa(a, "A fazer").Should().Equal("C", "D", "A", "B");
        a.Vm.Tarefas.Select(l => l.Nome).Should().Equal(["A", "B", "C", "D"], "a ordem dos cards é só do quadro");
        a.Vm.TextoDesfazer.Should().Be("Desfazer Reordenar 2 cards");
    });

    [Fact]
    public Task Kanban_DeleteNoCard_ExcluiATarefa_EODesfazerTrazDeVolta() => Tela.Executar(() =>
    {
        var a = NoKanban();
        a.Janela.MouseDown(Meio(a, CardNaTela(a, "C")), MouseButton.Left);
        a.Janela.MouseUp(Meio(a, CardNaTela(a, "C")), MouseButton.Left);
        Pintar(a);

        Apertar(a, Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);

        CardsDa(a, "A fazer").Should().Equal("A", "B", "D");
        a.Vm.TarefaSelecionada!.Nome.Should().Be("D", "o card que ficou no lugar");
        a.Janela.FindControl<Border>("AvisoComDesfazer")!.IsVisible.Should().BeTrue();
        a.ClicarComOMouse(a.Janela.FindControl<Button>("DesfazerDoAviso")!);
        CardsDa(a, "A fazer").Should().Equal("A", "B", "C", "D");
    });
}
