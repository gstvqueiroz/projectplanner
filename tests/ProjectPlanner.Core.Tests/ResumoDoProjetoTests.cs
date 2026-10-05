using ProjectPlanner.Core;

namespace ProjectPlanner.Core.Tests;

// Bloco 25 (C7): o resumo do projeto numa data de status. Projeto começando na seg 21/09/2026.
public class ResumoDoProjetoTests
{
    private static readonly DateTime Inicio = new(2026, 9, 21);
    private static readonly DateTime Hoje = new(2026, 9, 28); // uma semana depois do início

    // Fase > A (5 d: 21/09–25/09, 100%), B (10 d: 28/09–09/10); C (4 d: 12/10–15/10); Marco 1 (25/09); Marco 2 (15/10).
    private static (Cronograma Cronograma, Dictionary<string, Tarefa> Tarefas) Obra()
    {
        var c = new Cronograma("Residência", Inicio);
        var t = new Dictionary<string, Tarefa>();
        void Nova(string nome, int duracao, string? pai = null)
        {
            t[nome] = new Tarefa(nome, Inicio, duracao);
            c.AdicionarTarefa(t[nome], pai is null ? null : t[pai].Id);
        }
        Nova("Fase", 1); Nova("A", 5, "Fase"); Nova("B", 10, "Fase"); Nova("C", 4); Nova("Marco 1", 0); Nova("Marco 2", 0);
        c.AdicionarPredecessora(new Predecessora(t["A"].Id, t["B"].Id));
        c.AdicionarPredecessora(new Predecessora(t["B"].Id, t["C"].Id));
        c.AdicionarPredecessora(new Predecessora(t["A"].Id, t["Marco 1"].Id));
        c.AdicionarPredecessora(new Predecessora(t["C"].Id, t["Marco 2"].Id));
        t["A"].AtualizarPercentualConcluido(100);
        CalculadoraCronograma.Calcular(c);
        return (c, t);
    }

    private static ResumoDoProjeto Resumo(Cronograma c, DateTime data) =>
        ResumoDoProjeto.Calcular(c, AcompanhamentoDoCronograma.Calcular(c, data), data);

    [Fact]
    public void AndamentoContagensEDatas()
    {
        var (c, _) = Obra();

        var r = Resumo(c, Hoje);

        (r.Nome, r.Inicio, r.Termino, r.DiasUteis).Should().Be(("Residência", Inicio, new DateTime(2026, 10, 15), 19));
        (r.PercentualConcluido, r.PercentualPrevisto).Should().Be((26, 26), "A (5 de 19 dias úteis de trabalho) está pronta e era o previsto");
        (r.Tarefas, r.Resumos, r.Marcos).Should().Be((3, 1, 2));
        (r.Concluidas, r.EmDia, r.NaoIniciadas, r.Atrasadas).Should().Be((1, 0, 3, 1), "o Marco 1 (25/09) passou sem 100%");
        r.Criticas.Should().Be(3, "B, C e o Marco 2 estão no caminho crítico e não concluídos");
        r.ListaDeAtrasadas.Should().Equal(new TarefaNoResumo("3", "Marco 1", new DateTime(2026, 9, 25), 0, 100));
        r.ProximosMarcos.Should().Equal(new TarefaNoResumo("4", "Marco 2", new DateTime(2026, 10, 15), 0, 0));
    }

    [Fact]
    public void TarefaAtrasada_EntraNaListaComOPrevisto()
    {
        var (c, t) = Obra();

        var r = Resumo(c, new DateTime(2026, 10, 2)); // 4 dos 10 dias de B já passaram

        r.ListaDeAtrasadas.Select(a => (a.Nome, a.PercentualConcluido, a.PercentualPrevisto)).Should().Equal(("Marco 1", 0, 100), ("B", 0, 40));
        t["B"].AtualizarPercentualConcluido(40);
        Resumo(c, new DateTime(2026, 10, 2)).ListaDeAtrasadas.Select(a => a.Nome).Should().Equal("Marco 1");
    }

    [Fact]
    public void ProximosMarcos_SoNosProximos30Dias_ENaoConcluidos()
    {
        var (c, t) = Obra();

        Resumo(c, new DateTime(2026, 9, 20)).ProximosMarcos.Select(m => m.Nome).Should().Equal("Marco 1", "Marco 2");
        Resumo(c, new DateTime(2026, 8, 26)).ProximosMarcos.Select(m => m.Nome).Should().Equal(["Marco 1"], "só o que cai nos próximos 30 dias");
        t["Marco 2"].AtualizarPercentualConcluido(100);
        Resumo(c, Hoje).ProximosMarcos.Should().BeEmpty();
    }

    [Fact]
    public void ProjetoVazio_EArgumentosNulos()
    {
        var vazio = new Cronograma("Novo", Inicio);

        var r = Resumo(vazio, Hoje);

        (r.Termino, r.DiasUteis, r.PercentualConcluido, r.Tarefas, r.ListaDeAtrasadas.Count).Should().Be(((DateTime?)null, 0, 0, 0, 0));
        var semCronograma = () => ResumoDoProjeto.Calcular(null!, new Dictionary<Guid, AndamentoDaTarefa>(), Hoje);
        var semAndamento = () => ResumoDoProjeto.Calcular(vazio, null!, Hoje);
        semCronograma.Should().Throw<ArgumentNullException>();
        semAndamento.Should().Throw<ArgumentNullException>();
    }
}
