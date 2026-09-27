namespace ProjectPlanner.IA;

/// <summary>Quem escreveu uma mensagem da conversa.</summary>
public enum AutorMensagem
{
    Usuario,
    Assistente,
}

/// <summary>
/// Uma mensagem da conversa com o assistente. O histórico fica só em memória, durante a sessão, e é
/// esvaziado quando outro projeto é aberto (Bloco 19, D4).
/// </summary>
/// <param name="Anexos">Os documentos enviados junto (só nas mensagens do usuário).</param>
/// <param name="EhErro">
/// Aviso local, não uma resposta do assistente: falha ao obter a resposta ou envio cancelado. Aparece
/// destacado no painel e não é mandado de volta ao assistente no histórico.
/// </param>
public sealed record MensagemChat(
    AutorMensagem Autor,
    string Texto,
    DateTimeOffset DataHora,
    IReadOnlyList<AnexoIA> Anexos,
    bool EhErro = false);
