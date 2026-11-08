using System.Net.Http.Json;
using System.Text.Json;
using ProjectPlanner.IA;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Bloco 85: um áudio mandado ao bot (a voz gravada no Telegram ou um arquivo de áudio).</summary>
/// <param name="Segundos">A duração que o Telegram informa (zero se não informa).</param>
public sealed record AudioDoTelegram(string Arquivo, string Tipo, int Segundos, long Bytes);

/// <summary>
/// Bloco 85: o áudio no Telegram. O canteiro fala em vez de digitar: o bot baixa o áudio, o Gemini mais barato configurado o
/// transcreve (o Claude não ouve áudio) e o texto segue como se tivesse sido escrito, com a transcrição no começo da resposta
/// ("🎤 …"), para a pessoa conferir o que foi entendido. Gasta os créditos do dono, como o resto (rotina "telegram").
/// </summary>
public sealed partial class AtendenteDoTelegram
{
    public const string PromptDaTranscricao =
        "Transcreva fielmente, em português do Brasil, o que a pessoa fala neste áudio: é uma mensagem do canteiro de obras para o " +
        "assistente da obra. Responda só com a transcrição, sem comentários nem aspas. Escreva números, unidades e valores como se " +
        "escreve (\"20 m²\", \"3 pedreiros\", \"R$ 1.200\"). Se não houver fala, responda com uma linha vazia.";

    public const string NaoEntendiOAudio = "Não entendi o áudio. Mande de novo, mais perto do microfone, ou escreva.";

    /// <summary>O áudio da mensagem (voz, ou um arquivo de áudio); nulo se ela não tem.</summary>
    public static AudioDoTelegram? AudioDa(JsonElement mensagem)
    {
        foreach (var campo in new[] { "voice", "audio" })
            if (mensagem.TryGetProperty(campo, out var audio) && audio.ValueKind == JsonValueKind.Object && Texto(audio, "file_id") is { } arquivo)
                return new AudioDoTelegram(arquivo, TipoParaOGemini(Texto(audio, "mime_type")),
                    audio.TryGetProperty("duration", out var d) && d.TryGetInt32(out var segundos) ? segundos : 0,
                    audio.TryGetProperty("file_size", out var t) && t.TryGetInt64(out var bytes) ? bytes : 0);
        return null;
    }

    // O Gemini chama o MP3 de "audio/mp3"; a voz do Telegram é OGG (Opus).
    private static string TipoParaOGemini(string? tipo) => tipo?.Trim().ToLowerInvariant() switch
    {
        null or "" => "audio/ogg",
        "audio/mpeg" => "audio/mp3",
        "audio/x-wav" => "audio/wav",
        var outro => outro,
    };

    /// <summary>Transcreve o áudio e atende o texto como uma mensagem escrita.</summary>
    private async Task AudioAsync(long chatId, long pessoaId, Usuario usuario, ObraNoServidor obra, AudioDoTelegram audio, DateTime agora, CancellationToken c,
        string? autor = null, bool aceitaAtualizacoes = true)
    {
        if (audio.Segundos > Config.SegundosPorAudio || audio.Bytes > Config.MegabytesPorFoto * 1024L * 1024L)
        {
            await telegram.EnviarAsync(chatId, $"O áudio passa de {Config.SegundosPorAudio / 60} minutos. Mande um mais curto (ou escreva).", c);
            return;
        }
        if (await TranscreverAsync(chatId, usuario, audio, agora, c) is not { } texto)
            return;
        await PerguntaAsync(chatId, pessoaId, usuario, obra, texto, agora, c, autor, aceitaAtualizacoes, prefixo: $"🎤 \"{CortarParaMostrar(texto)}\"\n\n");
    }

