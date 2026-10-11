using System.Text.Json;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Data;

/// <summary>As linhas de base do projeto (Bloco 31) em JSON, na tabela LinhasDeBaseDoProjeto.</summary>
internal static class FormatoDasLinhasDeBase
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serializar(Cronograma cronograma) => JsonSerializer.Serialize(new Dados(
        cronograma.LinhaDeBaseEmUso?.Numero,
        [.. cronograma.LinhasDeBase.Select(l => new Linha(l.Numero, l.Nome, l.SalvaEm,
            [.. l.Tarefas.Select(t => new TarefaDaLinha(t.Key, t.Value.Inicio, t.Value.Termino, t.Value.Duracao, t.Value.Valor, t.Value.EhResumo))]))]), Opcoes);

    /// <exception cref="InvalidDataException">JSON com defeito ou valores que o domínio recusa.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("As linhas de base estão vazias.");
            foreach (var linha in dados.Linhas ?? [])
                cronograma.AdicionarLinhaDeBase(new LinhaDeBase(linha.Numero, linha.Nome, linha.SalvaEm,
                    (linha.Tarefas ?? []).ToDictionary(t => t.Id, t => new TarefaNaLinhaDeBase(t.Inicio, t.Termino, t.Duracao, t.Valor, t.EhResumo))),
                    emUso: linha.Numero == dados.EmUso);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DomainValidationException or ArgumentException)
        {
            throw new InvalidDataException($"As linhas de base não puderam ser lidas: {ex.Message}", ex);
        }
    }

    private sealed record Dados(int? EmUso, List<Linha>? Linhas);

    private sealed record Linha(int Numero, string? Nome, DateTime SalvaEm, List<TarefaDaLinha>? Tarefas);

    // Bloco 35: EhResumo (opcional; ausente nas linhas de base salvas na 0.31).
    private sealed record TarefaDaLinha(Guid Id, DateTime Inicio, DateTime Termino, int Duracao, decimal? Valor, bool? EhResumo = null);
}
