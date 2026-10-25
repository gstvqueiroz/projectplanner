using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;

namespace ProjectPlanner.Data;

/// <summary>As ordens de serviço (Bloco 58) em JSON, na tabela OrdensDeServicoDoProjeto; os enums vão como texto.</summary>
internal static class FormatoDasOrdens
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serializar(Cronograma cronograma) => JsonSerializer.Serialize(new Dados(
        [.. cronograma.OrdensDeServico.Todas.Select(o => new Ordem(o.Id, o.Numero, o.Situacao, o.Emissao, o.Conclusao,
            o.Dados.Titulo, o.Dados.Tipo, o.Dados.Executante, o.Dados.RecursoId, o.Dados.Contato, [.. o.Dados.Tarefas],
            [.. o.Dados.Itens.Select(i => new Item(i.TarefaId, i.Descricao, i.Unidade, i.Quantidade, i.PrecoUnitario))],
            o.Dados.InicioPrevisto, o.Dados.Prazo, o.Dados.FormaDePagamento, o.Dados.Observacoes,
            [.. o.Dados.Verificacao.Select(v => new Verificacao(v.Texto, v.Feito))],
            [.. o.Historico.Select(e => new Evento(e.Quando, e.Situacao, e.Descricao, e.Autor))], o.NoTelegram ? true : null))]), Opcoes);

    /// <exception cref="InvalidDataException">JSON com defeito ou valores que o domínio recusa.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("As ordens de serviço estão vazias.");
            cronograma.OrdensDeServico.Restaurar((dados.Ordens ?? []).Select(o => OrdemDeServico.Restaurar(o.Id, o.Numero,
                new DadosDaOrdem(o.Titulo, o.Tipo, o.Executante, o.RecursoId, o.Contato, o.Tarefas,
                    (o.Itens ?? []).Select(i => new ItemDaOrdem(i.TarefaId, i.Descricao, i.Unidade, i.Quantidade, i.PrecoUnitario)),
                    o.InicioPrevisto, o.Prazo, o.FormaDePagamento, o.Observacoes,
                    (o.Verificacao ?? []).Select(v => new ItemDeVerificacao(v.Texto, v.Feito))),
                o.Situacao, o.Emissao, o.Conclusao,
                (o.Historico ?? []).Select(e => new EventoDaOrdem(e.Quando, e.Situacao, e.Descricao ?? "", e.Autor)), o.NoTelegram == true)).ToList());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DomainValidationException or ArgumentException)
        {
            throw new InvalidDataException($"As ordens de serviço não puderam ser lidas: {ex.Message}", ex);
        }
    }

    private sealed record Dados(List<Ordem>? Ordens);

    private sealed record Ordem(Guid Id, int Numero, SituacaoDaOrdem Situacao, DateOnly? Emissao, DateOnly? Conclusao, string? Titulo,
        TipoDeExecutante Tipo, string? Executante, Guid? RecursoId, string? Contato, List<Guid>? Tarefas, List<Item>? Itens,
        DateOnly? InicioPrevisto, DateOnly? Prazo, FormaDePagamentoDaOrdem FormaDePagamento, string? Observacoes, List<Verificacao>? Verificacao,
        List<Evento>? Historico, bool? NoTelegram = null);

    private sealed record Item(Guid? TarefaId, string? Descricao, string? Unidade, decimal Quantidade, decimal PrecoUnitario);

    private sealed record Verificacao(string? Texto, bool Feito);

    private sealed record Evento(DateTime Quando, SituacaoDaOrdem Situacao, string? Descricao, string? Autor);
}
