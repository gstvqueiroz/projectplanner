using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Bloco 49: o que o servidor pede ao Telegram (mandar mensagem, "digitando…", baixar foto, registrar o webhook).</summary>
public interface IClienteDoTelegram
{
    Task EnviarAsync(long chat, string texto, CancellationToken c);
    Task DigitandoAsync(long chat, CancellationToken c);

    /// <returns>Os bytes da foto; nulo se o Telegram não entregou.</returns>
    Task<byte[]?> BaixarAsync(string arquivo, CancellationToken c);

    Task RegistrarWebhookAsync(string url, string segredo, CancellationToken c);

    /// <summary>Bloco 59: manda com botões embaixo (numa linha); devolve o id da mensagem, ou nulo se o Telegram recusou.</summary>
    Task<long?> EnviarComBotoesAsync(long chat, string texto, IReadOnlyList<BotaoDoTelegram> botoes, CancellationToken c);

    /// <summary>Bloco 59: troca o texto e os botões de uma mensagem já mandada (sem botões, eles somem).</summary>
    Task EditarAsync(long chat, long mensagem, string texto, IReadOnlyList<BotaoDoTelegram> botoes, CancellationToken c);

    /// <summary>Bloco 83: tira os botões de uma mensagem já mandada (o texto fica).</summary>
    Task TirarBotoesAsync(long chat, long mensagem, CancellationToken c);

    /// <summary>Bloco 86: manda respondendo a uma mensagem (se ela não existe mais, manda assim mesmo).</summary>
    Task EnviarRespondendoAsync(long chat, long mensagem, string texto, CancellationToken c);

    /// <summary>Bloco 59: responde ao toque num botão (o aviso rápido no alto da tela de quem tocou).</summary>
    Task ResponderAoBotaoAsync(string consulta, string texto, CancellationToken c);

    /// <summary>
    /// Bloco 83: o menu de comandos do bot (o que aparece ao digitar "/" e no botão Menu ao lado do campo de mensagem), para um
    /// tipo de chat: <see cref="EscoposDosComandos.Privados"/> ou <see cref="EscoposDosComandos.Grupos"/>.
    /// </summary>
    Task DefinirComandosAsync(IReadOnlyList<ComandoDoBot> comandos, string escopo, CancellationToken c);
}

/// <summary>Bloco 59: um botão embaixo de uma mensagem; os dados voltam no toque (até 64 bytes). Bloco 83: a linha do teclado.</summary>
public sealed record BotaoDoTelegram(string Texto, string Dados, int Linha = 0);

/// <summary>Bloco 83: um comando do menu do bot ("resumo", "a obra em números").</summary>
public sealed record ComandoDoBot(string Comando, string Descricao);

/// <summary>Bloco 83: os escopos do menu de comandos do Telegram (BotCommandScope).</summary>
public static class EscoposDosComandos
{
    public const string Privados = "all_private_chats";
    public const string Grupos = "all_group_chats";
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

