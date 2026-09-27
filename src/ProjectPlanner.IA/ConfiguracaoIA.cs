namespace ProjectPlanner.IA;

/// <summary>
/// De onde vem a chave de API do assistente.
/// </summary>
/// <remarks>
/// PONTO DE EXTENSÃO: a chave nunca vai no arquivo .pplan nem num JSON em texto puro. No bloco da integração,
/// uma implementação lerá o cofre do sistema (Chaves do macOS, Gerenciador de Credenciais do Windows), onde a
/// chave é gravada por uma tela Configurações > Assistente; uma variável de ambiente pode servir só para
/// desenvolvimento.
/// </remarks>
public interface IConfiguracaoIA
{
    /// <summary>A chave de API; nula quando não há.</summary>
    string? ChaveApi { get; }
}

/// <summary>A configuração desta fase: nunca há chave.</summary>
public sealed class SemChaveDeApi : IConfiguracaoIA
{
    public string? ChaveApi => null;
}

/// <summary>Escolhe a implementação de <see cref="IAssistenteIA"/> conforme a configuração.</summary>
public static class FabricaAssistenteIA
{
    public static IAssistenteIA Criar(IConfiguracaoIA configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        if (string.IsNullOrWhiteSpace(configuracao.ChaveApi))
            return new AssistenteIAIndisponivel();

        // PONTO DE EXTENSÃO: com chave, devolver aqui a implementação real (ex.: new AssistenteIAClaude(chave)).
        // Enquanto ela não existe, uma chave não muda nada.
        return new AssistenteIAIndisponivel();
    }
}
