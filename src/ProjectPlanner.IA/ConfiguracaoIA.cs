using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA;

/// <summary>A chave de API e o modelo que o assistente usa agora.</summary>
/// <remarks>
/// A chave nunca vai no arquivo .pplan nem num JSON em texto puro. No programa (Bloco 23), quem cumpre este contrato
/// lê o cofre do sistema (Chaves do macOS, Gerenciador de Credenciais do Windows), gravado pela tela de configurações
/// do assistente; a variável de ambiente GEMINI_API_KEY serve só para desenvolvimento. Ver docs/ASSISTENTE-IA.md.
/// </remarks>
public interface IConfiguracaoIA
{
    /// <summary>A chave de API; nula quando não há. Lida a cada envio: salvar ou remover a chave vale na hora.</summary>
    string? ChaveApi { get; }

    /// <summary>O modelo do provedor, como "gemini-2.5-flash".</summary>
    string Modelo { get; }
}

/// <summary>Sem chave: o assistente responde que não está configurado.</summary>
public sealed class SemChaveDeApi : IConfiguracaoIA
{
    public string? ChaveApi => null;

    public string Modelo => ProvedorGemini.ModeloRecomendado;
}

/// <summary>Monta o assistente do painel a partir da configuração e do provedor.</summary>
public static class FabricaAssistenteIA
{
    public static IAssistenteIA Criar(IConfiguracaoIA configuracao, IProvedorIA provedor)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(provedor);
        return new AssistenteConformeConfiguracao(configuracao, provedor);
    }
}
