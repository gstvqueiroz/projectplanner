namespace ProjectPlanner.Core;

/// <summary>
/// Vínculo de dependência: a tarefa <see cref="TarefaOrigemId"/> (predecessora) condiciona a
/// tarefa <see cref="TarefaDestinoId"/> (sucessora) conforme o <see cref="Tipo"/> e o <see cref="Lag"/>.
/// </summary>
/// <remarks>
/// Objeto de valor imutável: dois vínculos com os mesmos dados são iguais. A semântica de cada
/// tipo e do lag está em <see cref="TipoDependencia"/>. As regras que dependem do grafo inteiro
/// (tarefas existentes, vínculo duplicado, ciclos) ficam no <see cref="Cronograma"/>.
/// </remarks>
public sealed record Predecessora
{
    public Predecessora(Guid tarefaOrigemId, Guid tarefaDestinoId,
        TipoDependencia tipo = TipoDependencia.FS, int lag = 0)
    {
        if (tarefaOrigemId == Guid.Empty)
            throw new DomainValidationException(nameof(TarefaOrigemId), "A tarefa predecessora é obrigatória.");
        if (tarefaDestinoId == Guid.Empty)
            throw new DomainValidationException(nameof(TarefaDestinoId), "A tarefa sucessora é obrigatória.");
        if (tarefaOrigemId == tarefaDestinoId)
            throw new DomainValidationException(nameof(TarefaOrigemId), "Uma tarefa não pode ser predecessora dela mesma.");
        if (!Enum.IsDefined(tipo))
            throw new DomainValidationException(nameof(Tipo), $"Tipo de dependência inválido: {(int)tipo}.");

        TarefaOrigemId = tarefaOrigemId;
        TarefaDestinoId = tarefaDestinoId;
        Tipo = tipo;
        Lag = lag;
    }

    /// <summary>Tarefa predecessora (A).</summary>
    public Guid TarefaOrigemId { get; }

    /// <summary>Tarefa sucessora (B), que depende da origem.</summary>
    public Guid TarefaDestinoId { get; }

    public TipoDependencia Tipo { get; }

    /// <summary>Em dias úteis: positivo é atraso, negativo é lead (antecipação).</summary>
    public int Lag { get; }
}
