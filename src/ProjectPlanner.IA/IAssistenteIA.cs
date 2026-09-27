namespace ProjectPlanner.IA;

/// <summary>
/// O assistente de IA do painel lateral (Bloco 19). A interface do chat só conhece este contrato; hoje ele é
/// cumprido por <see cref="AssistenteIAIndisponivel"/>, que não faz nenhuma chamada de rede.
/// </summary>
/// <remarks>
/// <para>
/// PARA A INTEGRAÇÃO REAL (bloco futuro, com chave de API): criar neste projeto uma classe que implemente esta
/// interface (ex.: <c>AssistenteIAClaude</c>), que leia os anexos, monte a chamada à API com o histórico e
/// devolva a resposta; e fazê-la ser escolhida por <see cref="FabricaAssistenteIA"/> quando houver chave. A UI
/// não muda. Detalhes em docs/ASSISTENTE-IA.md.
/// </para>
/// <para>
/// Respostas em partes (streaming) poderiam vir num método adicional que devolva <c>IAsyncEnumerable</c>;
/// não há necessidade disso enquanto não houver API.
/// </para>
/// </remarks>
public interface IAssistenteIA
{
    /// <summary>Falso enquanto não há chave de API: o painel avisa que as respostas são simuladas.</summary>
    bool EstaConfigurado { get; }

    /// <summary>Manda a mensagem (com anexos e histórico) e devolve a resposta do assistente.</summary>
    /// <exception cref="OperationCanceledException">O usuário cancelou antes da resposta.</exception>
    /// <remarks>Falhas (rede, limite de uso) chegam como exceções; o painel as mostra como aviso na conversa.</remarks>
    Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento);
}
