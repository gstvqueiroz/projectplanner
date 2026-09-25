using System.ComponentModel;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

public class GanttTests
{
    private readonly Ambiente _ambiente = new();

    // "Hoje" é seg 28/09/2026, que vira o início do projeto; o eixo do Gantt começa 3 dias antes, na
    // sex 25/09. Com o zoom inicial de 20 px/dia:
    //   A (3 dias) seg 28/09 a qua 30/09 → X 3×20 = 60, largura 60, crítica
    //   B (2 dias, depois de A) qui 01/10 a sex 02/10 → X 6×20 = 120, largura 40, crítica
    //   C (1 dia, sem vínculos) seg 28/09 → X 60, largura 20, folga até sex 02/10 → (7+1)×20 = 160
    private MainWindowViewModel Cenario()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "A", 3);
        Ambiente.AdicionarTarefa(vm, "B", 2, "1");
        Ambiente.AdicionarTarefa(vm, "C", 1);
        return vm;
    }

    private static List<string?> AvisosDe(INotifyPropertyChanged origem)
    {
        var avisos = new List<string?>();
        origem.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);
        return avisos;
    }

    [Fact]
    public void JanelaNova_JaMostraOGanttDoProjetoVazioNoZoomInicial()
    {
        var vm = _ambiente.CriarViewModel();

        vm.Gantt.PixelsPorDia.Should().Be(20);
        vm.Gantt.Layout.Should().NotBeNull();
        vm.Gantt.Layout!.Barras.Should().BeEmpty();
    }

    [Fact]
    public void RecalcularCronograma_RefazOLayoutComAsBarrasNaOrdemDaGrid()
    {
        var vm = Cenario();
        var avisos = AvisosDe(vm.Gantt);

        vm.RecalcularCronogramaCommand.Execute(null);

        avisos.Should().Contain(nameof(GanttViewModel.Layout));
        var layout = vm.Gantt.Layout!;
        layout.Barras.Select(b => (b.Nome, b.X, b.Largura, b.EhCritica, b.FimDaFolga)).Should().Equal(
            ("A", 60.0, 60.0, true, (double?)null),
            ("B", 120.0, 40.0, true, null),
            ("C", 60.0, 20.0, false, 160.0));
        layout.Setas.Should().ContainSingle().Which.EhCritica.Should().BeTrue();
    }

    [Fact]
    public void EditarUmaTarefa_MoveAsBarrasDasSucessoras()
    {
        // A passa a 5 dias (seg 28/09 a sex 02/10); B vai para seg 05/10, 10 dias depois do início do eixo.
        var vm = Cenario();

        vm.Tarefas[0].Duracao = 5;

        vm.Gantt.Layout!.Barras[0].Largura.Should().Be(100);
        vm.Gantt.Layout.Barras[1].X.Should().Be(200);
    }

    [Fact]
    public void MudarOZoom_RefazOLayoutNaNovaEscalaSemRecalcularOCpm()
    {
        var vm = Cenario();
        var antes = vm.Gantt.Layout!;
        var chamadasAoCpm = _ambiente.Calculadora.Chamadas;
        var avisos = AvisosDe(vm.Gantt);

        vm.Gantt.PixelsPorDia = 40;

        avisos.Should().Contain(nameof(GanttViewModel.Layout));
        vm.Gantt.Layout!.Barras.Select(b => (b.X, b.Largura, b.Y)).Should().Equal(
            antes.Barras.Select(b => (b.X * 2, b.Largura * 2, b.Y)));
        vm.Gantt.Layout.Largura.Should().Be(antes.Largura * 2);
        _ambiente.Calculadora.Chamadas.Should().Be(chamadasAoCpm);
    }

    [Theory]
    [InlineData(100, 60)]
    [InlineData(1, 4)]
    [InlineData(double.NaN, 20)]
    public void ZoomForaDosLimites_FicaNoLimite(double pedido, double aplicado)
    {
        var vm = Cenario();

        vm.Gantt.PixelsPorDia = pedido;

        vm.Gantt.PixelsPorDia.Should().Be(aplicado);
        vm.Gantt.Layout!.Barras[0].X.Should().Be(3 * aplicado);
    }

    [Fact]
    public void CalculoQueFalha_LimpaOGantt()
    {
        // Projeto começando na última segunda representável: B não cabe antes de 31/12/9999.
        var vm = _ambiente.CriarViewModel();
        vm.InicioProjeto = new DateTime(9999, 12, 27);
        Ambiente.AdicionarTarefa(vm, "A", 5);

        Ambiente.AdicionarTarefa(vm, "B", 1, "1");

        vm.Gantt.Layout.Should().BeNull();
    }

    [Fact]
    public void AbrirProjeto_MostraOGanttDoProjetoAberto()
    {
        var projeto = new Cronograma("Obra", Ambiente.Segunda);
        var (x, y) = (new Tarefa("X", Ambiente.Segunda, 3), new Tarefa("Y", Ambiente.Segunda, 2));
        projeto.AdicionarTarefa(x);
        projeto.AdicionarTarefa(y);
        projeto.AdicionarPredecessora(new Predecessora(x.Id, y.Id));
        _ambiente.Dialogos.ArquivoParaAbrir = "/obra.pplan";
        _ambiente.Repositorio.AoAbrir = _ => projeto;
        var vm = _ambiente.CriarViewModel();

        vm.AbrirProjetoCommand.Execute(null);

        vm.Gantt.Layout!.Barras.Select(b => (b.Nome, b.X)).Should().Equal(("X", 60.0), ("Y", 120.0));
    }
}
