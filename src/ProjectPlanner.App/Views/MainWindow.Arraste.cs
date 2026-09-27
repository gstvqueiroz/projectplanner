using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

// Bloco 18: reordenar tarefas arrastando a linha pela célula Nº (como no MS Project). Só entre irmãs; a
// decisão de onde cai e a mudança ficam no MainWindowViewModel (DestinoAoSoltar, SoltarTarefa). Aqui só se
// traduz o mouse em "linha visível e metade" pela geometria (linhas de altura fixa), como no Gantt.
public partial class MainWindow
{
    private const double LimiarDoArraste = 5;
    private const double BordaDaRolagem = 20;

    private static readonly Cursor CursorDaAlca = new(StandardCursorType.Hand);
    private static readonly Cursor CursorArrastando = new(StandardCursorType.DragMove);
    private static readonly Cursor CursorProibido = new(StandardCursorType.No);

    private TarefaViewModel? _linhaArmada;
    private TarefaViewModel? _linhaArrastada;
    private Point _pontoDoClique;
    private Point _ultimoPontoDoArraste;
    private readonly DispatcherTimer _rolagemDoArraste = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private void LigarArrasteDaGrade()
    {
        Grade.AddHandler(PointerPressedEvent, GradeApertada, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grade.AddHandler(PointerMovedEvent, GradeMovida, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grade.AddHandler(PointerReleasedEvent, GradeSolta, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grade.AddHandler(PointerCaptureLostEvent, (_, _) => EncerrarArrasteDaGrade(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _linhaArrastada is not null)
            {
                EncerrarArrasteDaGrade();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        _rolagemDoArraste.Tick += (_, _) => RolarDuranteOArraste();
    }

    private void GradeApertada(object? sender, PointerPressedEventArgs e)
    {
        _linhaArmada = null;
        if (!e.GetCurrentPoint(Grade).Properties.IsLeftButtonPressed || !SobreAColunaNumero(e.GetPosition(Grade)))
            return;
        var linha = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        if (linha?.DataContext is TarefaViewModel tarefa)
        {
            _linhaArmada = tarefa;
            _pontoDoClique = e.GetPosition(Grade);
        }
    }

    private void GradeMovida(object? sender, PointerEventArgs e)
    {
        var ponto = e.GetPosition(Grade);
        if (_linhaArrastada is null)
        {
            if (_linhaArmada is not null && e.GetCurrentPoint(Grade).Properties.IsLeftButtonPressed
                && Distancia(ponto, _pontoDoClique) >= LimiarDoArraste)
                ComecarArraste(_linhaArmada, e.Pointer);
            else
            {
                Grade.Cursor = SobreAColunaNumero(ponto) && LinhaSob(ponto).Linha is not null ? CursorDaAlca : null;
                return;
            }
        }
        _ultimoPontoDoArraste = ponto;
        AtualizarArraste();
        e.Handled = true;
    }

    private void GradeSolta(object? sender, PointerReleasedEventArgs e)
    {
        _linhaArmada = null;
        if (_linhaArrastada is not { } arrastada || _viewModel is null)
            return;
        var (linha, metade) = LinhaSob(e.GetPosition(Grade));
        EncerrarArrasteDaGrade();
        _viewModel.SoltarTarefa(arrastada, linha, metade);
        e.Handled = true;
    }

    private void ComecarArraste(TarefaViewModel linha, IPointer ponteiro)
    {
        if (Grade.IsReadOnly is false && Grade.CommitEdit() is false)
            return;
        _linhaArrastada = linha;
        _linhaArmada = null;
        linha.SendoArrastada = true;
        ponteiro.Capture(Grade);
        _rolagemDoArraste.Start();
    }

    // Guia e cursor para a posição atual do mouse.
    private void AtualizarArraste()
    {
        if (_linhaArrastada is null || _viewModel is null)
            return;
        var (linha, metade) = LinhaSob(_ultimoPontoDoArraste);
        if (_viewModel.DestinoAoSoltar(_linhaArrastada, linha, metade) is { } destino && AreaDasLinhas() is { } naJanela
            && this.TranslatePoint(naJanela.TopLeft, GuiaDeArraste) is { } origem)
        {
            // A guia é desenhada na camada dela, que começa na grid (não no canto da janela).
            var area = new Rect(origem, naJanela.Size);
            var y = area.Top + destino.LinhaDaGuia * GanttViewModel.AlturaLinha - DeslocamentoVertical();
            GuiaDeArraste.Mostrar(Math.Clamp(y, area.Top, area.Bottom), area.Left, area.Right);
            Grade.Cursor = CursorArrastando;
        }
        else
        {
            GuiaDeArraste.Esconder();
            Grade.Cursor = CursorProibido;
        }
    }

    private void EncerrarArrasteDaGrade()
    {
        _rolagemDoArraste.Stop();
        if (_linhaArrastada is { } linha)
            linha.SendoArrastada = false;
        _linhaArrastada = null;
        _linhaArmada = null;
        GuiaDeArraste.Esconder();
        Grade.Cursor = null;
    }

    // Perto da borda de cima ou de baixo das linhas, a grid rola uma linha por vez (D8).
    private void RolarDuranteOArraste()
    {
        if (_linhaArrastada is null || _viewModel is null || AreaDasLinhas() is not { } area || _viewModel.LinhasVisiveis.Count == 0)
            return;
        var y = Grade.TranslatePoint(_ultimoPontoDoArraste, this)!.Value.Y;
        var primeira = (int)(DeslocamentoVertical() / GanttViewModel.AlturaLinha);
        var ultima = (int)((DeslocamentoVertical() + area.Height) / GanttViewModel.AlturaLinha);
        var alvo = y < area.Top + BordaDaRolagem ? primeira - 1 : y > area.Bottom - BordaDaRolagem ? ultima + 1 : -1;
        if (alvo < 0 || alvo >= _viewModel.LinhasVisiveis.Count)
            return;
        Grade.ScrollIntoView(_viewModel.LinhasVisiveis[alvo], null);
        AtualizarArraste();
    }

    // A linha visível sob o ponto (da grid) e se o ponto está na metade de baixo dela; nula fora das linhas
    // de tarefa (cabeçalho, barra de rolagem, linhas vazias do preenchimento).
    private (int? Linha, bool MetadeDeBaixo) LinhaSob(Point pontoNaGrade)
    {
        if (_viewModel is null || AreaDasLinhas() is not { } area || Grade.TranslatePoint(pontoNaGrade, this) is not { } ponto
            || !area.Contains(ponto))
            return (null, false);
        var y = ponto.Y - area.Top + DeslocamentoVertical();
        var indice = (int)Math.Floor(y / GanttViewModel.AlturaLinha);
        if (indice < 0 || indice >= _viewModel.LinhasVisiveis.Count)
            return (null, false);
        return (indice, y - indice * GanttViewModel.AlturaLinha >= GanttViewModel.AlturaLinha / 2);
    }

    // A área das linhas da grid (sem cabeçalho nem barras de rolagem), nas coordenadas da janela.
    private Rect? AreaDasLinhas()
    {
        var linhas = Grade.GetVisualDescendants().OfType<DataGridRowsPresenter>().FirstOrDefault();
        return linhas?.TranslatePoint(default, this) is { } origem ? new Rect(origem, linhas.Bounds.Size) : null;
    }

    private double DeslocamentoVertical() => _rolagemDaGrade?.Value ?? 0;

    // Se o ponto (da grid) está na coluna Nº, pela posição do cabeçalho dela (vale com colunas reordenadas).
    private bool SobreAColunaNumero(Point pontoNaGrade)
    {
        var cabecalho = Grade.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => Equals(h.Content, "Nº") && h.IsVisible);
        if (cabecalho?.TranslatePoint(default, Grade) is not { } inicio)
            return false;
        return pontoNaGrade.X >= inicio.X && pontoNaGrade.X < inicio.X + cabecalho.Bounds.Width;
    }

    private static double Distancia(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
