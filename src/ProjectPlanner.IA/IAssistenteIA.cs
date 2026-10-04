namespace ProjectPlanner.IA;

/// <summary>
/// O assistente de IA do painel lateral (Bloco 19). A interface do chat só conhece este contrato: com uma chave de API
/// (Bloco 23), quem o cumpre é o <see cref="Gemini.AssistenteGemini"/>; sem chave, o <see cref="AssistenteIAIndisponivel"/>.
/// A escolha é feita a cada envio por <see cref="AssistenteConformeConfiguracao"/>.
/// </summary>
/// <remarks>
/// Outro provedor (Claude, um servidor próprio) é outra implementação desta interface, criada por um
/// <see cref="IProvedorIA"/>. Detalhes em docs/ASSISTENTE-IA.md.
/// </remarks>
public interface IAssistenteIA
{
    /// <summary>Falso sem chave de API: o painel avisa que as respostas são simuladas.</summary>
    bool EstaConfigurado { get; }

    /// <summary>Manda a mensagem (com anexos e histórico) e devolve a resposta do assistente.</summary>
    /// <exception cref="OperationCanceledException">O usuário cancelou antes da resposta.</exception>
    /// <remarks>Falhas (rede, limite de uso) chegam como exceções; o painel as mostra como aviso na conversa.</remarks>
    Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento);
}
