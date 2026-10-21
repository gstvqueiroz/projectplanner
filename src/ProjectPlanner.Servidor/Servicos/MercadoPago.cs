using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Uma falha ao falar com o Mercado Pago, com a mensagem para o usuário (o detalhe vai para o log).</summary>
public sealed class ErroDoMercadoPagoException(string mensagem) : Exception(mensagem);

/// <summary>Um link de pagamento criado: o id no Mercado Pago e o endereço que o programa abre no navegador.</summary>
public sealed record LinkDePagamento(string Id, string Url);

/// <summary>Uma assinatura como o Mercado Pago informa (só o que o servidor usa).</summary>
public sealed record AssinaturaNoMercadoPago(string Id, string Status, string? Referencia, decimal Valor, DateTime? ProximaCobranca);

/// <summary>Um pagamento como o Mercado Pago informa (só o que o servidor usa).</summary>
public sealed record PagamentoNoMercadoPago(string Id, string Status, string? Referencia, decimal Valor);

/// <summary>
/// Bloco 47: as chamadas à API do Mercado Pago, com o access token do servidor. A assinatura do Profissional é um
/// "preapproval" sem plano (o valor vai em cada uma), criada como pendente: o Mercado Pago devolve o link onde o usuário
/// escolhe Pix ou cartão. O pacote de créditos é uma "preference" do Checkout Pro (um pagamento só).
/// </summary>
public sealed class MercadoPago(IHttpClientFactory clientes, IOptions<OpcoesDoServidor> opcoes, ILogger<MercadoPago> log)
{
    public const string Cliente = "mercadopago";

    /// <summary>A referência de um pacote: "creditos:{usuario}:{créditos}:{aleatório}" (a assinatura usa só o id do usuário).</summary>
    public const string PrefixoDosCreditos = "creditos:";

    private OpcoesDoMercadoPago Config => opcoes.Value.MercadoPago;

    /// <summary>O endereço público do servidor, sem a barra do fim.</summary>
    public string? UrlPublica => (string.IsNullOrWhiteSpace(Config.UrlPublica) ? opcoes.Value.Telegram.UrlPublica : Config.UrlPublica)?.Trim().TrimEnd('/');

    public async Task<LinkDePagamento> CriarAssinaturaAsync(Guid usuario, string email, decimal valor, CancellationToken c)
    {
        var corpo = new JsonObject
        {
            ["reason"] = "ProjectPlanner Profissional",
            ["external_reference"] = usuario.ToString(),
            // No modo de teste, comprador e vendedor precisam ser usuários de teste.
            ["payer_email"] = Config.ModoDeTeste && !string.IsNullOrWhiteSpace(Config.EmailDoCompradorDeTeste) ? Config.EmailDoCompradorDeTeste.Trim() : email,
            ["auto_recurring"] = new JsonObject
            {
                ["frequency"] = 1,
                ["frequency_type"] = "months",
                ["transaction_amount"] = valor,
                ["currency_id"] = "BRL",
            },
            ["back_url"] = $"{UrlPublica}/pagamento",
            ["status"] = "pending",
        };
        var resposta = await PedirAsync(HttpMethod.Post, "preapproval", corpo, c);
        return new LinkDePagamento(Texto(resposta, "id") ?? throw Falhou("a assinatura veio sem id"),
            Texto(resposta, "init_point") ?? throw Falhou("a assinatura veio sem link"));
    }

