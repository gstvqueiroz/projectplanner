using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// Bloco 29: a IA olhando as fotos, pelo Gemini. Cada pedido vai numa chamada só, com as instruções, os dados (tarefas,
/// datas, %) e as imagens (JPEG em base64, a versão guardada no projeto), e o Gemini é obrigado a responder no formato
/// JSON de cada pedido. As respostas são conferidas (ids que existem, % de 0 a 100, textos curtos) antes de virar sugestão.
/// </summary>
public sealed class AnalistaGemini : IAnalistaDeFotos
{
    /// <summary>O tamanho máximo de uma legenda sugerida.</summary>
    public const int TamanhoDaLegenda = 300;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly HttpClient _http;
    private readonly string _chave;

    public AnalistaGemini(HttpClient http, string chave, string modelo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ChamadaGemini.ValidarChave(chave);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        (_http, _chave, Modelo) = (http, chave, modelo);
    }

    public string Modelo { get; }

    public bool EstaConfigurado => true;

    // ---- Instruções ----

    internal const string InstrucoesGerais =
        "Você é um engenheiro civil experiente que acompanha obras e escreve relatórios fotográficos em português do Brasil. " +
        "Descreva só o que dá para ver nas fotos e o que os dados informam; não invente serviços, materiais, quantidades ou " +
        "problemas. Use linguagem técnica de obra, simples e objetiva, sem exageros nem adjetivos de venda.";

    internal const string InstrucoesDasLegendas =
        "Escreva uma legenda para cada foto: uma ou duas frases curtas (até 200 caracteres) dizendo o serviço ou a etapa que " +
        "aparece, o estado (em execução, concluído, preparado para...) e algum detalhe visível útil (armadura, forma, " +
        "alvenaria, instalação...). Não repita a data nem o nome da tarefa, que já vão na legenda automática do relatório. " +
        "Responda com o id de cada foto exatamente como recebido.";

    internal const string InstrucoesDoAvanco =
        "Para cada tarefa, olhe as fotos mais recentes e estime o % concluído que elas mostram (0 a 100, em múltiplos de 5). " +
        "Compare com o % informado no cronograma: marque diverge=true quando a diferença for de 15 pontos ou mais. Na " +
        "justificativa (até 200 caracteres), diga o que nas fotos sustenta a estimativa. Se as fotos não mostram o serviço " +
        "com clareza, mantenha o % informado, diverge=false, e diga isso na justificativa. Responda com o id de cada tarefa " +
        "exatamente como recebido.";

    internal const string InstrucoesDoTexto =
        "Escreva o texto do relatório fotográfico do período: de 1 a 4 parágrafos curtos sobre o andamento, por etapa (o que " +
        "foi executado, o que está em andamento, o que começou), citando o % das etapas quando ajudar, e um parágrafo final " +
        "com a situação geral em relação ao previsto. Depois, de 0 a 6 pontos de atenção objetivos (atrasos, etapas críticas " +
        "próximas, algo que as fotos ou os dados indiquem). Não use títulos, listas nem markdown nos parágrafos.";

    // ---- Legendas ----

