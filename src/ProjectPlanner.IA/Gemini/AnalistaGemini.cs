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
    private readonly AcessoAoGemini _acesso;

    public AnalistaGemini(HttpClient http, string chave, string modelo) : this(http, new AcessoPorChave(chave), modelo)
    {
    }

    /// <summary>Bloco 45: pelo acesso escolhido (a chave própria ou a conta ProjectPlanner).</summary>
    public AnalistaGemini(HttpClient http, AcessoAoGemini acesso, string modelo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(acesso);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        (_http, _acesso, Modelo) = (http, acesso, modelo);
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

    // ---- Bloco 42: onde cada foto se encaixa ----

    internal const string InstrucoesDaClassificacao =
        "Para cada foto, diga em que tarefa do cronograma ela se encaixa, olhando o que aparece na imagem (o serviço, a etapa, " +
        "o material) e a data dela comparada com o período previsto das tarefas; as marcadas como em execução no período são " +
        "as mais prováveis, mas a imagem manda. Responda com o id da tarefa exatamente como recebido (T1, T2…) ou sem tarefa " +
        "quando não der para saber. confianca: alta (o serviço da foto é claramente o da tarefa), media (provável) ou baixa " +
        "(palpite). Escreva uma legenda curta (até 200 caracteres) do que aparece, sem repetir a data nem o nome da tarefa. Se " +
        "dá para saber o local e ele está na lista de locais da obra, mande-o exatamente como na lista. Se a foto mostra o " +
        "avanço do serviço, estime o % concluído da tarefa (0 a 100, múltiplos de 5). Na justificativa (até 200 caracteres), " +
        "diga o que na foto sustenta a escolha. Responda com o id de cada foto exatamente como recebido.";

    public async Task<IReadOnlyList<ClassificacaoDaFoto>> ClassificarFotosAsync(PedidoDeClassificacao pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.Fotos.Count == 0)
            return [];
        var resposta = await ChamarAsync(MontarClassificacao(pedido), cancelamento);
        var fotos = pedido.Fotos.Select(f => f.Id).ToHashSet();
        var tarefas = pedido.Tarefas.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var classificacoes = new List<ClassificacaoDaFoto>();
        foreach (var item in Lista(resposta, "fotos"))
        {
            if (Texto(item, "id") is not { } id || !fotos.Remove(id))
                continue;
            var tarefa = Texto(item, "tarefa")?.Trim() is { Length: > 0 } t && tarefas.Contains(t) ? pedido.Tarefas.First(x => string.Equals(x.Id, t, StringComparison.OrdinalIgnoreCase)).Id : null;
            var confianca = (Texto(item, "confianca") ?? "").Trim().ToLowerInvariant() switch
            {
                "alta" => ConfiancaDaIA.Alta,
                "media" or "média" => ConfiancaDaIA.Media,
                _ => ConfiancaDaIA.Baixa,
            };
            var local = Texto(item, "local")?.Trim() is { Length: > 0 } l
                ? pedido.Locais.FirstOrDefault(x => string.Equals(x, l, StringComparison.CurrentCultureIgnoreCase))
                : null;
            int? percentual = item.TryGetProperty("percentual", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v)
                ? (int)Math.Clamp(Math.Round(v), 0, 100) : null;
            classificacoes.Add(new ClassificacaoDaFoto(id, tarefa, tarefa is null ? ConfiancaDaIA.Baixa : confianca, Limitar(Texto(item, "legenda"), TamanhoDaLegenda),
                local, percentual, Limitar(Texto(item, "justificativa"), TamanhoDaLegenda) ?? ""));
        }
        return classificacoes;
    }

    internal static JsonObject MontarClassificacao(PedidoDeClassificacao pedido)
    {
        var dados = new StringBuilder()
            .AppendLine($"Obra: {pedido.Obra}. Hoje: {Data(pedido.Hoje)}. {pedido.Fotos.Count} foto(s) para classificar.")
            .AppendLine(pedido.Locais.Count == 0 ? "Locais da obra: (nenhum cadastrado)" : "Locais da obra: " + string.Join("; ", pedido.Locais))
            .AppendLine("Tarefas (id | EDT nome | em | previsto | % concluído | em execução no período das fotos):");
        foreach (var t in pedido.Tarefas)
            dados.AppendLine($"{t.Id} | {t.Edt} {t.Nome} | {(t.Caminho.Length == 0 ? "-" : t.Caminho)} | {Data(t.Inicio)} a {Data(t.Termino)} | " +
                             $"{t.Percentual}% | {(t.EmExecucao ? "sim" : "não")}");
        var partes = new JsonArray { Nota(dados.ToString()) };
        foreach (var foto in pedido.Fotos)
            AdicionarFoto(partes, foto);
        return Corpo(InstrucoesGerais + "\n\n" + InstrucoesDaClassificacao, partes, Esquema("fotos", new JsonObject
        {
            ["id"] = Tipo("STRING"),
            ["tarefa"] = Tipo("STRING"),
            ["confianca"] = new JsonObject { ["type"] = "STRING", ["enum"] = new JsonArray("alta", "media", "baixa") },
            ["legenda"] = Tipo("STRING"),
            ["local"] = Tipo("STRING"),
            ["percentual"] = Tipo("INTEGER"),
            ["justificativa"] = Tipo("STRING"),
        }, "id", "confianca", "legenda", "justificativa"));
    }

    // ---- Comum ----

    private Task<JsonElement> ChamarAsync(JsonObject corpo, CancellationToken cancelamento) =>
        ChamarJsonAsync(_http, _acesso, Modelo, corpo, "A resposta do Gemini ficou grande demais e veio cortada. Tente com menos fotos.", cancelamento);

    /// <summary>Uma chamada que responde em JSON (o objeto da raiz); também a do consultor de orçamento (Bloco 38).</summary>
    internal static async Task<JsonElement> ChamarJsonAsync(HttpClient http, AcessoAoGemini acesso, string modelo, JsonObject corpo, string seCortada,
        CancellationToken cancelamento)
    {
        using var resposta = await ChamadaGemini.ExecutarAsync(http, acesso, HttpMethod.Post, $"models/{modelo}:generateContent",
            corpo.ToJsonString(), modelo, cancelamento);
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
                ? seCortada
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

    internal static JsonObject Tipo(string tipo) => new() { ["type"] = tipo };

    internal static JsonObject Nota(string texto) => new() { ["text"] = texto };

    internal static IEnumerable<JsonElement> Lista(JsonElement raiz, string nome) =>
        raiz.TryGetProperty(nome, out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object)
            : [];

    internal static string? Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    internal static string? Limitar(string? texto, int maximo)
    {
        var limpo = texto?.Trim();
        if (string.IsNullOrEmpty(limpo))
            return null;
        return limpo.Length <= maximo ? limpo : limpo[..(maximo - 1)].TrimEnd() + "…";
    }

    private static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", Brasil);
}
