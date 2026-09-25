using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

public class RoundTripCpmTests : TesteComArquivo
{
    // Cenário com os quatro tipos, lag, lead e dois caminhos críticos empatados. O início do projeto
    // (seg 28/09/2026, dia útil 0) é diferente das datas planejadas das tarefas, para que perder
    // Cronograma.DataInicio mude o resultado.
    //
    //   Tarefa  Duração  Vínculos de entrada          Ida       Volta     Folga
    //     A        3      —                           0–2       0–2       0
    //     B        4      A FS                        3–6       3–6       0
    //     C        4      A FS                        3–6       3–6       0
    //     D        2      A SS +1                     1–2       7–8       6
    //     E        2      B FS, C FS                  7–8       7–8       0
    //     F        3      E FF −1                     5–7       6–8       1
    //     G        2      F SF                        3–4       7–8       4
    //     H        1      E FS −1 (lead)              8–8       8–8       0
    //
    // Fim do projeto: dia 8 (qui 08/10/2026). Caminhos críticos: A → B → E → H e A → C → E → H.
    private static Cronograma Cenario()
    {
        var projeto = new Cronograma("Round-trip", Segunda);
        var planejada = new DateTime(2027, 1, 4);
        var tarefas = new Dictionary<string, Tarefa>();
        foreach (var (nome, duracao) in new[] { ("A", 3), ("B", 4), ("C", 4), ("D", 2), ("E", 2), ("F", 3), ("G", 2), ("H", 1) })
        {
            tarefas[nome] = new Tarefa(nome, planejada, duracao);
            projeto.AdicionarTarefa(tarefas[nome]);
        }

        void Vincular(string origem, string destino, TipoDependencia tipo = TipoDependencia.FS, int lag = 0) =>
            projeto.AdicionarPredecessora(new Predecessora(tarefas[origem].Id, tarefas[destino].Id, tipo, lag));

        Vincular("A", "B");
        Vincular("A", "C");
        Vincular("A", "D", TipoDependencia.SS, 1);
        Vincular("B", "E");
        Vincular("C", "E");
        Vincular("E", "F", TipoDependencia.FF, -1);
        Vincular("F", "G", TipoDependencia.SF);
        Vincular("E", "H", TipoDependencia.FS, -1);
        return projeto;
    }

    private static object Instantaneo(Cronograma cronograma, ResultadoCronograma resultado) => new
    {
        resultado.DataTermino,
        Tarefas = cronograma.Tarefas.Select(t => new
        {
            t.Id, t.Nome, t.InicioMaisCedo, t.TerminoMaisCedo, t.InicioMaisTarde, t.TerminoMaisTarde, t.Folga, t.EhCritica,
        }).ToList(),
        Criticas = resultado.TarefasCriticas.Select(t => t.Id).ToList(),
        Caminhos = resultado.CaminhosCriticos.Select(c => string.Join(" → ", c.Select(t => t.Nome))).ToList(),
    };

    [Fact]
    public void SalvarAbrirERecalcular_DaOMesmoCaminhoCriticoDeAntesDeSalvar()
    {
        var original = Cenario();
        var resultadoOriginal = CalculadoraCronograma.Calcular(original);

        Repositorio.Salvar(original, Caminho);
        var aberto = Repositorio.Abrir(Caminho);
        var resultadoAberto = CalculadoraCronograma.Calcular(aberto);

        Instantaneo(aberto, resultadoAberto).Should().BeEquivalentTo(
            Instantaneo(original, resultadoOriginal), o => o.WithStrictOrdering());

        // E o resultado é o do cálculo manual acima, não só igual ao de antes.
        resultadoAberto.DataTermino.Should().Be(new DateTime(2026, 10, 8));
        resultadoAberto.CaminhosCriticos.Select(c => string.Join(" → ", c.Select(t => t.Nome)))
            .Should().Equal("A → B → E → H", "A → C → E → H");
        aberto.Tarefas.Select(t => (t.Nome, t.Folga)).Should().Equal(
            ("A", 0), ("B", 0), ("C", 0), ("D", 6), ("E", 0), ("F", 1), ("G", 4), ("H", 0));
    }
}
