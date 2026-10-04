using System.Text.Json;

namespace ProjectPlanner.IA;

/// <summary>
/// A resposta do assistente em JSON (Bloco 24), igual para qualquer provedor: {"mensagem": "...", "alteracoes": [...]}.
/// O Gemini é obrigado a seguir o formato (esquema de resposta); outro provedor recebe o mesmo pelas instruções.
/// </summary>
public static class FormatoDaResposta
{
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
            return new RespostaIA(mensagem.Trim() + (cortada ? NotaDeCorte : "")) { Alteracoes = alteracoes };
        }
    }

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
