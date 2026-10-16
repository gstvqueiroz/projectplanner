using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Bloco 32: o catálogo pessoal de recursos (os que você usa sempre, com os custos de referência), fora dos projetos: serve
/// para trazer recursos para um projeto e guardar os de uma obra para as próximas.
/// </summary>
public interface ICatalogoDeRecursos
{
    IReadOnlyList<Recurso> Carregar();

    /// <exception cref="IOException">Não deu para gravar.</exception>
    void Salvar(IReadOnlyList<Recurso> recursos);
}

/// <summary>Sem arquivo: o catálogo fica só na memória (testes).</summary>
public sealed class CatalogoDeRecursosEmMemoria : ICatalogoDeRecursos
{
    public List<Recurso> Recursos { get; } = [];

    public IReadOnlyList<Recurso> Carregar() => [.. Recursos];

    public void Salvar(IReadOnlyList<Recurso> recursos)
    {
        Recursos.Clear();
        Recursos.AddRange(recursos);
    }
}

/// <summary>O catálogo em recursos.json, na pasta das preferências (ao lado de colunas.json).</summary>
public sealed class ArquivoCatalogoDeRecursos(string caminho) : ICatalogoDeRecursos
{
    private const int VersaoDoFormato = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public ArquivoCatalogoDeRecursos() : this(CaminhoPadrao())
    {
    }

    public string Caminho { get; } = caminho;

    public static string CaminhoPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "recursos.json");

    /// <summary>Os recursos do arquivo; sem arquivo, ou com um que não dá para ler, vazio (e os recursos com defeito ficam de fora).</summary>
    public IReadOnlyList<Recurso> Carregar()
    {
        try
        {
            if (!File.Exists(Caminho))
                return [];
            var arquivo = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(Caminho), Opcoes);
            if (arquivo is not { Versao: VersaoDoFormato })
                return [];
            var recursos = new List<Recurso>();
            foreach (var r in arquivo.Recursos ?? [])
            {
                try
                {
                    recursos.Add(new Recurso(r.Id, r.Nome, r.Tipo, r.Unidade, r.CustoUnitario, r.Disponivel, r.Fornecedor));
                }
                catch (DomainValidationException)
                {
                    // Um recurso com defeito (arquivo mexido por fora) fica de fora; os outros valem.
                }
            }
            return recursos;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public void Salvar(IReadOnlyList<Recurso> recursos)
    {
        var temporario = Caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(temporario, JsonSerializer.Serialize(new Arquivo(VersaoDoFormato,
                [.. recursos.Select(r => new RecursoGravado(r.Id, r.Nome, r.Tipo, r.Unidade, r.CustoUnitario, r.Disponivel, r.Fornecedor))]), Opcoes));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            throw new IOException($"Não foi possível gravar o catálogo de recursos: {ex.Message}", ex);
        }
    }

    private sealed record Arquivo(int Versao, List<RecursoGravado>? Recursos);

    private sealed record RecursoGravado(Guid Id, string Nome, TipoDeRecurso Tipo, string Unidade, decimal CustoUnitario, decimal? Disponivel, string? Fornecedor);
}
