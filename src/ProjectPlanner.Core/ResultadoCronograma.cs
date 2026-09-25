namespace ProjectPlanner.Core;

/// <summary>
/// Resultado de <see cref="CalculadoraCronograma.Calcular"/>.
/// </summary>
/// <param name="DataTermino">
/// Fim do projeto: o maior término mais cedo entre todas as tarefas. Num cronograma vazio, é o
/// próprio início do projeto.
/// </param>
/// <param name="TarefasCriticas">Tarefas com folga zero, em ordem topológica.</param>
/// <param name="CaminhosCriticos">
/// Todas as sequências de tarefas críticas em que cada uma determina o início mais cedo da seguinte
/// (vínculo sem folga). Cada sequência vai de uma tarefa crítica sem vínculo crítico de entrada até
/// uma sem vínculo crítico de saída. Com caminhos empatados, todos aparecem, ordenados pela
/// ordem topológica das tarefas.
/// </param>
public sealed record ResultadoCronograma(
    DateTime DataTermino,
    IReadOnlyList<Tarefa> TarefasCriticas,
    IReadOnlyList<IReadOnlyList<Tarefa>> CaminhosCriticos);
