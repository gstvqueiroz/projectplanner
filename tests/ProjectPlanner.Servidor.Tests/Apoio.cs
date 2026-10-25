using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

/// <summary>Um relógio que os testes fazem andar (códigos e sessões vencem, o mês vira).</summary>
public sealed class RelogioDeTeste(DateTimeOffset inicio) : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = inicio;
    public override DateTimeOffset GetUtcNow() => Agora;
    public void Andar(TimeSpan quanto) => Agora += quanto;
}

public sealed record EmailEnviado(string Para, string Assunto, string Texto)
{
    public string? Codigo => Regex.Match(Texto, @"\b\d{6}\b") is { Success: true } m ? m.Value : null;
}

public sealed class EmailsDeTeste : IEnviadorDeEmail
{
    public ConcurrentQueue<EmailEnviado> Enviados { get; } = new();
    public Task EnviarAsync(string para, string assunto, string texto, CancellationToken cancelamento)
    {
        Enviados.Enqueue(new EmailEnviado(para, assunto, texto));
        return Task.CompletedTask;
    }

    public EmailEnviado Ultimo => Enviados.Last();
}

public sealed class GoogleDeTeste : IValidadorDoGoogle
{
    public Dictionary<string, ContaGoogle> Tokens { get; } = [];
    public Task<ContaGoogle?> ValidarAsync(string idToken) => Task.FromResult(Tokens.GetValueOrDefault(idToken));
}

/// <summary>Bloco 45: o Google trocando o código do navegador pelo idToken (o código "bom-…" vira o idToken "token-…").</summary>
public sealed class TrocaDeTeste : ITrocaDoCodigoDoGoogle
{
    public List<(string Codigo, string Verificador, string Redirecionamento)> Trocas { get; } = [];

    public Task<string?> TrocarAsync(string codigo, string verificador, string redirecionamento, CancellationToken cancelamento)
    {
        Trocas.Add((codigo, verificador, redirecionamento));
        return Task.FromResult(codigo.StartsWith("bom-") ? "token-" + codigo["bom-".Length..] : null);
    }
}

/// <summary>Bloco 49: o Telegram de mentira: guarda o que o servidor mandou e entrega as fotos do teste.</summary>
public sealed class TelegramDeTeste : IClienteDoTelegram
{
    public ConcurrentQueue<(long Chat, string Texto)> Mensagens { get; } = new();
    public ConcurrentDictionary<string, byte[]> Arquivos { get; } = new();
    public ConcurrentQueue<(string Url, string Segredo)> Webhooks { get; } = new();
    public int Digitando;

    public Task EnviarAsync(long chat, string texto, CancellationToken c)
    {
        Mensagens.Enqueue((chat, texto));
        return Task.CompletedTask;
    }

    public Task DigitandoAsync(long chat, CancellationToken c)
    {
        Interlocked.Increment(ref Digitando);
        return Task.CompletedTask;
    }

    public Task<byte[]?> BaixarAsync(string arquivo, CancellationToken c) => Task.FromResult(Arquivos.GetValueOrDefault(arquivo));

    public Task RegistrarWebhookAsync(string url, string segredo, CancellationToken c)
    {
        Webhooks.Enqueue((url, segredo));
        return Task.CompletedTask;
    }

    public string Ultima => Mensagens.Last().Texto;
}

public sealed record PedidoAoGemini(HttpMethod Metodo, Uri Endereco, string? Chave, bool TemAuthorization, string Corpo);

/// <summary>O Gemini de mentira: guarda os pedidos e responde o que o teste mandar.</summary>
public sealed class GeminiDeTeste : HttpMessageHandler
{
    public const string RespostaPadrao = """
        {"candidates":[{"content":{"parts":[{"text":"Olá!"}],"role":"model"}}],
         "usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":40,"thoughtsTokenCount":10,"totalTokenCount":150}}
        """;

    public ConcurrentQueue<PedidoAoGemini> Pedidos { get; } = new();
    public ConcurrentQueue<string?> Versoes { get; } = new();
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string Resposta { get; set; } = RespostaPadrao;

    /// <summary>Respostas para os próximos pedidos, uma por pedido; vazia, vale <see cref="Resposta"/>.</summary>
    public ConcurrentQueue<string> Proximas { get; } = new();
    public Exception? Erro { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        Versoes.Enqueue(pedido.Headers.TryGetValues("anthropic-version", out var versao) ? versao.Single() : null);
        Pedidos.Enqueue(new PedidoAoGemini(pedido.Method, pedido.RequestUri!,
            pedido.Headers.TryGetValues("x-goog-api-key", out var v) || pedido.Headers.TryGetValues("x-api-key", out v) ? v.Single() : null,
            pedido.Headers.Authorization is not null,
            pedido.Content is null ? "" : await pedido.Content.ReadAsStringAsync(cancelamento)));
        if (Erro is not null)
            throw Erro;
        var resposta = Proximas.TryDequeue(out var proxima) ? proxima : Resposta;
        return new HttpResponseMessage(Status) { Content = new StringContent(resposta, Encoding.UTF8, "application/json") };
    }
}

