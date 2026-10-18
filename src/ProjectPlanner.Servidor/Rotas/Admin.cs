using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

public sealed record PedidoDePlano(string? Plano);

/// <summary>
/// Bloco 44: trocar o plano de uma conta à mão (até a cobrança automática do Bloco 46). Protegida pela chave de admin no
/// cabeçalho X-Chave-Admin; sem chave configurada, a rota fica fechada.
/// </summary>
public static class Admin
{
    public const string Cabecalho = "X-Chave-Admin";

    public static void Mapear(WebApplication app) =>
        app.MapPost("/v1/admin/usuarios/{email}/plano", TrocarPlano).RequireRateLimiting(LimitesDeUso.Contas);

    private static async Task<IResult> TrocarPlano(string email, PedidoDePlano pedido, HttpRequest http, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes)
    {
        var chave = opcoes.Value.Admin.Chave;
        if (string.IsNullOrWhiteSpace(chave) || !Segredos.Iguais(chave, http.Headers[Cabecalho].ToString()))
            return Contas.Falha(401, "Chave de admin ausente ou errada.");
        if (pedido.Plano is not { } plano || !Planos.Todos.Contains(plano))
            return Contas.Falha(400, $"Plano desconhecido. Use um destes: {string.Join(", ", Planos.Todos)}.");
        var normalizado = Segredos.NormalizarEmail(email);
        if (await banco.Usuarios.FirstOrDefaultAsync(u => u.EmailNormalizado == normalizado) is not { } usuario)
            return Contas.Falha(404, "Não há conta com esse e-mail.");
        usuario.Plano = plano;
        await banco.SaveChangesAsync();
        return Results.Ok(EmissorDeTokens.Perfil(usuario));
    }
}
