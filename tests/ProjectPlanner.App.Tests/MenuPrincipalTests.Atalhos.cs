using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (P1), na janela de verdade: os atalhos de teclado, nos menus e nas dicas.
public partial class MenuPrincipalTests
{
    private static RawInputModifiers Comando => AtalhosDeTeclado.EhMac ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Apertar(Abertura a, Key tecla, RawInputModifiers modificadores, PhysicalKey fisica)
    {
        a.Janela.KeyPress(tecla, modificadores, fisica, null);
        Pintar(a);
    }

    [Fact]
    public Task Atalhos_SalvarEModos() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";

        Apertar(a, Key.S, Comando, PhysicalKey.S);
        a.Ambiente.Repositorio.Salvos.Select(s => s.Caminho).Should().Equal("/obra.pplan");

        Apertar(a, Key.D2, Comando, PhysicalKey.Digit2);
        a.Vm.ModoVisualizacao.Should().Be(ModoVisualizacao.SomenteTabela);
        Apertar(a, Key.D3, Comando, PhysicalKey.Digit3);
        a.Vm.ModoVisualizacao.Should().Be(ModoVisualizacao.SomenteGantt);
        Apertar(a, Key.D1, Comando, PhysicalKey.Digit1);
        a.Vm.ModoVisualizacao.Should().Be(ModoVisualizacao.Dividido);

