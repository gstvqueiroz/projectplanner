using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Bloco 69: manda ao suporte (o painel do servidor) os erros inesperados do programa, quando o usuário ligou a opção em Conta >
/// Minha conta e está com a conta conectada. Vai o tipo do erro, a mensagem e o começo da pilha, com as pastas do usuário
/// trocadas por "~" e os caminhos do código encurtados; nunca o conteúdo das obras. No máximo <see cref="MaximoPorSessao"/>
/// por sessão, e o mesmo erro uma vez só. Nunca lança: sem internet, o erro fica só no registro local.
/// </summary>
public sealed partial class EnvioDeErros(IContaProjectPlanner conta, IPreferenciasInterface preferencias)
{
    public const int MaximoPorSessao = 10;

    private readonly HashSet<string> _enviados = [];
    private readonly Lock _trava = new();

    public bool Ligado => preferencias.Carregar()?.EnviarErros == true && conta.Conectada;

    /// <summary>Manda o erro (se ligado), em segundo plano; a tarefa termina quando o servidor respondeu ou desistiu.</summary>
    public Task EnviarAsync(Exception erro, string origem)
    {
        try
        {
            if (!Ligado)
                return Task.CompletedTask;
            var mandado = Montar(erro, origem, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            lock (_trava)
            {
                if (_enviados.Count >= MaximoPorSessao || !_enviados.Add($"{mandado.Tipo}|{mandado.Mensagem}"))
                    return Task.CompletedTask;
            }
            return Task.Run(async () =>
            {
                try
                {
                    using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await conta.EnviarErroAsync(mandado, tempo.Token);
                }
                catch (Exception)
                {
                    // Sem internet ou sem sessão: o erro já está no registro local.
                }
            });
        }
        catch (Exception)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>O que vai ao suporte: o erro de dentro (o de verdade, num AggregateException), sem as pastas do usuário.</summary>
    public static ErroParaOSuporte Montar(Exception erro, string origem, string pastaDoUsuario)
    {
        ArgumentNullException.ThrowIfNull(erro);
        var real = erro is AggregateException { InnerExceptions.Count: 1 } agregado ? agregado.InnerExceptions[0] : erro;
        string Limpar(string texto)
        {
            if (!string.IsNullOrWhiteSpace(pastaDoUsuario))
                texto = texto.Replace(pastaDoUsuario, "~", StringComparison.OrdinalIgnoreCase);
            return CaminhoDoCodigo().Replace(texto, "$1");
        }
        return new ErroParaOSuporte(VersaoDoProduto.Atual, Sistema(), origem, real.GetType().FullName ?? real.GetType().Name,
            Cortar(Limpar(real.Message), 1000), real.StackTrace is { Length: > 0 } pilha ? Cortar(Limpar(pilha.Trim()), 4000) : null);
    }

    private static string Sistema() => $"{RuntimeInformation.OSDescription.Trim()} {RuntimeInformation.OSArchitecture}".Trim();

    private static string Cortar(string texto, int tamanho) => texto.Length <= tamanho ? texto : texto[..tamanho];

    // "/Users/quem/Documents/Project/src/" ou "C:\build\src\": o caminho de quem compilou, até a pasta src (ou tests).
    [GeneratedRegex(@"(?:[A-Za-z]:)?[\\/](?:[^\\/\s:]+[\\/])*?((?:src|tests)[\\/])")]
    private static partial Regex CaminhoDoCodigo();
}