    /// <returns>O que a pessoa falou; nulo se não deu (e a resposta já foi para o chat).</returns>
    private async Task<string?> TranscreverAsync(long chatId, Usuario usuario, AudioDoTelegram audio, DateTime agora, CancellationToken c)
    {
        var saldo = await Rotas.Cotas.SaldoAsync(banco, usuario, opcoes.Value.Planos, relogio);
        if (saldo.Esgotado)
        {
            await telegram.EnviarAsync(chatId, Rotas.Ia.CotaEsgotada, c);
            return null;
        }
        // O Gemini mais barato com a chave configurada (o áudio custa pouco: cerca de 32 tokens por segundo de fala).
        var modelo = catalogo.Todos.Where(m => m.Provedor == Provedores.Gemini && catalogo.Disponivel(m))
            .OrderBy(m => m.DolaresPorMilhaoDeEntrada + m.DolaresPorMilhaoDeSaida).FirstOrDefault();
        if (modelo is null)
        {
            await telegram.EnviarAsync(chatId, "Ainda não consigo ouvir áudios por aqui: mande a mensagem por escrito.", c);
            return null;
        }
        await telegram.DigitandoAsync(chatId, c);
        var bytes = await telegram.BaixarAsync(audio.Arquivo, c);
        if (bytes is not { Length: > 0 })
        {
            await telegram.EnviarAsync(chatId, "Não deu para receber o áudio. Mande de novo.", c);
            return null;
        }
        string? texto;
        TokensDoPedido tokens;
        using (var medicao = MedidorDeTokens.Abrir())
        {
            texto = await PedirTranscricaoAsync(modelo, bytes, audio.Tipo, c);
            tokens = medicao.Tokens;
        }
        var custo = tokens.Microdolares(modelo);
        if (custo > 0)
        {
            banco.UsosDeIa.Add(UsoDeIa.Novo(usuario.Id, agora, modelo.Id, tokens, custo, RotinasDeIa.Telegram));
            Rotas.Cotas.Debitar(usuario, saldo, custo);
            await banco.SaveChangesAsync(c);
        }
        if (string.IsNullOrWhiteSpace(texto))
        {
            await telegram.EnviarAsync(chatId, NaoEntendiOAudio, c);
            return null;
        }
        return Cortar(texto.Trim());
    }

    // O generateContent do Gemini com o áudio em base64; o texto das partes da resposta (nulo se falhou).
    private async Task<string?> PedirTranscricaoAsync(ModeloDeIa modelo, byte[] audio, string tipo, CancellationToken c)
    {
        var endereco = new Uri(new Uri(opcoes.Value.Gemini.Endereco.TrimEnd('/') + "/"), $"models/{modelo.Id}:generateContent");
        var corpo = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[] { new { inlineData = new { mimeType = tipo, data = Convert.ToBase64String(audio) } }, new { text = PromptDaTranscricao } },
                },
            },
        };
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, endereco) { Content = JsonContent.Create(corpo) };
            pedido.Headers.Add("x-goog-api-key", opcoes.Value.Gemini.Chave);
            using var resposta = await clientes.CreateClient(MedidorDeTokens.Cliente).SendAsync(pedido, c);
            if (!resposta.IsSuccessStatusCode)
            {
                log.LogWarning("O Gemini não transcreveu o áudio ({Modelo}, {Status})", modelo.Id, (int)resposta.StatusCode);
                return null;
            }
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(c));
            if (!json.RootElement.TryGetProperty("candidates", out var candidatos) || candidatos.ValueKind != JsonValueKind.Array || candidatos.GetArrayLength() == 0
                || !candidatos[0].TryGetProperty("content", out var conteudo) || !conteudo.TryGetProperty("parts", out var partes) || partes.ValueKind != JsonValueKind.Array)
                return null;
            return string.Concat(partes.EnumerateArray().Where(p => !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
                .Select(p => Texto(p, "text") ?? ""));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !c.IsCancellationRequested))
        {
            log.LogWarning(ex, "O Gemini não transcreveu o áudio ({Modelo})", modelo.Id);
            return null;
        }
    }

    private static string CortarParaMostrar(string texto) => texto.Length <= 300 ? texto : texto[..299] + "…";
}
