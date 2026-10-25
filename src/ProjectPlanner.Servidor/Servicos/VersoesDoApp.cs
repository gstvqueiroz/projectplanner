using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 69: as versões do programa. Cada pedido do programa traz a versão dele no cabeçalho <see cref="Cabecalho"/>: abaixo de
/// <c>App:VersaoMinima</c> o servidor responde 426 com o aviso para atualizar; e a versão de cada conta fica no banco (só
/// quando muda), para o painel mostrar quantas contas usam cada versão.
/// </summary>
public sealed class VersoesDoApp(IServiceScopeFactory escopos, TimeProvider relogio, ILogger<VersoesDoApp> log)
{
    public const string Cabecalho = "X-ProjectPlanner-Versao";

    private readonly ConcurrentDictionary<Guid, string> _vistas = new();

    /// <summary>"0.69.0" → 0.69.0; o que não é versão, nulo.</summary>
    public static Version? Ler(string? texto) =>
        Version.TryParse(texto?.Trim(), out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;

    public static string Aviso(string versao, OpcoesDoApp app) =>
        $"Esta versão do ProjectPlanner ({versao}) não funciona mais com o servidor. Atualize para a {app.UltimaVersao ?? app.VersaoMinima} ou mais nova" +
        (string.IsNullOrWhiteSpace(app.Download) ? "." : $": {app.Download.Trim()}");

    /// <summary>O middleware: confere a versão mínima nas rotas /v1 e anota a versão da conta.</summary>
    public async Task ConferirAsync(HttpContext contexto, Func<Task> seguir)
    {
        var texto = contexto.Request.Headers[Cabecalho].ToString();
        var versao = Ler(texto);
        if (versao is not null && contexto.Request.Path.StartsWithSegments("/v1") && !contexto.Request.Path.StartsWithSegments("/v1/versao"))
        {
            var app = contexto.RequestServices.GetRequiredService<IOptions<OpcoesDoServidor>>().Value.App;
            if (Ler(app.VersaoMinima) is { } minima && versao < minima)
            {
                contexto.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                await contexto.Response.WriteAsJsonAsync(new Rotas.Erro(Aviso(texto.Trim(), app)));
                return;
            }
            if (Guid.TryParse(contexto.User.FindFirstValue("sub") ?? contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario))
                await AnotarAsync(usuario, versao.ToString(3));
        }
        await seguir();
    }

    private async Task AnotarAsync(Guid usuario, string versao)
    {
        if (_vistas.TryGetValue(usuario, out var anterior) && anterior == versao)
            return;
        _vistas[usuario] = versao;
        try
        {
            using var escopo = escopos.CreateScope();
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDoServidor>();
            if (await banco.Usuarios.FindAsync(usuario) is { } conta)
            {
                (conta.VersaoDoApp, conta.VersaoVistaEm) = (versao, relogio.GetUtcNow().UtcDateTime);
                await banco.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _vistas.TryRemove(usuario, out _);
            log.LogWarning(ex, "Não deu para anotar a versão do programa da conta {Usuario}", usuario);
        }
    }
}
