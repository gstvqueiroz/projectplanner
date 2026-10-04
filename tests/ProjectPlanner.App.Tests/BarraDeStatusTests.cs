using System.ComponentModel;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (V1): a barra de status com o projeto, a selecionada e a versão ("hoje" é seg 28/09/2026).
public class BarraDeStatusTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public BarraDeStatusTests() => _vm = _ambiente.CriarViewModel();

    [Fact]
    public void ProjetoVazio()
    {
        (_vm.TextoTarefasNaBarra, _vm.TextoPeriodoNaBarra, _vm.TextoSelecionadaNaBarra).Should().Be(("Nenhuma tarefa", "", ""));
        _vm.TextoVersaoNaBarra.Should().Be($"v{VersaoDoProduto.Atual}");
    }

    [Fact]
    public void TarefasPeriodoESelecionada_ComAsDatasDoCalculo()
    {
        var a = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        var b = Ambiente.AdicionarTarefa(_vm, "Estrutura", 4, "1");

        _vm.TextoTarefasNaBarra.Should().Be("2 tarefas");
        _vm.TextoPeriodoNaBarra.Should().Be("28/09/2026 → 06/10/2026 · 7 dias úteis");

        _vm.TarefaSelecionada = b;
        _vm.TextoSelecionadaNaBarra.Should().Be("2 Estrutura: 01/10/2026 → 06/10/2026", "as datas do cálculo, como no Gantt");
        _vm.TarefaSelecionada = null;
        _vm.TextoSelecionadaNaBarra.Should().BeEmpty();
    }

    [Fact]
    public void ResumosEMarcos_NaContagem()
    {
        var fase = Ambiente.AdicionarTarefa(_vm, "Fase", 1);
        _vm.TarefaSelecionada = fase;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        _vm.AdicionarMarcoCommand.Execute(null);

        _vm.TextoTarefasNaBarra.Should().Be("3 tarefas (1 resumo, 1 marco)");
        _vm.TarefaSelecionada = _vm.Tarefas.Single(t => t.EhMarco);
        _vm.TextoSelecionadaNaBarra.Should().MatchRegex(@"^\d(\.\d)* .+: \d\d/\d\d/\d{4}$", "marco: um dia só");
    }

    [Fact]
    public void MudancasNoCronograma_AvisamABarra()
    {
        var a = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.TarefaSelecionada = a;
        var avisos = new List<string?>();
        ((INotifyPropertyChanged)_vm).PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        a.Duracao = 5;

        avisos.Should().Contain([nameof(MainWindowViewModel.TextoPeriodoNaBarra), nameof(MainWindowViewModel.TextoSelecionadaNaBarra),
            nameof(MainWindowViewModel.TextoTarefasNaBarra)]);
        _vm.TextoSelecionadaNaBarra.Should().Be("1 Fundação: 28/09/2026 → 02/10/2026");
    }
}
