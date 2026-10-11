using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.Views;

/// <summary>
/// Bloco 35: a Curva S desenhada (% acumulado por período): a linha de base tracejada, o cronograma atual e o ponto do
/// realizado na data de hoje. Bloco 37: o medido (verde), o custo real (roxo, pontilhado) e a linha do realizado pelas
/// medições aprovadas. O mesmo desenho do PDF (GeradorFisicoFinanceiroPdf), em Avalonia.
/// </summary>
public sealed class CurvaSView : Control
{
    public static readonly StyledProperty<CurvaS?> CurvaProperty = AvaloniaProperty.Register<CurvaSView, CurvaS?>(nameof(Curva));

    private static readonly IBrush Grade = new SolidColorBrush(Color.FromArgb(0x40, 0x90, 0xA4, 0xAE));
    private static IBrush CorDa(TipoDaSerie tipo) => tipo switch
    {
        TipoDaSerie.Prevista => new SolidColorBrush(Color.Parse("#90A4AE")),
        TipoDaSerie.Medida => new SolidColorBrush(Color.Parse("#43A047")),
        TipoDaSerie.CustoReal => new SolidColorBrush(Color.Parse("#AB47BC")),
        _ => new SolidColorBrush(Color.Parse("#1E88E5")),
    };
    private static readonly IBrush Realizado = new SolidColorBrush(Color.Parse("#E65100"));

    static CurvaSView() => AffectsRender<CurvaSView>(CurvaProperty);

    public CurvaSView() => ActualThemeVariantChanged += (_, _) => InvalidateVisual(); // o texto muda de cor com o tema

    public CurvaS? Curva
    {
        get => GetValue(CurvaProperty);
        set => SetValue(CurvaProperty, value);
    }

    public override void Render(DrawingContext contexto)
    {
        base.Render(contexto);
        if (Curva is not { } curva || Bounds.Width < 120 || Bounds.Height < 120)
            return;
        // A cor do texto do tema em uso (no escuro, clara); sem o tema, a busca simples devolveria a do claro.
        var texto = this.TryFindResource("SystemControlForegroundBaseHighBrush", ActualThemeVariant, out var recurso) && recurso is IBrush pincel
            ? pincel
            : Brushes.Gray;
        var area = new Rect(48, 12, Bounds.Width - 64, Bounds.Height - 70);
        var n = Math.Max(1, curva.Periodos.Count);
        double X(double posicao) => area.Left + posicao / n * area.Width;
        double Y(double percentual) => area.Bottom - Math.Clamp(percentual, 0, 100) / 100 * area.Height;
        var caneta = new Pen(Grade, 1);
        for (var p = 0; p <= 100; p += 20)
        {
            contexto.DrawLine(caneta, new Point(area.Left, Y(p)), new Point(area.Right, Y(p)));
            Escrever(contexto, $"{p}%", new Point(area.Left - 40, Y(p) - 8), texto, 11);
        }
        var passo = Math.Max(1, (int)Math.Ceiling(curva.Periodos.Count / Math.Max(1, area.Width / 70)));
        for (var i = 0; i < curva.Periodos.Count; i += passo)
            Escrever(contexto, curva.Periodos[i].Rotulo, new Point(X(i + 0.5) - 20, area.Bottom + 6), texto, 10);

        var xLegenda = area.Left;
        var yLegenda = area.Bottom + 28;
        void Legenda(string rotulo, Action<Point> marca)
        {
            var formatado = new FormattedText(rotulo, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, texto);
            if (xLegenda + 44 + formatado.Width > area.Right && xLegenda > area.Left)
                (xLegenda, yLegenda) = (area.Left, yLegenda + 16);
            marca(new Point(xLegenda, yLegenda + 8));
            contexto.DrawText(formatado, new Point(xLegenda + 28, yLegenda));
            xLegenda += 44 + formatado.Width;
        }

        foreach (var serie in curva.Series)
        {
            var traco = serie.Tipo switch
            {
                TipoDaSerie.Prevista => new DashStyle([3, 2], 0),
                TipoDaSerie.CustoReal => new DashStyle([1, 1.5], 0),
                _ => null,
            };
            var linha = new Pen(CorDa(serie.Tipo), 2.2, traco);
            var anterior = new Point(X(0), Y(0));
            for (var i = 0; i < serie.PercentualAcumulado.Count; i++)
            {
                var ponto = new Point(X(i + 1), Y(serie.PercentualAcumulado[i]));
                contexto.DrawLine(linha, anterior, ponto);
                anterior = ponto;
            }
            Legenda(serie.Nome, p => contexto.DrawLine(linha, p, p + new Point(22, 0)));
        }
        if (curva.Realizado is { } realizado && curva.PosicaoDe(realizado.Data) is { } posicao)
        {
            var pontos = curva.Historico.Select(h => (Posicao: curva.PosicaoDe(h.Data), h.Percentual)).Where(h => h.Posicao is not null)
                .Select(h => new Point(X(h.Posicao!.Value), Y(h.Percentual))).Append(new Point(X(posicao), Y(realizado.Percentual))).ToList();
            var linhaDoRealizado = new Pen(Realizado, 1.6);
            for (var i = 1; i < pontos.Count; i++)
                contexto.DrawLine(linhaDoRealizado, pontos[i - 1], pontos[i]);
            foreach (var p in pontos.SkipLast(1))
                contexto.DrawEllipse(Realizado, null, p, 3, 3);
            contexto.DrawEllipse(Realizado, null, pontos[^1], 5, 5);
            Legenda($"Realizado em {realizado.Data:dd/MM/yyyy}: {realizado.Percentual:0.0}%", p => contexto.DrawEllipse(Realizado, null, p + new Point(11, 0), 5, 5));
        }
    }

    private static double Escrever(DrawingContext contexto, string texto, Point ponto, IBrush cor, double tamanho)
    {
        var formatado = new FormattedText(texto, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, tamanho, cor);
        contexto.DrawText(formatado, ponto);
        return formatado.Width;
    }
}
