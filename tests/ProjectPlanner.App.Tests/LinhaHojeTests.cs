using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (C1): a linha "Hoje" no Gantt, pelo relógio do programa ("hoje" é seg 28/09/2026).
public class LinhaHojeTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public LinhaHojeTests() => _vm = _ambiente.CriarViewModel();

    [Fact]
    public void LigadaPorPadrao_NoDiaDoRelogio()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 5);

        (_vm.Gantt.MostrarHoje, _vm.Gantt.Hoje).Should().Be((true, Ambiente.Segunda));
        var layout = _vm.Gantt.Layout!;
        layout.XDeHoje.Should().Be((Ambiente.Segunda - layout.InicioEixo).Days * _vm.Gantt.PixelsPorDia + _vm.Gantt.PixelsPorDia / 2);
    }

    [Fact]
    public void Desligar_TiraALinha_ENaoMarcaOProjeto()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 5);
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);

        _vm.Gantt.MostrarHoje = false;
        _vm.Gantt.Layout!.XDeHoje.Should().BeNull();
        _vm.Gantt.MostrarHoje = true;
        _vm.Gantt.Layout!.XDeHoje.Should().NotBeNull();

        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("é só exibição");
    }

    [Fact]
    public void ProjetoQueComecaDepois_HojeForaDoEixo()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 5);

        _vm.InicioProjeto = new DateTime(2027, 3, 1);

        _vm.Gantt.Layout!.XDeHoje.Should().BeNull();
    }
}
