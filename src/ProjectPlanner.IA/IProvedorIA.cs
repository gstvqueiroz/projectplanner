namespace ProjectPlanner.IA;

/// <summary>
/// Um serviço de IA (Bloco 23): hoje o Gemini; depois o Claude, ou um servidor próprio quando o programa for
/// vendido (ver docs/ASSISTENTE-IA.md). Cria o assistente com a chave e o modelo, e testa a conexão para a tela
/// de configurações. O painel e o resto do programa não sabem qual é o provedor.
/// </summary>
public interface IProvedorIA
{
    /// <summary>Nome para a tela, como "Gemini (Google)".</summary>
    string Nome { get; }

    /// <summary>O modelo usado quando o usuário não escolheu outro.</summary>
    string ModeloPadrao { get; }

    /// <summary>O nome do modelo limpo (sem espaços nem o prefixo "models/").</summary>
    /// <exception cref="ArgumentException">Nome vazio ou com caracteres que não existem em nomes de modelo.</exception>
    string NormalizarModelo(string modelo);

    /// <summary>O assistente que fala com o serviço usando a chave e o modelo.</summary>
    IAssistenteIA CriarAssistente(string chave, string modelo);

    /// <summary>Bloco 29: a IA que olha as fotos (legendas, % pelas fotos, texto do relatório), com a mesma chave e modelo.</summary>
    IAnalistaDeFotos CriarAnalista(string chave, string modelo);

    /// <summary>Bloco 38: a IA que sugere o orçamento, com a mesma chave e modelo.</summary>
    IConsultorDeOrcamento CriarConsultor(string chave, string modelo);

    /// <summary>Bloco 43: a IA que analisa os indicadores da obra, com a mesma chave e modelo.</summary>
    IAnalistaDaObra CriarAnalistaDaObra(string chave, string modelo);

    /// <summary>Confere se a chave funciona com o modelo, sem gastar a cota de mensagens.</summary>
    /// <exception cref="ArgumentException">Chave vazia ou com espaços, ou nome de modelo inválido.</exception>
    /// <exception cref="ErroDoAssistenteException">A chave não vale, o modelo não existe ou não há conexão.</exception>
    Task TestarAsync(string chave, string modelo, CancellationToken cancelamento);
}

/// <summary>Uma falha ao falar com o serviço de IA, já com a mensagem para o usuário (sem detalhes técnicos nem a chave).</summary>
public sealed class ErroDoAssistenteException(string mensagem, Exception? causa = null) : Exception(mensagem, causa);
