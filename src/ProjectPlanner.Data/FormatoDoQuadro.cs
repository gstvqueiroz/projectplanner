using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.Data;

/// <summary>
/// Bloco 75: o quadro Kanban em JSON, na tabela QuadroDoProjeto: as colunas, as etiquetas, as raias e os cards (a coluna, a
/// ordem, o último % abaixo de 100, se está arquivado, as etiquetas e o checklist). Sem a linha (um arquivo de antes do
/// quadro, ou um projeto sem tarefas), o quadro abre com as colunas padrão e distribui os cards pelo % (K7).
/// </summary>
internal static class FormatoDoQuadro
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool Conhece(int versao) => versao == Versao;

    public static string Serializar(Cronograma cronograma)
    {
        var quadro = cronograma.Quadro;
        // Na ordem do cronograma, para o arquivo não mudar à toa entre um salvamento e outro.
        var posicao = cronograma.Tarefas.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return JsonSerializer.Serialize(new Dados(
            [.. quadro.Colunas.Select(c => new Coluna(c.Id, c.Nome, c.Tipo, c.Cor, c.Limite))],
            quadro.Etiquetas.Count == 0 ? null : [.. quadro.Etiquetas.Select(e => new Etiqueta(e.Id, e.Nome, e.Cor))],
            quadro.Raias == ModoDasRaias.Nenhuma ? null : quadro.Raias,
            [.. quadro.Cards.Where(c => posicao.ContainsKey(c.TarefaId)).OrderBy(c => posicao[c.TarefaId]).Select(c => new Card(c.TarefaId, c.ColunaId,
                c.Ordem, c.PercentualAnterior, c.Arquivado ? true : null, c.Etiquetas.Count == 0 ? null : [.. c.Etiquetas],
                c.Checklist.Count == 0 ? null : [.. c.Checklist.Select(i => new Item(i.Texto, i.Feito))]))]), Opcoes);
    }

    /// <exception cref="InvalidDataException">JSON com defeito ou colunas que o quadro não aceita.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("O quadro está vazio.");
            cronograma.Quadro.Restaurar(
                (dados.Colunas ?? []).Select(c => new ColunaDoQuadro(c.Id, c.Nome ?? "", c.Tipo, c.Cor, c.Limite)),
                (dados.Etiquetas ?? []).Select(e => new EtiquetaDoQuadro(e.Id, e.Nome ?? "", e.Cor ?? "")),
                (dados.Cards ?? []).Select(c => new CardDoQuadro(c.Tarefa, c.Coluna, c.Ordem)
                {
                    PercentualAnterior = c.Anterior,
                    Arquivado = c.Arquivado == true,
                    Etiquetas = c.Etiquetas ?? [],
                    Checklist = [.. (c.Checklist ?? []).Select(i => new ItemDoChecklist(i.Texto ?? "", i.Feito))],
                }),
                dados.Raias ?? ModoDasRaias.Nenhuma);
        }
        catch (Exception ex) when (ex is JsonException or DomainValidationException or NotSupportedException)
        {
            throw new InvalidDataException($"O quadro Kanban tem um defeito: {ex.Message}", ex);
        }
    }

    private sealed record Dados(List<Coluna>? Colunas, List<Etiqueta>? Etiquetas, ModoDasRaias? Raias, List<Card>? Cards);

    private sealed record Coluna(Guid Id, string? Nome, TipoDeColuna Tipo, string? Cor, int? Limite);

    private sealed record Etiqueta(Guid Id, string? Nome, string? Cor);

    private sealed record Card(Guid Tarefa, Guid Coluna, int Ordem, int? Anterior, bool? Arquivado, List<Guid>? Etiquetas, List<Item>? Checklist);

    private sealed record Item(string? Texto, bool Feito);
}
