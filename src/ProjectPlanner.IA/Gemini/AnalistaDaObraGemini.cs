using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static ProjectPlanner.IA.Gemini.AnalistaGemini;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// Bloco 43: a análise dos indicadores pelo Gemini: uma chamada com os dados em texto e a resposta no formato JSON
/// {resumo, pontos, recomendacoes}, conferida (textos aparados, listas limitadas).
/// </summary>
public sealed class AnalistaDaObraGemini : IAnalistaDaObra
{
    public const int MaximoDePontos = 6;
    public const int TamanhoDoItem = 300;

    private readonly HttpClient _http;
    private readonly AcessoAoGemini _acesso;

    public AnalistaDaObraGemini(HttpClient http, string chave, string modelo) : this(http, new AcessoPorChave(chave), modelo)
    {
    }

    /// <summary>Bloco 45: pelo acesso escolhido (a chave própria ou a conta ProjectPlanner).</summary>
    public AnalistaDaObraGemini(HttpClient http, AcessoAoGemini acesso, string modelo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(acesso);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        (_http, _acesso, Modelo) = (http, acesso, modelo);
    }

    public string Modelo { get; }

    public bool EstaConfigurado => true;

    internal const string Instrucoes =
        "Você é um engenheiro civil gerente de obras, experiente em controle de prazo e custo (valor agregado), medições e " +
        "fluxo de caixa de obras de edificação no Brasil. Escreva em português do Brasil, linguagem técnica simples e direta, " +
        "sem markdown. Use só os dados recebidos; não invente números, causas ou serviços. Quando citar valores, use os dos " +
        "dados. Responda com: \"resumo\" (1 ou 2 parágrafos curtos: a situação de prazo, custo e margem, medições e caixa, " +
        "e o que mais importa agora), \"pontos\" (de 0 a 6 pontos de atenção objetivos, cada um com a etapa ou o número que o " +
        "sustenta) e \"recomendacoes\" (de 0 a 6 ações práticas para a próxima semana ou o próximo mês). Se faltam dados " +
        "(sem custo orçado, sem custos realizados, sem medições), diga isso e recomende como completar.";

    public async Task<AnaliseDaObraIA> AnalisarAsync(PedidoDeAnaliseDaObra pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var resposta = await ChamarJsonAsync(_http, _acesso, Modelo, Montar(pedido),
            "A resposta do Gemini ficou grande demais e veio cortada. Tente de novo.", cancelamento);
        return Interpretar(resposta);
    }

    internal static JsonObject Montar(PedidoDeAnaliseDaObra pedido)
    {
        var dados = new StringBuilder();
        dados.AppendLine($"Obra: {pedido.Obra}. Data de status: {pedido.Data.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"))}.");
        if (pedido.Ficha.Count > 0)
        {
            dados.AppendLine("Ficha da obra:");
            foreach (var item in pedido.Ficha)
                dados.AppendLine("- " + item);
        }
        dados.AppendLine("Indicadores:");
        foreach (var linha in pedido.Dados)
            dados.AppendLine("- " + linha);
        static JsonObject Lista() => new() { ["type"] = "ARRAY", ["items"] = Tipo("STRING") };
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
                    ["properties"] = new JsonObject { ["resumo"] = Tipo("STRING"), ["pontos"] = Lista(), ["recomendacoes"] = Lista() },
                    ["required"] = new JsonArray("resumo", "pontos", "recomendacoes"),
                    ["propertyOrdering"] = new JsonArray("resumo", "pontos", "recomendacoes"),
                },
            },
        };
    }

    internal static AnaliseDaObraIA Interpretar(JsonElement raiz)
    {
        var resumo = Texto(raiz, "resumo")?.Trim();
        if (string.IsNullOrEmpty(resumo))
            throw new ErroDoAssistenteException("O Gemini não escreveu a análise. Tente de novo.");
        IReadOnlyList<string> Itens(string nome) => raiz.TryGetProperty(nome, out var lista) && lista.ValueKind == JsonValueKind.Array
            ? [.. lista.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => Limitar(i.GetString(), TamanhoDoItem))
                .OfType<string>().Distinct().Take(MaximoDePontos)]
            : [];
        return new AnaliseDaObraIA(resumo, Itens("pontos"), Itens("recomendacoes"));
    }
}
