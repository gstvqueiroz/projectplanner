using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.App.Services;

/// <summary>Bloco 88: os modelos de relatório do usuário e o último usado (para a tela abrir nele).</summary>
public sealed record ModelosSalvos(IReadOnlyList<ModeloDeRelatorio> Modelos, string? Ultimo)
{
    public static ModelosSalvos Vazios { get; } = new([], null);
}

/// <summary>
/// Bloco 88: onde ficam os modelos de relatório do usuário (valem para todas as obras, como as colunas e o logotipo).
/// </summary>
public interface IModelosDeRelatorio
{
    ModelosSalvos Carregar();

    void Salvar(ModelosSalvos modelos);
}

/// <summary>Bloco 88: os modelos no arquivo modelos-de-relatorio.json, ao lado das outras preferências.</summary>
public sealed class ArquivoModelosDeRelatorio(string caminho) : IModelosDeRelatorio
{
    internal static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SoOQueSeLeDeVolta } },
    };

    // As propriedades calculadas (as seções ativas, o título ou o padrão) não vão para o arquivo: só o que se lê de volta.
    private static void SoOQueSeLeDeVolta(JsonTypeInfo tipo)
    {
        if (tipo.Kind != JsonTypeInfoKind.Object)
            return;
        foreach (var propriedade in tipo.Properties.Where(p => p.Set is null).ToList())
            tipo.Properties.Remove(propriedade);
    }

    public ArquivoModelosDeRelatorio() : this(CaminhoPadrao())
    {
    }

    public string Caminho { get; } = caminho;

    public static string CaminhoPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "modelos-de-relatorio.json");

    /// <summary>Os modelos do arquivo; os que não valem (editados à mão, de outra versão) ficam de fora. Sem arquivo, nenhum.</summary>
    public ModelosSalvos Carregar()
    {
        try
        {
            if (!File.Exists(Caminho) || JsonSerializer.Deserialize<ModelosSalvos>(File.ReadAllText(Caminho), Opcoes) is not { } lidos)
                return ModelosSalvos.Vazios;
            return lidos with { Modelos = [.. (lidos.Modelos ?? []).Where(Vale)] };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return ModelosSalvos.Vazios;
        }
    }

    public void Salvar(ModelosSalvos modelos)
    {
        ArgumentNullException.ThrowIfNull(modelos);
        var temporario = Caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(temporario, JsonSerializer.Serialize(modelos, Opcoes));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
        }
    }

    private static bool Vale(ModeloDeRelatorio? modelo)
    {
        if (modelo is null)
            return false;
        try
        {
            modelo.Validar();
            return true;
        }
        catch (Exception ex) when (ex is DomainValidationException or NullReferenceException)
        {
            return false;
        }
    }
}

/// <summary>Bloco 88: sem arquivo (os testes): na memória.</summary>
public sealed class ModelosDeRelatorioEmMemoria : IModelosDeRelatorio
{
    public ModelosSalvos Salvos { get; set; } = ModelosSalvos.Vazios;

    public ModelosSalvos Carregar() => Salvos;

    public void Salvar(ModelosSalvos modelos) => Salvos = modelos;
}
