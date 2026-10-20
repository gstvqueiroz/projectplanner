using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

/// <summary>
/// O ponto de IA (Blocos 44 e 46). Espelha o caminho do Gemini (<c>models/{modelo}:generateContent</c>), para o programa
/// reaproveitar as mesmas classes. Confere o login, o modelo e o plano (cada nível tem o seu), o tamanho e os créditos do
/// mês; chama o Gemini, ou o Claude traduzindo o pedido, com a chave do servidor; registra os tokens e o custo (nunca o
/// conteúdo) e devolve a resposta no formato do Gemini, com o saldo de créditos nos cabeçalhos.
/// </summary>
public static class Ia
{
    public const string ClienteDoGemini = "gemini";
    public const string ClienteDaAnthropic = "anthropic";
    public const string CabecalhoDosCreditosUsados = "X-Creditos-Usados";
    public const string CabecalhoDosCreditosRestantes = "X-Creditos-Restantes";
    public const string CotaEsgotada = "Os créditos de IA do seu plano acabaram neste mês. Eles voltam no dia 1º (ou assine o plano Profissional).";
    public const string SemChave = "A IA do servidor ainda não está configurada. Tente mais tarde.";

    public static void Mapear(WebApplication app)
    {
        var ia = app.MapGroup("/v1/ia").RequireAuthorization().RequireRateLimiting(LimitesDeUso.Ia);
        ia.MapPost("/models/{modelo}:generateContent", Gerar);
        ia.MapGet("/models/{modelo}", Testar);
        app.MapGet("/v1/modelos", Modelos).RequireAuthorization();
    }

    private static IResult Falha(int status, string mensagem) => Contas.Falha(status, mensagem);

