using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>O que o Google garante sobre quem entrou (do idToken validado).</summary>
public sealed record ContaGoogle(string Id, string Email, bool EmailVerificado, string Nome);

/// <summary>Valida o idToken do "Entrar com Google" (assinatura do Google, validade e a audiência = os nossos client IDs).</summary>
public interface IValidadorDoGoogle
{
    /// <returns>Nulo se o token não vale.</returns>
    Task<ContaGoogle?> ValidarAsync(string idToken);
}

public sealed class ValidadorDoGoogle(IOptions<OpcoesDoServidor> opcoes, ILogger<ValidadorDoGoogle> log) : IValidadorDoGoogle
{
    public async Task<ContaGoogle?> ValidarAsync(string idToken)
    {
        var clientIds = opcoes.Value.Google.Audiencias;
        if (clientIds.Length == 0)
            return null;
        try
        {
            var p = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = clientIds });
            return new ContaGoogle(p.Subject, p.Email, p.EmailVerified, p.Name ?? p.Email);
        }
        catch (InvalidJwtException ex)
        {
            log.LogInformation("idToken do Google recusado: {Motivo}", ex.Message);
            return null;
        }
    }
}

/// <summary>
/// Bloco 45: troca o código que o Google mandou ao navegador (login do app, com PKCE) pelo idToken, usando o segredo do
/// client ID do app, que fica só no servidor.
/// </summary>
public interface ITrocaDoCodigoDoGoogle
{
    /// <returns>O idToken; nulo se o Google recusou o código.</returns>
    Task<string?> TrocarAsync(string codigo, string verificador, string redirecionamento, CancellationToken cancelamento);
}

public sealed class TrocaDoCodigoDoGoogle(IHttpClientFactory clientes, IOptions<OpcoesDoServidor> opcoes, ILogger<TrocaDoCodigoDoGoogle> log)
    : ITrocaDoCodigoDoGoogle
{
    public const string Cliente = "google";

    public async Task<string?> TrocarAsync(string codigo, string verificador, string redirecionamento, CancellationToken cancelamento)
    {
        var google = opcoes.Value.Google;
        using var resposta = await clientes.CreateClient(Cliente).PostAsync(google.EnderecoDoToken, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = codigo,
            ["code_verifier"] = verificador,
            ["redirect_uri"] = redirecionamento,
            ["client_id"] = google.ClientIdDoApp!,
            ["client_secret"] = google.SegredoDoApp!,
            ["grant_type"] = "authorization_code",
        }), cancelamento);
        var corpo = await resposta.Content.ReadAsStringAsync(cancelamento);
        if (!resposta.IsSuccessStatusCode)
        {
            log.LogInformation("O Google recusou o código do login ({Status}): {Corpo}", (int)resposta.StatusCode, corpo.Length > 300 ? corpo[..300] : corpo);
            return null;
        }
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(corpo);
            return json.RootElement.TryGetProperty("id_token", out var id) ? id.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