public sealed record PedidoAoMercadoPago(HttpMethod Metodo, string Caminho, string? Token, string Corpo);

/// <summary>Bloco 47: o Mercado Pago de mentira: responde por "MÉTODO caminho" (sem a barra do começo) o que o teste mandar.</summary>
public sealed class MercadoPagoDeTeste : HttpMessageHandler
{
    public ConcurrentQueue<PedidoAoMercadoPago> Pedidos { get; } = new();
    public Dictionary<string, (HttpStatusCode Status, string Json)> Respostas { get; } = new()
    {
        ["POST preapproval"] = (HttpStatusCode.Created, """{"id":"assin-1","init_point":"https://www.mercadopago.com.br/subscriptions/checkout?preapproval_id=assin-1","status":"pending"}"""),
        ["POST checkout/preferences"] = (HttpStatusCode.Created,
            """{"id":"pref-1","init_point":"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=pref-1","sandbox_init_point":"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id=pref-1"}"""),
    };

    public PedidoAoMercadoPago Ultimo => Pedidos.Last();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        var caminho = pedido.RequestUri!.AbsolutePath.TrimStart('/');
        Pedidos.Enqueue(new PedidoAoMercadoPago(pedido.Method, caminho, pedido.Headers.Authorization?.Parameter,
            pedido.Content is null ? "" : await pedido.Content.ReadAsStringAsync(cancelamento)));
        var (status, json) = Respostas.TryGetValue($"{pedido.Method} {caminho}", out var r) ? r : (HttpStatusCode.NotFound, """{"message":"not found"}""");
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

/// <summary>
/// O servidor de verdade, em memória (TestServer), com SQLite num arquivo temporário e os serviços externos falsos. Com a
/// variável PP_SERVIDOR_POSTGRES (por exemplo "Host=/tmp;Username=eu"), cada teste roda num banco PostgreSQL temporário
/// (criado pelas migrações e apagado no fim).
/// </summary>
public sealed class Servidor : WebApplicationFactory<Program>
{
    public const string ChaveDoGemini = "chave-do-gemini-do-servidor-0123456789";
    public const string ChaveDeAdmin = "chave-de-admin-de-teste-0123456789";

