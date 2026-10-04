using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