    public async Task<long?> EnviarComBotoesAsync(long chat, string texto, IReadOnlyList<BotaoDoTelegram> botoes, CancellationToken c)
    {
        using var r = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("sendMessage"),
            new { chat_id = chat, text = Cortar(texto), reply_markup = Teclado(botoes) }, c);
        if (!r.IsSuccessStatusCode)
        {
            log.LogWarning("O Telegram recusou a mensagem com botões ({Status})", (int)r.StatusCode);
            return null;
        }
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(c));
        return json.RootElement.TryGetProperty("result", out var resultado) && resultado.TryGetProperty("message_id", out var id) && id.TryGetInt64(out var n)
            ? n : null;
    }

    public async Task EditarAsync(long chat, long mensagem, string texto, IReadOnlyList<BotaoDoTelegram> botoes, CancellationToken c)
    {
        using var r = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("editMessageText"),
            new { chat_id = chat, message_id = mensagem, text = Cortar(texto), reply_markup = Teclado(botoes) }, c);
        if (!r.IsSuccessStatusCode) // inclusive "message is not modified", quando nada mudou
            log.LogInformation("O Telegram não editou a mensagem ({Status})", (int)r.StatusCode);
    }

    public async Task EnviarRespondendoAsync(long chat, long mensagem, string texto, CancellationToken c)
    {
        using var r = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("sendMessage"),
            new { chat_id = chat, text = Cortar(texto), reply_parameters = new { message_id = mensagem, allow_sending_without_reply = true } }, c);
        if (!r.IsSuccessStatusCode)
            log.LogWarning("O Telegram recusou a resposta ({Status})", (int)r.StatusCode);
    }

    public async Task TirarBotoesAsync(long chat, long mensagem, CancellationToken c)
    {
        using var r = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("editMessageReplyMarkup"),
            new { chat_id = chat, message_id = mensagem, reply_markup = Teclado([]) }, c);
        if (!r.IsSuccessStatusCode)
            log.LogInformation("O Telegram não tirou os botões da mensagem ({Status})", (int)r.StatusCode);
    }

    public async Task ResponderAoBotaoAsync(string consulta, string texto, CancellationToken c)
    {
        using var _ = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("answerCallbackQuery"),
            new { callback_query_id = consulta, text = texto.Length > 190 ? texto[..190] : texto }, c);
    }

    public async Task DefinirComandosAsync(IReadOnlyList<ComandoDoBot> comandos, string escopo, CancellationToken c)
    {
        using var r = await clientes.CreateClient(Cliente).PostAsJsonAsync(Metodo("setMyCommands"),
            new { commands = comandos.Select(k => new { command = k.Comando, description = k.Descricao }).ToArray(), scope = new { type = escopo } }, c);
        if (!r.IsSuccessStatusCode)
            log.LogWarning("O Telegram recusou o menu de comandos ({Status}): {Motivo}", (int)r.StatusCode, await r.Content.ReadAsStringAsync(c));
    }

    /// <summary>Os botões em linhas (pela <see cref="BotaoDoTelegram.Linha"/>, na ordem); sem botões, o teclado some.</summary>
    internal static object Teclado(IReadOnlyList<BotaoDoTelegram> botoes) =>
        new { inline_keyboard = botoes.GroupBy(b => b.Linha).OrderBy(g => g.Key).Select(g => g.Select(b => new { text = b.Texto, callback_data = b.Dados }).ToArray()).ToArray() };

    private static string Cortar(string texto) => texto.Length > TamanhoMaximo ? texto[..TamanhoMaximo] + "…" : texto;

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
            new { url, secret_token = segredo, allowed_updates = new[] { "message", "callback_query" }, drop_pending_updates = false }, c);
        if (r.IsSuccessStatusCode)
            log.LogInformation("Webhook do Telegram registrado em {Url}", url);
        else // o motivo vem no corpo (o token não aparece nele)
            log.LogError("O Telegram recusou o webhook ({Status}): {Motivo}", (int)r.StatusCode, await r.Content.ReadAsStringAsync(c));
    }
}

/// <summary>
/// Bloco 49: os avisos do Telegram esperando o atendente (o webhook responde na hora; a IA pode levar um minuto). Bloco 69: a
/// fila fica no banco (<see cref="AvisoDoTelegram"/>): um deploy, uma reinicialização ou o Render "dormindo" não perde mais o
/// que estava esperando; ao subir, o processador atende o que ficou.
/// </summary>
public sealed class FilaDoTelegram
{
    /// <summary>Um atendimento que caiu no meio (o servidor reiniciou) tenta mais uma vez; depois sai da fila.</summary>
    public const int MaximoDeTentativas = 2;

    /// <summary>Um aviso mais velho que isso não é mais atendido (a conversa já passou) e sai do banco (o atendido também).</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim _sinal = new(0);

    /// <summary>Guarda o aviso e acorda o processador. Um que o Telegram mandou de novo (o mesmo update_id) não entra duas vezes.</summary>
    /// <returns>Se entrou na fila.</returns>
    public async Task<bool> GuardarAsync(BancoDoServidor banco, JsonElement aviso, DateTime agora, CancellationToken c)
    {
        long? numero = aviso.ValueKind == JsonValueKind.Object && aviso.TryGetProperty("update_id", out var u) && u.TryGetInt64(out var n) ? n : null;
        if (numero is { } id && await banco.AvisosDoTelegram.AnyAsync(a => a.UpdateId == id, c))
            return false;
        var novo = new AvisoDoTelegram { UpdateId = numero, Json = aviso.GetRawText(), Recebido = agora };
        banco.AvisosDoTelegram.Add(novo);
        try
        {
            await banco.SaveChangesAsync(c);
        }
        catch (DbUpdateException) when (numero is not null)
        {
            banco.Entry(novo).State = EntityState.Detached; // o mesmo aviso chegou duas vezes ao mesmo tempo
            return false;
        }
        _sinal.Release();
        return true;
    }

    /// <summary>Espera um aviso novo (ou o limite).</summary>
    public async Task EsperarAsync(TimeSpan limite, CancellationToken c) => await _sinal.WaitAsync(limite, c);
}

