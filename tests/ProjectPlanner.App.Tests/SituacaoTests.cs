using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (C2): a situação das tarefas na grid, no Gantt e na dica ("hoje" é seg 28/09/2026).
public class SituacaoTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public SituacaoTests() => _vm = _ambiente.CriarViewModel();

    [Fact]
    public void ProjetoQueComecouAntes_TarefaAtrasada_ColunaGanttEDica()
    {
        _vm.InicioProjeto = new DateTime(2026, 9, 21); // começou uma semana antes de "hoje"
        var a = Ambiente.AdicionarTarefa(_vm, "Fundação", 10);
        var b = Ambiente.AdicionarTarefa(_vm, "Estrutura", 5, "1");

        (a.Situacao, a.EstaAtrasada).Should().Be(("Atrasada", true), "5 dos 10 dias já passaram e está em 0%");
        (b.Situacao, b.EstaAtrasada).Should().Be(("Não iniciada", false));
        _vm.Gantt.TarefasAtrasadas.Should().BeEquivalentTo([a.Tarefa.Id]);
        _vm.DicaDaTarefaNoGantt(a.Tarefa.Id).Should().Contain("Situação: Atrasada (previsto 50% hoje)");

        a.PercentualConcluido = 50;

        (a.Situacao, a.EstaAtrasada).Should().Be(("Em dia", false));
        _vm.Gantt.TarefasAtrasadas.Should().BeEmpty();
    }

    [Fact]
    public void ConcluidaEResumo()
    {
        _vm.InicioProjeto = new DateTime(2026, 9, 21);
        var fase = Ambiente.AdicionarTarefa(_vm, "Fase", 1);
        _vm.TarefaSelecionada = fase;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        var sub = _vm.TarefaSelecionada!;
        (sub.Duracao, sub.PercentualConcluido) = (3, 100);

        (sub.Situacao, fase.Situacao).Should().Be(("Concluída", "Concluída"));
        _vm.AndamentoDe(fase.Tarefa.Id)!.Situacao.Should().Be(SituacaoDaTarefa.Concluida);
    }

    [Fact]
    public void ColunaNoPadrao_AntesDasNotas_Visivel()
    {
        var ids = ColunasViewModel.Padrao.Select(p => p.Id).ToList();

        ids.IndexOf("Situacao").Should().Be(ids.IndexOf("Notas") - 1);
        _vm.Colunas["Situacao"].Should().Match<ColunaViewModel>(c => c.Visivel && c.Titulo == "Situação" && c.PodeOcultar);
    }

    [Fact]
    public void LayoutSalvoSemASituacao_ElaApareceNaPosicaoPadrao()
    {
        var preferencias = new PreferenciasEmMemoria();
        var antigas = ColunasViewModel.Padrao.Where(p => p.Id != "Situacao")
            .Select((p, i) => new ProjectPlanner.App.Models.EstadoColuna(p.Id, true, p.Largura, i)).ToList();
        preferencias.Salvar(new ProjectPlanner.App.Models.LayoutColunas(antigas));

        var colunas = new ColunasViewModel(preferencias);

        var ordem = colunas.Colunas.OrderBy(c => c.Posicao).Select(c => c.Id).ToList();
        ordem.IndexOf("Situacao").Should().Be(ordem.IndexOf("Critica") + 1, "logo depois da que a antecede no padrão");
        colunas["Situacao"].Visivel.Should().BeTrue();
    }
}
