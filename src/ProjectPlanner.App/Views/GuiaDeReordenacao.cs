using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ProjectPlanner.App.Views;

/// <summary>
/// A linha guia do arraste de tarefas na grid (Bloco 18): uma linha horizontal onde a tarefa vai cair ao ser solta. Fica por
/// cima da grid e não recebe o mouse. Bloco 73: a linha já sai recuada no nível que a tarefa vai ter, e junto do cursor vai a
/// etiqueta com o que é arrastado e o que acontece ao soltar ("Tornar subtarefa de 'Escavação'"), ou o motivo do bloqueio.
/// </summary>
public sealed class GuiaDeReordenacao : Control
{
    /// <summary>A mesma cor do contorno da seleção.</summary>
    public static readonly IBrush Cor = new SolidColorBrush(Color.Parse("#6A8CAF"));

    private static readonly IBrush CorDoBloqueio = new SolidColorBrush(Color.Parse("#C62828"));

    private (double Y, double Esquerda, double Direita)? _linha;
    private (Point Ponto, string Texto, bool Valido)? _etiqueta;

    /// <summary>Onde a guia está desenhada agora (nula quando não há).</summary>
    public (double Y, double Esquerda, double Direita)? Linha => _linha;

    /// <summary>A etiqueta junto do cursor (nula quando não há).</summary>
    public string? Etiqueta => _etiqueta?.Texto;

    public void Mostrar(double y, double esquerda, double direita)
    {
        if (_linha == (y, esquerda, direita))
            return;
        _linha = (y, esquerda, direita);
        InvalidateVisual();
    }

    /// <summary>Bloco 73: a etiqueta junto do cursor (em vermelho quando o destino não vale).</summary>
    public void Etiquetar(Point ponto, string texto, bool valido)
    {
        if (_etiqueta == (ponto, texto, valido))
            return;
        _etiqueta = (ponto, texto, valido);
        InvalidateVisual();
    }

    public void Esconder()
    {
        if (_linha is null && _etiqueta is null)
            return;
        (_linha, _etiqueta) = (null, null);
        InvalidateVisual();
    }

    public void EsconderALinha()
    {
        if (_linha is null)
            return;
        _linha = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext contexto)
    {
        if (_linha is var (y, esquerda, direita))
        {
            contexto.DrawLine(new Pen(Cor, 2), new Point(esquerda, y), new Point(direita, y));
            // Marcas nas pontas, como nos editores de lista.
            contexto.DrawEllipse(Cor, null, new Point(esquerda + 4, y), 3.5, 3.5);
        }
        if (_etiqueta is var (ponto, texto, valido))
        {
            var formatado = new FormattedText(texto, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);
            var caixa = new Rect(ponto.X + 14, ponto.Y + 10, formatado.Width + 12, formatado.Height + 6);
            contexto.DrawRectangle(valido ? Cor : CorDoBloqueio, null, caixa, 4, 4);
            contexto.DrawText(formatado, caixa.TopLeft + new Vector(6, 3));
        }
    }
}