/// <summary>Atende os avisos da fila, um por vez e na ordem de chegada, cada um com o seu escopo (banco).</summary>
public sealed class ProcessadorDoTelegram(FilaDoTelegram fila, IServiceScopeFactory escopos, TimeProvider relogio, ILogger<ProcessadorDoTelegram> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        while (!parar.IsCancellationRequested)
        {
            try
            {
                await AtenderOsGuardadosAsync(parar);
                // Sem consultar o banco de tempos em tempos (o Neon dormiria menos e gastaria as horas do plano): só quando chega um aviso.
                await fila.EsperarAsync(Timeout.InfiniteTimeSpan, parar);
            }
            catch (OperationCanceledException) when (parar.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha ao ler a fila do Telegram");
                await Task.Delay(TimeSpan.FromSeconds(5), parar);
            }
        }
    }

    // Um atendimento de cada vez: duas chamadas ao mesmo tempo (a rotina do servidor e outra) pegariam o mesmo aviso e
    // responderiam duas vezes.
    private readonly SemaphoreSlim _umDeCadaVez = new(1, 1);

    /// <summary>Atende tudo o que está na fila (antes, os vencidos saem do banco e os que já caíram duas vezes saem da fila).</summary>
    public async Task AtenderOsGuardadosAsync(CancellationToken parar)
    {
        await _umDeCadaVez.WaitAsync(parar);
        try
        {
            await AtenderAsync(parar);
        }
        finally
        {
            _umDeCadaVez.Release();
        }
    }

    private async Task AtenderAsync(CancellationToken parar)
    {
        while (true)
        {
            AvisoDoTelegram? aviso;
            using (var escopo = escopos.CreateScope())
            {
                var banco = escopo.ServiceProvider.GetRequiredService<BancoDoServidor>();
                var agora = relogio.GetUtcNow().UtcDateTime;
                var limite = agora - FilaDoTelegram.Validade;
                var vencidos = await banco.AvisosDoTelegram.Where(a => a.Recebido < limite).ToListAsync(parar);
                var desistidos = await banco.AvisosDoTelegram.Where(a => a.Atendido == null && a.Recebido >= limite && a.Tentativas >= FilaDoTelegram.MaximoDeTentativas)
                    .ToListAsync(parar);
                if (vencidos.Count(a => a.Atendido is null) + desistidos.Count is var semAtendimento and > 0)
                    log.LogWarning("{Quantos} aviso(s) do Telegram saíram da fila sem atendimento (vencidos ou que caíram duas vezes)", semAtendimento);
                banco.AvisosDoTelegram.RemoveRange(vencidos);
                foreach (var a in desistidos)
                    a.Atendido = agora;
                aviso = await banco.AvisosDoTelegram.Where(a => a.Atendido == null && a.Recebido >= limite).OrderBy(a => a.Id).FirstOrDefaultAsync(parar);
                if (aviso is not null)
                    aviso.Tentativas++;
                await banco.SaveChangesAsync(parar);
            }
            if (aviso is null)
                return;
            try
            {
                using var escopo = escopos.CreateScope();
                using var json = JsonDocument.Parse(aviso.Json);
                await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>().AtenderAsync(json.RootElement, parar);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !parar.IsCancellationRequested)
            {
                // Uma falha no atendimento não se repete (seria responder duas vezes): o aviso sai da fila.
                log.LogError(ex, "Falha ao atender um aviso do Telegram");
            }
            using (var escopo = escopos.CreateScope())
            {
                // Fica marcado por um dia: o Telegram pode mandar de novo o mesmo aviso.
                var banco = escopo.ServiceProvider.GetRequiredService<BancoDoServidor>();
                var agora = relogio.GetUtcNow().UtcDateTime;
                await banco.AvisosDoTelegram.Where(a => a.Id == aviso.Id).ExecuteUpdateAsync(a => a.SetProperty(x => x.Atendido, agora), CancellationToken.None);
            }
        }
    }
}

/// <summary>
/// Ao subir: registra o webhook no Telegram (se o token, o segredo e o endereço público estão configurados) e, Bloco 83, o menu
/// de comandos do bot nos chats privados e nos grupos.
/// </summary>
public sealed class RegistroDoWebhook(IOptions<OpcoesDoServidor> opcoes, IClienteDoTelegram telegram, ILogger<RegistroDoWebhook> log) : IHostedService
{
    public async Task StartAsync(CancellationToken c)
    {
        var config = opcoes.Value.Telegram;
        if (!config.Ligado)
            return;
        try
        {
            if (!string.IsNullOrWhiteSpace(config.UrlPublica))
                await telegram.RegistrarWebhookAsync($"{config.UrlPublica.TrimEnd('/')}/v1/telegram/webhook", config.SegredoParaOTelegram!, c);
            await telegram.DefinirComandosAsync(AjudaDoTelegram.ComandosDoPrivado, EscoposDosComandos.Privados, c);
            await telegram.DefinirComandosAsync(AjudaDoTelegram.ComandosDoGrupo, EscoposDosComandos.Grupos, c);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Não deu para registrar o webhook ou os comandos do Telegram agora");
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
        /// <summary>Bloco 50: a soma das chamadas, com os tokens do cache.</summary>
        public TokensDoPedido Tokens { get; internal set; }

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
                    medicao.Tokens = medicao.Tokens.Mais(TokensDoPedido.DoUso(uso));
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