    /// <summary>Os modelos de cada nível, se o plano libera e quantos créditos um pedido típico gasta.</summary>
    private static async Task<IResult> Modelos(ClaimsPrincipal quem, BancoDoServidor banco, CatalogoDeModelos catalogo, IOptions<OpcoesDoServidor> opcoes)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return Falha(401, "Sessão vencida: entre de novo.");
        var planos = opcoes.Value.Planos;
        return Results.Ok(catalogo.Todos.Select(m => new
        {
            id = m.Id,
            nivel = m.Nivel,
            nome = m.Nome,
            liberado = planos.Libera(usuario.Plano, m.Nivel),
            disponivel = catalogo.Disponivel(m),
            creditosPorPedido = Math.Max(1, (int)Math.Round(planos.Creditos(m.Microdolares(CatalogoDeModelos.EntradaTipica, CatalogoDeModelos.SaidaTipica)))),
        }));
    }

    // O que vale para os dois pontos: a conta, o modelo, o plano e a chave do provedor.
    private static async Task<(Usuario? Usuario, ModeloDeIa? Modelo, IResult? Falha)> Conferir(ClaimsPrincipal quem, string id, BancoDoServidor banco,
        CatalogoDeModelos catalogo, OpcoesDosPlanos planos)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return (null, null, Falha(401, "Sessão vencida: entre de novo."));
        if (catalogo.Achar(id) is not { } modelo)
            return (null, null, Falha(400, $"O modelo \"{id}\" não está disponível. Use um destes: {string.Join(", ", catalogo.Todos.Select(m => m.Id))}."));
        if (!planos.Libera(usuario.Plano, modelo.Nivel))
            return (null, null, Falha(403, $"O nível {Niveis.Nome(modelo.Nivel)} ({modelo.Nome}) é do plano Profissional. Assine em Conta > Minha conta, " +
                                          "ou volte ao nível Padrão."));
        if (!catalogo.Disponivel(modelo))
            return (null, null, Falha(503, SemChave));
        return (usuario, modelo, null);
    }

    /// <summary>Testar a conexão (o programa chama ao salvar as configurações): não vai à IA nem gasta créditos.</summary>
    private static async Task<IResult> Testar(string modelo, ClaimsPrincipal quem, BancoDoServidor banco, CatalogoDeModelos catalogo,
        IOptions<OpcoesDoServidor> opcoes)
    {
        var (_, encontrado, falha) = await Conferir(quem, modelo, banco, catalogo, opcoes.Value.Planos);
        return falha ?? Results.Ok(new { name = "models/" + encontrado!.Id, displayName = encontrado.Nome });
    }

    private static async Task<IResult> Gerar(string modelo, HttpContext http, ClaimsPrincipal quem, BancoDoServidor banco, CatalogoDeModelos catalogo,
        IOptions<OpcoesDoServidor> opcoes, IHttpClientFactory clientes, TimeProvider relogio, ILoggerFactory logs, CancellationToken c)
    {
        var config = opcoes.Value;
        var planos = config.Planos;
        var (usuario, escolhido, falha) = await Conferir(quem, modelo, banco, catalogo, planos);
        if (falha is not null)
            return falha;
        var limite = planos.CreditosPorMes(usuario!.Plano);
        var (_, gastos) = await Cotas.NoMes(banco, usuario.Id, relogio);
        if (planos.Creditos(gastos) >= limite)
            return Falha(402, CotaEsgotada);

        var tamanho = config.Limites.MegabytesPorPedido * 1024L * 1024L;
        var corpo = http.Request.ContentLength > tamanho ? null : await LerAte(http.Request.Body, tamanho, c);
        if (corpo is null)
            return Falha(413, $"O pedido passa de {config.Limites.MegabytesPorPedido} MB. Mande menos fotos ou anexos menores.");

        var log = logs.CreateLogger(typeof(Ia));
        var resultado = escolhido!.Provedor == Provedores.Anthropic
            ? await PeloClaude(escolhido, corpo, config.Anthropic, clientes, log, c)
            : await PeloGemini(escolhido, corpo, config.Gemini, clientes, log, c);
        if (resultado.Falha is not null)
            return resultado.Falha;

        var custo = escolhido.Microdolares(resultado.Entrada, resultado.Saida);
        banco.UsosDeIa.Add(new UsoDeIa
        {
            UsuarioId = usuario.Id, Quando = relogio.GetUtcNow().UtcDateTime, Modelo = escolhido.Id, TokensDeEntrada = (int)resultado.Entrada,
            TokensDeSaida = (int)resultado.Saida, TokensTotais = (int)(resultado.Entrada + resultado.Saida), CustoEmMicrodolares = custo,
        });
        await banco.SaveChangesAsync(c);
        var cultura = System.Globalization.CultureInfo.InvariantCulture;
        http.Response.Headers[CabecalhoDosCreditosUsados] = Math.Round(planos.Creditos(custo), 1, MidpointRounding.AwayFromZero).ToString(cultura);
        http.Response.Headers[CabecalhoDosCreditosRestantes] = Math.Max(0, Math.Round(limite - planos.Creditos(gastos + custo), 1, MidpointRounding.AwayFromZero)).ToString(cultura);
        return Results.Bytes(resultado.Resposta!, "application/json");
    }

    private sealed record Resultado(byte[]? Resposta, long Entrada, long Saida, IResult? Falha)
    {
        public static Resultado Erro(IResult falha) => new(null, 0, 0, falha);
    }

    private static async Task<Resultado> PeloGemini(ModeloDeIa modelo, byte[] corpo, OpcoesDoGemini gemini, IHttpClientFactory clientes, ILogger log,
        CancellationToken c)
    {
        var chave = gemini.Chave!;
        using var mensagem = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(gemini.Endereco), $"models/{modelo.Id}:generateContent"))
        {
            Content = new ByteArrayContent(corpo) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };
        mensagem.Headers.Add("x-goog-api-key", chave);
        if (await Enviar(clientes.CreateClient(ClienteDoGemini), mensagem, "Gemini", modelo, log, c) is not { } resposta)
            return Resultado.Erro(Falha(504, "O Gemini não respondeu. Tente de novo."));
        using (resposta)
        {
            var bytes = await resposta.Content.ReadAsByteArrayAsync(c);
            var status = (int)resposta.StatusCode;
            if (resposta.IsSuccessStatusCode)
            {
                var (entrada, saida) = TokensDoGemini(bytes, log, modelo.Id);
                return new Resultado(bytes, entrada, saida, null);
            }
            var texto = Encoding.UTF8.GetString(bytes).Replace(chave, "(chave)", StringComparison.Ordinal);
            // Um problema da chave do servidor não é do usuário: não passa adiante o que o Google disse.
            if (status is 401 or 403 or 404 || (status == 400 && texto.Contains("API_KEY", StringComparison.Ordinal)))
                return Resultado.Erro(Configuracao(log, "Gemini", status, modelo.Id));
            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
                return Resultado.Erro(Falha(503, "A IA está muito procurada agora. Espere um pouco e tente de novo."));
            if (status >= 500)
                return Resultado.Erro(Falha(502, "O Gemini está com problemas agora. Tente de novo em instantes."));
            return Resultado.Erro(Results.Text(texto, "application/json", statusCode: status)); // um pedido mal formado: o motivo é útil ao programa
        }
    }

    private static async Task<Resultado> PeloClaude(ModeloDeIa modelo, byte[] corpo, OpcoesDaAnthropic anthropic, IHttpClientFactory clientes, ILogger log,
        CancellationToken c)
    {
        JsonObject pedido;
        try
        {
            pedido = TradutorClaude.Pedido(JsonNode.Parse(corpo) ?? throw new JsonException(), modelo.Id, anthropic.MaximoDeTokens);
        }
        catch (PedidoIncompativelException ex)
        {
            return Resultado.Erro(Falha(400, ex.Message));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Resultado.Erro(Falha(400, "O pedido de IA veio num formato que o servidor não entendeu. Atualize o programa."));
        }
        var chave = anthropic.Chave!;
        using var mensagem = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(anthropic.Endereco), "messages"))
        {
            Content = new StringContent(pedido.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        mensagem.Headers.Add("x-api-key", chave);
        mensagem.Headers.Add("anthropic-version", anthropic.Versao);
        if (await Enviar(clientes.CreateClient(ClienteDaAnthropic), mensagem, "Claude", modelo, log, c) is not { } resposta)
            return Resultado.Erro(Falha(504, "O Claude não respondeu. Tente de novo."));
        using (resposta)
        {
            var texto = (await resposta.Content.ReadAsStringAsync(c)).Replace(chave, "(chave)", StringComparison.Ordinal);
            var status = (int)resposta.StatusCode;
            if (resposta.IsSuccessStatusCode)
            {
                try
                {
                    var (gemini, entrada, saida) = TradutorClaude.Resposta(JsonNode.Parse(texto) ?? throw new JsonException());
                    return new Resultado(Encoding.UTF8.GetBytes(gemini.ToJsonString(TradutorClaude.Json)), entrada, saida, null);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    log.LogWarning(ex, "Resposta do Claude sem JSON válido ({Modelo})", modelo.Id);
                    return Resultado.Erro(Falha(502, "O Claude mandou uma resposta que não deu para entender. Tente de novo."));
                }
            }
            if (status is 401 or 403 or 404)
                return Resultado.Erro(Configuracao(log, "Claude", status, modelo.Id));
            if (status is 429 or 529)
                return Resultado.Erro(Falha(503, "A IA está muito procurada agora. Espere um pouco e tente de novo."));
            if (status >= 500)
                return Resultado.Erro(Falha(502, "O Claude está com problemas agora. Tente de novo em instantes."));
            return Resultado.Erro(Falha(status, $"O Claude recusou o pedido: {TradutorClaude.MensagemDeErro(texto) ?? "pedido inválido"}"));
        }
    }

    private static async Task<HttpResponseMessage?> Enviar(HttpClient cliente, HttpRequestMessage mensagem, string servico, ModeloDeIa modelo, ILogger log,
        CancellationToken c)
    {
        try
        {
            return await cliente.SendAsync(mensagem, c);
        }
        catch (OperationCanceledException) when (c.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            log.LogWarning(ex, "Falha ao chamar o {Servico} ({Modelo})", servico, modelo.Id);
            return null;
        }
    }

    private static IResult Configuracao(ILogger log, string servico, int status, string modelo)
    {
        log.LogError("O {Servico} recusou a chave ou o modelo do servidor ({Status}, {Modelo})", servico, status, modelo);
        return Falha(502, "A IA do servidor está com um problema de configuração. Já registramos; tente mais tarde.");
    }

    // Lê o corpo até o limite (nulo se passar).
    private static async Task<byte[]?> LerAte(Stream corpo, long limite, CancellationToken c)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        int lidos;
        while ((lidos = await corpo.ReadAsync(buffer, c)) > 0)
        {
            if (memoria.Length + lidos > limite)
                return null;
            memoria.Write(buffer, 0, lidos);
        }
        return memoria.ToArray();
    }

    // Os tokens do usageMetadata do Gemini (a saída inclui os de raciocínio, cobrados como saída).
    private static (long Entrada, long Saida) TokensDoGemini(byte[] resposta, ILogger log, string modelo)
    {
        try
        {
            using var json = JsonDocument.Parse(resposta);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("usageMetadata", out var uso))
            {
                long entrada = Numero(uso, "promptTokenCount"), saida = Numero(uso, "candidatesTokenCount") + Numero(uso, "thoughtsTokenCount");
                if (entrada + saida == 0)
                    entrada = Numero(uso, "totalTokenCount");
                return (entrada, saida);
            }
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Resposta do Gemini sem JSON válido ({Modelo})", modelo);
        }
        return (0, 0);
    }

    private static long Numero(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
