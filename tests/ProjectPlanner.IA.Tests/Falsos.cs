using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace ProjectPlanner.IA.Tests;

/// <summary>O "servidor" do Gemini nos testes: guarda os pedidos e responde o que o teste mandar (Bloco 23).</summary>
internal sealed class ManipuladorFalso : HttpMessageHandler
{
    public List<(HttpRequestMessage Pedido, string Corpo)> Pedidos { get; } = [];

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        (_, _) => Task.FromResult(Json(200, Resposta("Olá!")));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        var corpo = pedido.Content is null ? "" : await pedido.Content.ReadAsStringAsync(cancelamento);
        Pedidos.Add((pedido, corpo));
        return await Responder(pedido, cancelamento);
    }

    /// <summary>O corpo JSON do último pedido.</summary>
    public JsonNode UltimoCorpo => JsonNode.Parse(Pedidos[^1].Corpo)!;

    public static HttpResponseMessage Json(int status, string json) =>
        new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static string Resposta(string texto, string fim = "STOP") => new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["content"] = new JsonObject { ["role"] = "model", ["parts"] = new JsonArray(new JsonObject { ["text"] = texto }) },
            ["finishReason"] = fim,
        }),
    }.ToJsonString();

    public static string Erro(int codigo, string mensagem, string? motivo = null) => new JsonObject
    {
        ["error"] = new JsonObject
        {
            ["code"] = codigo,
            ["message"] = mensagem,
            ["status"] = "INVALID_ARGUMENT",
            ["details"] = motivo is null ? new JsonArray() : new JsonArray(new JsonObject { ["reason"] = motivo }),
        },
    }.ToJsonString();
}

/// <summary>Uma configuração que o teste muda à vontade.</summary>
internal sealed class ConfiguracaoFixa(string? chave = null, string modelo = "gemini-2.5-flash") : IConfiguracaoIA
{
    public string? ChaveApi { get; set; } = chave;

    public string Modelo { get; set; } = modelo;
}

/// <summary>Um provedor sem rede: cria assistentes que respondem com a chave e o modelo usados.</summary>
internal sealed class ProvedorDeTeste : IProvedorIA
{
    public List<(string Chave, string Modelo)> Criados { get; } = [];

    public string Nome => "Teste";

    public string ModeloPadrao => "modelo-padrao";

    public string NormalizarModelo(string modelo) => modelo.Trim();

    public IAssistenteIA CriarAssistente(string chave, string modelo)
    {
        Criados.Add((chave, modelo));
        return new AssistenteDeTeste(chave, modelo);
    }

    public List<(string Chave, string Modelo)> AnalistasCriados { get; } = [];

    public IAnalistaDeFotos CriarAnalista(string chave, string modelo)
    {
        AnalistasCriados.Add((chave, modelo));
        return new AnalistaIndisponivel();
    }

    public Task TestarAsync(string chave, string modelo, CancellationToken cancelamento) => Task.CompletedTask;

    private sealed class AssistenteDeTeste(string chave, string modelo) : IAssistenteIA
    {
        public bool EstaConfigurado => true;

        public Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento) =>
            Task.FromResult(new RespostaIA($"{chave}/{modelo}: {pedido.Mensagem}"));
    }
}