    private static readonly string? Postgres = Environment.GetEnvironmentVariable("PP_SERVIDOR_POSTGRES");
    private readonly string _banco = Path.Combine(Path.GetTempPath(), $"pp-servidor-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _config;

    public RelogioDeTeste Relogio { get; } = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    public EmailsDeTeste Emails { get; } = new();
    public GoogleDeTeste Google { get; } = new();
    public GeminiDeTeste Gemini { get; } = new();
    public GeminiDeTeste Claude { get; } = new() { Resposta = ClaudeDeTeste };
    public MercadoPagoDeTeste MercadoPago { get; } = new();
    public const string TokenDoMercadoPago = "TEST-token-do-mercado-pago";
    public const string CompradorDeTeste = "comprador@testuser.com";
    public const string ChaveDaAnthropic = "chave-da-anthropic-do-servidor-0123456789";

    /// <summary>Bloco 46: a resposta do Claude de mentira (a ferramenta "responder" com o JSON pedido).</summary>
    public const string ClaudeDeTeste = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
         "content":[{"type":"tool_use","id":"t1","name":"responder","input":{"mensagem":"Olá do Claude","alteracoes":[]}}],
         "stop_reason":"tool_use","usage":{"input_tokens":1000,"output_tokens":200,"cache_read_input_tokens":500}}
        """;
    public TrocaDeTeste Troca { get; } = new();
    public TelegramDeTeste Telegram { get; } = new();
    public const string SegredoDoTelegram = "segredo-do-webhook-de-teste";

    /// <summary>
    /// O catálogo dos testes antigos (Flash, 3.1 Pro e Opus, com o Gemini falso no Padrão). Com <paramref name="catalogoDoCodigo"/>,
    /// vale o do código (Haiku, 3.1 Pro e Sonnet, desde a 0.47.2).
    /// </summary>
    public Servidor(Dictionary<string, string?>? config = null, bool catalogoDoCodigo = false)
    {
        _config = new()
        {
            ["ConnectionStrings:Banco"] = string.IsNullOrWhiteSpace(Postgres) ? $"Data Source={_banco}" : $"{Postgres};Database=pp_teste_{Guid.NewGuid():N}",
            ["Jwt:Chave"] = "chave-do-jwt-de-teste-com-mais-de-32-bytes",
            ["Gemini:Chave"] = ChaveDoGemini,
            ["Anthropic:Chave"] = ChaveDaAnthropic,
            ["Admin:Chave"] = ChaveDeAdmin,
            ["Telegram:Token"] = "123:token-de-teste",
            ["Telegram:Segredo"] = SegredoDoTelegram,
            ["Telegram:NomeDoBot"] = "ProjectPlannerTesteBot",
            ["MercadoPago:AccessToken"] = TokenDoMercadoPago,
            ["MercadoPago:UrlPublica"] = "https://servidor.teste/",
            ["MercadoPago:EmailDoCompradorDeTeste"] = CompradorDeTeste,
            ["Limites:PedidosDeContaPorMinuto"] = "1000",
            ["Limites:PedidosDeIaPorMinuto"] = "1000",
        };
        if (!catalogoDoCodigo && !(config ?? []).Keys.Any(k => k.StartsWith("Modelos:", StringComparison.Ordinal)))
        {
            string[][] modelos =
            [
                ["gemini-2.5-flash", "padrao", "gemini", "Gemini 2.5 Flash", "0.30", "2.50"],
                ["gemini-3.1-pro-preview", "avancado", "gemini", "Gemini 3.1 Pro", "2.00", "12.00"],
                ["claude-opus-5-5", "maximo", "anthropic", "Claude Opus 5.5", "4.00", "20.00"],
            ];
            for (var i = 0; i < modelos.Length; i++)
            {
                _config[$"Modelos:{i}:Id"] = modelos[i][0];
                _config[$"Modelos:{i}:Nivel"] = modelos[i][1];
                _config[$"Modelos:{i}:Provedor"] = modelos[i][2];
                _config[$"Modelos:{i}:Nome"] = modelos[i][3];
                _config[$"Modelos:{i}:DolaresPorMilhaoDeEntrada"] = modelos[i][4];
                _config[$"Modelos:{i}:DolaresPorMilhaoDeSaida"] = modelos[i][5];
            }
        }
        foreach (var (k, v) in config ?? [])
            _config[k] = v;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var (k, v) in _config)
            builder.UseSetting(k, v);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<TimeProvider>(Relogio);
            s.AddSingleton<IEnviadorDeEmail>(Emails);
            s.AddSingleton<IValidadorDoGoogle>(Google);
            s.AddSingleton<ITrocaDoCodigoDoGoogle>(Troca);
            s.AddSingleton<IClienteDoTelegram>(Telegram);
            s.AddHttpClient(MedidorDeTokens.Cliente).ConfigurePrimaryHttpMessageHandler(() => Gemini);
            s.AddHttpClient(Ia.ClienteDoGemini).ConfigurePrimaryHttpMessageHandler(() => Gemini);
            s.AddHttpClient(Ia.ClienteDaAnthropic).ConfigurePrimaryHttpMessageHandler(() => Claude);
            s.AddHttpClient(ProjectPlanner.Servidor.Servicos.MercadoPago.Cliente).ConfigurePrimaryHttpMessageHandler(() => MercadoPago);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (!string.IsNullOrWhiteSpace(Postgres))
        {
            try
            {
                NoBanco(b => b.Database.EnsureDeleted());
            }
            catch (InvalidOperationException) // o servidor nem chegou a subir
            {
            }
            Npgsql.NpgsqlConnection.ClearAllPools();
        }
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _banco, _banco + "-wal", _banco + "-shm" })
            File.Delete(f);
    }

    public T NoBanco<T>(Func<BancoDoServidor, T> consulta)
    {
        using var escopo = Services.CreateScope();
        return consulta(escopo.ServiceProvider.GetRequiredService<BancoDoServidor>());
    }

    // ---- atalhos dos fluxos ----

    public Task<HttpResponseMessage> Post(string rota, object corpo, string? acesso = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, rota) { Content = JsonContent.Create(corpo) };
        if (acesso is not null)
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        return CreateClient().SendAsync(pedido);
    }

    public Task<HttpResponseMessage> Get(string rota, string? acesso = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, rota);
        if (acesso is not null)
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        return CreateClient().SendAsync(pedido);
    }

    /// <summary>Cadastra e confirma uma conta; devolve a sessão.</summary>
    public async Task<Sessao> CriarConta(string email = "ana@exemplo.com", string senha = "senha-boa-123", string nome = "Ana")
    {
        (await Post("/v1/contas", new { email, senha, nome })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var resposta = await Post("/v1/contas/confirmar", new { email, codigo = Emails.Ultimo.Codigo, senha });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<Sessao>())!;
    }

    public static async Task<string?> Erro(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.TryGetProperty("erro", out var e) ? e.GetString() : null;
}
