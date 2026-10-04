using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ProjectPlanner.IA.Gemini;

/// <summary>Uma chamada à API do Gemini: monta o pedido com a chave no cabeçalho e traduz as falhas para o usuário.</summary>
internal static class ChamadaGemini
{
    public static void ValidarChave(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Any(char.IsWhiteSpace) || chave.Any(char.IsControl))
            throw new ArgumentException("A chave do Gemini está vazia ou tem espaços.");
    }

    public static HttpRequestMessage Pedido(HttpMethod metodo, string caminho, string chave)
    {
        var pedido = new HttpRequestMessage(metodo, new Uri(ProvedorGemini.Endereco, caminho));
        pedido.Headers.Add("x-goog-api-key", chave);
        return pedido;
    }

    /// <summary>Manda o pedido e devolve o JSON da resposta.</summary>
    /// <exception cref="OperationCanceledException">O usuário cancelou.</exception>
    /// <exception cref="ErroDoAssistenteException">Qualquer outra falha, com a mensagem para o usuário.</exception>
    public static async Task<JsonDocument> ExecutarAsync(HttpClient http, HttpRequestMessage pedido, string chave, string modelo,
        CancellationToken cancelamento)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await http.SendAsync(pedido, cancelamento);
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex) // o tempo limite do HttpClient
        {
            throw new ErroDoAssistenteException(
                $"O Gemini demorou mais de {Duracao(http.Timeout)} para responder. Tente de novo (com anexos menores, se houver).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ErroDoAssistenteException("Sem conexão com o Gemini. Verifique a internet e tente de novo.", ex);
        }

        using (resposta)
        {
            var corpo = await resposta.Content.ReadAsStringAsync(cancelamento);
            if (!resposta.IsSuccessStatusCode)
                throw Traduzir(resposta.StatusCode, corpo, chave, modelo);
            try
            {
                return JsonDocument.Parse(corpo);
            }
            catch (JsonException ex)
            {
                throw new ErroDoAssistenteException("O Gemini mandou uma resposta que não deu para entender. Tente de novo.", ex);
            }
        }
    }

    /// <summary>A mensagem para o usuário de uma resposta de erro da API (sem a chave, mesmo que o Google a repita).</summary>
    internal static ErroDoAssistenteException Traduzir(HttpStatusCode status, string corpo, string chave, string modelo)
    {
        var (mensagem, motivo) = LerErro(corpo);
        if (mensagem is not null && chave.Length > 0)
            mensagem = mensagem.Replace(chave, "(chave)", StringComparison.Ordinal);
        var codigo = (int)status;
        var texto = codigo switch
        {
            400 when motivo == "API_KEY_INVALID" || mensagem?.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) == true =>
                "A chave do Gemini não é válida. Confira a chave nas configurações do assistente.",
            400 => $"O Gemini recusou o pedido: {mensagem ?? "pedido inválido"}",
            401 or 403 => "A chave do Gemini não tem permissão para este uso. Confira no Google AI Studio se ela está ativa" +
                          (mensagem is null ? "." : $" ({mensagem})."),
            404 => $"O modelo \"{modelo}\" não existe ou não está disponível para esta chave. Confira o nome do modelo nas " +
                   "configurações do assistente.",
            429 => "O limite de uso do Gemini foi atingido. Espere um pouco e tente de novo (a cota da chave aparece no " +
                   "Google AI Studio).",
            >= 500 => $"O Gemini está com problemas no momento (erro {codigo}). Tente de novo em instantes.",
            _ => $"O Gemini respondeu com erro {codigo}" + (mensagem is null ? "." : $": {mensagem}"),
        };
        return new ErroDoAssistenteException(texto);
    }

    // {"error": {"code": 400, "message": "...", "status": "INVALID_ARGUMENT", "details": [{"reason": "API_KEY_INVALID"}]}}
    private static (string? Mensagem, string? Motivo) LerErro(string corpo)
    {
        try
        {
            using var json = JsonDocument.Parse(corpo);
            if (!json.RootElement.TryGetProperty("error", out var erro) || erro.ValueKind != JsonValueKind.Object)
                return (null, null);
            var mensagem = erro.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            string? motivo = null;
            if (erro.TryGetProperty("details", out var detalhes) && detalhes.ValueKind == JsonValueKind.Array)
                foreach (var detalhe in detalhes.EnumerateArray())
                    if (detalhe.ValueKind == JsonValueKind.Object && detalhe.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                        motivo ??= r.GetString();
            return (string.IsNullOrWhiteSpace(mensagem) ? null : mensagem.Trim(), motivo);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    internal static string Duracao(TimeSpan tempo)
    {
        if (tempo.TotalMinutes >= 1)
            return tempo.TotalMinutes == 1 ? "1 minuto" : $"{tempo.TotalMinutes.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))} minutos";
        var segundos = Math.Max(1, (int)tempo.TotalSeconds);
        return segundos == 1 ? "1 segundo" : $"{segundos} segundos";
    }
}
