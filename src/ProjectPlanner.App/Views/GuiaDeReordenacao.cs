using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ProjectPlanner.App.Views;

/// <summary>
/// A linha guia do arraste de tarefas na grid (Bloco 18): uma linha horizontal onde a tarefa vai cair ao
/// ser solta. Fica por cima da grid e não recebe o mouse.
/// </summary>
public sealed class GuiaDeReordenacao : Control
{
    /// <summary>A mesma cor do contorno da seleção.</summary>
    public static readonly IBrush Cor = new SolidColorBrush(Color.Parse("#6A8CAF"));

    private (double Y, double Esquerda, double Direita)? _linha;

    /// <summary>Onde a guia está desenhada agora (nula quando não há).</summary>
    public (double Y, double Esquerda, double Direita)? Linha => _linha;

    public void Mostrar(double y, double esquerda, double direita)
    {
        if (_linha == (y, esquerda, direita))
            return;
        _linha = (y, esquerda, direita);
        InvalidateVisual();
    }

    public void Esconder()
    {
        if (_linha is null)
            return;
        _linha = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext contexto)
    {
        if (_linha is not var (y, esquerda, direita))
            return;
        contexto.DrawLine(new Pen(Cor, 2), new Point(esquerda, y), new Point(direita, y));
        // Marcas nas pontas, como nos editores de lista.
        contexto.DrawEllipse(Cor, null, new Point(esquerda + 4, y), 3.5, 3.5);
    }
}
