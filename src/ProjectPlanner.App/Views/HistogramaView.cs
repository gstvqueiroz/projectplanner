using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.Views;

/// <summary>
/// Bloco 34: o histograma de um recurso: uma barra por dia útil (do primeiro ao último dia de uso), a linha do disponível por
/// dia e, em vermelho, os dias acima dele. O texto acompanha o tema.
/// </summary>
public sealed class HistogramaView : Control
{
    public static readonly StyledProperty<UsoDoRecurso?> UsoProperty = AvaloniaProperty.Register<HistogramaView, UsoDoRecurso?>(nameof(Uso));

    private static readonly IBrush Barra = new SolidColorBrush(Color.Parse("#1E88E5"));
    private static readonly IBrush Acima = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly IBrush Grade = new SolidColorBrush(Color.FromArgb(0x40, 0x90, 0xA4, 0xAE));
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    static HistogramaView() => AffectsRender<HistogramaView>(UsoProperty);

    public HistogramaView() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public UsoDoRecurso? Uso
    {
        get => GetValue(UsoProperty);
        set => SetValue(UsoProperty, value);
    }

    public override void Render(DrawingContext contexto)
    {
        base.Render(contexto);
        var texto = this.TryFindResource("SystemControlForegroundBaseHighBrush", ActualThemeVariant, out var recurso) && recurso is IBrush pincel
            ? pincel
            : Brushes.Gray;
        if (Uso is not { Dias.Count: > 0 } uso || Bounds.Width < 120 || Bounds.Height < 100)
        {
            Escrever(contexto, "Nenhuma tarefa usa este recurso.", new Point(12, 12), texto, 12);
            return;
        }

        // Os dias úteis do primeiro ao último uso (os dias sem uso ficam com barra zero).
        var porDia = uso.Dias.ToDictionary(d => d.Dia, d => d.Quantidade);
        var dias = new List<DateOnly>();
        for (var dia = uso.Dias[0].Dia; dia <= uso.Dias[^1].Dia; dia = dia.AddDays(1))
            if (dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                dias.Add(dia);
        var area = new Rect(48, 12, Bounds.Width - 60, Bounds.Height - 48);
        var limite = uso.Recurso.Disponivel;
        var maximo = (double)Math.Max(uso.Pico, limite ?? 0) * 1.15;
        if (maximo <= 0)
            maximo = 1;
        double Y(double valor) => area.Bottom - valor / maximo * area.Height;
        var largura = area.Width / dias.Count;

        var caneta = new Pen(Grade, 1);
        foreach (var marca in Marcas(maximo))
        {
            contexto.DrawLine(caneta, new Point(area.Left, Y(marca)), new Point(area.Right, Y(marca)));
            var rotulo = marca.ToString("0.##", Brasil);
            Escrever(contexto, rotulo, new Point(area.Left - 8 - Medir(rotulo, 11), Y(marca) - 8), texto, 11);
        }
        for (var i = 0; i < dias.Count; i++)
        {
            var valor = (double)porDia.GetValueOrDefault(dias[i]);
            if (valor > 0)
                contexto.FillRectangle(limite is { } l && valor > (double)l ? Acima : Barra,
                    new Rect(area.Left + i * largura + Math.Min(1, largura * 0.15), Y(valor), Math.Max(1, largura - Math.Min(2, largura * 0.3)), area.Bottom - Y(valor)));
            if (i == 0 || dias[i].Month != dias[i - 1].Month || (largura > 14 && dias[i].DayOfWeek == DayOfWeek.Monday))
            {
                var rotulo = i == 0 || dias[i].Month != dias[i - 1].Month ? dias[i].ToString("dd/MM", Brasil) : dias[i].Day.ToString(Brasil);
                contexto.DrawLine(caneta, new Point(area.Left + i * largura, area.Bottom), new Point(area.Left + i * largura, area.Bottom + 4));
                Escrever(contexto, rotulo, new Point(area.Left + i * largura + 2, area.Bottom + 4), texto, 10);
            }
        }
        if (limite is { } disponivel)
        {
            var y = Y((double)disponivel);
            contexto.DrawLine(new Pen(Acima, 1.5, new DashStyle([4, 3], 0)), new Point(area.Left, y), new Point(area.Right, y));
            var rotulo = $"Disponível: {disponivel.ToString("0.##", Brasil)} por dia";
            Escrever(contexto, rotulo, new Point(area.Right - Medir(rotulo, 11) - 4, y - 18), Acima, 11);
        }
    }

    // Linhas da grade em valores redondos (1, 2, 5, 10…).
    private static IEnumerable<double> Marcas(double maximo)
    {
        var passo = Math.Pow(10, Math.Floor(Math.Log10(maximo / 4)));
        foreach (var fator in new[] { 1, 2, 5, 10 })
            if (maximo / (passo * fator) <= 6)
            {
                passo *= fator;
                break;
            }
        for (var v = 0.0; v <= maximo; v += passo)
            yield return v;
    }

    private static double Medir(string texto, double tamanho) =>
        new FormattedText(texto, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, tamanho, Brushes.Black).Width;

    private static void Escrever(DrawingContext contexto, string texto, Point ponto, IBrush cor, double tamanho) =>
        contexto.DrawText(new FormattedText(texto, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, tamanho, cor), ponto);
}
