namespace ProjectPlanner.Core.Gantt;

/// <summary>A alça de uma ponta de barra: de onde sai o arraste que cria um vínculo.</summary>
/// <param name="Ponto">A ponta da barra (onde nasce a linha guia e onde as setas saem ou chegam).</param>
public sealed record AlcaGantt(BarraGantt Barra, BordaDaBarra Borda, PontoGantt Ponto);

/// <summary>Onde um arraste de vínculo seria solto: a barra da linha sob o mouse e a ponta que vale.</summary>
public sealed record DestinoGantt(BarraGantt Barra, BordaDaBarra Borda, PontoGantt Ponto);

/// <summary>
/// O que está sob o mouse no Gantt, calculado só com a geometria do <see cref="LayoutGantt"/> (sem
/// depender do desenho), para o clique de seleção e o arraste de vínculos.
/// </summary>
/// <remarks>
/// <para>
/// Alças: nas duas pontas das barras de tarefas comuns e de marcos (resumos não têm vínculos, então
/// não têm alças). Cada alça pega <see cref="AlcaParaDentro"/> px para dentro da barra e
/// <see cref="AlcaParaFora"/> px para fora, na altura da barra com <see cref="FolgaVertical"/> px acima e
/// abaixo. Numa barra estreita demais para ter meio (menos de 2 × <see cref="AlcaParaDentro"/>) e nos
/// marcos, a metade esquerda é a alça do início e a direita, a do término.
/// </para>
/// <para>
/// Destino: vale a faixa inteira da linha (não só a barra), da ponta da barra −
/// <see cref="AlcaParaFora"/> até a outra + <see cref="AlcaParaFora"/>, para não exigir mira exata.
/// Soltar sobre a barra vale como o início dela; só a alça do término vale como término (nas barras
/// estreitas e nos marcos, pelas metades, como nas alças).
/// </para>
/// </remarks>
public static class LocalizadorGantt
{
    public const double AlcaParaDentro = 8;
    public const double AlcaParaFora = 4;
    public const double FolgaVertical = 3;

    /// <summary>A alça sob o ponto, se houver.</summary>
    public static AlcaGantt? AlcaEm(LayoutGantt layout, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var barra in layout.Barras)
        {
            if (barra.Tipo == TipoTarefa.Resumo
                || y < barra.Y - FolgaVertical || y > barra.Y + barra.Altura + FolgaVertical
                || x < barra.X - AlcaParaFora || x > barra.Direita + AlcaParaFora)
                continue;

            if (SoTemAlcas(barra))
                return Alca(barra, x < barra.X + barra.Largura / 2 ? BordaDaBarra.Inicio : BordaDaBarra.Termino);
            if (x <= barra.X + AlcaParaDentro)
                return Alca(barra, BordaDaBarra.Inicio);
            if (x >= barra.Direita - AlcaParaDentro)
                return Alca(barra, BordaDaBarra.Termino);
        }
        return null;
    }

    /// <summary>A barra sob o ponto, para o clique que seleciona (inclui resumos).</summary>
    public static BarraGantt? BarraEm(LayoutGantt layout, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.Barras.FirstOrDefault(b =>
            x >= b.X && x <= b.Direita && y >= b.Y && y <= b.Y + b.Altura);
    }

    /// <summary>Onde um arraste de vínculo seria solto no ponto, se houver uma barra ali.</summary>
    public static DestinoGantt? DestinoEm(LayoutGantt layout, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (y < 0)
            return null;
        var linha = (int)Math.Floor(y / layout.AlturaLinha);
        var barra = layout.Barras.FirstOrDefault(b => b.Linha == linha);
        if (barra is null || x < barra.X - AlcaParaFora || x > barra.Direita + AlcaParaFora)
            return null;

        var borda = barra.Tipo == TipoTarefa.Resumo ? BordaDaBarra.Inicio
            : SoTemAlcas(barra) ? (x < barra.X + barra.Largura / 2 ? BordaDaBarra.Inicio : BordaDaBarra.Termino)
            : x >= barra.Direita - AlcaParaDentro ? BordaDaBarra.Termino
            : BordaDaBarra.Inicio;
        return new DestinoGantt(barra, borda, Ponta(barra, borda));
    }

    /// <summary>A ponta da barra: o início à esquerda, o término à direita, na altura do centro.</summary>
    public static PontoGantt Ponta(BarraGantt barra, BordaDaBarra borda) =>
        new(borda == BordaDaBarra.Inicio ? barra.X : barra.Direita, barra.CentroY);

    private static bool SoTemAlcas(BarraGantt barra) =>
        barra.Tipo == TipoTarefa.Marco || barra.Largura < 2 * AlcaParaDentro;

    private static AlcaGantt Alca(BarraGantt barra, BordaDaBarra borda) => new(barra, borda, Ponta(barra, borda));
}
