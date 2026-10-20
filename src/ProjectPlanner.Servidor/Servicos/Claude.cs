using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Um pedido que não dá para mandar ao Claude (um anexo num formato que ele não lê, por exemplo).</summary>
public sealed class PedidoIncompativelException(string mensagem) : Exception(mensagem);

/// <summary>
/// Bloco 46: o programa fala sempre no formato do Gemini (<c>generateContent</c>); para os modelos da Anthropic, o servidor
/// traduz o pedido para a API de mensagens e a resposta de volta. A resposta estruturada (o <c>responseSchema</c> que o
/// cronograma, as fotos e o orçamento usam) vira a ferramenta "responder", cuja entrada é o JSON pedido.
/// </summary>
/// <remarks>
/// 0.45.1: a ferramenta vai com <c>tool_choice: auto</c> e a instrução de sempre usá-la. Os modelos que pensam antes de
/// responder (o Opus 5.5) recusam forçar uma ferramenta (<c>tool_choice: tool</c>). Se a resposta vier em texto, o JSON é
/// tirado do texto.
/// </remarks>
public static class TradutorClaude
{
    public const string Ferramenta = "responder";

    /// <summary>Acrescentada às instruções quando o pedido quer a resposta estruturada.</summary>
    public const string InstrucaoDaFerramenta =
        "Formato da resposta: responda sempre chamando a ferramenta \"responder\" uma única vez, com todos os campos no formato " +
        "pedido. Não escreva texto fora da ferramenta.";

    /// <summary>Sem escapar os acentos (o texto vai dentro de outro JSON, que já é UTF-8).</summary>
    public static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Os formatos de imagem que o Claude lê (HEIC, por exemplo, não).</summary>
    public static readonly IReadOnlySet<string> Imagens = new HashSet<string> { "image/jpeg", "image/png", "image/gif", "image/webp" };

