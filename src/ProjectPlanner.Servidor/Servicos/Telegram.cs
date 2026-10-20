using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Bloco 49: o que o servidor pede ao Telegram (mandar mensagem, "digitando…", baixar foto, registrar o webhook).</summary>
public interface IClienteDoTelegram
{
    Task EnviarAsync(long chat, string texto, CancellationToken c);
    Task DigitandoAsync(long chat, CancellationToken c);

    /// <returns>Os bytes da foto; nulo se o Telegram não entregou.</returns>
    Task<byte[]?> BaixarAsync(string arquivo, CancellationToken c);

    Task RegistrarWebhookAsync(string url, string segredo, CancellationToken c);
}

/// <summary>A API de bots do Telegram (https://api.telegram.org/bot&lt;token&gt;/…).</summary>
public sealed class ClienteDoTelegram(IHttpClientFactory clientes, IOptions<OpcoesDoServidor> opcoes, ILogger<ClienteDoTelegram> log) : IClienteDoTelegram
{
    public const string Cliente = "telegram";

    /// <summary>O Telegram aceita até 4.096 caracteres por mensagem.</summary>
    public const int TamanhoMaximo = 4000;

    private OpcoesDoTelegram Config => opcoes.Value.Telegram;

    private Uri Metodo(string nome) => new($"{Config.Endereco.TrimEnd('/')}/bot{Config.Token}/{nome}");

    public async Task EnviarAsync(long chat, string texto, CancellationToken c)
    {
        var mensagem = texto.Length > TamanhoMaximo ? texto[..TamanhoMaximo] + "…" : texto;
        using var r = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("sendMessage"), new { chat_id = chat, text = mensagem }, c);
        if (!r.IsSuccessStatusCode)
            log.LogWarning("O Telegram recusou a mensagem ({Status})", (int)r.StatusCode);
    }

    public async Task DigitandoAsync(long chat, CancellationToken c)
    {
        using var _ = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("sendChatAction"), new { chat_id = chat, action = "typing" }, c);
    }

    public async Task<byte[]?> BaixarAsync(string arquivo, CancellationToken c)
    {
        var http = clientes.CreateClient(Cliente);
        using var r = await http.PostAsJsonAsync(Metodo("getFile"), new { file_id = arquivo }, c);
        if (!r.IsSuccessStatusCode)
            return null;
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(c));
        if (!json.RootElement.TryGetProperty("result", out var resultado) || !resultado.TryGetProperty("file_path", out var caminho))
            return null;
        using var arquivoBaixado = await http.GetAsync(new Uri($"{Config.Endereco.TrimEnd('/')}/file/bot{Config.Token}/{caminho.GetString()}"), c);
        return arquivoBaixado.IsSuccessStatusCode ? await arquivoBaixado.Content.ReadAsByteArrayAsync(c) : null;
    }

    public async Task RegistrarWebhookAsync(string url, string segredo, CancellationToken c)
    {
        using var r = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("setWebhook"),
            new { url, secret_token = segredo, allowed_updates = new[] { "message" }, drop_pending_updates = false }, c);
        log.LogInformation("Webhook do Telegram registrado: {Status}", (int)r.StatusCode);
    }
}

/// <summary>Bloco 49: os avisos do Telegram esperando o atendente (o webhook responde na hora; a IA pode levar um minuto).</summary>
public sealed class FilaDoTelegram
{
    private readonly Channel<JsonElement> _fila = Channel.CreateBounded<JsonElement>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public bool Colocar(JsonElement aviso) => _fila.Writer.TryWrite(aviso.Clone());

    public IAsyncEnumerable<JsonElement> Ler(CancellationToken c) => _fila.Reader.ReadAllAsync(c);
}

/// <summary>Atende os avisos da fila, um por vez, cada um com o seu escopo (banco).</summary>
public sealed class ProcessadorDoTelegram(FilaDoTelegram fila, IServiceScopeFactory escopos, ILogger<ProcessadorDoTelegram> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        await foreach (var aviso in fila.Ler(parar))
        {
            try
            {
                using var escopo = escopos.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>().AtenderAsync(aviso, parar);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !parar.IsCancellationRequested)
            {
                log.LogError(ex, "Falha ao atender um aviso do Telegram");
            }
        }
    }
}

/// <summary>Ao subir: registra o webhook no Telegram (se o token, o segredo e o endereço público estão configurados).</summary>
public sealed class RegistroDoWebhook(IOptions<OpcoesDoServidor> opcoes, IClienteDoTelegram telegram, ILogger<RegistroDoWebhook> log) : IHostedService
{
    public async Task StartAsync(CancellationToken c)
    {
        var config = opcoes.Value.Telegram;
        if (!config.Ligado || string.IsNullOrWhiteSpace(config.UrlPublica))
            return;
        try
        {
            await telegram.RegistrarWebhookAsync($"{config.UrlPublica.TrimEnd('/')}/v1/telegram/webhook", config.Segredo!, c);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Não deu para registrar o webhook do Telegram agora");
        }
    }

    public Task StopAsync(CancellationToken c) => Task.CompletedTask;
}

/// <summary>
/// Bloco 49: mede os tokens das respostas do Gemini que passam por ele (o assistente do programa, reaproveitado no Telegram,
/// não devolve o uso). Cada atendimento abre uma <see cref="Medicao"/>, e o que passar pelo cliente "gemini-medido" soma nela.
/// </summary>
public sealed class MedidorDeTokens : DelegatingHandler
{
    public const string Cliente = "gemini-medido";

    private static readonly AsyncLocal<Medicao?> Atual = new();

    public sealed class Medicao : IDisposable
    {
        public long Entrada { get; internal set; }
        public long Saida { get; internal set; }

        public void Dispose() => Atual.Value = null;
    }

    public static Medicao Abrir() => Atual.Value = new Medicao();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken c)
    {
        var resposta = await base.SendAsync(pedido, c);
        if (Atual.Value is { } medicao && resposta.IsSuccessStatusCode)
        {
            var bytes = await resposta.Content.ReadAsByteArrayAsync(c);
            try
            {
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.TryGetProperty("usageMetadata", out var uso))
                {
                    long N(string nome) => uso.TryGetProperty(nome, out var v) && v.TryGetInt64(out var n) ? n : 0;
                    medicao.Entrada += N("promptTokenCount");
                    medicao.Saida += N("candidatesTokenCount") + N("thoughtsTokenCount");
                }
            }
            catch (JsonException)
            {
            }
            var conteudo = new ByteArrayContent(bytes);
            foreach (var cabecalho in resposta.Content.Headers)
                conteudo.Headers.TryAddWithoutValidation(cabecalho.Key, cabecalho.Value);
            resposta.Content = conteudo;
        }
        return resposta;
    }
}
