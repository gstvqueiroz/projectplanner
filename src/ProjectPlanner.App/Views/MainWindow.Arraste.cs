using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Edicao;

namespace ProjectPlanner.App.Views;

// Bloco 18: arrastar tarefas pela célula Nº (como no MS Project). Bloco 73 (o plano "Arrastar para mover", M1 a M7): o arrasto
// muda a ordem e a hierarquia pelas regras de RegrasDeMovimento (as mesmas no Gantt): a tarefa mantém o nível se couber;
// puxar para a direita a põe como subtarefa da linha de cima, para a esquerda sobe um nível. Com várias selecionadas, vão
// todas. Começa depois de 5 px (no toque, segurando 0,4 s); a linha guia sai recuada no nível de destino, com a etiqueta do
// que acontece (ou do motivo do bloqueio) junto do cursor; perto das bordas a grade rola; Esc ou soltar fora da grade cancela.
public partial class MainWindow
{
    private const double LimiarDoArraste = 5;
    private const double BordaDaRolagem = 20;

    /// <summary>Quanto puxar para o lado (px) para mudar o nível.</summary>
    public const double PuxaoParaONivel = 28;

    private static readonly TimeSpan SegurarNoToque = TimeSpan.FromMilliseconds(400);
    private static readonly Cursor CursorDaAlca = new(StandardCursorType.Hand);
    private static readonly Cursor CursorArrastando = new(StandardCursorType.DragMove);
    private static readonly Cursor CursorProibido = new(StandardCursorType.No);

    private IReadOnlyList<TarefaViewModel>? _armadas;
    private IReadOnlyList<TarefaViewModel>? _arrastadas;
    private Point _pontoDoClique;
    private Point _ultimoPontoDoArraste;
    private IPointer? _ponteiroDoToque;
    private readonly DispatcherTimer _rolagemDoArraste = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _seguraDoToque = new() { Interval = SegurarNoToque };

