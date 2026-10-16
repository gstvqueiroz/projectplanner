using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Orcamento;

namespace ProjectPlanner.Data;

/// <summary>
/// O orçamento (Bloco 33) em JSON, na tabela OrcamentoDoProjeto: o BDI e a composição aberta de cada tarefa, com a base de
/// onde veio. Versão 2 (Bloco 38): as quantidades estimadas pela IA; sem elas o arquivo continua na versão 1, que a 0.37 lê.
/// </summary>
internal static class FormatoDoOrcamento
{
    public const int Versao = 2;

    /// <summary>A versão que vai no arquivo: a 1 enquanto não há nada da 2.</summary>
    public static int VersaoDe(Cronograma cronograma) => cronograma.Orcamento.QuantidadesEstimadas.Count > 0 ? Versao : 1;

    /// <summary>As versões que esta sabe ler.</summary>
    public static bool Conhece(int versao) => versao is >= 1 and <= Versao;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serializar(Cronograma cronograma)
    {
        var o = cronograma.Orcamento;
        return JsonSerializer.Serialize(new Dados(o.Configuracao.Bdi, o.Configuracao.BdiDeMateriais,
            [.. o.Servicos.Select(s => new Servico(s.Key, s.Value.Base.Fonte, s.Value.Base.Uf, s.Value.Base.Referencia, s.Value.Base.Desonerado,
                s.Value.Codigo, s.Value.Descricao, s.Value.Unidade,
                [.. s.Value.Insumos.Select(i => new InsumoGravado(i.Codigo, i.Descricao, i.Unidade, i.Tipo, i.Coeficiente, i.Preco))]))],
            o.QuantidadesEstimadas.Count == 0 ? null : [.. o.QuantidadesEstimadas.Select(q => new QuantidadeEstimada(q.Key, q.Value))]), Opcoes);
    }

    /// <exception cref="InvalidDataException">JSON com defeito ou valores que o domínio recusa.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var d = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("O orçamento está vazio.");
            cronograma.Orcamento.Restaurar(new ConfiguracaoDoOrcamento { Bdi = d.Bdi, BdiDeMateriais = d.BdiDeMateriais },
                (d.Servicos ?? []).Select(s => (s.TarefaId, new ComposicaoAberta(new BaseDeReferencia(s.Fonte, s.Uf, s.Referencia, s.Desonerado),
                    s.Codigo ?? "", s.Descricao ?? "", s.Unidade ?? "",
                    [.. (s.Insumos ?? []).Select(i => new InsumoNaComposicao(i.Codigo ?? "", i.Descricao ?? "", i.Unidade ?? "", i.Tipo, i.Coeficiente, i.Preco))]))).ToList(),
                (d.QuantidadesEstimadas ?? []).Select(q => (q.TarefaId, q.Quantidade)).ToList());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DomainValidationException or ArgumentException)
        {
            throw new InvalidDataException($"O orçamento não pôde ser lido: {ex.Message}", ex);
        }
    }

    private sealed record Dados(decimal Bdi, decimal? BdiDeMateriais, List<Servico>? Servicos, List<QuantidadeEstimada>? QuantidadesEstimadas = null);

    private sealed record QuantidadeEstimada(Guid TarefaId, decimal Quantidade);

    private sealed record Servico(Guid TarefaId, string? Fonte, string? Uf, string? Referencia, bool? Desonerado, string? Codigo, string? Descricao,
        string? Unidade, List<InsumoGravado>? Insumos);

    private sealed record InsumoGravado(string? Codigo, string? Descricao, string? Unidade, TipoDeInsumo Tipo, decimal Coeficiente, decimal Preco);
}
