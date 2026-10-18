namespace ProjectPlanner.IA;

/// <summary>Bloco 43: os indicadores da obra numa data, já em texto (uma linha por dado), para a IA analisar.</summary>
/// <param name="Dados">"Prazo: IDP 0,91; 3 atrasadas…", "Etapa 2 Estrutura: contratado R$ 120.000,00…".</param>
/// <param name="Ficha">O que se sabe da obra (a ficha do assistente), "Campo: valor".</param>
public sealed record PedidoDeAnaliseDaObra(string Obra, DateOnly Data, IReadOnlyList<string> Dados, IReadOnlyList<string> Ficha);

/// <summary>A análise: um resumo executivo, os pontos de atenção e as recomendações.</summary>
public sealed record AnaliseDaObraIA(string Resumo, IReadOnlyList<string> Pontos, IReadOnlyList<string> Recomendacoes);

/// <summary>
/// Bloco 43: a IA lendo os indicadores da obra (prazo, custo, margem, medições, caixa, alertas) e escrevendo um resumo
/// executivo. É texto para o usuário revisar; nada muda no projeto.
/// </summary>
public interface IAnalistaDaObra
{
    bool EstaConfigurado { get; }

    /// <exception cref="ErroDoAssistenteException">Falha da IA, já com a mensagem para o usuário.</exception>
    Task<AnaliseDaObraIA> AnalisarAsync(PedidoDeAnaliseDaObra pedido, CancellationToken cancelamento);
}

/// <summary>Sem chave de API: avisa que é preciso configurar o assistente.</summary>
public sealed class AnalistaDaObraIndisponivel : IAnalistaDaObra
{
    public bool EstaConfigurado => false;

    public Task<AnaliseDaObraIA> AnalisarAsync(PedidoDeAnaliseDaObra pedido, CancellationToken cancelamento) =>
        Task.FromException<AnaliseDaObraIA>(new ErroDoAssistenteException(AnalistaIndisponivel.Mensagem));
}

/// <summary>Usa a configuração do momento, como o assistente do painel.</summary>
public sealed class AnalistaDaObraConformeConfiguracao(IConfiguracaoIA configuracao, IProvedorIA provedor) : IAnalistaDaObra
{
    private readonly IAnalistaDaObra _semChave = new AnalistaDaObraIndisponivel();

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(configuracao.ChaveApi);

    public Task<AnaliseDaObraIA> AnalisarAsync(PedidoDeAnaliseDaObra pedido, CancellationToken cancelamento) =>
        (configuracao.ChaveApi is { Length: > 0 } chave && !string.IsNullOrWhiteSpace(chave)
            ? provedor.CriarAnalistaDaObra(chave, configuracao.Modelo)
            : _semChave).AnalisarAsync(pedido, cancelamento);
}
