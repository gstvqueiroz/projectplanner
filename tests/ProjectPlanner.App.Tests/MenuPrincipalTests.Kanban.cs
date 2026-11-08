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

    // Bloco 76 (K2): o menu ⋯ da coluna (também no botão direito do cabeçalho) e o "+ Adicionar coluna".
    private static IEnumerable<string> Titulos(ContextMenu menu) =>
        menu.Items.Select(i => i is Separator ? Linha : (string)((MenuItem)i!).Header!);

    [Fact]
    public Task Kanban_MenuDaColuna_PeloBotaoEPeloBotaoDireito() => Tela.Executar(() =>
    {
        var a = NoKanban();
        var impedida = ColunaNaTela(a, "Impedida");

        a.ClicarComOMouse(impedida.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("opcoes")));

        var menu = a.Janela.QuadroKanban.MenuAberto!;
        Titulos(menu).Should().Equal("Renomear…", "Cor", "Tipo", "Limite de cards…", Linha, "Mover para a esquerda", "Mover para a direita", Linha,
            "Excluir coluna…");
        var tipo = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Tipo");
        tipo.Items.OfType<MenuItem>().Where(i => i.IsChecked).Select(i => (string)i.Header!).Should().Equal("Em andamento (o card mantém o %)");
        menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Cor").Items.OfType<MenuItem>().Should().HaveCount(8);
        var excluir = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Excluir coluna…");
        excluir.Command!.Execute(excluir.CommandParameter);
        menu.Close();
        Pintar(a);
        a.Vm.Kanban.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Concluída");
        a.Janela.FindControl<Border>("AvisoComDesfazer")!.IsVisible.Should().BeTrue();

        var cabecalho = ColunaNaTela(a, "A fazer").GetVisualDescendants().OfType<DockPanel>().First(d => d.Classes.Contains("cabecalho"));
        a.Janela.MouseDown(Meio(a, cabecalho), MouseButton.Right);
        a.Janela.MouseUp(Meio(a, cabecalho), MouseButton.Right);
        menu = a.Janela.QuadroKanban.MenuAberto!;
        menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Mover para a esquerda").IsEnabled.Should().BeFalse("é a primeira");
        menu.Close();
    });

    [Fact]
    public Task Kanban_AdicionarColuna_PeloBotao_SemPerderASelecao() => Tela.Executar(() =>
    {
        var a = NoKanban();
        a.Janela.MouseDown(Meio(a, CardNaTela(a, "B")), MouseButton.Left);
        a.Janela.MouseUp(Meio(a, CardNaTela(a, "B")), MouseButton.Left);
        a.Ambiente.Dialogos.TextoDigitado = "Em revisão";

        a.ClicarComOMouse(a.Janela.QuadroKanban.FindControl<Button>("BotaoAdicionarColuna")!);
        Pintar(a);

        a.Vm.Kanban.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Impedida", "Em revisão", "Concluída");
        ColunaNaTela(a, "Em revisão").IsVisible.Should().BeTrue();
        a.Vm.TarefaSelecionada!.Nome.Should().Be("B", "clicar no botão não é clicar numa área vazia");
    });

    // Bloco 77 (K4 e K5): o painel do card (duplo clique), o menu do card e os filtros, na janela de verdade.
    [Fact]
    public Task Kanban_DuploCliqueAbreOPainel_EnterAdicionaItem_EDeleteNoPainelNaoExclui() => Tela.Executar(() =>
    {
        var a = NoKanban();
        var b = Meio(a, CardNaTela(a, "B"));

        a.Janela.MouseDown(b, MouseButton.Left);
        a.Janela.MouseUp(b, MouseButton.Left);
        a.Janela.MouseDown(b, MouseButton.Left);
        a.Janela.MouseUp(b, MouseButton.Left);
        Pintar(a);

        var quadro = a.Janela.QuadroKanban;
        quadro.FindControl<Border>("PainelDoCard")!.IsVisible.Should().BeTrue();
        quadro.FindControl<TextBox>("NomeNoPainel")!.Text.Should().Be("B");

        var novoItem = quadro.FindControl<TextBox>("NovoItemDoChecklist")!;
        novoItem.Focus();
        novoItem.Text = "Conferir o nível";
        Apertar(a, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
        a.Vm.Kanban.CardDe(a.Vm.Tarefas[1].Tarefa.Id)!.Checklist.Should().Be("0/1");
        novoItem.Text.Should().BeEmpty();

        Apertar(a, Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);
        quadro.FindControl<Button>("BotaoArquivados")!.Focus();
        Apertar(a, Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);
        a.Vm.Tarefas.Should().HaveCount(4, "no painel e na barra dos filtros, o Delete não exclui tarefas (E1)");

        var nome = quadro.FindControl<TextBox>("NomeNoPainel")!;
        nome.Focus();
        nome.Text = "B revisada";
        quadro.FindControl<TextBox>("PercentualNoPainel")!.Focus(); // sair do campo grava
        Pintar(a);
        a.Vm.Tarefas[1].Nome.Should().Be("B revisada");
        CardsDa(a, "A fazer").Should().Contain("B revisada");
    });

    [Fact]
    public Task Kanban_MenuDoCard_MoverParaConcluida_EArquivar() => Tela.Executar(() =>
    {
        var a = NoKanban();
        var cardA = Meio(a, CardNaTela(a, "A"));

        a.Janela.MouseDown(cardA, MouseButton.Right);
        a.Janela.MouseUp(cardA, MouseButton.Right);

        var menu = a.Janela.QuadroKanban.MenuAberto!;
        Titulos(menu).Should().Equal("Abrir card", "Mover para", Linha, "Arquivar", "Excluir tarefa");
        var mover = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Mover para");
        mover.Items.OfType<MenuItem>().Select(i => ((string)i.Header!, i.IsEnabled))
            .Should().Equal(("A fazer", false), ("Em andamento", true), ("Impedida", true), ("Concluída", true));
        var concluida = mover.Items.OfType<MenuItem>().Last();
        concluida.Command!.Execute(null);
        menu.Close();
        Pintar(a);

        (CardsDa(a, "Concluída"), a.Vm.Tarefas[0].PercentualConcluido).Should().BeEquivalentTo((new[] { "A" }, 100));

        var cardB = Meio(a, CardNaTela(a, "B"));
        a.Janela.MouseDown(cardB, MouseButton.Right);
        a.Janela.MouseUp(cardB, MouseButton.Right);
        menu = a.Janela.QuadroKanban.MenuAberto!;
        var arquivar = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Arquivar");
        arquivar.Command!.Execute(null);
        menu.Close();
        Pintar(a);
        CardsDa(a, "A fazer").Should().Equal("C", "D");
        a.Janela.QuadroKanban.FindControl<Button>("BotaoArquivados")!.Content.Should().Be("Arquivados (1)");
    });

    [Fact]
    public Task Kanban_BuscaNaBarra_EscondeCards_ELimparFiltrosMostraTodos() => Tela.Executar(() =>
    {
        var a = NoKanban();
        var quadro = a.Janela.QuadroKanban;
        var limpar = quadro.FindControl<Button>("BotaoLimparFiltros")!;
        limpar.IsVisible.Should().BeFalse();

        quadro.FindControl<TextBox>("BuscaNoQuadro")!.Text = "c";
        Pintar(a);

        CardsDa(a, "A fazer").Should().Equal("C");
        ColunaNaTela(a, "A fazer").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("1 de 4");
        limpar.IsVisible.Should().BeTrue();
        a.ClicarComOMouse(limpar);
        Pintar(a);
        CardsDa(a, "A fazer").Should().Equal("A", "B", "C", "D");
    });
}
