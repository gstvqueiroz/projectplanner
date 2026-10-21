namespace ProjectPlanner.IA;

/// <summary>
/// Quanto da conversa vai a cada envio (Bloco 46). As APIs de chat não guardam nada entre as chamadas, então tudo o que
/// vai é cobrado de novo a cada mensagem; numa conversa longa com PDFs, o custo cresce a cada pergunta. Por isso só vão
/// as mensagens mais recentes e só os anexos das últimas mensagens do usuário.
/// </summary>
/// <remarks>
/// O preço: o assistente deixa de "enxergar" um PDF antigo. Ele sabe que o arquivo foi enviado (vai uma nota com o nome),
/// mas para voltar a lê-lo o usuário anexa de novo. A condução da conversa (ficha do projeto, Bloco 26) e o retrato do
/// cronograma vão inteiros nas instruções, então o que foi entendido do projeto não se perde com o corte.
/// </remarks>
public static class LimitesDaConversa
{
    /// <summary>Quantas mensagens anteriores (sem os avisos locais) vão a cada envio, as mais recentes.</summary>
    public const int MensagensDoHistorico = 20;

    /// <summary>
    /// Os anexos de quantas mensagens anteriores do usuário (as mais recentes) ainda vão como arquivo, além dos da mensagem
    /// atual. Os das mais antigas viram a nota de <see cref="NotaDeAnexoAntigo"/>.
    /// </summary>
    public const int MensagensComAnexos = 2;

    /// <summary>O que vai no lugar de um anexo antigo: "(anexo: planta.pdf, enviado antes)".</summary>
    public static string NotaDeAnexoAntigo(AnexoIA anexo) => $"(anexo: {anexo.Nome}, enviado antes)";
}
