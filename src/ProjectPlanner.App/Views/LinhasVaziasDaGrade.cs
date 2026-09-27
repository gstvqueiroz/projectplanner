using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.Views;

/// <summary>
/// Desenha, sobre a área vazia da grid de tarefas, as linhas que faltam para preencher a altura visível,
/// com a mesma grade (bordas horizontais e verticais) das linhas reais. É só desenho: fica por cima da
/// grid, não recebe o mouse e não existe para o DataGrid (nada a editar nem a selecionar). A janela chama
/// <see cref="Atualizar"/> quando a grid muda de tamanho, de colunas ou de quantidade de linhas.
/// </summary>
public sealed class LinhasVaziasDaGrade : Control
{
    private IReadOnlyList<double> _bordasDasColunas = [];
    private Rect _areaDasLinhas;
    private double _inicio;
    private int _quantidade;
    private double _alturaLinha = 1;
    private IBrush _cor = Brushes.LightGray;

    /// <summary>Quantas linhas vazias estão desenhadas agora.</summary>
    public int Quantidade => _quantidade;

    /// <param name="areaDasLinhas">A área das linhas da grid (sem o cabeçalho nem a barra de rolagem), neste controle.</param>
    /// <param name="linhasReais">Quantas linhas de tarefa a grid mostra.</param>
    /// <param name="alturaLinha">Distância entre uma linha e a próxima, com a borda.</param>
    /// <param name="bordasDasColunas">Onde terminam as colunas visíveis (x neste controle), na ordem da tela.</param>
    /// <param name="cor">A cor das bordas das células da grid.</param>
    public void Atualizar(Rect areaDasLinhas, int linhasReais, double alturaLinha, IReadOnlyList<double> bordasDasColunas, IBrush cor)
    {
        var quantidade = PreenchimentoDaGrade.LinhasVazias(linhasReais, areaDasLinhas.Height, alturaLinha);
        var inicio = areaDasLinhas.Top + linhasReais * alturaLinha;
        if (quantidade == _quantidade && inicio == _inicio && areaDasLinhas == _areaDasLinhas && alturaLinha == _alturaLinha
            && ReferenceEquals(cor, _cor) && bordasDasColunas.SequenceEqual(_bordasDasColunas))
            return;
        (_areaDasLinhas, _quantidade, _inicio, _alturaLinha, _bordasDasColunas, _cor) =
            (areaDasLinhas, quantidade, inicio, alturaLinha, bordasDasColunas.ToList(), cor);
        InvalidateVisual();
    }

    public override void Render(DrawingContext contexto)
    {
        if (_quantidade == 0)
            return;
        var caneta = new Pen(_cor, 1);
        var fim = Math.Min(_areaDasLinhas.Bottom, _inicio + _quantidade * _alturaLinha);
        using (contexto.PushClip(_areaDasLinhas))
        {
            // Borda de baixo de cada linha vazia (a de cima já é a da última linha real).
            for (var i = 1; i <= _quantidade; i++)
            {
                var y = Math.Floor(_inicio + i * _alturaLinha) - 0.5;
                contexto.DrawLine(caneta, new Point(_areaDasLinhas.Left, y), new Point(_areaDasLinhas.Right, y));
            }
            foreach (var x in _bordasDasColunas)
            {
                var xLinha = Math.Floor(x) - 0.5;
                contexto.DrawLine(caneta, new Point(xLinha, _inicio), new Point(xLinha, fim));
            }
        }
    }
}
