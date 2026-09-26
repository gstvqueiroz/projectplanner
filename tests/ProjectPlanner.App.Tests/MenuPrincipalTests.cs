using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.App.Views;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// Bloco 15: a barra de ferramentas única virou menu (Arquivo, Exibir, Inserir, Tarefa) mais uma barra de
// acesso rápido. Estes testes abrem a MainWindow de verdade (headless) e disparam cada item como um clique,
// para garantir que nenhum comando ficou órfão e que cada um ainda faz o que o botão antigo fazia.
public class MenuPrincipalTests
{
    private const string Linha = "—";

    private sealed record Abertura(MainWindow Janela, MainWindowViewModel Vm, Ambiente Ambiente)
    {
        // O item no fim do caminho, com os menus do caminho abertos, como quando o usuário vai até ele.
        public MenuItem Item(params string[] caminho)
        {
            Janela.MenuPrincipal.Close();
            IEnumerable<object?> itens = Janela.MenuPrincipal.Items;
            MenuItem? atual = null;
            foreach (var nome in caminho)
            {
                if (atual is not null)
                {
                    atual.Open();
                    Dispatcher.UIThread.RunJobs();
                }
                atual = itens.OfType<MenuItem>().SingleOrDefault(i => Equals(i.Header, nome))
                        ?? throw new InvalidOperationException($"Não há \"{nome}\" no menu ({string.Join(" > ", caminho)}).");
                itens = atual.Items;
            }
            return atual!;
        }

        public void Clicar(params string[] caminho) => Clicar(Item(caminho));

        // Clique de mouse de verdade no item, com o submenu dele aberto (Item já abre o caminho). O menu
        // marca/desmarca os itens com ✓ e executa o comando, como para o usuário; item desabilitado não faz nada.
        public void Clicar(MenuItem item)
        {
            AbrirOCaminhoAte(item);
            ClicarComOMouse(item);
            Janela.MenuPrincipal.Close();
            Dispatcher.UIThread.RunJobs();
        }

        private void AbrirOCaminhoAte(MenuItem item)
        {
            var caminho = new List<MenuItem>();
            for (var pai = item.Parent as MenuItem; pai is not null; pai = pai.Parent as MenuItem)
                caminho.Insert(0, pai);
            foreach (var menu in caminho)
            {
                menu.Open();
                Dispatcher.UIThread.RunJobs();
            }
        }

