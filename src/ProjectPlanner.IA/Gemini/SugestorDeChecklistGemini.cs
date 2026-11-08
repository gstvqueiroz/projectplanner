using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static ProjectPlanner.IA.Gemini.AnalistaGemini;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// Bloco 78: o checklist de um card sugerido pelo Gemini: uma chamada com a tarefa em texto e a resposta no formato JSON
/// {itens}, conferida (textos aparados e limitados, sem repetir os que já existem, no máximo <see cref="MaximoDeItens"/>).
/// </summary>
public sealed class SugestorDeChecklistGemini : ISugestorDeChecklist
{
    public const int MaximoDeItens = 8;
    public const int TamanhoDoItem = 120;

    private readonly HttpClient _http;
    private readonly AcessoAoGemini _acesso;

    public SugestorDeChecklistGemini(HttpClient http, string chave, string modelo) : this(http, new AcessoPorChave(chave), modelo)
    {
    }

    /// <summary>Pelo acesso escolhido (a chave própria ou a conta ProjectPlanner).</summary>
    public SugestorDeChecklistGemini(HttpClient http, AcessoAoGemini acesso, string modelo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(acesso);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        (_http, _acesso, Modelo) = (http, acesso, modelo);
    }

    public string Modelo { get; }

    public bool EstaConfigurado => true;

    internal const string Instrucoes =
        "Você é um engenheiro civil de obras de edificação no Brasil. Para a tarefa recebida, escreva o checklist de execução e " +
        "conferência que o encarregado usa no canteiro: de 3 a 8 itens curtos (até 12 palavras cada), no imperativo, na ordem em " +
        "que se fazem, práticos e verificáveis (materiais e equipamentos à mão, marcação, execução, conferência de medidas, prumo, " +
        "nível ou cura, limpeza e liberação para a próxima etapa, conforme o serviço). Português do Brasil, sem markdown, sem " +
        "numeração e sem repetir os itens que já existem. Não invente normas, marcas nem quantidades.";

    public async Task<IReadOnlyList<string>> SugerirAsync(PedidoDeChecklist pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var resposta = await ChamarJsonAsync(_http, _acesso, Modelo, Montar(pedido),
            "A resposta do Gemini ficou grande demais e veio cortada. Tente de novo.", RotinasDeIa.Checklist, cancelamento);
        return Interpretar(resposta, pedido.JaNoChecklist);
    }

    internal static JsonObject Montar(PedidoDeChecklist pedido)
    {
        var dados = new StringBuilder();
        dados.AppendLine($"Tarefa: {pedido.Tarefa}.");
        if (!string.IsNullOrWhiteSpace(pedido.Resumo))
            dados.AppendLine($"Etapa (resumo): {pedido.Resumo}.");
        dados.AppendLine($"Duração: {pedido.Duracao} dia(s) útil(eis).");
        if (!string.IsNullOrWhiteSpace(pedido.Descricao))
            dados.AppendLine($"Descrição: {pedido.Descricao.Trim()}");
        if (pedido.JaNoChecklist.Count > 0)
            dados.AppendLine("Já no checklist (não repita): " + string.Join("; ", pedido.JaNoChecklist));
        if (pedido.Ficha.Count > 0)
            dados.AppendLine("Ficha da obra: " + string.Join("; ", pedido.Ficha));
        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Instrucoes }) },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(Nota(dados.ToString())) }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject { ["itens"] = new JsonObject { ["type"] = "ARRAY", ["items"] = Tipo("STRING") } },
                    ["required"] = new JsonArray("itens"),
                },
            },
        };
    }

    internal static IReadOnlyList<string> Interpretar(JsonElement raiz, IReadOnlyList<string> jaNoChecklist)
    {
        var existentes = jaNoChecklist.Select(i => i.Trim()).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var itens = raiz.TryGetProperty("itens", out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String)
                .Select(i => Limitar(i.GetString()?.Trim().TrimStart('-', '•', '*', ' ').Trim(), TamanhoDoItem))
                .OfType<string>().Where(i => i.Length > 0 && !existentes.Contains(i)).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(MaximoDeItens).ToList()
            : [];
        return itens.Count > 0 ? itens : throw new ErroDoAssistenteException("O Gemini não sugeriu itens para o checklist. Tente de novo.");
    }
}