        a.Ambiente.Dialogos.ArquivoParaSalvar = "/copia.pplan";
        Apertar(a, Key.S, Comando | RawInputModifiers.Shift, PhysicalKey.S);
        a.Ambiente.Repositorio.Salvos.Select(s => s.Caminho).Should().Equal("/obra.pplan", "/copia.pplan");
    });

    [Fact]
    public Task Atalhos_RecalcularAssistenteEFotos() => Tela.Executar(() =>
    {
        var a = Abrir();
        var linha = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var calculos = a.Ambiente.Calculadora.Chamadas;

        Apertar(a, Key.F9, RawInputModifiers.None, PhysicalKey.F9);
        a.Ambiente.Calculadora.Chamadas.Should().BeGreaterThan(calculos);

        Apertar(a, Key.J, Comando, PhysicalKey.J);
        a.Vm.Assistente.Visivel.Should().BeTrue();
        Apertar(a, Key.J, Comando, PhysicalKey.J);
        a.Vm.Assistente.Visivel.Should().BeFalse();

        a.Vm.TarefaSelecionada = null;
        Apertar(a, Key.F, Comando | RawInputModifiers.Shift, PhysicalKey.F);
        a.Ambiente.Dialogos.Galerias.Should().BeEmpty("sem tarefa selecionada não há galeria");
        a.Vm.TarefaSelecionada = linha;
        Apertar(a, Key.F, Comando | RawInputModifiers.Shift, PhysicalKey.F);
        a.Ambiente.Dialogos.Galerias.Should().ContainSingle();
    });

    [Fact]
    public Task Atalhos_NovoEAbrir_PassamPelasPerguntasDeSempre() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.Cancelar;

        Apertar(a, Key.N, Comando, PhysicalKey.N);
        Apertar(a, Key.O, Comando, PhysicalKey.O);

        a.Ambiente.Dialogos.VezesQuePerguntouSobreAlteracoes.Should().Be(2);
        a.Vm.Tarefas.Should().ContainSingle("cancelou as duas vezes");
    });

    [Fact]
    public Task Atalhos_EditandoUmaCelula_ConfirmaAntesDeSalvar() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        Pintar(a);
        var grade = a.Janela.Grade;
        grade.SelectedIndex = 0;
        grade.CurrentColumn = grade.Columns.First(c => (string?)c.Tag == "Nome");
        grade.BeginEdit();
        Pintar(a);
        var caixa = grade.GetVisualDescendants().OfType<TextBox>().Single(); // a da célula em edição
        caixa.Text = "Fundação profunda";

        Apertar(a, Key.S, Comando, PhysicalKey.S);

        a.Ambiente.Repositorio.Salvos.Single().Cronograma.Tarefas.Single().Nome.Should().Be("Fundação profunda");
    });

    // Bloco 25 (P3, P8)
    [Fact]
    public Task Atalhos_IrParaTarefa_SelecionaERolaAGrid() => Tela.Executar(() =>
    {
        var a = Abrir();
        for (var i = 1; i <= 60; i++)
            Ambiente.AdicionarTarefa(a.Vm, $"Tarefa {i}", 2);
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        Pintar(a);
        a.Ambiente.Dialogos.TextoDigitado = "55";

        Apertar(a, Key.G, Comando, PhysicalKey.G);

        a.Vm.TarefaSelecionada!.Numero.Should().Be(55);
        a.Janela.Grade.GetVisualDescendants().OfType<DataGridRow>().Should().Contain(r => r.DataContext == a.Vm.TarefaSelecionada,
            "a linha 55 foi trazida para a vista");
        a.Item("Tarefa", "Ir para a tarefa…").InputGesture.Should().Be(AtalhosDeTeclado.Gesto(AcaoComAtalho.IrParaTarefa, AtalhosDeTeclado.EhMac));
        a.Janela.MenuPrincipal.Close();
    });

    [Fact]
    public Task Zoom_AjustarAoProjeto_EIrParaHoje() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 80);
        Pintar(a);
        Button Botao(string nome) => a.Janela.ControleDeZoom.GetLogicalDescendants().OfType<Button>()
            .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == nome);
        var rolagem = a.Janela.Gantt.FindControl<ScrollViewer>("RolagemCorpo")!;

        a.ClicarComOMouse(Botao("Ajustar ao projeto"));
        Pintar(a);
        a.Vm.Gantt.PixelsPorDia.Should().BeLessThan(GanttViewModel.EscalaInicial, "80 dias úteis não cabiam a 30 px/dia");
        a.Vm.Gantt.Layout!.Barras[0].Direita.Should().BeLessThanOrEqualTo(rolagem.Bounds.Width);
        rolagem.Offset.X.Should().Be(0);

        a.Vm.Gantt.PixelsPorDia = GanttViewModel.EscalaMaxima;
        a.Vm.InicioProjeto = new DateTime(2026, 8, 3); // hoje (28/09) fica bem à direita do começo
        Pintar(a);
        a.ClicarComOMouse(Botao("Ir para hoje"));
        Pintar(a);
        rolagem.Offset.X.Should().BeGreaterThan(0);
        var xDeHoje = (Ambiente.Segunda - a.Vm.Gantt.Layout!.InicioEixo).Days * a.Vm.Gantt.PixelsPorDia;
        (xDeHoje - rolagem.Offset.X).Should().BeInRange(0, rolagem.Bounds.Width, "hoje ficou à vista");
    });

    [Fact]
    public Task Atalhos_AparecemNosMenusENasDicas() => Tela.Executar(() =>
    {
        var a = Abrir();
        var mac = AtalhosDeTeclado.EhMac;

        a.Item("Arquivo", "Salvar").InputGesture.Should().Be(AtalhosDeTeclado.Gesto(AcaoComAtalho.Salvar, mac));
        a.Item("Arquivo", "Salvar como…").InputGesture.Should().Be(AtalhosDeTeclado.Gesto(AcaoComAtalho.SalvarComo, mac));
        a.Item("Exibir", "Somente Gantt").InputGesture.Should().Be(AtalhosDeTeclado.Gesto(AcaoComAtalho.SomenteGantt, mac));
        a.Item("Tarefa", "Recalcular cronograma").InputGesture.Should().Be(new KeyGesture(Key.F9));
        a.Item("Tarefa", "Gerenciar fotos…").InputGesture.Should().Be(AtalhosDeTeclado.Gesto(AcaoComAtalho.FotosDaTarefa, mac));
        a.Janela.MenuPrincipal.Close();
        ToolTip.GetTip(Botao(a, "Salvar")).Should().Be($"Salvar ({AtalhosDeTeclado.Texto(AcaoComAtalho.Salvar, mac)})");
        ToolTip.GetTip(Botao(a, "Recalcular cronograma")).Should().Be("Recalcular cronograma (F9)");
        ToolTip.GetTip(Botao(a, "Fotos da tarefa")).Should().Be(
            $"Fotos da tarefa: selecione uma tarefa para ver as fotos dela ({AtalhosDeTeclado.Texto(AcaoComAtalho.FotosDaTarefa, mac)})");
    });
}