    /// <exception cref="PedidoIncompativelException">O pedido não tem conversa, ou tem um anexo que o Claude não lê.</exception>
    public static JsonObject Pedido(JsonNode gemini, string modelo, int maximoDeTokens)
    {
        var pedido = new JsonObject { ["model"] = modelo };
        var configuracao = gemini["generationConfig"];
        var esquema = configuracao?["responseSchema"] as JsonObject;
        var sistema = Texto(gemini["systemInstruction"]) ?? "";
        if (esquema is not null)
            sistema = (sistema.Length > 0 ? sistema + "\n\n" : "") + InstrucaoDaFerramenta;
        if (sistema.Length > 0)
            pedido["system"] = sistema;

        var mensagens = new JsonArray();
        foreach (var conteudo in gemini["contents"]?.AsArray() ?? [])
        {
            var papel = conteudo?["role"]?.GetValue<string>() == "model" ? "assistant" : "user";
            var partes = new JsonArray();
            foreach (var parte in conteudo?["parts"]?.AsArray() ?? [])
                if (Parte(parte) is { } convertida)
                    partes.Add(convertida);
            if (partes.Count == 0)
                continue;
            // O Claude exige papéis alternados, começando pelo usuário: mensagens seguidas do mesmo papel viram uma só.
            if (mensagens.Count > 0 && mensagens[^1]!["role"]!.GetValue<string>() == papel)
                foreach (var p in partes.ToList())
                {
                    partes.Remove(p);
                    mensagens[^1]!["content"]!.AsArray().Add(p);
                }
            else
                mensagens.Add(new JsonObject { ["role"] = papel, ["content"] = partes });
        }
        if (mensagens.Count == 0)
            throw new PedidoIncompativelException("O pedido não tem nenhuma mensagem.");
        if (mensagens[0]!["role"]!.GetValue<string>() != "user")
            mensagens.Insert(0, new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "(início da conversa)" }) });
        pedido["messages"] = mensagens;

        var maximo = configuracao?["maxOutputTokens"]?.GetValue<int>() is { } m && m > 0 ? Math.Min(m, maximoDeTokens) : maximoDeTokens;
        pedido["max_tokens"] = maximo;
        if (configuracao?["temperature"] is JsonValue temperatura)
            pedido["temperature"] = Math.Clamp(temperatura.GetValue<double>(), 0, 1);
        if (esquema is not null)
        {
            pedido["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = Ferramenta,
                ["description"] = "Responda sempre por esta ferramenta, preenchendo os campos no formato pedido.",
                ["input_schema"] = Esquema(esquema),
            });
            pedido["tool_choice"] = new JsonObject { ["type"] = "auto" };
        }
        return pedido;
    }

    /// <summary>A resposta do Claude no formato do Gemini, e os tokens para a cobrança.</summary>
    public static (JsonObject Resposta, long Entrada, long Saida) Resposta(JsonNode claude)
    {
        // A entrada da ferramenta, se houver; senão, o texto (sem o pensamento), com o JSON tirado de uma cerca de código.
        var blocos = claude["content"]?.AsArray() ?? [];
        var ferramenta = blocos.FirstOrDefault(b => b?["type"]?.GetValue<string>() == "tool_use");
        var textos = ferramenta is not null
            ? [ferramenta["input"]?.ToJsonString(Json) ?? "{}"]
            : blocos.Where(b => b?["type"]?.GetValue<string>() == "text").Select(b => b!["text"]?.GetValue<string>() ?? "").ToList();
        if (ferramenta is null && SoOObjeto(string.Concat(textos)) is { } json)
            textos = [json];
        var uso = claude["usage"];
        long Numero(string nome) => uso?[nome] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;
        var entrada = Numero("input_tokens") + Numero("cache_creation_input_tokens") + Numero("cache_read_input_tokens");
        var saida = Numero("output_tokens");
        var fim = claude["stop_reason"]?.GetValue<string>() == "max_tokens" ? "MAX_TOKENS" : "STOP";
        var resposta = new JsonObject
        {
            ["candidates"] = new JsonArray(new JsonObject
            {
                ["content"] = new JsonObject
                {
                    ["role"] = "model",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = string.Concat(textos) }),
                },
                ["finishReason"] = fim,
            }),
            ["usageMetadata"] = new JsonObject
            {
                ["promptTokenCount"] = entrada,
                ["candidatesTokenCount"] = saida,
                ["totalTokenCount"] = entrada + saida,
            },
            ["modelVersion"] = claude["model"]?.GetValue<string>(),
        };
        return (resposta, entrada, saida);
    }

    /// <summary>O esquema do Gemini (tipos em maiúsculas, <c>nullable</c>) como JSON Schema.</summary>
    public static JsonObject Esquema(JsonObject gemini)
    {
        var esquema = new JsonObject();
        var tipo = gemini["type"]?.GetValue<string>()?.ToLowerInvariant();
        var anulavel = gemini["nullable"] is JsonValue n && n.GetValue<bool>();
        if (tipo is not null)
            esquema["type"] = anulavel ? new JsonArray(tipo, "null") : tipo;
        if (gemini["description"]?.GetValue<string>() is { } descricao)
            esquema["description"] = descricao;
        if (gemini["enum"] is JsonArray valores)
            esquema["enum"] = valores.DeepClone();
        if (gemini["properties"] is JsonObject propriedades)
        {
            var convertidas = new JsonObject();
            foreach (var (nome, valor) in propriedades)
                if (valor is JsonObject propriedade)
                    convertidas[nome] = Esquema(propriedade);
            esquema["properties"] = convertidas;
        }
        if (gemini["required"] is JsonArray obrigatorios)
            esquema["required"] = obrigatorios.DeepClone();
        if (gemini["items"] is JsonObject itens)
            esquema["items"] = Esquema(itens);
        return esquema;
    }

    private static JsonObject? Parte(JsonNode? parte)
    {
        if (parte?["text"]?.GetValue<string>() is { } texto)
            return texto.Length == 0 ? null : new JsonObject { ["type"] = "text", ["text"] = texto };
        if (parte?["inlineData"] is JsonObject dados)
        {
            var tipo = dados["mimeType"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
            var base64 = dados["data"]?.GetValue<string>() ?? "";
            if (Imagens.Contains(tipo))
                return new JsonObject { ["type"] = "image", ["source"] = Fonte(tipo, base64) };
            if (tipo == "application/pdf")
                return new JsonObject { ["type"] = "document", ["source"] = Fonte(tipo, base64) };
            throw new PedidoIncompativelException(tipo.Contains("hei")
                ? "O Claude não lê fotos HEIC. Use o nível Padrão ou Avançado, ou converta a foto para JPG."
                : $"O Claude não lê anexos do tipo {tipo}. Use o nível Padrão ou Avançado para esse anexo.");
        }
        return null;
    }

    // Um texto com um objeto JSON no meio (numa cerca de código, por exemplo) vira só o objeto; nulo se não há um válido.
    private static string? SoOObjeto(string texto)
    {
        var (inicio, fim) = (texto.IndexOf('{'), texto.LastIndexOf('}'));
        if (inicio < 0 || fim <= inicio || (inicio == 0 && fim == texto.Length - 1))
            return null; // sem objeto, ou já é só o objeto
        var trecho = texto[inicio..(fim + 1)];
        try
        {
            return JsonNode.Parse(trecho) is JsonObject ? trecho : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonObject Fonte(string tipo, string base64) => new() { ["type"] = "base64", ["media_type"] = tipo, ["data"] = base64 };

    private static string? Texto(JsonNode? conteudo) =>
        conteudo?["parts"] is JsonArray partes
            ? string.Join("\n\n", partes.Select(p => p?["text"]?.GetValue<string>()).Where(t => !string.IsNullOrEmpty(t)))
            : null;

    /// <summary>A mensagem de um erro da Anthropic (<c>{"error": {"message": ...}}</c>).</summary>
    public static string? MensagemDeErro(string corpo)
    {
        try
        {
            return JsonNode.Parse(corpo)?["error"]?["message"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