        // Clique de mouse de verdade no meio do controle, na janela (ou no popup do menu) em que ele está.
        public void ClicarComOMouse(Control controle)
        {
            var topo = TopLevel.GetTopLevel(controle)
                       ?? throw new InvalidOperationException($"\"{controle}\" não está visível (o menu dele não abriu?).");
            var meio = controle.TranslatePoint(new Point(controle.Bounds.Width / 2, controle.Bounds.Height / 2), topo)!.Value;
            topo.MouseMove(meio);
            topo.MouseDown(meio, MouseButton.Left);
            topo.MouseUp(meio, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Abertura Abrir(Action<Ambiente>? preparar = null)
    {
        var ambiente = new Ambiente();
        preparar?.Invoke(ambiente);
        var vm = ambiente.CriarViewModel();
        var janela = new MainWindow { DataContext = vm, Width = 1470, Height = 700 };
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        return new Abertura(janela, vm, ambiente);
    }

    private static IEnumerable<MenuItem> TodosOsItens(ItemsControl raiz) =>
        raiz.Items.OfType<MenuItem>().SelectMany(i => TodosOsItens(i).Prepend(i));

    private static IEnumerable<string> Titulos(MenuItem menu) =>
        menu.Items.Select(i => i is Separator ? Linha : (string)((MenuItem)i!).Header!);

    [Fact]
    public Task Menus_TemAsCategoriasAprovadas_NaOrdem() => Tela.Executar(() =>
    {
        var a = Abrir();

        a.Janela.MenuPrincipal.Items.OfType<MenuItem>().Select(m => m.Header)
            .Should().Equal("Arquivo", "Exibir", "Inserir", "Tarefa");
        Titulos(a.Item("Arquivo")).Should().Equal("Novo", "Abrir…", Linha, "Salvar", "Salvar como…");
        Titulos(a.Item("Exibir")).Should().Equal("Seleção", "Caminho crítico", "Fotos", Linha, "Colunas");
        Titulos(a.Item("Inserir")).Should().Equal("Adicionar tarefa", "Adicionar subtarefa", Linha, "Fotos de exemplo (desenvolvimento)");
        Titulos(a.Item("Tarefa")).Should().Equal("Remover tarefa", Linha, "Promover", "Rebaixar", Linha, "Recalcular cronograma");
    });

    [Fact]
    public Task NenhumComandoDoViewModelFicouOrfao() => Tela.Executar(() =>
    {
        var a = Abrir();
        var comandosDoViewModel = typeof(MainWindowViewModel).GetProperties()
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => (Nome: p.Name, Comando: (ICommand)p.GetValue(a.Vm)!))
            .Append((Nome: nameof(ColunasViewModel.RestaurarPadraoCommand), Comando: (ICommand)a.Vm.Colunas.RestaurarPadraoCommand))
            .ToList();

        var noMenu = TodosOsItens(a.Janela.MenuPrincipal).Select(i => i.Command).OfType<ICommand>().ToHashSet();

        comandosDoViewModel.Should().HaveCountGreaterThanOrEqualTo(12);
        comandosDoViewModel.Where(c => !noMenu.Contains(c.Comando)).Select(c => c.Nome)
            .Should().BeEmpty("todo comando da antiga barra de ferramentas precisa estar em algum menu");
    });

    [Fact]
    public Task BarraAntiga_Sumiu() => Tela.Executar(() =>
    {
        var a = Abrir();
        var antigos = new[] { "Novo", "Abrir…", "Salvar como…", "Remover tarefa", "Subtarefa", "Recalcular", "Colunas ▾" };

        a.Janela.GetLogicalDescendants().OfType<Button>().Select(b => b.Content).OfType<string>()
            .Should().NotIntersectWith(antigos);
        a.Janela.GetLogicalDescendants().OfType<CheckBox>().Select(c => c.Content).OfType<string>()
            .Should().NotIntersectWith(["Seleção", "Caminho crítico", "Fotos"]);
    });

    [Fact]
    public Task AcessoRapido_SalvarAdicionarTarefaERecalcular_OsMesmosComandosDoMenu() => Tela.Executar(() =>
    {
        var a = Abrir();
        var botoes = a.Janela.AcessoRapido.Children.OfType<Button>().ToList();

        botoes.Select(b => (b.Command, ToolTip.GetTip(b))).Should().Equal(
            (a.Vm.SalvarProjetoCommand, "Salvar"),
            (a.Vm.AdicionarTarefaCommand, "Adicionar tarefa"),
            (a.Vm.RecalcularCronogramaCommand, "Recalcular cronograma"));
        botoes.Should().OnlyContain(b => b.Content is Avalonia.Controls.Shapes.Path, "são botões só de ícone");
        botoes.Select(Avalonia.Automation.AutomationProperties.GetName)
            .Should().Equal(["Salvar", "Adicionar tarefa", "Recalcular cronograma"], "sem texto, o leitor de tela precisa do nome");
    });

    [Fact]
    public Task AcessoRapido_CliqueFazOEfeito() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Ambiente.Dialogos.ArquivoParaSalvar = "/projetos/obra.pplan";
        var (salvar, adicionar, recalcular) = (a.Janela.AcessoRapido.Children[0], a.Janela.AcessoRapido.Children[1],
            a.Janela.AcessoRapido.Children[2]);
        var calculos = a.Ambiente.Calculadora.Chamadas;

        a.ClicarComOMouse(adicionar);
        a.ClicarComOMouse(recalcular);
        a.ClicarComOMouse(salvar);

        a.Vm.Tarefas.Should().ContainSingle();
        a.Ambiente.Calculadora.Chamadas.Should().BeGreaterThan(calculos);
        a.Ambiente.Repositorio.Salvos.Select(s => s.Caminho).Should().Equal("/projetos/obra.pplan");
    });

    [Fact]
    public Task InserirETarefa_PeloMenu_FazemOQueOsBotoesFaziam() => Tela.Executar(() =>
    {
        var a = Abrir();

        a.Clicar("Inserir", "Adicionar tarefa");
        var mae = a.Vm.Tarefas.Should().ContainSingle().Subject;
        a.Clicar("Inserir", "Adicionar subtarefa");
        var filha = a.Vm.Tarefas[1];
        filha.Tarefa.TarefaPaiId.Should().Be(mae.Tarefa.Id);

        a.Vm.TarefaSelecionada = filha;
        a.Clicar("Tarefa", "Promover");
        filha.Tarefa.TarefaPaiId.Should().BeNull();
        a.Clicar("Tarefa", "Rebaixar");
        filha.Tarefa.TarefaPaiId.Should().Be(mae.Tarefa.Id);

        var calculos = a.Ambiente.Calculadora.Chamadas;
        a.Clicar("Tarefa", "Recalcular cronograma");
        a.Ambiente.Calculadora.Chamadas.Should().BeGreaterThan(calculos);

        a.Vm.TarefaSelecionada = filha;
        a.Clicar("Tarefa", "Remover tarefa"); // tarefa comum: sai sem perguntar (só resumo pede confirmação)
        a.Vm.Tarefas.Should().Equal(mae);
    });

