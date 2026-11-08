using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;

namespace ProjectPlanner.Data;

/// <summary>
/// Bloco 81: o Diário de Obra (RDO) em JSON, na tabela DiarioDaObra: um registro por dia, com o clima, o efetivo, os equipamentos,
/// os serviços, as ocorrências e o fechamento. Sem a linha, o diário está vazio.
/// </summary>
internal static class FormatoDoDiario
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool Conhece(int versao) => versao == Versao;

    public static string Serializar(Cronograma cronograma) => JsonSerializer.Serialize(new Dados(
        [.. cronograma.Diario.Dias.Select(d => new Dia(d.Data,
            new Clima(d.Manha.Tempo, d.Manha.Praticavel ? null : false), new Clima(d.Tarde.Tempo, d.Tarde.Praticavel ? null : false),
            d.Efetivo.Count == 0 ? null : [.. d.Efetivo.Select(e => new Pessoas(e.Funcao, e.Quantidade, e.Empresa))],
            d.Equipamentos.Count == 0 ? null : [.. d.Equipamentos.Select(e => new Equipamento(e.Nome, e.Quantidade))],
            d.Atividades.Count == 0 ? null : [.. d.Atividades.Select(a => new Atividade(a.TarefaId, a.Descricao, a.Percentual, a.Quantidade, a.Unidade))],
            d.Ocorrencias, d.Fechamento?.Quando, d.Fechamento?.Responsavel))]), Opcoes);

    /// <exception cref="InvalidDataException">JSON com defeito ou dias que o diário não aceita.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("O diário está vazio.");
            cronograma.Diario.Restaurar((dados.Dias ?? []).Select(d => new DiaDaObra(d.Data)
            {
                Manha = new ClimaDoPeriodo(d.Manha?.Tempo ?? TempoNoPeriodo.Bom, d.Manha?.Praticavel ?? true),
                Tarde = new ClimaDoPeriodo(d.Tarde?.Tempo ?? TempoNoPeriodo.Bom, d.Tarde?.Praticavel ?? true),
                Efetivo = [.. (d.Efetivo ?? []).Select(e => new EfetivoNoDia(e.Funcao ?? "", e.Quantidade, e.Empresa))],
                Equipamentos = [.. (d.Equipamentos ?? []).Select(e => new EquipamentoNoDia(e.Nome ?? "", e.Quantidade))],
                Atividades = [.. (d.Atividades ?? []).Select(a => new AtividadeNoDia(a.Tarefa, a.Descricao ?? "", a.Percentual, a.Quantidade, a.Unidade))],
                Ocorrencias = d.Ocorrencias,
                Fechamento = d.FechadoEm is { } quando ? new FechamentoDoDia(quando, d.FechadoPor) : null,
            }));
        }
        catch (Exception ex) when (ex is JsonException or DomainValidationException or NotSupportedException)
        {
            throw new InvalidDataException($"O Diário de Obra tem um defeito: {ex.Message}", ex);
        }
    }

    private sealed record Dados(List<Dia>? Dias);

    private sealed record Dia(DateOnly Data, Clima? Manha, Clima? Tarde, List<Pessoas>? Efetivo, List<Equipamento>? Equipamentos,
        List<Atividade>? Atividades, string? Ocorrencias, DateTime? FechadoEm, string? FechadoPor);

    private sealed record Clima(TempoNoPeriodo Tempo, bool? Praticavel);

    private sealed record Pessoas(string? Funcao, int Quantidade, string? Empresa);

    private sealed record Equipamento(string? Nome, int Quantidade);

    // Bloco 84: a quantidade e a unidade são opcionais (os arquivos de antes não têm, e a versão continua a 1).
    private sealed record Atividade(Guid? Tarefa, string? Descricao, int? Percentual, decimal? Quantidade = null, string? Unidade = null);
}