    public async Task<LinkDePagamento> CriarCompraDeCreditosAsync(Guid usuario, long creditos, decimal valor, CancellationToken c)
    {
        var corpo = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["title"] = $"ProjectPlanner: {creditos.ToString("N0", new CultureInfo("pt-BR"))} créditos de IA",
                ["quantity"] = 1,
                ["unit_price"] = valor,
                ["currency_id"] = "BRL",
            }),
            ["external_reference"] = $"{PrefixoDosCreditos}{usuario}:{creditos.ToString(CultureInfo.InvariantCulture)}:{Guid.NewGuid():N}",
            ["notification_url"] = $"{UrlPublica}/v1/pagamentos/webhook",
            ["back_urls"] = new JsonObject
            {
                ["success"] = $"{UrlPublica}/pagamento", ["pending"] = $"{UrlPublica}/pagamento", ["failure"] = $"{UrlPublica}/pagamento",
            },
        };
        var resposta = await PedirAsync(HttpMethod.Post, "checkout/preferences", corpo, c);
        // Com um token TEST-, o link do ambiente de teste (com as credenciais de um vendedor de teste, o link normal).
        var url = (Config.TokenDeTeste ? Texto(resposta, "sandbox_init_point") : null) ?? Texto(resposta, "init_point");
        return new LinkDePagamento(Texto(resposta, "id") ?? throw Falhou("a compra veio sem id"), url ?? throw Falhou("a compra veio sem link"));
    }

    public async Task<AssinaturaNoMercadoPago> AssinaturaAsync(string id, CancellationToken c)
    {
        var r = await PedirAsync(HttpMethod.Get, $"preapproval/{Uri.EscapeDataString(id)}", null, c);
        return new AssinaturaNoMercadoPago(Texto(r, "id") ?? id, Texto(r, "status") ?? "", Texto(r, "external_reference"),
            Numero(r?["auto_recurring"], "transaction_amount"), Data(r, "next_payment_date"));
    }

    public async Task CancelarAssinaturaAsync(string id, CancellationToken c) =>
        await PedirAsync(HttpMethod.Put, $"preapproval/{Uri.EscapeDataString(id)}", new JsonObject { ["status"] = "cancelled" }, c);

    public async Task<PagamentoNoMercadoPago> PagamentoAsync(string id, CancellationToken c)
    {
        var r = await PedirAsync(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(id)}", null, c);
        return new PagamentoNoMercadoPago(Texto(r, "id") ?? id, Texto(r, "status") ?? "", Texto(r, "external_reference"), Numero(r, "transaction_amount"));
    }

    private async Task<JsonNode?> PedirAsync(HttpMethod metodo, string caminho, JsonObject? corpo, CancellationToken c)
    {
        using var pedido = new HttpRequestMessage(metodo, new Uri(new Uri(Config.Endereco), caminho));
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.AccessToken?.Trim());
        if (corpo is not null)
        {
            pedido.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
            pedido.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        }
        HttpResponseMessage resposta;
        try
        {
            resposta = await clientes.CreateClient(Cliente).SendAsync(pedido, c);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !c.IsCancellationRequested)
        {
            log.LogWarning(ex, "Falha ao chamar o Mercado Pago ({Caminho})", caminho);
            throw new ErroDoMercadoPagoException("O Mercado Pago não respondeu. Tente de novo em instantes.");
        }
        using (resposta)
        {
            var texto = await resposta.Content.ReadAsStringAsync(c);
            if (!resposta.IsSuccessStatusCode)
            {
                log.LogWarning("O Mercado Pago recusou {Metodo} {Caminho}: {Status} {Corpo}", metodo, caminho, (int)resposta.StatusCode,
                    texto.Length > 500 ? texto[..500] : texto);
                throw new ErroDoMercadoPagoException("O Mercado Pago recusou o pedido. Tente de novo; se continuar, fale com o suporte.");
            }
            try
            {
                return texto.Length == 0 ? null : JsonNode.Parse(texto);
            }
            catch (JsonException)
            {
                throw Falhou("resposta sem JSON");
            }
        }
    }

    private ErroDoMercadoPagoException Falhou(string motivo)
    {
        log.LogWarning("Resposta inesperada do Mercado Pago: {Motivo}", motivo);
        return new ErroDoMercadoPagoException("O Mercado Pago mandou uma resposta inesperada. Tente de novo.");
    }

    // O id vem como número (pagamentos) ou texto (assinaturas).
    private static string? Texto(JsonNode? no, string campo) => no?[campo] switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<long>(out var n) => n.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static decimal Numero(JsonNode? no, string campo) =>
        no?[campo] is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : 0;

    private static DateTime? Data(JsonNode? no, string campo) =>
        Texto(no, campo) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.UtcDateTime : null;
}

/// <summary>
/// Bloco 47: confere a assinatura (<c>x-signature: ts=…,v1=…</c>) de um aviso do Mercado Pago: o HMAC-SHA256, com a assinatura
/// secreta do webhook, de <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c> (o id em minúsculas).
/// </summary>
public static class AssinaturaDoWebhook
{
    public static bool Valida(string segredo, string? cabecalho, string? idDoPedido, string? idDoRecurso)
    {
        if (string.IsNullOrWhiteSpace(segredo) || string.IsNullOrWhiteSpace(cabecalho))
            return false;
        string? ts = null, v1 = null;
        foreach (var parte in cabecalho.Split(','))
        {
            var (chave, valor) = parte.Split('=', 2) is [var k, var v] ? (k.Trim(), v.Trim()) : ("", "");
            if (chave == "ts")
                ts = valor;
            else if (chave == "v1")
                v1 = valor;
        }
        if (ts is null || v1 is null)
            return false;
        var manifesto = new StringBuilder();
        if (!string.IsNullOrEmpty(idDoRecurso))
            manifesto.Append("id:").Append(idDoRecurso.ToLowerInvariant()).Append(';');
        if (!string.IsNullOrEmpty(idDoPedido))
            manifesto.Append("request-id:").Append(idDoPedido).Append(';');
        manifesto.Append("ts:").Append(ts).Append(';');
        var esperado = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo.Trim()), Encoding.UTF8.GetBytes(manifesto.ToString())));
        return Segredos.Iguais(esperado, v1.ToLowerInvariant());
    }
}
