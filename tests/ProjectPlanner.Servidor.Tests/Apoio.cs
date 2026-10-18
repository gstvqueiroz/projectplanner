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

public sealed record PedidoAoGemini(HttpMethod Metodo, Uri Endereco, string? Chave, bool TemAuthorization, string Corpo);

/// <summary>O Gemini de mentira: guarda os pedidos e responde o que o teste mandar.</summary>
public sealed class GeminiDeTeste : HttpMessageHandler
{
    public const string RespostaPadrao = """
        {"candidates":[{"content":{"parts":[{"text":"Olá!"}],"role":"model"}}],
         "usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":40,"thoughtsTokenCount":10,"totalTokenCount":150}}
        """;

    public ConcurrentQueue<PedidoAoGemini> Pedidos { get; } = new();
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string Resposta { get; set; } = RespostaPadrao;
    public Exception? Erro { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        Pedidos.Enqueue(new PedidoAoGemini(pedido.Method, pedido.RequestUri!,
            pedido.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null, pedido.Headers.Authorization is not null,
            pedido.Content is null ? "" : await pedido.Content.ReadAsStringAsync(cancelamento)));
        if (Erro is not null)
            throw Erro;
        return new HttpResponseMessage(Status) { Content = new StringContent(Resposta, Encoding.UTF8, "application/json") };
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

    public Servidor(Dictionary<string, string?>? config = null)
    {
        _config = new()
        {
            ["ConnectionStrings:Banco"] = string.IsNullOrWhiteSpace(Postgres) ? $"Data Source={_banco}" : $"{Postgres};Database=pp_teste_{Guid.NewGuid():N}",
            ["Jwt:Chave"] = "chave-do-jwt-de-teste-com-mais-de-32-bytes",
            ["Gemini:Chave"] = ChaveDoGemini,
            ["Admin:Chave"] = ChaveDeAdmin,
            ["Limites:PedidosDeContaPorMinuto"] = "1000",
            ["Limites:PedidosDeIaPorMinuto"] = "1000",
        };
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
            s.AddHttpClient(Ia.ClienteDoGemini).ConfigurePrimaryHttpMessageHandler(() => Gemini);
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
