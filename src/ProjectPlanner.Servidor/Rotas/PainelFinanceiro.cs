using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

/// <summary>
/// Bloco 48: o painel financeiro. A página (<c>/admin</c>) é estática e não tem dado nenhum; os números vêm de
/// <c>/v1/admin/painel</c>, que exige a chave de admin no cabeçalho X-Chave-Admin (a página pede a chave e a guarda só na aba).
/// </summary>
public static class PainelFinanceiro
{
    private const string Seguranca = "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; img-src 'self' data:; " +
                                     "frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public static void Mapear(WebApplication app)
    {
        app.MapGet("/admin", (HttpContext http) => Arquivo(http, "painel.html", "text/html; charset=utf-8"));
        app.MapGet("/admin/painel.js", (HttpContext http) => Arquivo(http, "painel.js", "text/javascript; charset=utf-8"));
        app.MapGet("/v1/admin/painel", Dados).RequireRateLimiting(LimitesDeUso.Contas);
    }

    private static IResult Arquivo(HttpContext http, string nome, string tipo)
    {
        http.Response.Headers.ContentSecurityPolicy = Seguranca;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers.CacheControl = "no-store";
        using var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ProjectPlanner.Servidor.Painel.{nome}")
                            ?? throw new InvalidOperationException($"A página {nome} não foi embutida no servidor.");
        using var leitor = new StreamReader(recurso);
        return Results.Text(leitor.ReadToEnd(), tipo);
    }

    /// <summary>Os números do período (padrão: o mês corrente, até hoje).</summary>
    private static async Task<IResult> Dados(HttpRequest http, string? de, string? ate, BancoDoServidor banco, CatalogoDeModelos catalogo,
        IOptions<OpcoesDoServidor> opcoes, TimeProvider relogio, CancellationToken c)
    {
        var chave = opcoes.Value.Admin.Chave;
        http.HttpContext.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(chave) || !Segredos.Iguais(chave, http.Headers[Admin.Cabecalho].ToString()))
            return Contas.Falha(401, "Chave de admin ausente ou errada.");
        var agora = relogio.GetUtcNow().UtcDateTime;
        var hoje = DateOnly.FromDateTime(agora);
        if (!Data(de, new DateOnly(hoje.Year, hoje.Month, 1), out var inicio) || !Data(ate, hoje, out var fim))
            return Contas.Falha(400, "Use datas no formato AAAA-MM-DD.");
        try
        {
            return Results.Ok(await Painel.MontarAsync(banco, catalogo, opcoes.Value, inicio, fim, agora, c));
        }
        catch (ArgumentException ex)
        {
            return Contas.Falha(400, ex.Message);
        }
    }

    private static bool Data(string? texto, DateOnly padrao, out DateOnly data)
    {
        data = padrao;
        return string.IsNullOrWhiteSpace(texto) || DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
    }
}
