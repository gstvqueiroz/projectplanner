using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Rotas;

/// <summary>Bloco 69: o que o programa manda ao suporte quando dá um erro inesperado (só com a opção ligada em Minha conta).</summary>
public sealed record ErroDoPrograma(string? Versao, string? Sistema, string? Origem, string? Tipo, string? Mensagem, string? Pilha);

/// <summary>
/// Bloco 69: as rotas do programa em si: a versão (para o aviso de versão nova, sem conta) e os erros que o usuário escolheu
/// mandar ao suporte (com conta; os últimos <see cref="Guardados"/> ficam, no máximo <see cref="PorDia"/> por conta por dia).
/// </summary>
public static class Programa
{
    public const int Guardados = 500;
    public const int PorDia = 20;

    public static void Mapear(WebApplication app)
    {
        app.MapGet("/v1/versao", Versao);
        app.MapPost("/v1/erros", Erro).RequireAuthorization().RequireRateLimiting(LimitesDeUso.Ia);
    }

    private static IResult Versao(IOptions<OpcoesDoServidor> opcoes)
    {
        var app = opcoes.Value.App;
        string? Limpa(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        return Results.Ok(new { ultima = Limpa(app.UltimaVersao), minima = Limpa(app.VersaoMinima), download = Limpa(app.Download) });
    }

    private static async Task<IResult> Erro(ErroDoPrograma erro, ClaimsPrincipal quem, BancoDoServidor banco, TimeProvider relogio, CancellationToken c)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return Contas.Falha(401, "Sessão vencida: entre de novo.");
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (await banco.FalhasDoApp.CountAsync(f => f.UsuarioId == usuario.Id && f.Quando >= agora.AddDays(-1), c) >= PorDia)
            return Results.Accepted(); // o programa não precisa saber; o painel já tem os do dia
        banco.FalhasDoApp.Add(new FalhaDoApp
        {
            Quando = agora, UsuarioId = usuario.Id, Versao = Cortar(erro.Versao, 20), Sistema = Cortar(erro.Sistema, 60), Origem = Cortar(erro.Origem, 60),
            Tipo = Cortar(erro.Tipo, 200), Mensagem = Cortar(erro.Mensagem, 1000), Pilha = erro.Pilha is null ? null : Cortar(erro.Pilha, 4000),
        });
        await banco.SaveChangesAsync(c);
        var velhas = await banco.FalhasDoApp.OrderByDescending(f => f.Id).Skip(Guardados).Select(f => f.Id).ToListAsync(c);
        if (velhas.Count > 0)
            await banco.FalhasDoApp.Where(f => velhas.Contains(f.Id)).ExecuteDeleteAsync(c);
        return Results.Accepted();
    }

    private static string Cortar(string? texto, int tamanho) => (texto ?? "").Trim() is var t && t.Length > tamanho ? t[..tamanho] : (texto ?? "").Trim();
}
