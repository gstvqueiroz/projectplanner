using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

public class GridDeTarefasTests
{
    private readonly Ambiente _ambiente = new();

    [Fact]
    public void NovaJanela_ComecaComProjetoSemTituloVazioSemAlteracoes()
    {
        var vm = _ambiente.CriarViewModel();

        vm.NomeProjeto.Should().Be("Projeto sem título");
        vm.InicioProjeto.Should().Be(Ambiente.Segunda);
        vm.Tarefas.Should().BeEmpty();
        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        vm.Titulo.Should().Be("Projeto sem título — ProjectPlanner");
    }

    [Fact]
    public void AdicionarTarefa_ApareceNaColecaoNumeradaESelecionada()
    {
        var vm = _ambiente.CriarViewModel();

        vm.AdicionarTarefaCommand.Execute(null);
        vm.AdicionarTarefaCommand.Execute(null);

        vm.Tarefas.Should().HaveCount(2);
        vm.Tarefas.Select(t => t.Numero).Should().Equal(1, 2);
        vm.Tarefas[1].Nome.Should().Be("Nova tarefa");
        vm.Tarefas[1].Duracao.Should().Be(1);
        vm.Tarefas[1].DataInicio.Should().Be(Ambiente.Segunda);
        vm.TarefaSelecionada.Should().BeSameAs(vm.Tarefas[1]);
        vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        vm.Titulo.Should().Be("*Projeto sem título — ProjectPlanner");
    }

    [Fact]
    public void AdicionarTarefa_JaVemCalculada()
    {
        var vm = _ambiente.CriarViewModel();

        vm.AdicionarTarefaCommand.Execute(null);

        vm.Tarefas[0].InicioMaisCedo.Should().Be(Ambiente.Segunda);
        vm.Tarefas[0].EhCritica.Should().BeTrue();
    }

    [Fact]
    public void RemoverTarefa_TiraDaColecaoRenumeraEAtualizaAsPredecessorasDasOutras()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "A", 2);
        var b = Ambiente.AdicionarTarefa(vm, "B", 3);
        var c = Ambiente.AdicionarTarefa(vm, "C", 1, "1; 2II");

        vm.TarefaSelecionada = b;
        vm.RemoverTarefaCommand.Execute(null);

        vm.Tarefas.Select(t => t.Nome).Should().Equal("A", "C");
        c.Numero.Should().Be(2);
        c.Predecessoras.Should().Be("1");
        vm.TarefaSelecionada.Should().BeSameAs(c);
    }

    [Fact]
    public void RemoverTarefa_SemSelecao_NaoPodeExecutar()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "A", 2);

        vm.TarefaSelecionada = null;

        vm.RemoverTarefaCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void EditarDataInicio_DeslocaOTermino()
    {
        var vm = _ambiente.CriarViewModel();
        var a = Ambiente.AdicionarTarefa(vm, "A", 3);

        a.DataInicio = new DateTime(2026, 10, 1);

        a.DataTermino.Should().Be(new DateTime(2026, 10, 5));
        a.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void EditarDataTermino_RecalculaADuracao()
    {
        var vm = _ambiente.CriarViewModel();
        var a = Ambiente.AdicionarTarefa(vm, "A", 3);

        a.DataTermino = new DateTime(2026, 10, 9);

        a.Duracao.Should().Be(10);
    }

    [Fact]
    public void EditarNomeDoProjeto_AtualizaTituloEMarcaAlteracao()
    {
        var vm = _ambiente.CriarViewModel();

        vm.NomeProjeto = "Obra da sede";

        vm.Titulo.Should().Be("*Obra da sede — ProjectPlanner");
    }

    [Fact]
    public void EditarInicioDoProjeto_RecalculaAsTarefas()
    {
        var vm = _ambiente.CriarViewModel();
        var a = Ambiente.AdicionarTarefa(vm, "A", 2);

        vm.InicioProjeto = new DateTime(2026, 10, 3);

        vm.InicioProjeto.Should().Be(new DateTime(2026, 10, 5), "sábado vai para a segunda seguinte");
        a.InicioMaisCedo.Should().Be(new DateTime(2026, 10, 5));
        vm.TerminoProjeto.Should().Be(new DateTime(2026, 10, 6));
    }
}
