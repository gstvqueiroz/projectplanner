using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

public sealed record PedidoDePlano(string? Plano);

/// <summary>Bloco 47: créditos avulsos a dar (positivo) ou tirar (negativo) de uma conta.</summary>
public sealed record PedidoDeCreditos(decimal? Creditos);

/// <summary>
/// Bloco 44: trocar o plano de uma conta à mão (a cobrança automática é do Bloco 47); desde o 47, também dar créditos avulsos.
/// Protegida pela chave de admin no
/// cabeçalho X-Chave-Admin; sem chave configurada, a rota fica fechada.
/// </summary>
public static class Admin
{
    public const string Cabecalho = "X-Chave-Admin";

    public const long MaximoDeCreditosPorVez = 1_000_000;

    public static void Mapear(WebApplication app)
    {
        app.MapPost("/v1/admin/usuarios/{email}/plano", TrocarPlano).RequireRateLimiting(LimitesDeUso.Contas);
        app.MapPost("/v1/admin/usuarios/{email}/creditos", DarCreditos).RequireRateLimiting(LimitesDeUso.Contas);
    }

    private static bool Autorizado(HttpRequest http, OpcoesDoServidor opcoes) =>
        !string.IsNullOrWhiteSpace(opcoes.Admin.Chave) && Segredos.Iguais(opcoes.Admin.Chave, http.Headers[Cabecalho].ToString());

    /// <summary>Bloco 47: dar créditos avulsos a uma conta (cortesia, teste ou reembolso). Eles não vencem no fim do mês.</summary>
    private static async Task<IResult> DarCreditos(string email, PedidoDeCreditos pedido, HttpRequest http, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes)
    {
        if (!Autorizado(http, opcoes.Value))
            return Contas.Falha(401, "Chave de admin ausente ou errada.");
        if (pedido.Creditos is not { } creditos || creditos == 0 || Math.Abs(creditos) > MaximoDeCreditosPorVez)
            return Contas.Falha(400, $"Informe os créditos (diferente de zero, até {MaximoDeCreditosPorVez:N0} por vez).");
        var normalizado = Segredos.NormalizarEmail(email);
        if (await banco.Usuarios.FirstOrDefaultAsync(u => u.EmailNormalizado == normalizado) is not { } usuario)
            return Contas.Falha(404, "Não há conta com esse e-mail.");
        var planos = opcoes.Value.Planos;
        usuario.MicrodolaresAvulsos = Math.Max(0, usuario.MicrodolaresAvulsos + planos.Microdolares(creditos));
        await banco.SaveChangesAsync();
        return Results.Ok(new { creditosAvulsos = Math.Round(planos.Creditos(usuario.MicrodolaresAvulsos), 1, MidpointRounding.AwayFromZero) });
    }

    private static async Task<IResult> TrocarPlano(string email, PedidoDePlano pedido, HttpRequest http, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes)
    {
        if (!Autorizado(http, opcoes.Value))
            return Contas.Falha(401, "Chave de admin ausente ou errada.");
        if (pedido.Plano is not { } plano || !Planos.Todos.Contains(plano))
            return Contas.Falha(400, $"Plano desconhecido. Use um destes: {string.Join(", ", Planos.Todos)}.");
        var normalizado = Segredos.NormalizarEmail(email);
        if (await banco.Usuarios.FirstOrDefaultAsync(u => u.EmailNormalizado == normalizado) is not { } usuario)
            return Contas.Falha(404, "Não há conta com esse e-mail.");
        usuario.Plano = plano;
        usuario.ProfissionalAte = null; // Bloco 47: o plano dado pelo admin não vence
        await banco.SaveChangesAsync();
        return Results.Ok(EmissorDeTokens.Perfil(usuario));
    }
}
