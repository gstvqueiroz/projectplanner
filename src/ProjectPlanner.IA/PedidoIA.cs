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
public sealed record PedidoIA(
    string Mensagem,
    IReadOnlyList<AnexoIA> Anexos,
    IReadOnlyList<MensagemChat> Historico,
    ContextoDoProjeto? Contexto = null);

/// <summary>A resposta do assistente: o texto para o usuário e, desde o Bloco 24, as alterações do cronograma.</summary>
public sealed record RespostaIA(string Texto)
{
    /// <summary>
    /// As alterações que o assistente pediu, na ordem (vazia numa resposta que só conversa). O App aplica todas ou
    /// nenhuma, com as regras do domínio, e guarda o cronograma de antes para o Desfazer.
    /// </summary>
    public IReadOnlyList<AlteracaoDoCronograma> Alteracoes { get; init; } = [];
}
