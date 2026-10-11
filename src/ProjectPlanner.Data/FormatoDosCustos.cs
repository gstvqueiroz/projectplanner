using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;

namespace ProjectPlanner.Data;

/// <summary>Os custos realizados (Bloco 37) em JSON, na tabela CustosDoProjeto; a categoria vai como texto.</summary>
internal static class FormatoDosCustos
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serializar(Cronograma cronograma) => JsonSerializer.Serialize(new Dados(
        [.. cronograma.Custos.Lancamentos.Select(l => new Lancamento(l.Id, l.Data, l.TarefaId, l.Categoria, l.Descricao, l.Documento, l.Valor))]), Opcoes);

    /// <exception cref="InvalidDataException">JSON com defeito ou valores que o domínio recusa.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("Os custos estão vazios.");
            cronograma.Custos.Restaurar((dados.Lancamentos ?? []).Select(l =>
                new LancamentoDeCusto(l.Id, l.Data, l.TarefaId, l.Categoria, l.Descricao, l.Documento, l.Valor)).ToList());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DomainValidationException or ArgumentException)
        {
            throw new InvalidDataException($"Os custos realizados não puderam ser lidos: {ex.Message}", ex);
        }
    }

    private sealed record Dados(List<Lancamento>? Lancamentos);

    private sealed record Lancamento(Guid Id, DateOnly Data, Guid? TarefaId, CategoriaDeCusto Categoria, string? Descricao, string? Documento, decimal Valor);
}
