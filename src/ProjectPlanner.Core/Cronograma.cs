namespace ProjectPlanner.Core;

/// <summary>
/// Agrega as tarefas de um projeto e o grafo de dependências entre elas.
/// </summary>
/// <remarks>
/// As dependências ficam numa lista única de arestas (<see cref="Predecessora"/>), e não dentro de
/// cada <see cref="Tarefa"/>, para que todo vínculo passe pelas regras do grafo:
/// <list type="bullet">
/// <item>as duas tarefas precisam pertencer ao cronograma;</item>
/// <item>só existe um vínculo por par (predecessora, sucessora); para mudar tipo ou lag, remova e adicione de novo;</item>
/// <item>nenhum vínculo pode fechar um ciclo.</item>
/// </list>
/// Toda violação lança <see cref="DomainValidationException"/> e deixa o cronograma inalterado.
/// </remarks>
public class Cronograma
{
    private readonly List<Tarefa> _tarefas = [];
    private readonly List<Predecessora> _predecessoras = [];

    public Cronograma(DateTime dataInicio)
    {
        DataInicio = DiasUteis.ProximoOuMesmo(dataInicio.Date);
    }

    /// <summary>
    /// Início do projeto: no cálculo do cronograma, nenhuma tarefa começa antes dele. Como nas
    /// tarefas, o horário é descartado e um fim de semana vai para a segunda-feira seguinte.
    /// </summary>
    public DateTime DataInicio { get; }

    /// <summary>Tarefas na ordem em que foram adicionadas.</summary>
    public IReadOnlyCollection<Tarefa> Tarefas => _tarefas.AsReadOnly();

    /// <summary>Todos os vínculos, na ordem em que foram adicionados.</summary>
    public IReadOnlyCollection<Predecessora> Predecessoras => _predecessoras.AsReadOnly();

    public void AdicionarTarefa(Tarefa tarefa)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        if (_tarefas.Exists(t => t.Id == tarefa.Id))
            throw new DomainValidationException(nameof(Tarefas),
                $"A tarefa \"{tarefa.Nome}\" já pertence ao cronograma.");

        _tarefas.Add(tarefa);
    }

    /// <exception cref="CircularDependencyException">O vínculo fecharia um ciclo.</exception>
    /// <exception cref="DomainValidationException">
    /// Alguma das tarefas não pertence ao cronograma, ou já existe vínculo entre elas.
    /// </exception>
    public void AdicionarPredecessora(Predecessora predecessora)
    {
        ArgumentNullException.ThrowIfNull(predecessora);
        var origem = ObterTarefa(predecessora.TarefaOrigemId, nameof(Predecessora.TarefaOrigemId));
        var destino = ObterTarefa(predecessora.TarefaDestinoId, nameof(Predecessora.TarefaDestinoId));

        if (Encontrar(origem.Id, destino.Id) is not null)
            throw new DomainValidationException(nameof(Predecessoras),
                $"Já existe um vínculo de \"{origem.Nome}\" para \"{destino.Nome}\".");

        // O vínculo origem → destino fecha um ciclo se a origem já é alcançável a partir do destino.
        if (Caminho(destino.Id, origem.Id) is { } caminho)
        {
            var ciclo = caminho.Prepend(origem.Id).ToList();
            var nomes = string.Join(" → ", ciclo.Select(id => _tarefas.Find(t => t.Id == id)!.Nome));
            throw new CircularDependencyException(ciclo,
                $"\"{origem.Nome}\" não pode ser predecessora de \"{destino.Nome}\": " +
                $"isso criaria a dependência circular {nomes}.");
        }

        _predecessoras.Add(predecessora);
    }

    public void RemoverPredecessora(Guid tarefaOrigemId, Guid tarefaDestinoId)
    {
        var predecessora = Encontrar(tarefaOrigemId, tarefaDestinoId)
            ?? throw new DomainValidationException(nameof(Predecessoras),
                "Não existe vínculo entre as tarefas informadas.");

        _predecessoras.Remove(predecessora);
    }

    /// <summary>
    /// Vínculos em que a tarefa é a sucessora, na ordem em que foram adicionados.
    /// </summary>
    public IReadOnlyList<Predecessora> PredecessorasDe(Guid tarefaId)
    {
        ObterTarefa(tarefaId, nameof(Tarefas));
        return _predecessoras.Where(p => p.TarefaDestinoId == tarefaId).ToList();
    }

    private Tarefa ObterTarefa(Guid id, string propriedade) =>
        _tarefas.Find(t => t.Id == id)
        ?? throw new DomainValidationException(propriedade, $"A tarefa {id} não pertence ao cronograma.");

    private Predecessora? Encontrar(Guid tarefaOrigemId, Guid tarefaDestinoId) =>
        _predecessoras.Find(p => p.TarefaOrigemId == tarefaOrigemId && p.TarefaDestinoId == tarefaDestinoId);

    /// <summary>
    /// Menor caminho de <paramref name="de"/> até <paramref name="ate"/> seguindo os vínculos
    /// (de predecessora para sucessora), incluindo as duas pontas; <c>null</c> se não houver.
    /// </summary>
    private List<Guid>? Caminho(Guid de, Guid ate)
    {
        var sucessoras = _predecessoras.ToLookup(p => p.TarefaOrigemId, p => p.TarefaDestinoId);
        var veioDe = new Dictionary<Guid, Guid> { [de] = de };
        var fila = new Queue<Guid>([de]);

        while (fila.TryDequeue(out var atual))
        {
            if (atual == ate)
            {
                var caminho = new List<Guid> { ate };
                while (caminho[^1] != de)
                    caminho.Add(veioDe[caminho[^1]]);
                caminho.Reverse();
                return caminho;
            }

            foreach (var proxima in sucessoras[atual])
                if (veioDe.TryAdd(proxima, atual))
                    fila.Enqueue(proxima);
        }

        return null;
    }
}
