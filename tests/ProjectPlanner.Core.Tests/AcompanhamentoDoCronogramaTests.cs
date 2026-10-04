using ProjectPlanner.Core;

namespace ProjectPlanner.Core.Tests;

// Bloco 25 (C2): a situação das tarefas numa data de status. Projeto começando na seg 28/09/2026.
public class AcompanhamentoDoCronogramaTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    // A (5 dias: 28/09 a 02/10) → B (5 dias: 05/10 a 09/10) → Entrega (marco, 09/10).
    private static (Cronograma Cronograma, Tarefa A, Tarefa B, Tarefa Entrega) Obra(int percentualA = 0, int percentualB = 0)
    {
        var cronograma = new Cronograma("Obra", Seg);
        var (a, b, entrega) = (new Tarefa("A", Seg, 5), new Tarefa("B", Seg, 5), new Tarefa("Entrega", Seg, 0));
        foreach (var tarefa in new[] { a, b, entrega })
            cronograma.AdicionarTarefa(tarefa);
        cronograma.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        cronograma.AdicionarPredecessora(new Predecessora(b.Id, entrega.Id));
        a.AtualizarPercentualConcluido(percentualA);
        b.AtualizarPercentualConcluido(percentualB);
        CalculadoraCronograma.Calcular(cronograma);
        return (cronograma, a, b, entrega);
    }

    [Theory]
    [InlineData("2026-09-25", 0, 0, SituacaoDaTarefa.NaoIniciada)] // antes do início
    [InlineData("2026-09-28", 0, 0, SituacaoDaTarefa.NaoIniciada)] // primeiro dia, de manhã: ainda 0% previsto
    [InlineData("2026-09-30", 0, 40, SituacaoDaTarefa.Atrasada)] // 2 dias úteis já passaram
    [InlineData("2026-09-30", 40, 40, SituacaoDaTarefa.EmDia)]
    [InlineData("2026-09-30", 60, 40, SituacaoDaTarefa.EmDia)] // adiantada também está em dia
    [InlineData("2026-10-03", 80, 100, SituacaoDaTarefa.Atrasada)] // sábado depois do término
    [InlineData("2026-10-03", 100, 100, SituacaoDaTarefa.Concluida)]
    [InlineData("2026-09-25", 100, 0, SituacaoDaTarefa.Concluida)] // concluída antes da hora
    public void TarefaComum(string data, int informado, int previsto, SituacaoDaTarefa situacao)
    {
        var (cronograma, a, _, _) = Obra(percentualA: informado);

        AcompanhamentoDoCronograma.Calcular(cronograma, DateTime.Parse(data))[a.Id]
            .Should().Be(new AndamentoDaTarefa(situacao, previsto));
    }

    [Theory]
    [InlineData("2026-10-09", 0, SituacaoDaTarefa.NaoIniciada)] // no próprio dia
    [InlineData("2026-10-12", 100, SituacaoDaTarefa.Atrasada)] // o dia passou
    public void Marco(string data, int previsto, SituacaoDaTarefa situacao)
    {
        var (cronograma, _, _, entrega) = Obra();

        AcompanhamentoDoCronograma.Calcular(cronograma, DateTime.Parse(data))[entrega.Id]
            .Should().Be(new AndamentoDaTarefa(situacao, previsto));
    }

    [Fact]
    public void MarcoConcluido()
    {
        var (cronograma, _, _, entrega) = Obra();
        entrega.AtualizarPercentualConcluido(100);

        AcompanhamentoDoCronograma.Calcular(cronograma, new DateTime(2026, 10, 12))[entrega.Id].Situacao.Should().Be(SituacaoDaTarefa.Concluida);
    }

    [Fact]
    public void Resumo_PelaSituacaoDasSubtarefas_EPrevistoPonderado()
    {
        var cronograma = new Cronograma("Obra", Seg);
        var fase = new Tarefa("Fase", Seg, 1);
        var (curta, longa) = (new Tarefa("Curta", Seg, 2), new Tarefa("Longa", Seg, 8));
        cronograma.AdicionarTarefa(fase);
        cronograma.AdicionarTarefa(curta, fase.Id);
        cronograma.AdicionarTarefa(longa, fase.Id);
        CalculadoraCronograma.Calcular(cronograma);
        var quarta = new DateTime(2026, 9, 30); // 2 dias úteis passados: curta 100%, longa 25%

        AcompanhamentoDoCronograma.Calcular(cronograma, quarta)[fase.Id].Should().Be(new AndamentoDaTarefa(SituacaoDaTarefa.Atrasada, 40));

        curta.AtualizarPercentualConcluido(100);
        longa.AtualizarPercentualConcluido(30);
        AcompanhamentoDoCronograma.Calcular(cronograma, quarta)[fase.Id].Situacao.Should().Be(SituacaoDaTarefa.EmDia);

        longa.AtualizarPercentualConcluido(100);
        AcompanhamentoDoCronograma.Calcular(cronograma, quarta)[fase.Id].Situacao.Should().Be(SituacaoDaTarefa.Concluida);
        AcompanhamentoDoCronograma.Calcular(cronograma, new DateTime(2026, 9, 1))[fase.Id].Should().Be(
            new AndamentoDaTarefa(SituacaoDaTarefa.Concluida, 0));
    }

    [Fact]
    public void SemCalculo_FicaDeFora_ETextos()
    {
        var cronograma = new Cronograma("Obra", Seg);
        cronograma.AdicionarTarefa(new Tarefa("Sem cálculo", Seg, 3));

        AcompanhamentoDoCronograma.Calcular(cronograma, Seg).Should().BeEmpty();
        Enum.GetValues<SituacaoDaTarefa>().Select(s => new AndamentoDaTarefa(s, 0).Texto)
            .Should().Equal("Não iniciada", "Em dia", "Atrasada", "Concluída");
        var nulo = () => AcompanhamentoDoCronograma.Calcular(null!, Seg);
        nulo.Should().Throw<ArgumentNullException>();
    }
}
