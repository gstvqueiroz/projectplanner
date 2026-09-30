using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 21: Inserir tarefa acima/abaixo da linha (menu de contexto) ou da selecionada (menu Tarefa).
public class InsercaoNaGridTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _fundacao, _escavar, _armar, _estrutura;

    // 1 Fundação / 1.1 Escavar / 1.2 Armar / 2 Estrutura
    public InsercaoNaGridTests()
    {
        _vm = _ambiente.CriarViewModel();
        _fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _escavar = Subtarefa(_fundacao, "Escavar");
        _armar = Subtarefa(_fundacao, "Armar");
        _estrutura = Ambiente.AdicionarTarefa(_vm, "Estrutura", 2, "3");
    }

    private TarefaViewModel Subtarefa(TarefaViewModel pai, string nome)
    {
        _vm.TarefaSelecionada = pai;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        _vm.TarefaSelecionada!.Nome = nome;
        return _vm.TarefaSelecionada;
    }

    private List<string> Grid() => _vm.LinhasVisiveis.Select(l => $"{l.Numero} {l.Edt} {l.Nome}").ToList();

    [Fact]
    public void Acima_DeUmaSubtarefa_EntraAntesDelaNoMesmoResumo()
    {
        _vm.InserirTarefaAcimaCommand.Execute(_armar);

        Grid().Should().Equal("1 1 Fundação", "2 1.1 Escavar", "3 1.2 Nova tarefa", "4 1.3 Armar", "5 2 Estrutura");
        _vm.TarefaSelecionada!.Nome.Should().Be("Nova tarefa");
        _vm.TarefaSelecionada.Tarefa.TarefaPaiId.Should().Be(_fundacao.Tarefa.Id);
    }

    [Fact]
    public void Abaixo_DeUmResumo_EntraDepoisDoResumoInteiro()
    {
        _vm.InserirTarefaAbaixoCommand.Execute(_fundacao);

        Grid().Should().Equal("1 1 Fundação", "2 1.1 Escavar", "3 1.2 Armar", "4 2 Nova tarefa", "5 3 Estrutura");
    }

    [Fact]
    public void SemParametro_UsaASelecionada_ComoNoMenuTarefa()
    {
        _vm.TarefaSelecionada = _escavar;

        _vm.InserirTarefaAbaixoCommand.Execute(null);

        Grid().Should().Equal("1 1 Fundação", "2 1.1 Escavar", "3 1.2 Nova tarefa", "4 1.3 Armar", "5 2 Estrutura");
    }

    [Fact]
    public void NoInicio_AcimaDaPrimeira()
    {
        _vm.InserirTarefaAcimaCommand.Execute(_fundacao);

        Grid()[0].Should().Be("1 1 Nova tarefa");
        Grid()[1].Should().Be("2 2 Fundação");
    }

    [Fact]
    public void Inserir_RecalculaOCronograma_RenumeraAsPredecessoras_EMarcaAlteracao()
    {
        var calculos = _ambiente.Calculadora.Chamadas;

        _vm.InserirTarefaAcimaCommand.Execute(_fundacao);

        _ambiente.Calculadora.Chamadas.Should().BeGreaterThan(calculos, "a tarefa nova entra no CPM");
        _estrutura.Predecessoras.Should().Be("4", "Armar agora é a linha 4");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Fact]
    public void SemTarefaSelecionada_Desabilitados()
    {
        _vm.TarefaSelecionada = null;

        (_vm.InserirTarefaAcimaCommand.CanExecute(null), _vm.InserirTarefaAbaixoCommand.CanExecute(null)).Should().Be((false, false));
        _vm.InserirTarefaAcimaCommand.CanExecute(_armar).Should().BeTrue("com a linha do menu de contexto, dá");
    }
}
