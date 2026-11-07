using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 72: a seleção múltipla (a mesma na grade e no Gantt) e o Delete pelas regras do plano (E1 a E8): tarefa comum sai na
// hora com o aviso "Desfazer", resumo pergunta grupo inteiro ou só o resumo, várias saem num passo só do Desfazer, e a seleção
// passa para a tarefa que ocupou o lugar.
public sealed class ExclusaoESelecaoTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public ExclusaoESelecaoTests()
    {
        _vm = _ambiente.CriarViewModel();
        foreach (var nome in new[] { "A", "B", "C", "D", "E" })
            Ambiente.AdicionarTarefa(_vm, nome, 2);
    }

    private TarefaViewModel L(string nome) => _vm.Tarefas.Single(t => t.Nome == nome);

    private IEnumerable<string> Nomes() => _vm.Tarefas.Select(t => t.Nome);

    [Fact]
    public void GanttComCtrlEShift_SelecionaVarias_EAreaVaziaLimpa()
    {
        _vm.Gantt.ClicarNaBarra(L("B").Tarefa.Id);
        _vm.Gantt.ClicarNaBarra(L("D").Tarefa.Id, adicionar: true, intervalo: false);

        _vm.Selecionadas.Select(l => l.Nome).Should().Equal("B", "D");
        _vm.TarefaSelecionada.Should().BeSameAs(L("D"), "a principal é a última clicada");
        _vm.Gantt.Destacadas.Should().BeEquivalentTo([L("B").Tarefa.Id, L("D").Tarefa.Id]);

        _vm.Gantt.ClicarNaBarra(L("B").Tarefa.Id, adicionar: true, intervalo: false);
        _vm.Selecionadas.Select(l => l.Nome).Should().Equal(["D"], "o Ctrl+clique numa selecionada a tira");

        _vm.Gantt.ClicarNaBarra(L("A").Tarefa.Id, adicionar: false, intervalo: true);
        _vm.Selecionadas.Select(l => l.Nome).Should().Equal("A", "B", "C", "D");

        _vm.TarefaSelecionada = L("E");
        _vm.Selecionadas.Select(l => l.Nome).Should().Equal(["E"], "selecionar uma só desfaz a seleção múltipla");
        _vm.Gantt.ClicarNoVazio();
        _vm.TarefaSelecionada.Should().BeNull();
        _vm.Selecionadas.Should().BeEmpty();
    }

    [Fact]
    public void Varias_SaemNumPassoSo_ComOAviso_EODesfazerVoltaTudo()
    {
        _vm.DefinirSelecao([L("B"), L("D")]);

        _vm.RemoverTarefaCommand.Execute(null);

        Nomes().Should().Equal("A", "C", "E");
        _ambiente.Dialogos.Confirmacoes.Should().BeEmpty("sem resumo, progresso nem fotos, sai na hora");
        _vm.AvisoComDesfazer.Should().Be("2 tarefas excluídas.");
        _vm.TarefaSelecionada.Should().BeSameAs(L("C"), "a que ocupou o lugar de B (a primeira excluída)");
        _vm.TextoDesfazer.Should().Be("Desfazer Excluir 2 tarefas");

        _vm.DesfazerAlteracaoCommand.Execute(null);

        Nomes().Should().Equal("A", "B", "C", "D", "E");
        _vm.AvisoComDesfazer.Should().BeNull("o Desfazer tira o aviso");
    }

    [Fact]
    public void AUltima_ASelecaoVaiParaADeCima_EOAvisoSomeNaProximaAlteracao()
    {
        _vm.TarefaSelecionada = L("E");
        _vm.RemoverTarefaCommand.Execute(null);

        _vm.TarefaSelecionada.Should().BeSameAs(L("D"));
        _vm.AvisoComDesfazer.Should().Be("Tarefa \"E\" excluída.");

        L("A").Duracao = 4;
        _vm.AvisoComDesfazer.Should().BeNull("o Desfazer do aviso era da exclusão");
    }

    [Fact]
    public void ResumoSoOResumo_AsSubtarefasSobem_NoMesmoLugar()
    {
        _vm.TarefaSelecionada = L("B");
        _vm.RebaixarCommand.Execute(null);
        _vm.TarefaSelecionada = L("C");
        _vm.RebaixarCommand.Execute(null);
        _vm.Tarefas.Select(t => $"{t.Edt} {t.Nome}").Should().Equal("1 A", "1.1 B", "1.2 C", "2 D", "3 E");
        _ambiente.Dialogos.Escolha = 1;
        _vm.TarefaSelecionada = L("A");

        _vm.RemoverTarefaCommand.Execute(null);

        _vm.Tarefas.Select(t => $"{t.Edt} {t.Nome}").Should().Equal("1 B", "2 C", "3 D", "4 E");
        _vm.AvisoComDesfazer.Should().Be("Resumo \"A\" excluído; 2 subtarefas subiram um nível.");
    }

    [Fact]
    public void MaisDeDez_Pergunta_ComAContagem()
    {
        for (var i = 0; i < 7; i++)
            Ambiente.AdicionarTarefa(_vm, $"T{i}", 1);
        _vm.DefinirSelecao(_vm.Tarefas);
        _ambiente.Dialogos.Confirmar = false;

        _vm.RemoverTarefaCommand.Execute(null);

        _ambiente.Dialogos.Confirmacoes.Should().Equal("Excluir 12 tarefas de uma vez?");
        _vm.Tarefas.Should().HaveCount(12, "cancelou");
    }
}
