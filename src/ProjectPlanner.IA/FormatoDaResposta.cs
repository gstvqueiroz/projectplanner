using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectPlanner.IA;

/// <summary>
/// A resposta do assistente em JSON (Bloco 24), igual para qualquer provedor: {"mensagem": "...", "alteracoes": [...]}.
/// O Gemini é obrigado a seguir o formato (esquema de resposta); outro provedor recebe o mesmo pelas instruções.
/// </summary>
/// <remarks>
/// Bloco 26: campos opcionais "tipo" (resposta, modo, perguntas, plano), "perguntas", "plano" e "ficha". Uma resposta
/// de escolha de modo, de perguntas ou de plano não altera o cronograma (as alterações que vierem nela são ignoradas);
/// perguntas ou plano vazios viram uma resposta comum. Os excessos são cortados nos limites abaixo.
/// </remarks>
public static class FormatoDaResposta
{
    public const int MaximoDePerguntas = 6;
    public const int MaximoDeOpcoes = 6;
    public const int TamanhoMaximoDaOpcao = 80;
    public const int MaximoDeEtapas = 60;
    public const int MaximoDePremissas = 20;
    public const int MaximoDeItensNaFicha = 60;

    private const string NotaDeCorte = "\n\n(Resposta cortada: ficou longa demais.)";

    /// <summary>A mensagem e as alterações; texto que não é JSON vira só a mensagem, sem alterações.</summary>
    /// <param name="cortada">O provedor parou pelo limite de tamanho: um JSON incompleto vira erro, e nada é alterado.</param>
    /// <exception cref="ErroDoAssistenteException">A resposta veio cortada no meio do JSON.</exception>
    public static RespostaIA Interpretar(string texto, bool cortada = false)
    {
        var limpo = SemCercaDeCodigo(texto.Trim());
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(limpo);
        }
        catch (JsonException)
        {
            if (limpo.StartsWith('{'))
                throw new ErroDoAssistenteException(cortada
                    ? "A resposta do assistente ficou longa demais e veio cortada, então nada foi alterado. Peça em partes " +
                      "(por exemplo, uma etapa da obra por vez)."
                    : "O assistente mandou uma resposta com defeito, então nada foi alterado. Tente de novo.");
            return new RespostaIA(texto.Trim() + (cortada ? NotaDeCorte : ""));
        }

        using (json)
        {
            var raiz = json.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return new RespostaIA(texto.Trim());
            var mensagem = Texto(raiz, "mensagem") ?? "";
            var perguntas = Perguntas(raiz);
            var plano = Plano(raiz);
            var tipo = (Texto(raiz, "tipo") ?? "").Trim().ToLowerInvariant() switch
            {
                "modo" => TipoDeResposta.EscolherModo,
                "perguntas" when perguntas.Count > 0 => TipoDeResposta.Perguntas,
                "plano" when plano is not null => TipoDeResposta.Plano,
                _ => TipoDeResposta.Resposta,
            };
            var alteracoes = new List<AlteracaoDoCronograma>();
            if (raiz.TryGetProperty("alteracoes", out var lista) && lista.ValueKind == JsonValueKind.Array)
                foreach (var item in lista.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
                    alteracoes.Add(new AlteracaoDoCronograma(
                        (Texto(item, "acao") ?? "").Trim().ToLowerInvariant(),
                        Texto(item, "ref"),
                        Texto(item, "tarefa"),
                        Texto(item, "nome"),
                        Inteiro(item, "duracao"),
                        Texto(item, "pai"),
                        Inteiro(item, "percentual"),
                        Texto(item, "notas"),
                        Texto(item, "de"),
                        Texto(item, "para"),
                        Texto(item, "tipo"),
                        Inteiro(item, "defasagem"),
                        Texto(item, "inicio")));
            return new RespostaIA(mensagem.Trim() + (cortada ? NotaDeCorte : ""))
            {
                // Escolher o modo, perguntar e propor o plano nunca mexem no cronograma.
                Alteracoes = tipo == TipoDeResposta.Resposta ? alteracoes : [],
                Tipo = tipo,
                Perguntas = tipo == TipoDeResposta.Perguntas ? perguntas : [],
                Plano = tipo == TipoDeResposta.Plano ? plano : null,
                Ficha = Ficha(raiz),
            };
        }
    }

