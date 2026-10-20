namespace ProjectPlanner.Servidor;

/// <summary>
/// A configuração do servidor (appsettings.json ou variáveis de ambiente, como <c>Gemini__Chave</c>). Os segredos (as chaves
/// do Gemini, do JWT e de admin, a senha do e-mail) vêm só do ambiente da nuvem, nunca do repositório.
/// </summary>
public sealed class OpcoesDoServidor
{
    public OpcoesDoJwt Jwt { get; set; } = new();
    public OpcoesDoGemini Gemini { get; set; } = new();
    public OpcoesDaAnthropic Anthropic { get; set; } = new();

    /// <summary>Bloco 46: os modelos de cada nível (vazio: os padrão).</summary>
    public List<ModeloDeIa> Modelos { get; set; } = [];
    public OpcoesDoGoogle Google { get; set; } = new();
    public OpcoesDosPlanos Planos { get; set; } = new();
    public OpcoesDeEmail Email { get; set; } = new();
    public OpcoesDosLimites Limites { get; set; } = new();
    public OpcoesDeAdmin Admin { get; set; } = new();

    /// <summary>Bloco 48: câmbio, impostos, preço e alerta, para o painel financeiro.</summary>
    public OpcoesFinanceiras Financeiro { get; set; } = new();

    /// <summary>Bloco 49: o bot do Telegram.</summary>
    public OpcoesDoTelegram Telegram { get; set; } = new();
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
}

/// <summary>Bloco 46: o Claude, da Anthropic (o nível Máximo).</summary>
public sealed class OpcoesDaAnthropic
{
    /// <summary>A chave da API da Anthropic: só no servidor. Sem ela, os modelos da Anthropic respondem que não estão configurados.</summary>
    public string? Chave { get; set; }
    public string Endereco { get; set; } = "https://api.anthropic.com/v1/";
    public string Versao { get; set; } = "2023-06-01";

    /// <summary>O máximo de tokens de resposta (a API da Anthropic exige um).</summary>
    public int MaximoDeTokens { get; set; } = 16_000;
}

/// <summary>Bloco 46: um modelo liberado, com o nível, quem o atende e o preço (US$ por milhão de tokens).</summary>
public sealed class ModeloDeIa
{
    public string Id { get; set; } = "";
    public string Nivel { get; set; } = Niveis.Padrao;
    public string Provedor { get; set; } = Provedores.Gemini;
    public string Nome { get; set; } = "";
    public decimal DolaresPorMilhaoDeEntrada { get; set; }
    public decimal DolaresPorMilhaoDeSaida { get; set; }

    /// <summary>O custo, em milionésimos de dólar (inteiro, para somar no banco sem perder centavos).</summary>
    public long Microdolares(long entrada, long saida) =>
        (long)Math.Ceiling(entrada * DolaresPorMilhaoDeEntrada + saida * DolaresPorMilhaoDeSaida);
}

public static class Niveis
{
    public const string Padrao = "padrao";
    public const string Avancado = "avancado";
    public const string Maximo = "maximo";

    public static string Nome(string nivel) => nivel switch
    {
        Avancado => "Avançado",
        Maximo => "Máximo",
        _ => "Padrão",
    };
}

public static class Provedores
{
    public const string Gemini = "gemini";
    public const string Anthropic = "anthropic";
}

public sealed class OpcoesDoGoogle
{
    /// <summary>Outros client IDs OAuth aceitos no idToken do "Entrar com Google" (por exemplo, o do Telegram, depois).</summary>
    public string[] ClientIds { get; set; } = [];

    /// <summary>
    /// Bloco 45: o client ID OAuth do tipo "App para computador" que o programa usa para abrir o login no navegador. O app o
    /// recebe do servidor (<c>GET /v1/google</c>); sem ele (ou sem o segredo), o login com Google fica desligado.
    /// </summary>
    public string? ClientIdDoApp { get; set; }

    /// <summary>O segredo desse client ID: só no servidor, que troca o código do navegador pelo idToken.</summary>
    public string? SegredoDoApp { get; set; }

