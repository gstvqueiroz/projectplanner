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
        var clientIds = opcoes.Value.Google.ClientIds;
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
