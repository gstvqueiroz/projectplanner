using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

/// <summary>
/// Bloco 44: o ponto de IA. Espelha o caminho do Gemini (<c>models/{modelo}:generateContent</c>), para o app reaproveitar as
/// mesmas classes trocando só o endereço e o cabeçalho. Confere o login, o modelo, o tamanho e a cota; chama o Gemini com a
/// chave do servidor; registra os tokens gastos (nunca o conteúdo) e devolve a resposta como veio.
/// </summary>
public static class Ia
{
    public const string ClienteDoGemini = "gemini";
    public const string CotaEsgotada = "A cota de IA do seu plano acabou neste mês. Ela volta no dia 1º (ou assine o plano Profissional).";
    public const string SemChave = "A IA do servidor ainda não está configurada. Tente mais tarde.";

    public static void Mapear(WebApplication app)
    {
        var ia = app.MapGroup("/v1/ia").RequireAuthorization().RequireRateLimiting(LimitesDeUso.Ia);
        ia.MapPost("/models/{modelo}:generateContent", Gerar);
        ia.MapGet("/models/{modelo}", Testar);
    }

    private static IResult Falha(int status, string mensagem) => Contas.Falha(status, mensagem);

    // O que vale para os dois pontos: a conta, o modelo e a chave do servidor.
    private static async Task<(Usuario? Usuario, IResult? Falha)> Conferir(ClaimsPrincipal quem, string modelo, BancoDoServidor banco, OpcoesDoGemini gemini)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return (null, Falha(401, "Sessão vencida: entre de novo."));
        if (!gemini.Liberados.Contains(modelo, StringComparer.Ordinal))
            return (null, Falha(400, $"O modelo \"{modelo}\" não está liberado. Use um destes: {string.Join(", ", gemini.Liberados)}."));
        if (string.IsNullOrWhiteSpace(gemini.Chave))
            return (null, Falha(503, SemChave));
        return (usuario, null);
    }

    /// <summary>Testar a conexão (o app chama ao salvar as configurações): não vai ao Gemini nem gasta cota.</summary>
    private static async Task<IResult> Testar(string modelo, ClaimsPrincipal quem, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes)
    {
        var (_, falha) = await Conferir(quem, modelo, banco, opcoes.Value.Gemini);
        return falha ?? Results.Ok(new { name = "models/" + modelo });
    }

    private static async Task<IResult> Gerar(string modelo, HttpRequest pedido, ClaimsPrincipal quem, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes,
        IHttpClientFactory clientes, TimeProvider relogio, ILoggerFactory logs, CancellationToken c)
    {
        var config = opcoes.Value;
        var (usuario, falha) = await Conferir(quem, modelo, banco, config.Gemini);
        if (falha is not null)
            return falha;
        if (await Cotas.UsadosNoMes(banco, usuario!.Id, relogio) >= config.Planos.TokensPorMes(usuario.Plano))
            return Falha(402, CotaEsgotada);

        var limite = config.Limites.MegabytesPorPedido * 1024L * 1024L;
        if (pedido.ContentLength > limite)
            return Falha(413, $"O pedido passa de {config.Limites.MegabytesPorPedido} MB. Mande menos fotos ou anexos menores.");
        var corpo = await LerAte(pedido.Body, limite, c);
        if (corpo is null)
            return Falha(413, $"O pedido passa de {config.Limites.MegabytesPorPedido} MB. Mande menos fotos ou anexos menores.");

        var log = logs.CreateLogger(typeof(Ia));
        var chave = config.Gemini.Chave!;
        using var mensagem = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(config.Gemini.Endereco), $"models/{modelo}:generateContent"))
        {
            Content = new ByteArrayContent(corpo) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };
        mensagem.Headers.Add("x-goog-api-key", chave);
        HttpResponseMessage resposta;
        try
        {
            resposta = await clientes.CreateClient(ClienteDoGemini).SendAsync(mensagem, c);
        }
        catch (OperationCanceledException) when (c.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            log.LogWarning(ex, "Falha ao chamar o Gemini ({Modelo})", modelo);
            return Falha(504, "O Gemini não respondeu. Tente de novo.");
        }

        using (resposta)
        {
            var bytes = await resposta.Content.ReadAsByteArrayAsync(c);
            var status = (int)resposta.StatusCode;
            if (resposta.IsSuccessStatusCode)
            {
                await Registrar(banco, usuario, modelo, bytes, relogio, log, c);
                return Results.Bytes(bytes, "application/json");
            }
            var texto = System.Text.Encoding.UTF8.GetString(bytes).Replace(chave, "(chave)", StringComparison.Ordinal);
            // Um problema da chave do servidor não é do usuário: não passa adiante o que o Google disse.
            if (status is 401 or 403 or 404 || (status == 400 && texto.Contains("API_KEY", StringComparison.Ordinal)))
            {
                log.LogError("O Gemini recusou a chave ou o modelo do servidor ({Status}, {Modelo})", status, modelo);
                return Falha(502, "A IA do servidor está com um problema de configuração. Já registramos; tente mais tarde.");
            }
            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
                return Falha(503, "A IA está muito procurada agora. Espere um pouco e tente de novo.");
            if (status >= 500)
                return Falha(502, "O Gemini está com problemas agora. Tente de novo em instantes.");
            return Results.Text(texto, "application/json", statusCode: status); // um pedido mal formado: o motivo é útil ao app
        }
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

    // Os tokens do usageMetadata (sem ele, nada a registrar).
    private static async Task Registrar(BancoDoServidor banco, Usuario usuario, string modelo, byte[] resposta, TimeProvider relogio, ILogger log,
        CancellationToken c)
    {
        int entrada = 0, saida = 0, total = 0;
        try
        {
            using var json = JsonDocument.Parse(resposta);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("usageMetadata", out var uso))
            {
                entrada = Numero(uso, "promptTokenCount");
                saida = Numero(uso, "candidatesTokenCount") + Numero(uso, "thoughtsTokenCount");
                total = Numero(uso, "totalTokenCount") is > 0 and var t ? t : entrada + saida;
            }
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Resposta do Gemini sem JSON válido ({Modelo})", modelo);
        }
        banco.UsosDeIa.Add(new UsoDeIa
        {
            UsuarioId = usuario.Id, Quando = relogio.GetUtcNow().UtcDateTime, Modelo = modelo, TokensDeEntrada = entrada, TokensDeSaida = saida,
            TokensTotais = total,
        });
        await banco.SaveChangesAsync(c);
    }

    private static int Numero(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
