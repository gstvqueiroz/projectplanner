namespace ProjectPlanner.IA;

/// <summary>
/// O assistente do painel (Bloco 23): sem chave, responde como <see cref="AssistenteIAIndisponivel"/>; com chave, usa
/// o provedor (Gemini). A configuração é lida a cada envio, então salvar ou remover a chave na tela de configurações
/// vale na hora, sem reiniciar o programa.
/// </summary>
public sealed class AssistenteConformeConfiguracao : IAssistenteIA
{
    private readonly IConfiguracaoIA _configuracao;
    private readonly IProvedorIA _provedor;
    private readonly IAssistenteIA _semChave;
    private readonly Lock _trava = new();
    private (string Chave, string Modelo, IAssistenteIA Assistente)? _atual;

    public AssistenteConformeConfiguracao(IConfiguracaoIA configuracao, IProvedorIA provedor, IAssistenteIA? semChave = null)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(provedor);
        (_configuracao, _provedor, _semChave) = (configuracao, provedor, semChave ?? new AssistenteIAIndisponivel());
    }

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(_configuracao.ChaveApi);

    public Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return Assistente().EnviarAsync(pedido, cancelamento);
    }

    /// <summary>O assistente da configuração de agora (o mesmo enquanto a chave e o modelo não mudam).</summary>
    internal IAssistenteIA Assistente()
    {
        var chave = _configuracao.ChaveApi;
        if (string.IsNullOrWhiteSpace(chave))
            return _semChave;
        var modelo = _configuracao.Modelo;
        lock (_trava)
        {
            if (_atual is { } atual && atual.Chave == chave && atual.Modelo == modelo)
                return atual.Assistente;
            var novo = _provedor.CriarAssistente(chave, modelo);
            _atual = (chave, modelo, novo);
            return novo;
        }
    }
}
