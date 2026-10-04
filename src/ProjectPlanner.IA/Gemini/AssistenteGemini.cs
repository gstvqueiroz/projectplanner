using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// O assistente falando com o Gemini (Bloco 23): manda as instruções com o retrato do cronograma, a conversa (o Gemini
/// não guarda nada entre as chamadas) e os anexos, e devolve o texto da resposta.
/// </summary>
/// <remarks>
/// Os anexos de toda a conversa vão a cada envio, para o assistente continuar enxergando os documentos nas perguntas
/// seguintes: PDF e imagens em base64 (<c>inlineData</c>), texto lido como parte da mensagem. Um anexo de uma mensagem
/// anterior que sumiu do disco vira uma nota; um da mensagem atual, que não dá para ler, interrompe o envio.
/// </remarks>
public sealed class AssistenteGemini : IAssistenteIA
{
    private readonly HttpClient _http;
    private readonly string _chave;

    public AssistenteGemini(HttpClient http, string chave, string modelo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ChamadaGemini.ValidarChave(chave);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        (_http, _chave, Modelo) = (http, chave, modelo);
    }

    public string Modelo { get; }

    public bool EstaConfigurado => true;

    public async Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var corpo = await MontarCorpoAsync(pedido, cancelamento);
        using var mensagem = ChamadaGemini.Pedido(HttpMethod.Post, $"models/{Modelo}:generateContent", _chave);
        mensagem.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
        using var resposta = await ChamadaGemini.ExecutarAsync(_http, mensagem, _chave, Modelo, cancelamento);
        return new RespostaIA(LerResposta(resposta.RootElement));
    }

    // ---- O pedido ----

    internal static async Task<JsonObject> MontarCorpoAsync(PedidoIA pedido, CancellationToken cancelamento)
    {
        var turnos = pedido.Historico
            .Where(m => !m.EhErro)
            .Select(m => (m.Autor, m.Texto, m.Anexos, Atual: false))
            .Append((Autor: AutorMensagem.Usuario, Texto: pedido.Mensagem, pedido.Anexos, Atual: true));

        var conteudos = new JsonArray();
        var bytesDosAnexos = 0L;
        string? papelAnterior = null;
        JsonArray? partesAnteriores = null;
        foreach (var (autor, texto, anexos, atual) in turnos)
        {
            var papel = autor == AutorMensagem.Usuario ? "user" : "model";
            var partes = new JsonArray();
            if (autor == AutorMensagem.Usuario)
                foreach (var anexo in anexos)
                    foreach (var parte in await PartesDoAnexoAsync(anexo, atual, cancelamento))
                    {
                        bytesDosAnexos += parte.Bytes;
                        if (bytesDosAnexos > AnexosAceitos.TamanhoMaximoPorEnvio)
                            throw new ErroDoAssistenteException(
                                $"Os anexos desta conversa passam de {AnexosAceitos.Megabytes(AnexosAceitos.TamanhoMaximoPorEnvio)} " +
                                "(o limite por envio). Use \"Limpar conversa\" ou anexe arquivos menores.");
                        partes.Add(parte.Json);
                    }
            if (!string.IsNullOrWhiteSpace(texto))
                partes.Add(new JsonObject { ["text"] = texto });
            if (partes.Count == 0)
                continue;

            // O Gemini espera as falas alternadas: duas seguidas do mesmo autor (uma resposta que falhou no meio) viram uma só.
            if (papel == papelAnterior)
            {
                foreach (var parte in partes.ToList())
                {
                    partes.Remove(parte);
                    partesAnteriores!.Add(parte);
                }
                continue;
            }
            conteudos.Add(new JsonObject { ["role"] = papel, ["parts"] = partes });
            (papelAnterior, partesAnteriores) = (papel, partes);
        }

        if (papelAnterior != "user")
            throw new ErroDoAssistenteException("Escreva uma mensagem ou anexe um documento.");

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = InstrucoesDoAssistente.Montar(pedido.Contexto) }),
            },
            ["contents"] = conteudos,
        };
    }

    private static async Task<IReadOnlyList<(JsonObject Json, long Bytes)>> PartesDoAnexoAsync(AnexoIA anexo, bool atual,
        CancellationToken cancelamento)
    {
        if (!AnexosAceitos.Aceita(anexo))
        {
            if (atual)
                throw new ErroDoAssistenteException(AnexosAceitos.Problema(anexo)!);
            return [(Nota($"[O anexo {anexo.Nome}, de uma mensagem anterior, não foi enviado: formato não aceito.]"), 0)];
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(anexo.CaminhoLocal, cancelamento);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (atual)
                throw new ErroDoAssistenteException(
                    $"Não deu para ler o anexo {anexo.Nome}: o arquivo foi movido, apagado ou está sem permissão.", ex);
            return [(Nota($"[O anexo {anexo.Nome}, de uma mensagem anterior, não está mais disponível.]"), 0)];
        }

        if (AnexosAceitos.EhTexto(anexo))
            return [(Nota($"Conteúdo do anexo \"{anexo.Nome}\":\n{Encoding.UTF8.GetString(bytes)}"), bytes.Length)];
        return
        [
            (Nota($"Anexo: {anexo.Nome}"), 0),
            (new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = AnexosAceitos.TipoMime(anexo),
                    ["data"] = Convert.ToBase64String(bytes),
                },
            }, bytes.Length),
        ];
    }

    private static JsonObject Nota(string texto) => new() { ["text"] = texto };

    // ---- A resposta ----

    private static readonly HashSet<string> Bloqueios = ["SAFETY", "RECITATION", "BLOCKLIST", "PROHIBITED_CONTENT", "SPII", "IMAGE_SAFETY"];

    // {"candidates": [{"content": {"parts": [{"text": "..."}]}, "finishReason": "STOP"}], "promptFeedback": {...}}
    internal static string LerResposta(JsonElement raiz)
    {
        if (raiz.ValueKind != JsonValueKind.Object)
            throw new ErroDoAssistenteException("O Gemini mandou uma resposta que não deu para entender. Tente de novo.");
        if (raiz.TryGetProperty("promptFeedback", out var avaliacao) && avaliacao.ValueKind == JsonValueKind.Object
            && avaliacao.TryGetProperty("blockReason", out var bloqueio) && bloqueio.ValueKind == JsonValueKind.String)
            throw new ErroDoAssistenteException($"O Gemini recusou a mensagem (motivo: {bloqueio.GetString()}).");
        if (!raiz.TryGetProperty("candidates", out var candidatos) || candidatos.ValueKind != JsonValueKind.Array
            || candidatos.GetArrayLength() == 0)
            throw new ErroDoAssistenteException("O Gemini não devolveu nenhuma resposta. Tente de novo.");

        var candidato = candidatos[0];
        var fim = candidato.TryGetProperty("finishReason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        var texto = new StringBuilder();
        if (candidato.TryGetProperty("content", out var conteudo) && conteudo.ValueKind == JsonValueKind.Object
            && conteudo.TryGetProperty("parts", out var partes) && partes.ValueKind == JsonValueKind.Array)
            foreach (var parte in partes.EnumerateArray())
            {
                var pensamento = parte.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True;
                if (!pensamento && parte.TryGetProperty("text", out var trecho) && trecho.ValueKind == JsonValueKind.String)
                    texto.Append(trecho.GetString());
            }

        var resposta = texto.ToString().Trim();
        if (resposta.Length == 0)
            throw new ErroDoAssistenteException(fim is not null && Bloqueios.Contains(fim)
                ? $"O Gemini não respondeu por um bloqueio de segurança ({fim})."
                : "O Gemini devolveu uma resposta vazia" + (fim is null ? "." : $" ({fim})."));
        return fim == "MAX_TOKENS" ? resposta + "\n\n(Resposta cortada: ficou longa demais.)" : resposta;
    }
}
