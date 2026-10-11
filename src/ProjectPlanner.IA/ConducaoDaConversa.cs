namespace ProjectPlanner.IA;

/// <summary>Como a conversa está trabalhando (Bloco 26): sem escolha ainda, montando direto ou estudando antes.</summary>
public enum ModoDeTrabalho
{
    NaoEscolhido,
    Direto,
    Estudo,
}

/// <summary>A preferência do usuário para um pedido de cronograma (Configurações do assistente).</summary>
public enum PreferenciaDeModo
{
    /// <summary>O assistente pergunta: montar agora ou estudar e conversar antes (padrão).</summary>
    Perguntar,
    Direto,
    Estudo,
}

/// <summary>O que a resposta do assistente é, além da mensagem.</summary>
public enum TipoDeResposta
{
    /// <summary>Conversa e, se houver, alterações do cronograma (como antes do Bloco 26).</summary>
    Resposta,

    /// <summary>Pede para o usuário escolher entre montar agora e estudar antes; não altera nada.</summary>
    EscolherModo,

    /// <summary>Perguntas do estudo, com respostas sugeridas; não altera nada.</summary>
    Perguntas,

    /// <summary>O plano do cronograma para aprovar; não altera nada.</summary>
    Plano,
}

/// <summary>Um dado da ficha do projeto ("Área construída": "100 m²"); valor vazio apaga o campo.</summary>
public sealed record ItemDeFicha(string Campo, string Valor);

/// <summary>Uma pergunta do assistente, com as respostas sugeridas (pode não ter nenhuma).</summary>
public sealed record PerguntaIA(string Texto, IReadOnlyList<string> Opcoes);

/// <summary>Uma etapa do plano: nome, duração estimada em dias úteis e uma observação opcional.</summary>
public sealed record EtapaIA(string Nome, int Duracao, string? Observacao = null);

/// <summary>O plano do cronograma que o assistente apresenta para aprovação (modo de estudo).</summary>
/// <param name="Termino">O término estimado, como o assistente escreveu (AAAA-MM-DD, de preferência).</param>
public sealed record PlanoIA(IReadOnlyList<EtapaIA> Etapas, string? Termino, IReadOnlyList<string> Premissas);

/// <summary>
/// Em que pé a conversa está (Bloco 26), mandado com cada pedido: o modo, a preferência do usuário, a ficha do projeto
/// e o andamento do estudo. As instruções mudam conforme isto (ver <see cref="InstrucoesDoAssistente.Conducao"/>).
/// </summary>
/// <param name="RodadasDePerguntas">Quantas rodadas de perguntas o assistente já fez neste estudo.</param>
/// <param name="PlanoAprovado">O usuário aprovou o plano: é hora de montar o cronograma completo.</param>
/// <param name="ApresentarPlanoAgora">O usuário pediu para parar as perguntas e ver o plano.</param>
public sealed record ConducaoDaConversa(
    ModoDeTrabalho Modo,
    PreferenciaDeModo Preferencia,
    IReadOnlyList<ItemDeFicha> Ficha,
    int RodadasDePerguntas = 0,
    bool PlanoAprovado = false,
    bool ApresentarPlanoAgora = false)
{
    /// <summary>Depois de tantas rodadas, o assistente apresenta o plano com o que tem.</summary>
    public const int MaximoDeRodadas = 4;

    /// <summary>O modo que vale para as instruções: sem escolha, a preferência "direto" ou "estudo" já decide.</summary>
    public ModoDeTrabalho ModoEfetivo => Modo != ModoDeTrabalho.NaoEscolhido ? Modo : Preferencia switch
    {
        PreferenciaDeModo.Direto => ModoDeTrabalho.Direto,
        PreferenciaDeModo.Estudo => ModoDeTrabalho.Estudo,
        _ => ModoDeTrabalho.NaoEscolhido,
    };
}