    public async Task<IReadOnlyList<LegendaSugerida>> SugerirLegendasAsync(PedidoDeLegendas pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.Fotos.Count == 0)
            return [];
        var resposta = await ChamarAsync(MontarLegendas(pedido), cancelamento);
        var ids = pedido.Fotos.Select(f => f.Id).ToHashSet();
        return Lista(resposta, "legendas")
            .Select(i => (Id: Texto(i, "id"), Legenda: Limitar(Texto(i, "legenda"), TamanhoDaLegenda)))
            .Where(i => i.Id is not null && ids.Remove(i.Id) && i.Legenda is not null) // cada foto uma vez, só as pedidas
            .Select(i => new LegendaSugerida(i.Id!, i.Legenda!))
            .ToList();
    }

    internal static JsonObject MontarLegendas(PedidoDeLegendas pedido)
    {
        var partes = new JsonArray { Nota($"Obra: {pedido.Obra}. {pedido.Fotos.Count} foto(s) para legendar.") };
        foreach (var foto in pedido.Fotos)
            AdicionarFoto(partes, foto);
        return Corpo(InstrucoesGerais + "\n\n" + InstrucoesDasLegendas, partes, Esquema("legendas", new JsonObject
        {
            ["id"] = Tipo("STRING"),
            ["legenda"] = Tipo("STRING"),
        }, "id", "legenda"));
    }

    // ---- % pelas fotos ----

    public async Task<IReadOnlyList<AvancoSugerido>> SugerirAvancoAsync(PedidoDeAvanco pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.Tarefas.Count == 0)
            return [];
        var resposta = await ChamarAsync(MontarAvanco(pedido), cancelamento);
        var ids = pedido.Tarefas.Select(t => t.Id).ToHashSet();
        var sugestoes = new List<AvancoSugerido>();
        foreach (var item in Lista(resposta, "tarefas"))
        {
            if (Texto(item, "id") is not { } id || !ids.Remove(id)
                || !item.TryGetProperty("percentual", out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetDouble(out var percentual))
                continue;
            var diverge = item.TryGetProperty("diverge", out var d) && d.ValueKind == JsonValueKind.True;
            sugestoes.Add(new AvancoSugerido(id, (int)Math.Clamp(Math.Round(percentual), 0, 100),
                Limitar(Texto(item, "justificativa"), TamanhoDaLegenda) ?? "", diverge));
        }
        return sugestoes;
    }

    internal static JsonObject MontarAvanco(PedidoDeAvanco pedido)
    {
        var partes = new JsonArray { Nota($"Obra: {pedido.Obra}. Hoje: {Data(pedido.Hoje)}. {pedido.Tarefas.Count} tarefa(s) para conferir.") };
        foreach (var tarefa in pedido.Tarefas)
        {
            partes.Add(Nota($"Tarefa id={tarefa.Id}: {tarefa.Edt} {tarefa.Nome} · previsto de {Data(tarefa.Inicio)} a {Data(tarefa.Termino)} · " +
                            $"{tarefa.Percentual}% concluído informado · {tarefa.PercentualPrevisto}% previsto para hoje · " +
                            $"{tarefa.Fotos.Count} foto(s) recente(s):"));
            foreach (var foto in tarefa.Fotos)
                AdicionarFoto(partes, foto);
        }
        return Corpo(InstrucoesGerais + "\n\n" + InstrucoesDoAvanco, partes, Esquema("tarefas", new JsonObject
        {
            ["id"] = Tipo("STRING"),
            ["percentual"] = Tipo("INTEGER"),
            ["justificativa"] = Tipo("STRING"),
            ["diverge"] = Tipo("BOOLEAN"),
        }, "id", "percentual", "justificativa", "diverge"));
    }

    // ---- Texto do relatório ----

    public async Task<TextoSugerido> RedigirRelatorioAsync(PedidoDeTextoDoRelatorio pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var resposta = await ChamarAsync(MontarTexto(pedido), cancelamento);
        var andamento = Texto(resposta, "andamento");
        if (string.IsNullOrWhiteSpace(andamento))
            throw new ErroDoAssistenteException("O Gemini não escreveu o texto do andamento. Tente de novo.");
        var pontos = resposta.TryGetProperty("pontos", out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!.Trim())
                .Where(p => p.Length > 0).Take(10).ToList()
            : [];
        return new TextoSugerido(andamento.Trim(), pontos);
    }

    internal static JsonObject MontarTexto(PedidoDeTextoDoRelatorio pedido)
    {
        var dados = new StringBuilder()
            .AppendLine($"Obra: {pedido.Obra}. Período do relatório: {Data(pedido.De)} a {Data(pedido.Ate)}.")
            .AppendLine("Andamento geral:");
        foreach (var linha in pedido.Resumo)
            dados.AppendLine("- " + linha);
        dados.AppendLine("Etapas do período (EDT | nome | previsto | % informado | % previsto | situação | legendas das fotos):");
        foreach (var e in pedido.Etapas)
            dados.AppendLine($"- {e.Edt} | {e.Nome} | {Data(e.Inicio)} a {Data(e.Termino)} | {e.Percentual}% | {e.PercentualPrevisto}% | {e.Situacao} | " +
                             (e.LegendasDasFotos.Count == 0 ? "sem fotos" : string.Join(" / ", e.LegendasDasFotos)));
        var partes = new JsonArray { Nota(dados.ToString()) };
        foreach (var foto in pedido.Fotos)
            AdicionarFoto(partes, foto);
        return Corpo(InstrucoesGerais + "\n\n" + InstrucoesDoTexto, partes, new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["andamento"] = Tipo("STRING"),
                ["pontos"] = new JsonObject { ["type"] = "ARRAY", ["items"] = Tipo("STRING") },
            },
            ["required"] = new JsonArray("andamento", "pontos"),
        });
    }

    // ---- Comum ----

    private async Task<JsonElement> ChamarAsync(JsonObject corpo, CancellationToken cancelamento)
    {
        using var mensagem = ChamadaGemini.Pedido(HttpMethod.Post, $"models/{Modelo}:generateContent", _chave);
        mensagem.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
        using var resposta = await ChamadaGemini.ExecutarAsync(_http, mensagem, _chave, Modelo, cancelamento);
        var (texto, fim) = AssistenteGemini.LerResposta(resposta.RootElement);
        try
        {
            using var json = JsonDocument.Parse(texto);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            return json.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ErroDoAssistenteException(fim == "MAX_TOKENS"
                ? "A resposta do Gemini ficou grande demais e veio cortada. Tente com menos fotos."
                : "O Gemini mandou uma resposta que não deu para entender. Tente de novo.", ex);
        }
    }

    private static void AdicionarFoto(JsonArray partes, FotoParaIA foto)
    {
        var dados = new List<string> { $"Foto id={foto.Id}", foto.Tarefa, Data(foto.Data) };
        if (foto.Local is { } local)
            dados.Add($"local: {local}");
        if (foto.PercentualNaData is { } p)
            dados.Add($"{p}% na data");
        if (foto.Titulo is { } titulo)
            dados.Add($"título: {titulo}");
        if (foto.Legenda is { } legenda)
            dados.Add($"legenda atual: {legenda}");
        partes.Add(Nota(string.Join(" · ", dados)));
        partes.Add(new JsonObject
        {
            ["inlineData"] = new JsonObject { ["mimeType"] = "image/jpeg", ["data"] = Convert.ToBase64String(foto.Jpeg) },
        });
    }

    private static JsonObject Corpo(string instrucoes, JsonArray partes, JsonObject esquema)
    {
        var bytes = partes.Sum(p => p?["inlineData"]?["data"]?.GetValue<string>().Length ?? 0) * 3L / 4;
        if (bytes > AnexosAceitos.TamanhoMaximoPorEnvio)
            throw new ErroDoAssistenteException(
                $"As fotos passam de {AnexosAceitos.Megabytes(AnexosAceitos.TamanhoMaximoPorEnvio)} por pedido. Escolha menos fotos.");
        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = instrucoes }) },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = partes }),
            ["generationConfig"] = new JsonObject { ["responseMimeType"] = "application/json", ["responseSchema"] = esquema },
        };
    }

    private static JsonObject Esquema(string lista, JsonObject propriedades, params string[] obrigatorios) => new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            [lista] = new JsonObject
            {
                ["type"] = "ARRAY",
                ["items"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = propriedades,
                    ["required"] = new JsonArray([.. obrigatorios.Select(o => (JsonNode)o)]),
                },
            },
        },
        ["required"] = new JsonArray(lista),
    };

    private static JsonObject Tipo(string tipo) => new() { ["type"] = tipo };

    private static JsonObject Nota(string texto) => new() { ["text"] = texto };

    private static IEnumerable<JsonElement> Lista(JsonElement raiz, string nome) =>
        raiz.TryGetProperty(nome, out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static string? Limitar(string? texto, int maximo)
    {
        var limpo = texto?.Trim();
        if (string.IsNullOrEmpty(limpo))
            return null;
        return limpo.Length <= maximo ? limpo : limpo[..(maximo - 1)].TrimEnd() + "…";
    }

    private static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", Brasil);
}