    private void LigarArrasteDaGrade()
    {
        Grade.AddHandler(PointerPressedEvent, GradeApertada, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grade.AddHandler(PointerMovedEvent, GradeMovida, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grade.AddHandler(PointerReleasedEvent, GradeSolta, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grade.AddHandler(PointerCaptureLostEvent, (_, _) => EncerrarArrasteDaGrade(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _arrastadas is not null)
            {
                EncerrarArrasteDaGrade();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        _rolagemDoArraste.Tick += (_, _) => RolarDuranteOArraste();
        _seguraDoToque.Tick += (_, _) =>
        {
            _seguraDoToque.Stop();
            if (_armadas is { } armadas && _ponteiroDoToque is { } ponteiro)
                ComecarArraste(armadas, ponteiro);
        };
    }

    private void GradeApertada(object? sender, PointerPressedEventArgs e)
    {
        _armadas = null;
        _seguraDoToque.Stop();
        if (_viewModel is null || !e.GetCurrentPoint(Grade).Properties.IsLeftButtonPressed || !SobreAColunaNumero(e.GetPosition(Grade)))
            return;
        var linha = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        if (linha?.DataContext is not TarefaViewModel tarefa)
            return;
        // M6: apertando numa das selecionadas, vão todas (a grade ainda não trocou a seleção por esta linha).
        _armadas = _viewModel.Selecionadas.Contains(tarefa) && e.KeyModifiers == KeyModifiers.None ? [.. _viewModel.Selecionadas] : [tarefa];
        _pontoDoClique = e.GetPosition(Grade);
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen)
        {
            // M1: no toque, o arrasto começa segurando 0,4 s (andar antes disso é rolar).
            _ponteiroDoToque = e.Pointer;
            _seguraDoToque.Start();
        }
    }

    private void GradeMovida(object? sender, PointerEventArgs e)
    {
        var ponto = e.GetPosition(Grade);
        if (_arrastadas is null)
        {
            if (_armadas is { } armadas && e.GetCurrentPoint(Grade).Properties.IsLeftButtonPressed
                && Distancia(ponto, _pontoDoClique) >= LimiarDoArraste)
            {
                if (_seguraDoToque.IsEnabled)
                {
                    _seguraDoToque.Stop(); // andou antes de segurar: é rolagem
                    _armadas = null;
                    return;
                }
                ComecarArraste(armadas, e.Pointer);
            }
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
        _armadas = null;
        _seguraDoToque.Stop();
        if (_arrastadas is not { } arrastadas || _viewModel is null)
            return;
        var ponto = e.GetPosition(Grade);
        var (lacuna, puxao) = (LacunaSob(ponto), Puxao(ponto));
        EncerrarArrasteDaGrade();
        if (lacuna is { } g)
            _viewModel.MoverArrastando(arrastadas, g, puxao);
        e.Handled = true;
    }

    private void ComecarArraste(IReadOnlyList<TarefaViewModel> linhas, IPointer ponteiro)
    {
        if (_viewModel is null || (Grade.IsReadOnly is false && Grade.CommitEdit() is false))
            return;
        _arrastadas = linhas;
        _armadas = null;
        _viewModel.DefinirSelecao(linhas, linhas[0]); // as arrastadas continuam selecionadas
        foreach (var linha in linhas)
            linha.SendoArrastada = true;
        ponteiro.Capture(Grade);
        _rolagemDoArraste.Start();
    }

    // Guia, etiqueta e cursor para a posição atual do mouse.
    private void AtualizarArraste()
    {
        if (_arrastadas is not { } arrastadas || _viewModel is null)
            return;
        var lacuna = LacunaSob(_ultimoPontoDoArraste);
        var naCamada = Grade.TranslatePoint(_ultimoPontoDoArraste, GuiaDeArraste) ?? default;
        var quais = arrastadas.Count == 1 ? arrastadas[0].Nome : $"{arrastadas.Count} tarefas";
        if (lacuna is not { } g)
        {
            GuiaDeArraste.EsconderALinha();
            GuiaDeArraste.Etiquetar(naCamada, $"{quais} · solte na grade", valido: false);
            Grade.Cursor = CursorProibido;
            return;
        }
        var plano = _viewModel.PreverMovimento(arrastadas, g, Puxao(_ultimoPontoDoArraste));
        if (plano.Valido && AreaDasLinhas() is { } naJanela && this.TranslatePoint(naJanela.TopLeft, GuiaDeArraste) is { } origem)
        {
            // A guia é desenhada na camada dela, que começa na grid (não no canto da janela), recuada no nível de destino.
            var area = new Rect(origem, naJanela.Size);
            var y = area.Top + g * GanttViewModel.AlturaLinha - DeslocamentoVertical();
            var esquerda = Math.Min(area.Right - 40, InicioDoNome(area.Left) + plano.Nivel * ColunaNomeDaTarefa.RecuoPorNivel);
            GuiaDeArraste.Mostrar(Math.Clamp(y, area.Top, area.Bottom), esquerda, area.Right);
            GuiaDeArraste.Etiquetar(naCamada, arrastadas.Count == 1 ? plano.Dica : $"{arrastadas.Count} · {plano.Dica}", valido: true);
            Grade.Cursor = CursorArrastando;
        }
        else
        {
            GuiaDeArraste.EsconderALinha();
            GuiaDeArraste.Etiquetar(naCamada, plano.Motivo ?? quais, valido: false);
            Grade.Cursor = CursorProibido;
        }
    }

    private void EncerrarArrasteDaGrade()
    {
        _rolagemDoArraste.Stop();
        _seguraDoToque.Stop();
        foreach (var linha in _arrastadas ?? [])
            linha.SendoArrastada = false;
        _arrastadas = null;
        _armadas = null;
        GuiaDeArraste.Esconder();
        Grade.Cursor = null;
    }

    // Perto da borda de cima ou de baixo das linhas, a grid rola uma linha por vez (D8).
    private void RolarDuranteOArraste()
    {
        if (_arrastadas is null || _viewModel is null || AreaDasLinhas() is not { } area || _viewModel.LinhasVisiveis.Count == 0)
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

    // A lacuna entre as linhas visíveis sob o ponto (da grid): a metade de cima de uma linha é a lacuna antes dela; a de baixo, a
    // depois. Abaixo da última linha (nas linhas vazias), a lacuna depois da última; fora da grade (para os lados), nula.
    private int? LacunaSob(Point pontoNaGrade)
    {
        if (_viewModel is null || AreaDasLinhas() is not { } area || Grade.TranslatePoint(pontoNaGrade, this) is not { } ponto
            || ponto.X < area.Left || ponto.X > area.Right || ponto.Y < area.Top - BordaDaRolagem || ponto.Y > area.Bottom + BordaDaRolagem)
            return null;
        var (linha, metadeDeBaixo) = LinhaSob(pontoNaGrade);
        if (linha is { } i)
            return metadeDeBaixo ? i + 1 : i;
        var y = ponto.Y - area.Top + DeslocamentoVertical();
        return y < 0 ? 0 : _viewModel.LinhasVisiveis.Count;
    }

    // M3: puxar para a direita (subtarefa da linha de cima) ou para a esquerda (sobe um nível).
    private int Puxao(Point pontoNaGrade) =>
        pontoNaGrade.X - _pontoDoClique.X >= PuxaoParaONivel ? 1 : pontoNaGrade.X - _pontoDoClique.X <= -PuxaoParaONivel ? -1 : 0;

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

    // Onde o nome começa na camada da guia (o recuo do nível 0); sem a coluna Nome à vista, a borda esquerda das linhas.
    private double InicioDoNome(double esquerdaDasLinhas)
    {
        var cabecalho = Grade.GetVisualDescendants().OfType<DataGridColumnHeader>().FirstOrDefault(h => Equals(h.Content, "Nome") && h.IsVisible);
        return cabecalho?.TranslatePoint(default, GuiaDeArraste) is { } inicio ? inicio.X + 8 : esquerdaDasLinhas;
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
