namespace ProjectPlanner.Core.OrdensDeServico;

/// <summary>Bloco 64: que OS entram no relatório.</summary>
public enum SituacoesDoRelatorio
{
    Todas,

    /// <summary>Rascunho e as que estão na rua (emitidas até concluídas).</summary>
    Abertas,

    /// <summary>Conferidas, pagas e canceladas.</summary>
    Encerradas,

    Vencidas,
}

/// <summary>Bloco 64: o filtro do relatório: o período (pela emissão; o rascunho, pela criação), as situações e o executante.</summary>
public sealed record FiltroDoRelatorioDasOrdens(DateOnly? De = null, DateOnly? Ate = null, SituacoesDoRelatorio Situacoes = SituacoesDoRelatorio.Todas,
    string? Executante = null)
{
    /// <summary>"Período de 01/10/2026 a 31/10/2026 · abertas · Construtora Silva", ou "Todas as OS".</summary>
    public string Texto
    {
        get
        {
            var brasil = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            var partes = new[]
            {
                (De, Ate) switch
                {
                    ({ } de, { } ate) => $"de {de.ToString("dd/MM/yyyy", brasil)} a {ate.ToString("dd/MM/yyyy", brasil)}",
                    ({ } de, null) => $"desde {de.ToString("dd/MM/yyyy", brasil)}",
                    (null, { } ate) => $"até {ate.ToString("dd/MM/yyyy", brasil)}",
                    _ => null,
                },
                Situacoes switch
                {
                    SituacoesDoRelatorio.Abertas => "abertas",
                    SituacoesDoRelatorio.Encerradas => "encerradas",
                    SituacoesDoRelatorio.Vencidas => "vencidas",
                    _ => null,
                },
                string.IsNullOrWhiteSpace(Executante) ? null : Executante.Trim(),
            }.OfType<string>().ToList();
            return partes.Count == 0 ? "Todas as OS" : "OS " + string.Join(" · ", partes);
        }
    }
}

/// <summary>Bloco 64: um executante no relatório: quantas OS, o contratado, o medido, o pago, o que falta pagar e os prazos.</summary>
public sealed record ExecutanteNoRelatorio(string Nome, int Ordens, decimal Valor, decimal Medido, decimal Pago, decimal APagar, string? Prazos);

/// <summary>
/// Bloco 64: o relatório das ordens de serviço: as OS do filtro com os dias de atraso, os totais (contratado, medido, pago, a
/// pagar, retido e por medir), quantas há em cada situação, os prazos, o resumo por executante e o que pede atenção (as
/// vencidas e as que esperam a conferência). As canceladas entram na lista, mas não nos valores.
/// </summary>
public sealed record RelatorioDasOrdens(
    FiltroDoRelatorioDasOrdens Filtro,
    DateOnly Hoje,
    IReadOnlyList<OrdemDeServico> Ordens,
    IReadOnlyList<(SituacaoDaOrdem Situacao, int Quantidade)> PorSituacao,
    IReadOnlyList<ExecutanteNoRelatorio> PorExecutante,
    decimal Valor,
    decimal Medido,
    decimal Pago,
    decimal APagar,
    decimal Retido,
    decimal PorMedir,
    string? Prazos,
    IReadOnlyList<OrdemDeServico> Vencidas,
    IReadOnlyList<OrdemDeServico> ParaConferir)
{
    public bool Vazio => Ordens.Count == 0;

    public int? DiasDeAtraso(OrdemDeServico ordem) => TextosDaOrdem.DiasDeAtraso(ordem, Hoje);

    /// <summary>A data que o período olha: a emissão; num rascunho, a criação.</summary>
    public static DateOnly DataDeReferencia(OrdemDeServico ordem) =>
        ordem.Emissao ?? DateOnly.FromDateTime(ordem.Historico.Count > 0 ? ordem.Historico[0].Quando : DateTime.MinValue);

    public static RelatorioDasOrdens Montar(IEnumerable<OrdemDeServico> ordens, FiltroDoRelatorioDasOrdens filtro, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(ordens);
        ArgumentNullException.ThrowIfNull(filtro);
        var lista = ordens.Where(o =>
        {
            var data = DataDeReferencia(o);
            if (filtro.De is { } de && data < de || filtro.Ate is { } ate && data > ate)
                return false;
            if (!string.IsNullOrWhiteSpace(filtro.Executante)
                && !string.Equals(o.Dados.Executante?.Trim(), filtro.Executante.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            return filtro.Situacoes switch
            {
                SituacoesDoRelatorio.Abertas => o.Situacao == SituacaoDaOrdem.Rascunho || TextosDaOrdem.EmAndamento(o.Situacao),
                SituacoesDoRelatorio.Encerradas => o.Situacao is SituacaoDaOrdem.Conferida or SituacaoDaOrdem.Paga or SituacaoDaOrdem.Cancelada,
                SituacoesDoRelatorio.Vencidas => o.Vencida(hoje),
                _ => true,
            };
        }).OrderBy(o => o.Numero).ToList();
        var validas = lista.Where(o => o.Situacao != SituacaoDaOrdem.Cancelada).ToList();
        var porExecutante = validas.Where(o => o.Situacao != SituacaoDaOrdem.Rascunho)
            .GroupBy(o => o.Dados.Executante ?? "sem executante", StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key)
            .Select(g => new ExecutanteNoRelatorio(g.Key, g.Count(), g.Sum(o => o.Total), g.Sum(o => o.TotalMedido), g.Sum(o => o.TotalPago),
                g.Sum(o => o.APagar), TextosDaOrdem.Prazos(g)))
            .ToList();
        return new RelatorioDasOrdens(filtro, hoje, lista,
            [.. lista.GroupBy(o => o.Situacao).OrderBy(g => g.Key).Select(g => (g.Key, g.Count()))],
            porExecutante,
            validas.Sum(o => o.Total), validas.Sum(o => o.TotalMedido), validas.Sum(o => o.TotalPago), validas.Sum(o => o.APagar),
            validas.Sum(o => o.Retido), validas.Where(o => TextosDaOrdem.EmAndamento(o.Situacao)).Sum(o => o.PorMedir),
            TextosDaOrdem.Prazos(lista),
            [.. lista.Where(o => o.Vencida(hoje))],
            [.. lista.Where(o => o.Situacao == SituacaoDaOrdem.Concluida)]);
    }
}
