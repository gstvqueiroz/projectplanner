using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Diagnostics;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// 0.55.1: os erros inesperados. Antes, o usuário via só "Erro no servidor" e o motivo ficava apenas no log do Render. Agora
/// cada um ganha um código curto (que aparece na mensagem), fica no banco com a rota, o tipo, a mensagem e o começo da
/// pilha, e o painel /admin mostra os últimos. Sem conteúdo dos pedidos.
/// </summary>
public static class Falhas
{
    public const int Guardadas = 200;
    private const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NovoCodigo() => string.Concat(Enumerable.Range(0, 6).Select(_ => Letras[RandomNumberGenerator.GetInt32(Letras.Length)]));

    public static string Mensagem(string codigo) => $"Erro no servidor (código {codigo}). Tente de novo; se continuar, avise o suporte com o código.";

    /// <summary>A resposta do tratador de exceções: registra (sem nunca falhar por isso) e devolve o código.</summary>
    public static async Task ResponderAsync(HttpContext contexto)
    {
        var excecao = contexto.Features.Get<IExceptionHandlerFeature>()?.Error;
        var codigo = NovoCodigo();
        var log = contexto.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Falhas));
        log.LogError(excecao, "Erro inesperado {Codigo} em {Metodo} {Rota}", codigo, contexto.Request.Method, contexto.Request.Path);
        try
        {
            using var escopo = contexto.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDoServidor>();
            var agora = escopo.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            var usuario = contexto.User.FindFirstValue("sub") ?? contexto.User.FindFirstValue(ClaimTypes.NameIdentifier);
            banco.FalhasDoServidor.Add(new FalhaDoServidor
            {
                Codigo = codigo, Quando = agora, Rota = Cortar($"{contexto.Request.Method} {contexto.Request.Path}", 200),
                Tipo = Cortar(excecao?.GetType().FullName ?? "(desconhecido)", 200), Mensagem = Cortar(excecao?.Message ?? "", 1000),
                Pilha = excecao is null ? null : Cortar(excecao.ToString(), 4000), UsuarioId = Guid.TryParse(usuario, out var id) ? id : null,
            });
            await banco.SaveChangesAsync(CancellationToken.None);
            // Só as mais recentes ficam.
            var velhas = banco.FalhasDoServidor.OrderByDescending(f => f.Id).Skip(Guardadas).Select(f => f.Id).ToList();
            if (velhas.Count > 0)
            {
                banco.FalhasDoServidor.RemoveRange(banco.FalhasDoServidor.Where(f => velhas.Contains(f.Id)));
                await banco.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Não deu para guardar o erro {Codigo}", codigo);
        }
        if (!contexto.Response.HasStarted)
        {
            contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await contexto.Response.WriteAsJsonAsync(new Rotas.Erro(Mensagem(codigo)));
        }
    }

    private static string Cortar(string texto, int tamanho) => texto.Length <= tamanho ? texto : texto[..tamanho];
}
