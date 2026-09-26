namespace ProjectPlanner.App.Services;

/// <summary>O projeto a abrir, entre os arquivos pedidos pelo sistema, e quantos outros ficaram de fora.</summary>
/// <param name="Caminho">Caminho completo do primeiro projeto pedido; nulo se nenhum argumento é um projeto.</param>
/// <param name="NaoAbertos">Quantos outros projetos foram pedidos juntos (o ProjectPlanner abre um por janela).</param>
public sealed record ProjetoPedido(string? Caminho, int NaoAbertos);

/// <summary>
/// Os arquivos que o sistema pede para abrir: argumentos da linha de comando (Windows, "Abrir com",
/// terminal) ou arquivos entregues ao app pelo Finder (macOS).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Só contam os que terminam em <c>.pplan</c> (maiúsculas ou minúsculas); o resto é ignorado em
/// silêncio, inclusive opções que o sistema às vezes passa (como <c>-psn_…</c> no macOS).</item>
/// <item>Aceita caminhos relativos (à pasta atual) e URIs <c>file://</c>, e devolve o caminho completo.</item>
/// <item>Vale o primeiro projeto; os demais são contados em <see cref="ProjetoPedido.NaoAbertos"/>
/// (repetições do mesmo arquivo não contam).</item>
/// <item>Não confere se o arquivo existe: quem abre trata arquivo inexistente ou inválido com as mesmas
/// mensagens do "Abrir…".</item>
/// </list>
/// </remarks>
public static class ArquivosPedidos
{
    public const string Extensao = "." + DialogosAvalonia.Extensao;

    public static ProjetoPedido Interpretar(IEnumerable<string?> argumentos)
    {
        ArgumentNullException.ThrowIfNull(argumentos);
        var projetos = argumentos
            .Select(CaminhoDoProjeto)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ProjetoPedido(projetos.FirstOrDefault(), Math.Max(0, projetos.Count - 1));
    }

    private static string? CaminhoDoProjeto(string? argumento)
    {
        var texto = argumento?.Trim();
        if (string.IsNullOrEmpty(texto) || !texto.EndsWith(Extensao, StringComparison.OrdinalIgnoreCase))
            return null;
        if (texto.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(texto, UriKind.Absolute, out var uri) && uri.IsFile)
            texto = uri.LocalPath;
        try
        {
            return Path.GetFullPath(texto);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
