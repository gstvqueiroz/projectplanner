using System.Text.Json;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Services;

/// <summary>O que o assistente guarda fora do cofre (Bloco 23): o modelo. A chave de API nunca vem para cá.</summary>
/// <param name="Modo">Bloco 26: o que fazer num pedido de cronograma (perguntar, montar direto ou estudar antes).</param>
public sealed record PreferenciasAssistente(string Modelo, PreferenciaDeModo Modo = PreferenciaDeModo.Perguntar);

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
/// <remarks>
/// O modo (Bloco 26) é um campo a mais, <c>"modo": "perguntar" | "direto" | "estudo"</c>, sem mudar a versão do formato:
/// um arquivo sem ele, ou com um valor desconhecido, vale como "perguntar", e as versões anteriores ignoram o campo. O campo
/// <c>"provedor"</c> que a 0.44 gravava (a chave própria ou a conta) deixou de existir no Bloco 46 e é ignorado.
/// </remarks>
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
            return arquivo is { Versao: VersaoDoFormato, Modelo: { Length: > 0 } modelo }
                ? new PreferenciasAssistente(modelo, ModoDoTexto(arquivo.Modo))
                : null;
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
            File.WriteAllText(temporario, JsonSerializer.Serialize(
                new Arquivo(VersaoDoFormato, preferencias.Modelo, TextoDoModo(preferencias.Modo)), Opcoes));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
        }
    }

    private static string TextoDoModo(PreferenciaDeModo modo) => modo switch
    {
        PreferenciaDeModo.Direto => "direto",
        PreferenciaDeModo.Estudo => "estudo",
        _ => "perguntar",
    };

    private static PreferenciaDeModo ModoDoTexto(string? texto) => texto?.Trim().ToLowerInvariant() switch
    {
        "direto" => PreferenciaDeModo.Direto,
        "estudo" => PreferenciaDeModo.Estudo,
        _ => PreferenciaDeModo.Perguntar,
    };

    private sealed record Arquivo(int Versao, string? Modelo, string? Modo = null);
}
