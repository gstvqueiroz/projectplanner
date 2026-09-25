namespace ProjectPlanner.Core;

/// <summary>
/// Lançada quando um novo vínculo entre tarefas fecharia um ciclo no grafo de dependências.
/// </summary>
public class CircularDependencyException : DomainValidationException
{
    public CircularDependencyException(IReadOnlyList<Guid> ciclo, string mensagem)
        : base(nameof(Cronograma.Predecessoras), mensagem)
    {
        Ciclo = ciclo;
    }

    /// <summary>
    /// Ids das tarefas que formariam o ciclo, seguindo os vínculos de predecessora para sucessora.
    /// Começa pela predecessora do vínculo recusado e termina nela de novo (ex.: C → A → B → C).
    /// </summary>
    public IReadOnlyList<Guid> Ciclo { get; }
}
