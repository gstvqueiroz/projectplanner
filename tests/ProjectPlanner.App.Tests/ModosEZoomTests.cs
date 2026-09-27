using System.ComponentModel;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 16: modos de visualização (Dividido, Somente Tabela, Somente Gantt), passos do zoom e a opção Folga.
public class ModosEZoomTests
{
    private readonly Ambiente _ambiente = new();

    [Fact]
    public void Modo_ComecaDividido_ComOsDoisPaineis()
    {
        var vm = _ambiente.CriarViewModel();

        (vm.ModoVisualizacao, vm.MostrarTabela, vm.MostrarGantt).Should().Be((ModoVisualizacao.Dividido, true, true));
        (vm.EhDividido, vm.EhSomenteTabela, vm.EhSomenteGantt).Should().Be((true, false, false));
    }

    [Theory]
    [InlineData(ModoVisualizacao.SomenteTabela, true, false)]
    [InlineData(ModoVisualizacao.SomenteGantt, false, true)]
    [InlineData(ModoVisualizacao.Dividido, true, true)]
    public void Modo_DefineQuaisPaineisAparecem(ModoVisualizacao modo, bool tabela, bool gantt)
    {
        var vm = _ambiente.CriarViewModel();
        vm.ModoVisualizacao = ModoVisualizacao.SomenteGantt;

        vm.DefinirModoVisualizacaoCommand.Execute(modo);

        (vm.MostrarTabela, vm.MostrarGantt).Should().Be((tabela, gantt));
        new[] { vm.EhDividido, vm.EhSomenteTabela, vm.EhSomenteGantt }.Count(x => x).Should().Be(1);
    }

    [Fact]
    public void Modo_AvisaATela_ENaoMarcaOProjetoComoAlterado()
    {
        var vm = _ambiente.CriarViewModel();
        var avisos = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.DefinirModoVisualizacaoCommand.Execute(ModoVisualizacao.SomenteTabela);

        avisos.Should().Contain([nameof(vm.ModoVisualizacao), nameof(vm.MostrarTabela), nameof(vm.MostrarGantt),
            nameof(vm.EhDividido), nameof(vm.EhSomenteTabela), nameof(vm.EhSomenteGantt)]);
        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void Modo_NaoMexeNosDados()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "Fundação", 3);
        var (linhas, layout, selecionada) = (vm.Tarefas.ToList(), vm.Gantt.Layout, vm.TarefaSelecionada);
        var calculos = _ambiente.Calculadora.Chamadas;

        vm.DefinirModoVisualizacaoCommand.Execute(ModoVisualizacao.SomenteGantt);
        vm.DefinirModoVisualizacaoCommand.Execute(ModoVisualizacao.Dividido);

        vm.Tarefas.Should().Equal(linhas);
        (vm.Gantt.Layout, vm.TarefaSelecionada, _ambiente.Calculadora.Chamadas).Should().Be((layout, selecionada, calculos));
    }

    [Fact]
    public void Zoom_PassosDe5_ComecandoEm30()
    {
        var gantt = _ambiente.CriarViewModel().Gantt;

        gantt.PixelsPorDia.Should().Be(30);
        gantt.AumentarZoomCommand.Execute(null);
        gantt.PixelsPorDia.Should().Be(35);
        gantt.DiminuirZoomCommand.Execute(null);
        gantt.DiminuirZoomCommand.Execute(null);
        gantt.PixelsPorDia.Should().Be(25);
    }

    [Fact]
    public void Zoom_NosLimites_ParaEOBotaoDesabilita()
    {
        var gantt = _ambiente.CriarViewModel().Gantt;

        gantt.PixelsPorDia = GanttViewModel.EscalaMaxima - 2;
        gantt.AumentarZoomCommand.Execute(null);
        (gantt.PixelsPorDia, gantt.AumentarZoomCommand.CanExecute(null)).Should().Be((GanttViewModel.EscalaMaxima, false));

        gantt.PixelsPorDia = GanttViewModel.EscalaMinima + 1;
        gantt.DiminuirZoomCommand.Execute(null);
        (gantt.PixelsPorDia, gantt.DiminuirZoomCommand.CanExecute(null)).Should().Be((GanttViewModel.EscalaMinima, false));
        gantt.AumentarZoomCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Folga_ComecaDesligada()
    {
        _ambiente.CriarViewModel().Gantt.MostrarFolga.Should().BeFalse();
    }
}
