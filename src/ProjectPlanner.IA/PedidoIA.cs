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

/// <summary>A resposta do assistente.</summary>
/// <remarks>
/// PONTO DE EXTENSÃO (bloco futuro): um campo <c>Propostas</c> com alterações tipadas do cronograma (adicionar
/// tarefa, mudar duração, criar vínculo...), que o painel mostra para o usuário aceitar ou recusar e aplica pelos
/// comandos do domínio, com as validações de sempre. Hoje o assistente só lê o cronograma.
/// </remarks>
public sealed record RespostaIA(string Texto);
