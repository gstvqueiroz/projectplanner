using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// O assistente falando com o Gemini (Bloco 23): manda as instruções com o retrato do cronograma, a conversa (o Gemini
/// não guarda nada entre as chamadas) e os anexos, e devolve a mensagem e as alterações do cronograma (Bloco 24), que o
/// Gemini é obrigado a mandar no formato de <see cref="FormatoDaResposta"/>.
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
        var (texto, fim) = LerResposta(resposta.RootElement);
        return FormatoDaResposta.Interpretar(texto, cortada: fim == "MAX_TOKENS");
    }

    // ---- O formato da resposta (Bloco 24): o Gemini é obrigado a devolver {mensagem, alteracoes} ----

    internal static JsonObject EsquemaDaResposta()
    {
        static JsonObject Tipo(string tipo, params string[] valores)
        {
            var campo = new JsonObject { ["type"] = tipo };
            if (valores.Length > 0)
                campo["enum"] = new JsonArray([.. valores.Select(v => (JsonNode)v)]);
            return campo;
        }

        string[] campos = ["acao", "ref", "tarefa", "nome", "duracao", "pai", "de", "para", "tipo", "defasagem", "percentual", "notas", "inicio"];
        var alteracao = new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["acao"] = Tipo("STRING", [.. AcoesNoCronograma.Todas]),
                ["ref"] = Tipo("STRING"),
                ["tarefa"] = Tipo("STRING"),
                ["nome"] = Tipo("STRING"),
                ["duracao"] = Tipo("INTEGER"),
                ["pai"] = Tipo("STRING"),
                ["de"] = Tipo("STRING"),
                ["para"] = Tipo("STRING"),
                ["tipo"] = Tipo("STRING", "TI", "II", "TT", "IT"),
                ["defasagem"] = Tipo("INTEGER"),
                ["percentual"] = Tipo("INTEGER"),
                ["notas"] = Tipo("STRING"),
                ["inicio"] = Tipo("STRING"),
            },
            ["required"] = new JsonArray("acao"),
            ["propertyOrdering"] = new JsonArray([.. campos.Select(c => (JsonNode)c)]),
        };
        static JsonObject Lista(JsonObject item) => new() { ["type"] = "ARRAY", ["items"] = item };
        static JsonObject Objeto(JsonObject propriedades, params string[] obrigatorios) => new()
        {
            ["type"] = "OBJECT",
            ["properties"] = propriedades,
            ["required"] = new JsonArray([.. obrigatorios.Select(o => (JsonNode)o)]),
        };

        // Bloco 26: tipo, ficha, perguntas e plano, todos opcionais (as respostas comuns continuam iguais).
        return new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["tipo"] = Tipo("STRING", "resposta", "modo", "perguntas", "plano"),
                ["mensagem"] = Tipo("STRING"),
                ["ficha"] = Lista(Objeto(new JsonObject { ["campo"] = Tipo("STRING"), ["valor"] = Tipo("STRING") }, "campo", "valor")),
                ["perguntas"] = Lista(Objeto(new JsonObject
                {
                    ["pergunta"] = Tipo("STRING"),
                    ["opcoes"] = Lista(Tipo("STRING")),
                }, "pergunta")),
                ["plano"] = Objeto(new JsonObject
                {
                    ["etapas"] = Lista(Objeto(new JsonObject
                    {
                        ["nome"] = Tipo("STRING"),
                        ["duracao"] = Tipo("INTEGER"),
                        ["observacao"] = Tipo("STRING"),
                    }, "nome", "duracao")),
                    ["termino"] = Tipo("STRING"),
                    ["premissas"] = Lista(Tipo("STRING")),
                }, "etapas"),
                ["alteracoes"] = new JsonObject { ["type"] = "ARRAY", ["items"] = alteracao },
                ["orcamento"] = Tipo("STRING"), // Bloco 38: o pedido de sugestões de orçamento
                // Bloco 41: as atualizações da obra (cartão para confirmar) e a tela para abrir.
                ["atualizacoes"] = Lista(Objeto(new JsonObject
                {
                    ["tipo"] = Tipo("STRING", [.. TiposDeAtualizacao.Todos]),
                    ["tarefa"] = Tipo("STRING"),
                    ["percentual"] = Tipo("NUMBER"),
                    ["quantidade"] = Tipo("NUMBER"),
                    ["medicao"] = Tipo("INTEGER"),
                    ["de"] = Tipo("STRING"),
                    ["ate"] = Tipo("STRING"),
                    ["data"] = Tipo("STRING"),
                    ["categoria"] = Tipo("STRING"),
                    ["descricao"] = Tipo("STRING"),
                    ["valor"] = Tipo("NUMBER"),
                    ["documento"] = Tipo("STRING"),
                    ["nome"] = Tipo("STRING"),
                }, "tipo")),
                ["abrir"] = Objeto(new JsonObject
                {
                    ["tela"] = Tipo("STRING", [.. TelasDoPrograma.Todas]),
                    ["aba"] = Tipo("STRING"),
                    ["medicao"] = Tipo("INTEGER"),
                    ["de"] = Tipo("STRING"),
                    ["ate"] = Tipo("STRING"),
                }, "tela"),
            },
            ["required"] = new JsonArray("mensagem", "alteracoes"),
            ["propertyOrdering"] = new JsonArray("tipo", "mensagem", "ficha", "perguntas", "plano", "alteracoes", "orcamento", "atualizacoes", "abrir"),
        };
    }

    // ---- O pedido ----

    internal static async Task<JsonObject> MontarCorpoAsync(PedidoIA pedido, CancellationToken cancelamento)
    {
        var turnos = pedido.Historico
            .Where(m => !m.EhErro)
            // Bloco 26: uma resposta estruturada volta como o JSON que ela foi, com as perguntas ou o plano.
            .Select(m => (m.Autor, Texto: m.Resposta is { Estruturada: true } r ? FormatoDaResposta.ComoJson(r) : m.Texto, m.Anexos, Atual: false))
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
                ["parts"] = new JsonArray(new JsonObject { ["text"] = InstrucoesDoAssistente.Montar(pedido.Contexto, pedido.Conducao) }),
            },
            ["contents"] = conteudos,
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = EsquemaDaResposta(),
            },
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
    internal static (string Texto, string? Fim) LerResposta(JsonElement raiz)
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
        return (resposta, fim);
    }
}
