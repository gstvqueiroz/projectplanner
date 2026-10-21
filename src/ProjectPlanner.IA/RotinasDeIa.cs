namespace ProjectPlanner.IA;

/// <summary>
/// Bloco 50: as rotinas do programa que chamam a IA. Pela conta, cada pedido diz ao servidor de qual rotina ele é (no
/// cabeçalho <see cref="Cabecalho"/>), e o painel financeiro mostra quanto custa cada uma. Nunca vai ao Google com a chave
/// própria (variável de ambiente): lá o cabeçalho não serve para nada.
/// </summary>
public static class RotinasDeIa
{
    public const string Cabecalho = "X-Rotina";

    public const string Chat = "chat";
    public const string Telegram = "telegram";
    public const string ClassificarFotos = "classificar-fotos";
    public const string Legendas = "legendas";
    public const string AvancoPelasFotos = "avanco-pelas-fotos";
    public const string Relatorio = "relatorio";
    public const string Orcamento = "orcamento";
    public const string AnaliseDaObra = "analise-da-obra";

    /// <summary>Qualquer outra (um programa mais novo, com uma rotina que o servidor não conhece, ou sem o cabeçalho).</summary>
    public const string Outra = "outra";

    public static IReadOnlyList<string> Todas { get; } =
        [Chat, Telegram, ClassificarFotos, Legendas, AvancoPelasFotos, Relatorio, Orcamento, AnaliseDaObra];

    /// <summary>A rotina como o servidor guarda: uma das conhecidas, ou <see cref="Outra"/>.</summary>
    public static string Normalizar(string? rotina) =>
        rotina?.Trim().ToLowerInvariant() is { } r && Todas.Contains(r) ? r : Outra;

    /// <summary>O nome para o painel: "Chat do programa", "Classificar fotos"…</summary>
    public static string Nome(string? rotina) => rotina switch
    {
        Chat => "Chat do programa",
        Telegram => "Telegram",
        ClassificarFotos => "Classificar fotos",
        Legendas => "Legendas das fotos",
        AvancoPelasFotos => "% pelas fotos",
        Relatorio => "Texto do relatório",
        Orcamento => "Orçamento",
        AnaliseDaObra => "Análise da obra",
        _ => "Outras",
    };
}
