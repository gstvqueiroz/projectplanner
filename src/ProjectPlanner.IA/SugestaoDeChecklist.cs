namespace ProjectPlanner.IA;

/// <summary>
/// Bloco 78 (o plano "Delete, arrastar e Kanban", gancho de IA do K7): o card para a IA sugerir o checklist: a tarefa, o resumo
/// em que ela está, a descrição, a duração, os itens que já existem (para não repetir) e a ficha da obra.
/// </summary>
public sealed record PedidoDeChecklist(string Tarefa, string? Resumo, string? Descricao, int Duracao, IReadOnlyList<string> JaNoChecklist,
    IReadOnlyList<string> Ficha);

/// <summary>Bloco 78: a IA sugerindo os itens do checklist de um card a partir da tarefa. Texto para o usuário revisar.</summary>
public interface ISugestorDeChecklist
{
    bool EstaConfigurado { get; }

    /// <returns>De 3 a 8 itens curtos, sem os que já estão no checklist.</returns>
    /// <exception cref="ErroDoAssistenteException">Falha da IA, já com a mensagem para o usuário.</exception>
    Task<IReadOnlyList<string>> SugerirAsync(PedidoDeChecklist pedido, CancellationToken cancelamento);
}

/// <summary>Sem chave de API: avisa que é preciso configurar o assistente.</summary>
public sealed class SugestorDeChecklistIndisponivel : ISugestorDeChecklist
{
    public bool EstaConfigurado => false;

    public Task<IReadOnlyList<string>> SugerirAsync(PedidoDeChecklist pedido, CancellationToken cancelamento) =>
        Task.FromException<IReadOnlyList<string>>(new ErroDoAssistenteException(AnalistaIndisponivel.Mensagem));
}

/// <summary>Usa a configuração do momento, como o assistente do painel.</summary>
public sealed class SugestorDeChecklistConformeConfiguracao(IConfiguracaoIA configuracao, IProvedorIA provedor) : ISugestorDeChecklist
{
    private readonly ISugestorDeChecklist _semChave = new SugestorDeChecklistIndisponivel();

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(configuracao.ChaveApi);

    public Task<IReadOnlyList<string>> SugerirAsync(PedidoDeChecklist pedido, CancellationToken cancelamento) =>
        (configuracao.ChaveApi is { Length: > 0 } chave && !string.IsNullOrWhiteSpace(chave)
            ? provedor.CriarSugestorDeChecklist(chave, configuracao.Modelo)
            : _semChave).SugerirAsync(pedido, cancelamento);
}
