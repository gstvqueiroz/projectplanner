namespace ProjectPlanner.IA;

/// <summary>Uma foto mandada à IA: a imagem (JPEG) e o que se sabe dela.</summary>
/// <param name="Id">Identifica a foto na resposta (o App usa o Id da foto).</param>
/// <param name="Tarefa">"2.1 Escavação".</param>
public sealed record FotoParaIA(string Id, byte[] Jpeg, string Tarefa, DateOnly Data, string? Titulo = null, string? Legenda = null,
    string? Local = null, int? PercentualNaData = null);

/// <summary>Bloco 29: legendas para fotos (as sem legenda, em geral), olhando as imagens.</summary>
public sealed record PedidoDeLegendas(string Obra, IReadOnlyList<FotoParaIA> Fotos);

/// <summary>Uma legenda sugerida: curta, objetiva, do que aparece na foto.</summary>
public sealed record LegendaSugerida(string Id, string Legenda);

/// <summary>Uma tarefa para conferir o % concluído pelas fotos recentes.</summary>
/// <param name="PercentualPrevisto">O previsto para hoje pelo cronograma.</param>
public sealed record TarefaParaAvanco(string Id, string Edt, string Nome, DateOnly Inicio, DateOnly Termino, int Percentual,
    int PercentualPrevisto, IReadOnlyList<FotoParaIA> Fotos);

/// <summary>Bloco 29 (IA7): o % concluído de tarefas sugerido pelas fotos recentes.</summary>
public sealed record PedidoDeAvanco(string Obra, DateOnly Hoje, IReadOnlyList<TarefaParaAvanco> Tarefas);

/// <summary>O % que as fotos indicam, com o porquê; <see cref="Diverge"/> quando não bate com o informado.</summary>
public sealed record AvancoSugerido(string Id, int Percentual, string Justificativa, bool Diverge);

/// <summary>Uma etapa (tarefa) do período, para o texto do relatório.</summary>
public sealed record EtapaParaTexto(string Edt, string Nome, DateOnly Inicio, DateOnly Termino, int Percentual, int PercentualPrevisto,
    string Situacao, IReadOnlyList<string> LegendasDasFotos);

/// <summary>Bloco 29 (RF6): o texto do andamento do período e os pontos de atenção, para o relatório fotográfico.</summary>
/// <param name="Resumo">O andamento geral em linhas ("72% concluído; previsto 80%", atrasadas...).</param>
/// <param name="Fotos">Algumas fotos do período, para a IA ver o que foi feito (opcional; custa mais).</param>
public sealed record PedidoDeTextoDoRelatorio(string Obra, DateOnly De, DateOnly Ate, IReadOnlyList<string> Resumo,
    IReadOnlyList<EtapaParaTexto> Etapas, IReadOnlyList<FotoParaIA> Fotos);

/// <summary>O texto sugerido: um ou mais parágrafos e os pontos de atenção.</summary>
public sealed record TextoSugerido(string Andamento, IReadOnlyList<string> PontosDeAtencao);

/// <summary>
/// Bloco 29: a IA olhando as fotos e o cronograma. Tudo o que vem daqui é sugestão: o App mostra para o usuário revisar,
/// editar e aplicar (nada muda no projeto sozinho).
/// </summary>
public interface IAnalistaDeFotos
{
    /// <summary>Falso sem chave de API: as telas avisam que é preciso configurar o assistente.</summary>
    bool EstaConfigurado { get; }

    /// <exception cref="ErroDoAssistenteException">Falha da IA, já com a mensagem para o usuário.</exception>
    Task<IReadOnlyList<LegendaSugerida>> SugerirLegendasAsync(PedidoDeLegendas pedido, CancellationToken cancelamento);

    /// <exception cref="ErroDoAssistenteException">Falha da IA, já com a mensagem para o usuário.</exception>
    Task<IReadOnlyList<AvancoSugerido>> SugerirAvancoAsync(PedidoDeAvanco pedido, CancellationToken cancelamento);

    /// <exception cref="ErroDoAssistenteException">Falha da IA, já com a mensagem para o usuário.</exception>
    Task<TextoSugerido> RedigirRelatorioAsync(PedidoDeTextoDoRelatorio pedido, CancellationToken cancelamento);
}

/// <summary>Os limites de cada pedido (o custo cresce com as imagens).</summary>
public static class LimitesDaAnalise
{
    /// <summary>Fotos por pedido de legendas.</summary>
    public const int FotosPorPedidoDeLegendas = 12;

    /// <summary>Tarefas por pedido de avanço.</summary>
    public const int TarefasPorPedidoDeAvanco = 10;

    /// <summary>Fotos recentes de cada tarefa no pedido de avanço.</summary>
    public const int FotosPorTarefa = 4;

    /// <summary>Fotos no pedido do texto do relatório.</summary>
    public const int FotosNoTexto = 8;
}

/// <summary>Sem chave de API: avisa que é preciso configurar o assistente.</summary>
public sealed class AnalistaIndisponivel : IAnalistaDeFotos
{
    public const string Mensagem =
        "A IA ainda não está configurada. Abra o painel do assistente (Exibir > Assistente) e, na engrenagem, informe a chave do Gemini.";

    public bool EstaConfigurado => false;

    public Task<IReadOnlyList<LegendaSugerida>> SugerirLegendasAsync(PedidoDeLegendas pedido, CancellationToken cancelamento) =>
        Task.FromException<IReadOnlyList<LegendaSugerida>>(new ErroDoAssistenteException(Mensagem));

    public Task<IReadOnlyList<AvancoSugerido>> SugerirAvancoAsync(PedidoDeAvanco pedido, CancellationToken cancelamento) =>
        Task.FromException<IReadOnlyList<AvancoSugerido>>(new ErroDoAssistenteException(Mensagem));

    public Task<TextoSugerido> RedigirRelatorioAsync(PedidoDeTextoDoRelatorio pedido, CancellationToken cancelamento) =>
        Task.FromException<TextoSugerido>(new ErroDoAssistenteException(Mensagem));
}

/// <summary>Usa a configuração do momento (a chave salva ou removida vale na hora), como o assistente do painel.</summary>
public sealed class AnalistaConformeConfiguracao(IConfiguracaoIA configuracao, IProvedorIA provedor) : IAnalistaDeFotos
{
    private readonly IAnalistaDeFotos _semChave = new AnalistaIndisponivel();

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(configuracao.ChaveApi);

    private IAnalistaDeFotos Analista() =>
        configuracao.ChaveApi is { Length: > 0 } chave && !string.IsNullOrWhiteSpace(chave)
            ? provedor.CriarAnalista(chave, configuracao.Modelo)
            : _semChave;

    public Task<IReadOnlyList<LegendaSugerida>> SugerirLegendasAsync(PedidoDeLegendas pedido, CancellationToken cancelamento) =>
        Analista().SugerirLegendasAsync(pedido, cancelamento);

    public Task<IReadOnlyList<AvancoSugerido>> SugerirAvancoAsync(PedidoDeAvanco pedido, CancellationToken cancelamento) =>
        Analista().SugerirAvancoAsync(pedido, cancelamento);

    public Task<TextoSugerido> RedigirRelatorioAsync(PedidoDeTextoDoRelatorio pedido, CancellationToken cancelamento) =>
        Analista().RedigirRelatorioAsync(pedido, cancelamento);
}