    [Fact]
    public Task Arquivo_PeloMenu_NovoAbrirSalvarESalvarComo() => Tela.Executar(() =>
    {
        var obra = new Cronograma("Obra da sede", Ambiente.Segunda);
        obra.AdicionarTarefa(new Tarefa("Fundação", Ambiente.Segunda, 3));
        var a = Abrir(amb => amb.Repositorio.AoAbrir = _ => obra);
        a.Ambiente.Dialogos.ArquivoParaSalvar = "/projetos/rascunho.pplan";
        Ambiente.AdicionarTarefa(a.Vm, "Rascunho", 1);

        a.Clicar("Arquivo", "Salvar");
        a.Clicar("Arquivo", "Salvar"); // já tem arquivo: não pergunta de novo
        a.Ambiente.Dialogos.ArquivoParaSalvar = "/projetos/copia.pplan";
        a.Clicar("Arquivo", "Salvar como…");
        a.Ambiente.Repositorio.Salvos.Select(s => s.Caminho)
            .Should().Equal("/projetos/rascunho.pplan", "/projetos/rascunho.pplan", "/projetos/copia.pplan");
        a.Ambiente.Dialogos.VezesQuePerguntouOndeSalvar.Should().Be(2);

        a.Clicar("Arquivo", "Novo");
        (a.Vm.Tarefas.Count, a.Vm.CaminhoArquivo).Should().Be((0, (string?)null));

        a.Ambiente.Dialogos.ArquivoParaAbrir = "/projetos/obra.pplan";
        a.Clicar("Arquivo", "Abrir…");
        a.Ambiente.Repositorio.Abertos.Should().Equal("/projetos/obra.pplan");
        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("Fundação");
    });

    [Fact]
    public Task Inserir_FotosDeExemplo_SoEmDebug() => Tela.Executar(() =>
    {
        var a = Abrir();
        var item = a.Item("Inserir", "Fotos de exemplo (desenvolvimento)");
        var linha = a.Item("Inserir").Items.OfType<Separator>().Single();

        (item.IsVisible, linha.IsVisible).Should().Be((a.Vm.ModoDesenvolvimento, a.Vm.ModoDesenvolvimento));
#if DEBUG
        var tarefa = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Clicar(item);
        tarefa.Tarefa.Fotos.Should().HaveCount(3);
#endif
    });

