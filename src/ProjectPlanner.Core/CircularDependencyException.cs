namespace ProjectPlanner.Core;

/// <summary>
/// Lançada quando o grafo de dependências entre tarefas tem, ou passaria a ter, um ciclo.
/// </summary>
public class CircularDependencyException : DomainValidationException
{
    public CircularDependencyException(IReadOnlyList<Guid> ciclo, string mensagem)
        : base(nameof(Cronograma.Predecessoras), mensagem)
    {
        Ciclo = ciclo;
    }

    /// <summary>
    /// Ids das tarefas do ciclo, seguindo os vínculos de predecessora para sucessora; o primeiro e
    /// o último são a mesma tarefa (ex.: C → A → B → C). Quando o ciclo é recusado em
    /// <see cref="Cronograma.AdicionarPredecessora"/>, começa pela predecessora do vínculo recusado.
    /// </summary>
    public IReadOnlyList<Guid> Ciclo { get; }
}
