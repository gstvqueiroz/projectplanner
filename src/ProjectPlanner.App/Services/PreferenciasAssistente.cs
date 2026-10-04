using System.Text.Json;

namespace ProjectPlanner.App.Services;

/// <summary>O que o assistente guarda fora do cofre (Bloco 23): só o modelo. A chave de API nunca vem para cá.</summary>
public sealed record PreferenciasAssistente(string Modelo);

public interface IPreferenciasAssistente
{
    /// <summary>As preferências salvas; nulas se ainda não há ou se não deu para ler.</summary>
    PreferenciasAssistente? Carregar();

    /// <summary>Grava as preferências. Uma falha ao gravar não interrompe o uso do programa.</summary>
    void Salvar(PreferenciasAssistente preferencias);
}

/// <summary>
/// Guarda as <see cref="PreferenciasAssistente"/> em <c>assistente.json</c>, ao lado do <c>colunas.json</c> e do
/// <c>interface.json</c>, do mesmo jeito que eles (arquivo temporário e troca).
/// </summary>
public sealed class ArquivoPreferenciasAssistente(string caminho) : IPreferenciasAssistente
{
    private const int VersaoDoFormato = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ArquivoPreferenciasAssistente() : this(CaminhoPadrao())
    {
    }

    public string Caminho { get; } = caminho;

    public static string CaminhoPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "assistente.json");

    public PreferenciasAssistente? Carregar()
    {
        try
        {
            if (!File.Exists(Caminho))
                return null;
            var arquivo = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(Caminho), Opcoes);
            return arquivo is { Versao: VersaoDoFormato, Modelo: { Length: > 0 } modelo } ? new PreferenciasAssistente(modelo) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public void Salvar(PreferenciasAssistente preferencias)
    {
        var temporario = Caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(temporario, JsonSerializer.Serialize(new Arquivo(VersaoDoFormato, preferencias.Modelo), Opcoes));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
        }
    }

    private sealed record Arquivo(int Versao, string? Modelo);
}
