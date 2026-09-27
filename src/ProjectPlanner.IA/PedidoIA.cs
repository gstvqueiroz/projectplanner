namespace ProjectPlanner.IA;

/// <summary>
/// O que o painel manda ao assistente a cada envio. É um registro (e não parâmetros soltos) para que o
/// bloco da integração real possa acrescentar campos sem mudar <see cref="IAssistenteIA.EnviarAsync"/>.
/// </summary>
/// <remarks>
/// PONTO DE EXTENSÃO (bloco da integração, ver docs/ASSISTENTE-IA.md): um campo <c>Contexto</c> com um retrato
/// só de leitura do cronograma aberto (tarefas com EDT, datas, vínculos, folga, criticidade), montado pelo
/// App. O assistente nunca recebe o <c>Cronograma</c> do domínio em si.
/// </remarks>
/// <param name="Mensagem">O texto que o usuário escreveu (pode ser vazio se só há anexos).</param>
/// <param name="Anexos">Os documentos anexados a esta mensagem.</param>
/// <param name="Historico">
/// As mensagens anteriores da conversa, da mais antiga para a mais recente, sem os avisos locais
/// (<see cref="MensagemChat.EhErro"/>): as APIs de chat não guardam a conversa entre as chamadas.
/// </param>
public sealed record PedidoIA(string Mensagem, IReadOnlyList<AnexoIA> Anexos, IReadOnlyList<MensagemChat> Historico);

/// <summary>A resposta do assistente.</summary>
/// <remarks>
/// PONTO DE EXTENSÃO (bloco da integração): um campo <c>Propostas</c> com alterações tipadas do cronograma
/// (adicionar tarefa, mudar duração, criar vínculo...), que o painel mostra para o usuário aceitar ou recusar
/// e aplica pelos comandos do domínio, com as validações de sempre.
/// </remarks>
public sealed record RespostaIA(string Texto);
