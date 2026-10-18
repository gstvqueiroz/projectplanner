namespace ProjectPlanner.Servidor;

/// <summary>
/// A configuração do servidor (appsettings.json ou variáveis de ambiente, como <c>Gemini__Chave</c>). Os segredos (as chaves
/// do Gemini, do JWT e de admin, a senha do e-mail) vêm só do ambiente da nuvem, nunca do repositório.
/// </summary>
public sealed class OpcoesDoServidor
{
    public OpcoesDoJwt Jwt { get; set; } = new();
    public OpcoesDoGemini Gemini { get; set; } = new();
    public OpcoesDoGoogle Google { get; set; } = new();
    public OpcoesDosPlanos Planos { get; set; } = new();
    public OpcoesDeEmail Email { get; set; } = new();
    public OpcoesDosLimites Limites { get; set; } = new();
    public OpcoesDeAdmin Admin { get; set; } = new();
}

public sealed class OpcoesDoJwt
{
    /// <summary>A chave que assina os tokens (32 bytes ou mais). Sem ela, em desenvolvimento, uma provisória.</summary>
    public string? Chave { get; set; }
    public string Emissor { get; set; } = "ProjectPlanner.Servidor";
    public string Publico { get; set; } = "ProjectPlanner.App";
    public int MinutosDeAcesso { get; set; } = 15;
    public int DiasDeRenovacao { get; set; } = 30;
}

public sealed class OpcoesDoGemini
{
    /// <summary>A chave do Gemini da empresa: só no servidor. Sem ela, a IA responde que não está configurada.</summary>
    public string? Chave { get; set; }
    public string Endereco { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    public static IReadOnlyList<string> ModelosPadrao { get; } = ["gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro"];

    /// <summary>
    /// Os modelos que os usuários podem usar (os outros são recusados); vazio, os padrão. Começa vazio porque a configuração
    /// soma os itens da lista aos que já houver.
    /// </summary>
    public string[] Modelos { get; set; } = [];

    public IReadOnlyList<string> Liberados => Modelos.Length > 0 ? Modelos.Distinct().ToArray() : ModelosPadrao;
}

public sealed class OpcoesDoGoogle
{
    /// <summary>Os client IDs OAuth aceitos no "Entrar com Google" (o do app de computador e, depois, o do Telegram).</summary>
    public string[] ClientIds { get; set; } = [];
}

public sealed class OpcoesDosPlanos
{
    public long GratisTokensPorMes { get; set; } = 300_000;
    public long ProfissionalTokensPorMes { get; set; } = 10_000_000;

    public long TokensPorMes(string plano) => plano == Planos.Profissional ? ProfissionalTokensPorMes : GratisTokensPorMes;
}

public static class Planos
{
    public const string Gratis = "gratis";
    public const string Profissional = "profissional";

    public static IReadOnlyList<string> Todos { get; } = [Gratis, Profissional];
}

public sealed class OpcoesDeEmail
{
    public string Remetente { get; set; } = "ProjectPlanner <nao-responda@projectplanner.app>";

    /// <summary>Sem servidor SMTP, os e-mails só vão para o log (desenvolvimento).</summary>
    public string? Smtp { get; set; }
    public int Porta { get; set; } = 587;
    public string? Usuario { get; set; }
    public string? Senha { get; set; }
}

public sealed class OpcoesDosLimites
{
    /// <summary>Pedidos de IA por minuto, por usuário.</summary>
    public int PedidosDeIaPorMinuto { get; set; } = 30;

    /// <summary>Pedidos às rotas de conta por minuto, por endereço IP.</summary>
    public int PedidosDeContaPorMinuto { get; set; } = 10;

    /// <summary>O tamanho máximo de um pedido de IA (as fotos vão nele).</summary>
    public int MegabytesPorPedido { get; set; } = 15;

    public int TentativasAteBloquear { get; set; } = 5;
    public int MinutosDeBloqueio { get; set; } = 15;

    /// <summary>
    /// Ligue quando o servidor roda atrás de um proxy da nuvem (que manda o IP de quem pediu em X-Forwarded-For): os limites
    /// por IP passam a usar esse endereço. Desligado, o cabeçalho é ignorado (senão qualquer um inventaria um IP).
    /// </summary>
    public bool AtrasDeProxy { get; set; }
}

public sealed class OpcoesDeAdmin
{
    /// <summary>A chave das rotas de administração (cabeçalho X-Chave-Admin); sem ela, as rotas ficam fechadas.</summary>
    public string? Chave { get; set; }
}