    /// <summary>
    /// A resposta de volta em JSON, no mesmo formato: é como ela vai no histórico, para o assistente ver o que já
    /// perguntou ou propôs.
    /// </summary>
    public static string ComoJson(RespostaIA resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        var json = new JsonObject
        {
            ["tipo"] = resposta.Tipo switch
            {
                TipoDeResposta.EscolherModo => "modo",
                TipoDeResposta.Perguntas => "perguntas",
                TipoDeResposta.Plano => "plano",
                _ => "resposta",
            },
            ["mensagem"] = resposta.Texto,
        };
        if (resposta.Ficha.Count > 0)
            json["ficha"] = new JsonArray([.. resposta.Ficha.Select(i => (JsonNode)new JsonObject { ["campo"] = i.Campo, ["valor"] = i.Valor })]);
        if (resposta.Perguntas.Count > 0)
            json["perguntas"] = new JsonArray([.. resposta.Perguntas.Select(p => (JsonNode)new JsonObject
            {
                ["pergunta"] = p.Texto,
                ["opcoes"] = new JsonArray([.. p.Opcoes.Select(o => (JsonNode)o)]),
            })]);
        if (resposta.Plano is { } plano)
            json["plano"] = new JsonObject
            {
                ["etapas"] = new JsonArray([.. plano.Etapas.Select(e =>
                {
                    var etapa = new JsonObject { ["nome"] = e.Nome, ["duracao"] = e.Duracao };
                    if (e.Observacao is not null)
                        etapa["observacao"] = e.Observacao;
                    return (JsonNode)etapa;
                })]),
                ["termino"] = plano.Termino,
                ["premissas"] = new JsonArray([.. plano.Premissas.Select(p => (JsonNode)p)]),
            };
        // As alterações não voltam no histórico (seriam centenas de itens a cada envio): o resultado delas já está no
        // retrato do cronograma, e a mensagem as resume.
        json["alteracoes"] = new JsonArray();
        return json.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static IReadOnlyList<PerguntaIA> Perguntas(JsonElement raiz)
    {
        var perguntas = new List<PerguntaIA>();
        foreach (var item in Objetos(raiz, "perguntas"))
        {
            var texto = (Texto(item, "pergunta") ?? Texto(item, "texto") ?? "").Trim();
            if (texto.Length == 0)
                continue;
            var opcoes = Textos(item, "opcoes")
                .Select(o => o.Length > TamanhoMaximoDaOpcao ? o[..TamanhoMaximoDaOpcao] : o)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximoDeOpcoes)
                .ToList();
            perguntas.Add(new PerguntaIA(texto, opcoes));
            if (perguntas.Count == MaximoDePerguntas)
                break;
        }
        return perguntas;
    }

    private static PlanoIA? Plano(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("plano", out var plano) || plano.ValueKind != JsonValueKind.Object)
            return null;
        var etapas = Objetos(plano, "etapas")
            .Select(e => new EtapaIA((Texto(e, "nome") ?? "").Trim(), Math.Max(0, Inteiro(e, "duracao") ?? 0),
                Texto(e, "observacao") is { Length: > 0 } observacao ? observacao.Trim() : null))
            .Where(e => e.Nome.Length > 0)
            .Take(MaximoDeEtapas)
            .ToList();
        if (etapas.Count == 0)
            return null;
        var termino = Texto(plano, "termino")?.Trim();
        return new PlanoIA(etapas, string.IsNullOrEmpty(termino) ? null : termino, [.. Textos(plano, "premissas").Take(MaximoDePremissas)]);
    }

    private static IReadOnlyList<ItemDeFicha> Ficha(JsonElement raiz) =>
    [
        .. Objetos(raiz, "ficha")
            .Select(i => new ItemDeFicha((Texto(i, "campo") ?? "").Trim(), (Texto(i, "valor") ?? "").Trim()))
            .Where(i => i.Campo.Length > 0)
            .Take(MaximoDeItensNaFicha),
    ];

    private static IEnumerable<JsonElement> Objetos(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).ToList()
            : [];

    private static IEnumerable<string> Textos(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String)
                .Select(i => i.GetString()!.Trim()).Where(t => t.Length > 0).ToList()
            : [];

    // Alguns modelos embrulham o JSON em ```json ... ```.
    private static string SemCercaDeCodigo(string texto)
    {
        if (!texto.StartsWith("```", StringComparison.Ordinal))
            return texto;
        var inicio = texto.IndexOf('\n');
        var fim = texto.LastIndexOf("```", StringComparison.Ordinal);
        return inicio >= 0 && fim > inicio ? texto[(inicio + 1)..fim].Trim() : texto;
    }

    // Números e textos aceitos dos dois jeitos ("3" ou 3), porque os modelos misturam.
    private static string? Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor) ? valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString(),
            JsonValueKind.Number => valor.GetRawText(),
            _ => null,
        } : null;

    private static int? Inteiro(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor))
            return null;
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDouble(out var numero))
            return (int)Math.Round(numero);
        if (valor.ValueKind == JsonValueKind.String && double.TryParse(valor.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lido))
            return (int)Math.Round(lido);
        return null;
    }
}
