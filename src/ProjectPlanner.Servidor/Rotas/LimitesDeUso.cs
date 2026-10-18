using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ProjectPlanner.Servidor.Rotas;

/// <summary>Os limites de pedidos por minuto: por endereço IP nas rotas de conta, por usuário na IA.</summary>
public static class LimitesDeUso
{
    public const string Contas = "contas";
    public const string Ia = "ia";

    public static void Configurar(RateLimiterOptions limites, OpcoesDosLimites opcoes)
    {
        limites.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limites.OnRejected = async (contexto, c) =>
        {
            contexto.HttpContext.Response.Headers.RetryAfter = "60";
            await contexto.HttpContext.Response.WriteAsJsonAsync(new Erro("Muitos pedidos seguidos. Espere um minuto e tente de novo."), c);
        };
        limites.AddPolicy(Contas, http => RateLimitPartition.GetFixedWindowLimiter(
            "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "?"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = opcoes.PedidosDeContaPorMinuto, Window = TimeSpan.FromMinutes(1) }));
        limites.AddPolicy(Ia, http => RateLimitPartition.GetFixedWindowLimiter(
            "usuario:" + (http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub") ?? "?"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = opcoes.PedidosDeIaPorMinuto, Window = TimeSpan.FromMinutes(1) }));
    }
}
