namespace ProjectPlanner.IA;

/// <summary>Quem escreveu uma mensagem da conversa.</summary>
public enum AutorMensagem
{
    Usuario,
    Assistente,
}

/// <summary>
/// Uma mensagem da conversa com o assistente. Desde o Bloco 26 a conversa fica no projeto (o App a guarda no .pplan),
/// e cada projeto tem a sua.
/// </summary>
/// <param name="Anexos">Os documentos enviados junto (só nas mensagens do usuário).</param>
/// <param name="EhErro">
/// Aviso local, não uma resposta do assistente: falha ao obter a resposta ou envio cancelado. Aparece
/// destacado no painel e não é mandado de volta ao assistente no histórico.
/// </param>
/// <param name="Resposta">
/// Bloco 26: a resposta estruturada do assistente (perguntas, plano, ficha), para ele ver no histórico o que já perguntou
/// ou propôs; nula numa resposta só de texto e nas mensagens do usuário.
/// </param>
public sealed record MensagemChat(
    AutorMensagem Autor,
    string Texto,
    DateTimeOffset DataHora,
    IReadOnlyList<AnexoIA> Anexos,
    bool EhErro = false,
    RespostaIA? Resposta = null);
