using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (V4): a dica rica das barras do Gantt ("hoje" é seg 28/09/2026).
public class DicaDasBarrasTests : TesteDeFotos
{
    [Fact]
    public async Task TarefaComum_DatasDuracaoPercentualFolgaPredecessorasEFotos()
    {
        var a = Ambiente.AdicionarTarefa(Vm, "Escavação", 3);
        var b = Ambiente.AdicionarTarefa(Vm, "Sapatas", 5, "1");
        b.PercentualConcluido = 40;
        await ComFotos(b, "x.jpg", "y.jpg");

        Vm.DicaDaTarefaNoGantt(b.Tarefa.Id).Split('\n').Should().Equal(
            "2 Sapatas",
            "01/10/2026 → 07/10/2026 · 5 dias",
            "40% concluído",
            "Folga: 0 dias (crítica)",
            "Situação: Em dia",
            "Predecessoras: 1",
            "2 fotos");
        Vm.DicaDaTarefaNoGantt(a.Tarefa.Id).Split('\n').Should().Equal(
            "1 Escavação", "28/09/2026 → 30/09/2026 · 3 dias", "0% concluído", "Folga: 0 dias (crítica)", "Situação: Não iniciada");
    }

    [Fact]
    public void ResumoMarcoETarefaComFolga()
    {
        var fase = Ambiente.AdicionarTarefa(Vm, "Fase", 1);
        Vm.TarefaSelecionada = fase;
        Vm.AdicionarSubtarefaCommand.Execute(null);
        var longa = Vm.TarefaSelecionada!;
        (longa.Nome, longa.Duracao) = ("Longa", 5);
        Vm.TarefaSelecionada = fase;
        Vm.AdicionarSubtarefaCommand.Execute(null);
        var curta = Vm.TarefaSelecionada!;
        (curta.Nome, curta.Duracao) = ("Curta", 2);
        var entrega = Ambiente.AdicionarTarefa(Vm, "Entrega", 0, "2");

        Vm.DicaDaTarefaNoGantt(fase.Tarefa.Id).Split('\n').Should().Equal(
            "1 Fase", "28/09/2026 → 02/10/2026 · 5 dias (resumo)", "0% concluído", "Situação: Não iniciada");
        Vm.DicaDaTarefaNoGantt(curta.Tarefa.Id).Should().Contain("Folga: 3 dias").And.NotContain("crítica");
        Vm.DicaDaTarefaNoGantt(entrega.Tarefa.Id).Split('\n').Should().Equal(
            "2 Entrega", "Marco em 02/10/2026", "Folga: 0 dias (crítica)", "Situação: Não iniciada", "Predecessoras: 2");
    }

    [Fact]
    public void TarefaQueNaoExiste_Vazia_EOGanttUsaAFuncaoDaJanela()
    {
        Vm.DicaDaTarefaNoGantt(Guid.NewGuid()).Should().BeEmpty();
        Vm.Gantt.DicaDaTarefa.Should().NotBeNull();
    }
}