    public string EnderecoDoToken { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>Os client IDs aceitos como audiência do idToken.</summary>
    public string[] Audiencias => [.. ClientIds.Append(ClientIdDoApp).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct()];

    public bool LoginDoAppLigado => !string.IsNullOrWhiteSpace(ClientIdDoApp) && !string.IsNullOrWhiteSpace(SegredoDoApp);
}

/// <summary>
/// Bloco 46: os planos em créditos. Cada pedido de IA gasta o custo real dele (pela tabela de preços do modelo), convertido em
/// créditos: 1 crédito = <see cref="DolaresPorCredito"/> de custo.
/// </summary>
public sealed class OpcoesDosPlanos
{
    public long GratisCreditosPorMes { get; set; } = 300;
    public long ProfissionalCreditosPorMes { get; set; } = 6_000;
    public decimal DolaresPorCredito { get; set; } = 0.0005m;

    /// <summary>Os níveis do plano Grátis, separados por vírgula (o Profissional usa todos).</summary>
    public string NiveisDoGratis { get; set; } = Niveis.Padrao;

    public long CreditosPorMes(string plano) => plano == Planos.Profissional ? ProfissionalCreditosPorMes : GratisCreditosPorMes;

    public bool Libera(string plano, string nivel) => plano == Planos.Profissional
        || NiveisDoGratis.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(nivel);

    public decimal Creditos(long microdolares) => microdolares / (DolaresPorCredito * 1_000_000m);
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

    /// <summary>Bloco 48: para onde vai o alerta de custo do mês; sem ele, o alerta só aparece no painel.</summary>
    public string? Email { get; set; }
}

/// <summary>
/// Bloco 48: o que o painel financeiro usa para passar de dólar a real e estimar a margem. A receita é estimada pelo preço
/// do plano Profissional até os pagamentos de verdade (Bloco 47).
/// </summary>
public sealed class OpcoesFinanceiras
{
    public decimal ReaisPorDolar { get; set; } = 5.50m;

    /// <summary>O IOF e o spread do cartão internacional sobre a conta do Google e da Anthropic.</summary>
    public decimal PercentualSobreODolar { get; set; } = 3.5m;

    /// <summary>Impostos (Simples Nacional) e a taxa do meio de pagamento, sobre a receita.</summary>
    public decimal PercentualDeImpostosETaxas { get; set; } = 11m;

    public decimal PrecoProfissionalEmReais { get; set; } = 59m;

    /// <summary>O custo de IA do mês (em reais) a partir do qual o alerta é mandado; 0 desliga.</summary>
    public decimal AlertaMensalEmReais { get; set; } = 100m;

    /// <summary>O custo em reais (com o IOF) de milionésimos de dólar.</summary>
    public decimal Reais(long microdolares) => microdolares / 1_000_000m * ReaisPorDolar * (1 + PercentualSobreODolar / 100);
}

/// <summary>Bloco 49: o bot do Telegram (criado no @BotFather). Sem o token, o Telegram fica desligado.</summary>
public sealed class OpcoesDoTelegram
{
    /// <summary>O token do bot (do @BotFather): só no servidor.</summary>
    public string? Token { get; set; }

    /// <summary>O nome do bot, sem o @ (para o link de conexão, t.me/NomeDoBot).</summary>
    public string? NomeDoBot { get; set; }

    /// <summary>O segredo que o Telegram manda em cada aviso (X-Telegram-Bot-Api-Secret-Token); confere que o aviso veio dele.</summary>
    public string? Segredo { get; set; }

    /// <summary>O endereço público do servidor (https://…onrender.com), para registrar o webhook ao subir.</summary>
    public string? UrlPublica { get; set; }

    public string Endereco { get; set; } = "https://api.telegram.org/";

    public int MegabytesPorFoto { get; set; } = 10;
    public int FotosPendentesPorConta { get; set; } = 50;
    public int MensagensPorMinuto { get; set; } = 20;

    public bool Ligado => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(Segredo);
}
