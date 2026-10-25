using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Bloco 51: o que o programa conta do pedido para a triagem (sem o retrato da obra nem os anexos em si).</summary>
/// <param name="Mensagem">O texto do usuário (cortado em <see cref="Triagem.MaximoDaMensagem"/> caracteres).</param>
/// <param name="Anexos">Quantos anexos vão com o pedido.</param>
/// <param name="BytesDosAnexos">O tamanho somado deles.</param>
/// <param name="Caracteres">O tamanho aproximado do pedido inteiro (retrato, conversa e mensagem), para estimar os tokens.</param>
/// <param name="Tarefas">Quantas tarefas a obra tem.</param>
public sealed record PedidoDeTriagem(string? Mensagem, int Anexos = 0, long BytesDosAnexos = 0, int Caracteres = 0, int Tarefas = 0);

/// <summary>
/// Bloco 51: o nível Automático. Decide o tamanho de um pedido do chat em três passos, do mais barato ao mais caro: uma regra
/// (mensagem curta, sem anexo e sem pedido de montar ou analisar = simples, sem chamar IA nenhuma); senão, uma pergunta curta
/// ao modelo do Padrão, só com a mensagem e alguns números (cerca de 0,2 crédito); e, se a triagem falhar, simples.
/// </summary>
public static partial class Triagem
{
    public const string Simples = "simples";
    public const string Media = "media";
    public const string Complexa = "complexa";

    public const string Automatico = "automatico";
    public const int MensagemCurta = 160;
    public const int MaximoDaMensagem = 2_000;

    /// <summary>Os níveis do mais barato ao mais caro: o Automático desce por eles quando o plano não libera o desejado.</summary>
    public static IReadOnlyList<string> NiveisEmOrdem { get; } = [Niveis.Padrao, Niveis.Intermediario, Niveis.Avancado, Niveis.Maximo];

    public const string Instrucoes =
        "Você faz a triagem de pedidos ao assistente de um programa de cronogramas de obras (como o MS Project, para construção " +
        "civil). Classifique quanto trabalho o pedido exige do modelo de IA:\n" +
        "- simples: perguntas sobre a obra, atualizar o % de poucas tarefas, mudar uma data ou duração, renomear, anotar algo, " +
        "dúvidas rápidas;\n" +
        "- media: alterar várias tarefas de uma vez, acrescentar uma etapa, resumir um documento curto, uma análise simples do andamento;\n" +
        "- complexa: montar um cronograma completo, reorganizar ou replanejar boa parte da obra, ler documentos longos ou vários anexos " +
        "(plantas, memoriais, contratos), montar ou revisar um orçamento inteiro, análises detalhadas de prazo e custo.\n" +
        "Responda só com a classificação e um motivo curto (até 80 caracteres, em português, sem citar o usuário).";

    [GeneratedRegex(@"\b(mont[ae]\w*|cri[ae]\w*|ger[ae]\w*|elabor\w*|refa[çz]\w*|reorganiz\w*|replanej\w*|reprogram\w*|analis\w*|compar\w*|orçamento|cronograma|memorial|planta|contrato)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PedidoGrande();

    /// <summary>A regra grátis: simples sem perguntar à IA; nulo quando é preciso a triagem.</summary>
    public static string? PelaRegra(PedidoDeTriagem pedido)
    {
        var texto = pedido.Mensagem?.Trim() ?? "";
        return pedido.Anexos == 0 && texto.Length <= MensagemCurta && !PedidoGrande().IsMatch(texto) ? Simples : null;
    }

    public static string NivelDa(string complexidade) => complexidade switch
    {
        Media => Niveis.Intermediario,
        Complexa => Niveis.Maximo,
        _ => Niveis.Padrao,
    };

    /// <summary>A saída típica de cada tamanho de pedido (uma resposta curta, várias alterações, um cronograma inteiro).</summary>
    public static long SaidaEstimada(string complexidade) => complexidade switch
    {
        Complexa => 8_000,
        Media => 2_500,
        _ => 800,
    };

    /// <summary>
    /// A entrada estimada: o pedido (cerca de 4 caracteres por token) e os anexos (cerca de 1 token a cada 100 bytes: um PDF de
    /// 1 MB, umas 10 páginas, dá uns 10 mil tokens). É uma estimativa; o custo cobrado é o real.
    /// </summary>
    public static long EntradaEstimada(PedidoDeTriagem pedido) =>
        Math.Max(2_000, Math.Max(0, pedido.Caracteres) / 4 + Math.Clamp(pedido.BytesDosAnexos, 0, 50_000_000) / 100);

    /// <summary>O pedido à IA, no formato do Gemini (o servidor traduz para o Claude), com a resposta estruturada.</summary>
    public static JsonObject Corpo(PedidoDeTriagem pedido)
    {
        var mensagem = pedido.Mensagem?.Trim() ?? "";
        if (mensagem.Length > MaximoDaMensagem)
            mensagem = mensagem[..MaximoDaMensagem] + "…";
        var dados = $"Pedido do usuário:\n{mensagem}\n\nAnexos: {pedido.Anexos}" +
                    (pedido.BytesDosAnexos > 0 ? $" ({pedido.BytesDosAnexos / 1024} KB no total)" : "") + $"\nTarefas na obra: {pedido.Tarefas}";
        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Instrucoes }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = dados }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["maxOutputTokens"] = 300,
                ["responseSchema"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["complexidade"] = new JsonObject { ["type"] = "STRING", ["enum"] = new JsonArray(Simples, Media, Complexa) },
                        ["motivo"] = new JsonObject { ["type"] = "STRING" },
                    },
                    ["required"] = new JsonArray("complexidade", "motivo"),
                },
            },
        };
    }

    /// <summary>A classificação da resposta da IA (no formato do Gemini); nula se não veio uma válida.</summary>
    public static (string Complexidade, string Motivo)? Ler(byte[] resposta)
    {
        try
        {
            using var json = JsonDocument.Parse(resposta);
            var texto = json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            using var classificacao = JsonDocument.Parse(texto ?? "");
            var raiz = classificacao.RootElement;
            var complexidade = raiz.TryGetProperty("complexidade", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (complexidade is not (Simples or Media or Complexa))
                return null;
            var motivo = raiz.TryGetProperty("motivo", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()?.Trim() ?? "" : "";
            return (complexidade, motivo.Length > 120 ? motivo[..120] : motivo);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// O modelo para o nível desejado: o do nível, se o plano libera e a chave existe; senão, o do nível abaixo, e assim até o
    /// Padrão. Nulo só se nem o Padrão está disponível.
    /// </summary>
    public static ModeloDeIa? Escolher(CatalogoDeModelos catalogo, OpcoesDosPlanos planos, string plano, string nivelDesejado)
    {
        var desejado = Math.Max(0, NiveisEmOrdem.ToList().IndexOf(nivelDesejado));
        for (var i = desejado; i >= 0; i--)
            if (catalogo.Todos.FirstOrDefault(m => m.Nivel == NiveisEmOrdem[i] && planos.Libera(plano, m.Nivel) && catalogo.Disponivel(m)) is { } modelo)
                return modelo;
        return null;
    }
}