    [Fact]
    public Task SemTarefaSelecionada_OsItensQueDependemDelaFicamDesabilitados() => Tela.Executar(() =>
    {
        var a = Abrir();

        bool Habilitado(params string[] caminho) => a.Item(caminho).IsEffectivelyEnabled;

        new[] { Habilitado("Inserir", "Adicionar subtarefa"), Habilitado("Tarefa", "Remover tarefa"),
                Habilitado("Tarefa", "Promover"), Habilitado("Tarefa", "Rebaixar") }
            .Should().AllBeEquivalentTo(false);
        new[] { Habilitado("Arquivo", "Novo"), Habilitado("Arquivo", "Salvar"), Habilitado("Inserir", "Adicionar tarefa"),
                Habilitado("Tarefa", "Recalcular cronograma") }
            .Should().AllBeEquivalentTo(true);

        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);

        new[] { Habilitado("Inserir", "Adicionar subtarefa"), Habilitado("Tarefa", "Remover tarefa") }
            .Should().AllBeEquivalentTo(true, "com uma tarefa selecionada");
    });

    [Theory]
    [InlineData("Seleção")]
    [InlineData("Caminho crítico")]
    [InlineData("Fotos")]
    public Task Exibir_CadaOpcaoMostraOEstadoComCheckEAlternaNoClique(string opcao) => Tela.Executar(() =>
    {
        var a = Abrir();
        var item = a.Item("Exibir", opcao);
        Func<bool> estado = opcao switch
        {
            "Seleção" => () => a.Vm.MostrarSelecao,
            "Caminho crítico" => () => a.Vm.Gantt.MostrarCaminhoCritico,
            _ => () => a.Vm.Gantt.MostrarFotos,
        };

        item.ToggleType.Should().Be(MenuItemToggleType.CheckBox);
        (estado(), item.IsChecked).Should().Be((true, true), "as três começam ligadas, como antes");

        a.Clicar(item);
        (estado(), item.IsChecked).Should().Be((false, false));

        a.Clicar(item);
        (estado(), item.IsChecked).Should().Be((true, true));
    });

    [Fact]
    public Task Exibir_OpcaoMudadaPorOutroCaminho_OCheckAcompanha() => Tela.Executar(() =>
    {
        var a = Abrir();

        a.Vm.Gantt.MostrarCaminhoCritico = false;
        a.Vm.MostrarSelecao = false;
        Dispatcher.UIThread.RunJobs();

        a.Item("Exibir", "Caminho crítico").IsChecked.Should().BeFalse();
        a.Item("Exibir", "Seleção").IsChecked.Should().BeFalse();
        a.Item("Exibir", "Fotos").IsChecked.Should().BeTrue();
    });

    [Fact]
    public Task Exibir_Colunas_UmaCaixaPorColunaMaisRestaurar() => Tela.Executar(() =>
    {
        var a = Abrir();
        var colunas = a.Vm.Colunas.Colunas;
        var itens = a.Item("Exibir", "Colunas").Items.ToList();

        itens.Should().HaveCount(colunas.Count + 2);
        itens.Take(colunas.Count).Cast<MenuItem>().Select(i => i.Header).Should().Equal(colunas.Select(c => c.Titulo));
        itens.Take(colunas.Count).Cast<MenuItem>().Should().OnlyContain(i =>
            i.ToggleType == MenuItemToggleType.CheckBox && i.StaysOpenOnClick && i.IsChecked);
        itens[colunas.Count].Should().BeOfType<Separator>();
        ((MenuItem)itens[^1]!).Header.Should().Be("Restaurar layout padrão de colunas");
    });

    [Fact]
    public Task Exibir_Colunas_OcultarERestaurar_AGridAcompanha() => Tela.Executar(() =>
    {
        var a = Abrir();
        var edt = a.Item("Exibir", "Colunas", "EDT");
        var colunaDaGrid = a.Janela.Grade.Columns.Single(c => Equals(c.Tag, "Edt"));

        a.Clicar(edt);
        (a.Vm.Colunas["Edt"].Visivel, edt.IsChecked, colunaDaGrid.IsVisible).Should().Be((false, false, false));

        a.Clicar("Exibir", "Colunas", "Restaurar layout padrão de colunas");
        (a.Vm.Colunas["Edt"].Visivel, edt.IsChecked, colunaDaGrid.IsVisible).Should().Be((true, true, true));
    });

    [Fact]
    public Task Exibir_Colunas_ANomeNaoPodeSerOcultada() => Tela.Executar(() =>
    {
        var a = Abrir();
        var nome = a.Item("Exibir", "Colunas", "Nome");

        nome.IsEnabled.Should().BeFalse();
        nome.IsChecked.Should().BeTrue();
    });

    [Fact]
    public Task Zoom_OSliderContinuaLigadoAoGantt() => Tela.Executar(() =>
    {
        var a = Abrir();

        a.Janela.Zoom.Value = 50;
        a.Vm.Gantt.PixelsPorDia.Should().Be(50);

        a.Vm.Gantt.PixelsPorDia = 20;
        Dispatcher.UIThread.RunJobs();
        a.Janela.Zoom.Value.Should().Be(20);
        (a.Janela.Zoom.Minimum, a.Janela.Zoom.Maximum).Should().Be((GanttViewModel.EscalaMinima, GanttViewModel.EscalaMaxima));
    });

    [Fact]
    public Task PromoverERebaixar_MostramOAtalho_EOAtalhoContinuaFuncionando() => Tela.Executar(() =>
    {
        var a = Abrir();
        var mae = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var filha = Ambiente.AdicionarTarefa(a.Vm, "Sapatas", 2);

        a.Item("Tarefa", "Promover").InputGesture.Should().Be(new KeyGesture(Key.Left, KeyModifiers.Alt | KeyModifiers.Shift));
        a.Item("Tarefa", "Rebaixar").InputGesture.Should().Be(new KeyGesture(Key.Right, KeyModifiers.Alt | KeyModifiers.Shift));

        a.Vm.TarefaSelecionada = filha;
        a.Janela.KeyPress(Key.Right, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
        Dispatcher.UIThread.RunJobs();
        filha.Tarefa.TarefaPaiId.Should().Be(mae.Tarefa.Id);

        a.Janela.KeyPress(Key.Left, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
        Dispatcher.UIThread.RunJobs();
        filha.Tarefa.TarefaPaiId.Should().BeNull();
    });
}
