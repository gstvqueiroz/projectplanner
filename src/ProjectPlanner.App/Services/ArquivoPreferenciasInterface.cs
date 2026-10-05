using System.Text.Json;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Guarda as <see cref="PreferenciasInterface"/> em <c>interface.json</c>, na mesma pasta e do mesmo jeito que
/// <see cref="ArquivoPreferenciasColunas"/> (o <c>colunas.json</c> não muda de formato). Arquivo ausente, ilegível
/// ou de outro formato vale como "sem preferência salva", e falhas ao gravar são ignoradas.
/// </summary>
/// <remarks>
/// O tema (Bloco 25) entrou como um campo a mais, <c>"tema": "sistema" | "claro" | "escuro"</c>, sem mudar a versão do
/// formato: um arquivo sem ele (das versões anteriores) vale como "sistema", um valor desconhecido também, e as versões
/// anteriores do programa ignoram o campo.
/// </remarks>
public sealed class ArquivoPreferenciasInterface(string caminho) : IPreferenciasInterface
{
    private const int VersaoDoFormato = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ArquivoPreferenciasInterface() : this(CaminhoPadrao())
    {
    }

    public string Caminho { get; } = caminho;

    public static string CaminhoPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "interface.json");

    public PreferenciasInterface? Carregar()
    {
        try
        {
            if (!File.Exists(Caminho))
                return null;
            var arquivo = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(Caminho), Opcoes);
            return arquivo is { Versao: VersaoDoFormato }
                ? new PreferenciasInterface(arquivo.MostrarAssistente, TemaDoTexto(arquivo.Tema))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public void Salvar(PreferenciasInterface preferencias)
    {
        var temporario = Caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(temporario, JsonSerializer.Serialize(
                new Arquivo(VersaoDoFormato, preferencias.MostrarAssistente, TextoDoTema(preferencias.Tema)), Opcoes));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
        }
    }

    private static string TextoDoTema(TemaDaInterface tema) => tema switch
    {
        TemaDaInterface.Claro => "claro",
        TemaDaInterface.Escuro => "escuro",
        _ => "sistema",
    };

    private static TemaDaInterface TemaDoTexto(string? texto) => texto?.Trim().ToLowerInvariant() switch
    {
        "claro" => TemaDaInterface.Claro,
        "escuro" => TemaDaInterface.Escuro,
        _ => TemaDaInterface.Sistema,
    };

    private sealed record Arquivo(int Versao, bool MostrarAssistente, string? Tema = null);
}
