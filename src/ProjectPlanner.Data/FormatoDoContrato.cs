using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Data;

/// <summary>
/// O contrato (Bloco 36) em JSON, na tabela ContratoDoProjeto: a configuração (retenção, índice e plano), os aditivos e as
/// medições, com o boletim guardado das aprovadas. Os nomes dos enums vão como texto.
/// </summary>
internal static class FormatoDoContrato
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serializar(Cronograma cronograma)
    {
        var contrato = cronograma.Contrato;
        var c = contrato.Configuracao;
        return JsonSerializer.Serialize(new Dados(
            new Configuracao(c.PercentualDeRetencao, c.NomeDoIndice, c.IndiceBase, c.Periodicidade, [.. c.Cortes]),
            [.. contrato.Aditivos.Select(a => new AditivoGravado(a.Numero, a.Data, a.Descricao,
                [.. a.Itens.Select(i => new AlteracaoGravada(i.TarefaId, i.Quantidade, i.Valor))]))],
            [.. contrato.Medicoes.Select(m => new MedicaoGravada(m.Numero, m.De, m.Ate, m.Situacao,
                [.. m.Itens.Select(i => new ItemGravado(i.TarefaId, i.Quantidade))],
                [.. m.Glosas.Select(g => new GlosaGravada(g.Descricao, g.Valor))],
                m.Indice, m.DevolverRetencao, m.Observacoes, m.AprovadaEm, m.PagaEm,
                m.Linhas?.Select(Gravar).ToList(), m.Resumo is { } r ? Gravar(r) : null))]), Opcoes);
    }

    /// <exception cref="InvalidDataException">JSON com defeito ou valores que o domínio recusa.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("O contrato está vazio.");
            var c = dados.Configuracao ?? new Configuracao(0, null, null, Periodicidade.Mensal, null);
            cronograma.Contrato.Restaurar(
                new ConfiguracaoDoContrato
                {
                    PercentualDeRetencao = c.Retencao, NomeDoIndice = c.NomeDoIndice, IndiceBase = c.IndiceBase, Periodicidade = c.Periodicidade,
                    Cortes = c.Cortes ?? [],
                },
                (dados.Aditivos ?? []).Select(a => new Aditivo(a.Numero, a.Data, a.Descricao,
                    (a.Itens ?? []).Select(i => new AlteracaoDoAditivo(i.TarefaId, i.Quantidade, i.Valor)))),
                (dados.Medicoes ?? []).Select(m => Medicao.Restaurar(m.Numero, m.De, m.Ate, m.Situacao,
                    (m.Itens ?? []).Select(i => new ItemMedido(i.TarefaId, i.Quantidade)),
                    (m.Glosas ?? []).Select(g => new GlosaDaMedicao(g.Descricao, g.Valor)),
                    m.Indice, m.DevolverRetencao, m.Observacoes, m.AprovadaEm, m.PagaEm,
                    m.Linhas?.Select(Ler).ToList(), m.Resumo is { } r ? Ler(r) : null)).ToList());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DomainValidationException or ArgumentException)
        {
            throw new InvalidDataException($"O contrato e as medições não puderam ser lidos: {ex.Message}", ex);
        }
    }

    private static LinhaGravada Gravar(LinhaDoBoletim l) => new(l.TarefaId, l.Edt, l.Descricao, l.Nivel, l.EhGrupo, l.PorPercentual, l.Unidade,
        l.QuantidadeContratada, l.PrecoUnitario, l.ValorContratado, l.QuantidadeAnterior, l.QuantidadeNoPeriodo, l.ValorAnterior, l.ValorNoPeriodo,
        l.Removida);

    private static LinhaDoBoletim Ler(LinhaGravada l) => new(l.TarefaId, l.Edt ?? "", l.Descricao ?? "", l.Nivel, l.EhGrupo, l.PorPercentual,
        l.Unidade ?? "", l.QuantidadeContratada, l.PrecoUnitario, l.ValorContratado, l.QuantidadeAnterior, l.QuantidadeNoPeriodo, l.ValorAnterior,
        l.ValorNoPeriodo, l.Removida);

    private static ResumoGravado Gravar(ResumoDoBoletim r) => new(r.ValorDosAditivos, r.QuantidadeDeAditivos, r.ValorContratado, r.MedidoAnterior,
        r.MedidoNoPeriodo, r.Reajuste, r.DescricaoDoReajuste, r.Glosas, r.PercentualDeRetencao, r.Retencao, r.Devolucao, r.RetidoAnterior, r.Fisico);

    private static ResumoDoBoletim Ler(ResumoGravado r) => new(r.ValorDosAditivos, r.QuantidadeDeAditivos, r.ValorContratado, r.MedidoAnterior,
        r.MedidoNoPeriodo, r.Reajuste, r.DescricaoDoReajuste, r.Glosas, r.PercentualDeRetencao, r.Retencao, r.Devolucao, r.RetidoAnterior, r.Fisico);

    private sealed record Dados(Configuracao? Configuracao, List<AditivoGravado>? Aditivos, List<MedicaoGravada>? Medicoes);

    private sealed record Configuracao(decimal Retencao, string? NomeDoIndice, decimal? IndiceBase, Periodicidade Periodicidade, List<DateOnly>? Cortes);

    private sealed record AditivoGravado(int Numero, DateOnly Data, string? Descricao, List<AlteracaoGravada>? Itens);

    private sealed record AlteracaoGravada(Guid TarefaId, decimal Quantidade, decimal Valor);

    private sealed record MedicaoGravada(int Numero, DateOnly De, DateOnly Ate, SituacaoDaMedicao Situacao, List<ItemGravado>? Itens,
        List<GlosaGravada>? Glosas, decimal? Indice, bool DevolverRetencao, string? Observacoes, DateTime? AprovadaEm, DateOnly? PagaEm,
        List<LinhaGravada>? Linhas, ResumoGravado? Resumo);

    private sealed record ItemGravado(Guid TarefaId, decimal Quantidade);

    private sealed record GlosaGravada(string? Descricao, decimal Valor);

    private sealed record LinhaGravada(Guid? TarefaId, string? Edt, string? Descricao, int Nivel, bool EhGrupo, bool PorPercentual, string? Unidade,
        decimal QuantidadeContratada, decimal PrecoUnitario, decimal ValorContratado, decimal QuantidadeAnterior, decimal QuantidadeNoPeriodo,
        decimal ValorAnterior, decimal ValorNoPeriodo, bool Removida);

    private sealed record ResumoGravado(decimal ValorDosAditivos, int QuantidadeDeAditivos, decimal ValorContratado, decimal MedidoAnterior,
        decimal MedidoNoPeriodo, decimal Reajuste, string? DescricaoDoReajuste, decimal Glosas, decimal PercentualDeRetencao, decimal Retencao,
        decimal Devolucao, decimal RetidoAnterior, double? Fisico);
}
