using CommunityToolkit.Mvvm.ComponentModel;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// O Gantt ao lado da grid: guarda o último cronograma calculado e o layout dele na escala atual.
/// O layout é refeito quando o cronograma é recalculado e quando o zoom muda (sem recalcular o CPM);
/// a View só desenha o que está em <see cref="Layout"/>.
/// </summary>
public sealed partial class GanttViewModel : ViewModelBase
{
    public const double EscalaMinima = 4;
    public const double EscalaMaxima = 60;
    public const double EscalaInicial = 20;

    /// <summary>Altura de cada linha; a grid usa a mesma, para que a linha N e a barra N fiquem lado a lado.</summary>
    public const double AlturaLinha = 30;

    /// <summary>
    /// O RowHeight a configurar no DataGrid: ele soma à altura da linha 1 px de linha de grade horizontal
    /// (espessura interna dele), então a linha ocupa exatamente <see cref="AlturaLinha"/>.
    /// </summary>
    public const double AlturaLinhaDaGrade = AlturaLinha - 1;

    /// <summary>Altura do cabeçalho de tempo; a grid usa a mesma no cabeçalho das colunas.</summary>
    public const double AlturaCabecalho = 48;

    private Cronograma? _cronograma;
    private ResultadoCronograma? _resultado;

    /// <summary>Zoom: largura de um dia, entre <see cref="EscalaMinima"/> e <see cref="EscalaMaxima"/>.</summary>
    [ObservableProperty]
    public partial double PixelsPorDia { get; set; } = EscalaInicial;

    /// <summary>O que desenhar; nulo quando não há cronograma calculado.</summary>
    [ObservableProperty]
    public partial LayoutGantt? Layout { get; private set; }

    /// <summary>Passa a mostrar um cronograma recém-calculado.</summary>
    public void Atualizar(Cronograma cronograma, ResultadoCronograma resultado)
    {
        _cronograma = cronograma;
        _resultado = resultado;
        RefazerLayout();
    }

    /// <summary>O cálculo falhou: não há o que desenhar.</summary>
    public void Limpar()
    {
        _cronograma = null;
        _resultado = null;
        Layout = null;
    }

    partial void OnPixelsPorDiaChanged(double value)
    {
        var limitado = double.IsNaN(value) ? EscalaInicial : Math.Clamp(value, EscalaMinima, EscalaMaxima);
        if (limitado != value)
        {
            PixelsPorDia = limitado;
            return;
        }
        RefazerLayout();
    }

    private void RefazerLayout() =>
        Layout = _cronograma is null || _resultado is null
            ? null
            : CalculadoraLayoutGantt.Calcular(_cronograma, _resultado, new OpcoesGantt(PixelsPorDia, AlturaLinha));
}
