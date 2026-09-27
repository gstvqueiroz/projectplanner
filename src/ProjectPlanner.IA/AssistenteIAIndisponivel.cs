namespace ProjectPlanner.IA;

/// <summary>
/// PROVISÓRIO até existir uma chave de API: responde sempre que a integração não está configurada. Não faz
/// chamada de rede e não lê o conteúdo dos anexos.
/// </summary>
/// <param name="espera">
/// Quanto esperar antes de responder (0,5 s no programa), só para o painel mostrar "Assistente digitando…"
/// como fará com a latência da API real. Os testes usam zero.
/// </param>
public sealed class AssistenteIAIndisponivel(TimeSpan espera) : IAssistenteIA
{
    /// <summary>A espera usada no programa.</summary>
    public static readonly TimeSpan EsperaPadrao = TimeSpan.FromMilliseconds(500);

    public const string Resposta =
        "A integração com IA ainda não foi configurada, então esta é uma resposta automática. " +
        "Sua mensagem não foi enviada a nenhum serviço";

    public AssistenteIAIndisponivel() : this(EsperaPadrao)
    {
    }

    public bool EstaConfigurado => false;

    public async Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (espera > TimeSpan.Zero)
            await Task.Delay(espera, cancelamento);
        cancelamento.ThrowIfCancellationRequested();

        var anexos = pedido.Anexos.Count switch
        {
            0 => "",
            1 => " (nem o anexo, que não foi lido)",
            var n => $" (nem os {n} anexos, que não foram lidos)",
        };
        return new RespostaIA($"{Resposta}{anexos}. Quando uma chave de API for configurada, o assistente poderá " +
                              "responder perguntas sobre o cronograma e analisar os documentos anexados.");
    }
}
