namespace ProjectPlanner.IA;

/// <summary>
/// O que o painel manda ao assistente a cada envio. É um registro (e não parâmetros soltos) para que campos novos
/// entrem sem mudar <see cref="IAssistenteIA.EnviarAsync"/>.
/// </summary>
/// <param name="Mensagem">O texto que o usuário escreveu (pode ser vazio se só há anexos).</param>
/// <param name="Anexos">Os documentos anexados a esta mensagem.</param>
/// <param name="Historico">
/// As mensagens anteriores da conversa, da mais antiga para a mais recente, sem os avisos locais
/// (<see cref="MensagemChat.EhErro"/>): as APIs de chat não guardam a conversa entre as chamadas.
/// </param>
/// <param name="Contexto">
/// Retrato só de leitura do cronograma aberto (Bloco 23), montado pelo App no momento do envio; nulo se não há.
/// </param>
/// <param name="Conducao">O modo da conversa, a ficha do projeto e o andamento do estudo (Bloco 26); nulo como antes dele.</param>
public sealed record PedidoIA(
    string Mensagem,
    IReadOnlyList<AnexoIA> Anexos,
    IReadOnlyList<MensagemChat> Historico,
    ContextoDoProjeto? Contexto = null,
    ConducaoDaConversa? Conducao = null);

/// <summary>A resposta do assistente: o texto para o usuário e, desde o Bloco 24, as alterações do cronograma.</summary>
public sealed record RespostaIA(string Texto)
{
    /// <summary>
    /// As alterações que o assistente pediu, na ordem (vazia numa resposta que só conversa). O App aplica todas ou
    /// nenhuma, com as regras do domínio, e guarda o cronograma de antes para o Desfazer.
    /// </summary>
    public IReadOnlyList<AlteracaoDoCronograma> Alteracoes { get; init; } = [];

    /// <summary>O que a resposta é (Bloco 26): conversa, escolha do modo, perguntas ou o plano.</summary>
    public TipoDeResposta Tipo { get; init; } = TipoDeResposta.Resposta;

    /// <summary>As perguntas do estudo (tipo <see cref="TipoDeResposta.Perguntas"/>).</summary>
    public IReadOnlyList<PerguntaIA> Perguntas { get; init; } = [];

    /// <summary>O plano para aprovar (tipo <see cref="TipoDeResposta.Plano"/>).</summary>
    public PlanoIA? Plano { get; init; }

    /// <summary>O que o assistente entendeu do projeto nesta resposta: campos novos ou corrigidos da ficha.</summary>
    public IReadOnlyList<ItemDeFicha> Ficha { get; init; } = [];

    /// <summary>Tem algo além do texto e das alterações (tipo, perguntas, plano, ficha).</summary>
    public bool Estruturada => Tipo != TipoDeResposta.Resposta || Ficha.Count > 0;
}
