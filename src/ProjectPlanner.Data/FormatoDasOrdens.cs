using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;

namespace ProjectPlanner.Data;

/// <summary>
/// As ordens de serviço (Bloco 58) em JSON, na tabela OrdensDeServicoDoProjeto; os enums vão como texto. Versão 2 (Bloco 60):
/// as medições e os pagamentos; sem eles o arquivo continua na versão 1, que a 0.58 lê.
/// </summary>
internal static class FormatoDasOrdens
{
    public const int Versao = 2;

    /// <summary>A versão que vai no arquivo: a 1 enquanto nenhuma OS tem medição.</summary>
    public static int VersaoDe(Cronograma cronograma) => cronograma.OrdensDeServico.Todas.Any(o => o.Medicoes.Count > 0) ? Versao : 1;

    /// <summary>As versões que esta sabe ler.</summary>
    public static bool Conhece(int versao) => versao is >= 1 and <= Versao;

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
            [.. o.Historico.Select(e => new Evento(e.Quando, e.Situacao, e.Descricao, e.Autor))], o.NoTelegram ? true : null,
            o.Medicoes.Count == 0 ? null : [.. o.Medicoes.Select(m => new Medicao(m.Id, m.Numero, m.Data, [.. m.Quantidades], m.Valor,
                m.Pagamento is { } p ? new Pagamento(p.Data, p.Documento, [.. p.Lancamentos]) : null))]))]), Opcoes);

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
                (o.Historico ?? []).Select(e => new EventoDaOrdem(e.Quando, e.Situacao, e.Descricao ?? "", e.Autor)), o.NoTelegram == true,
                (o.Medicoes ?? []).Select(m => new MedicaoDaOrdem(m.Id, m.Numero, m.Data, m.Quantidades ?? [], m.Valor,
                    m.Pagamento is { } p ? new PagamentoDaMedicao(p.Data, p.Documento, p.Lancamentos ?? []) : null)))).ToList());
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
        List<Evento>? Historico, bool? NoTelegram = null, List<Medicao>? Medicoes = null);

    private sealed record Medicao(Guid Id, int Numero, DateOnly Data, List<decimal>? Quantidades, decimal Valor, Pagamento? Pagamento);

    private sealed record Pagamento(DateOnly Data, string? Documento, List<Guid>? Lancamentos);

    private sealed record Item(Guid? TarefaId, string? Descricao, string? Unidade, decimal Quantidade, decimal PrecoUnitario);

    private sealed record Verificacao(string? Texto, bool Feito);

    private sealed record Evento(DateTime Quando, SituacaoDaOrdem Situacao, string? Descricao, string? Autor);
}
