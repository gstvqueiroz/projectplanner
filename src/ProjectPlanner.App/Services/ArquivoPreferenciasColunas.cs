using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.App.Models;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Guarda o layout das colunas em <c>colunas.json</c>, na pasta de dados do usuário:
/// <list type="bullet">
/// <item>macOS: <c>~/Library/Application Support/ProjectPlanner/colunas.json</c></item>
/// <item>Windows: <c>%APPDATA%\ProjectPlanner\colunas.json</c></item>
/// <item>Linux: <c>~/.config/ProjectPlanner/colunas.json</c></item>
/// </list>
/// Arquivo ausente, ilegível ou de outro formato vale como "sem layout salvo" (a grid usa o padrão),
/// e falhas ao gravar são ignoradas: preferência de tela nunca deve impedir o uso do programa.
/// </summary>
public sealed class ArquivoPreferenciasColunas(string caminho) : IPreferenciasColunas
{
    private const int VersaoDoFormato = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ArquivoPreferenciasColunas() : this(CaminhoPadrao())
    {
    }

    public string Caminho { get; } = caminho;

    public static string CaminhoPadrao()
    {
        var pasta = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(pasta, "ProjectPlanner", "colunas.json");
    }

    public LayoutColunas? Carregar()
    {
        try
        {
            if (!File.Exists(Caminho))
                return null;
            var arquivo = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(Caminho), Opcoes);
            if (arquivo is not { Versao: VersaoDoFormato, Colunas: { } colunas })
                return null;
            return new LayoutColunas(colunas
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => new EstadoColuna(c.Id!, c.Visivel, c.Largura, c.Posicao))
                .ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public void Salvar(LayoutColunas layout)
    {
        var temporario = Caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            var arquivo = new Arquivo(VersaoDoFormato,
                layout.Colunas.Select(c => new Coluna(c.Id, c.Visivel, c.Largura, c.Posicao)).ToList());
            File.WriteAllText(temporario, JsonSerializer.Serialize(arquivo, Opcoes));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
        }
    }

    private sealed record Arquivo(int Versao, List<Coluna>? Colunas);

    private sealed record Coluna(
        string? Id,
        bool Visivel,
        [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] double Largura,
        int Posicao);
}
