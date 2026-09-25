using CommunityToolkit.Mvvm.ComponentModel;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// O Gantt ao lado da grid: guarda o último cronograma calculado e o layout dele na escala atual.
/// O layout é refeito quando o cronograma é recalculado, quando o zoom muda e quando a área visível
/// muda de tamanho (sem recalcular o CPM); a View só desenha o que está em <see cref="Layout"/>.
/// </summary>
public sealed partial class GanttViewModel : ViewModelBase
{
    public const double EscalaMinima = 4;
    public const double EscalaMaxima = 60;
    public const double EscalaInicial = OpcoesGantt.EscalaPadrao;

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
    private IReadOnlySet<Guid> _ocultas = new HashSet<Guid>();
    private double _larguraVisivel;
    private double _alturaVisivel;

    /// <summary>Zoom: largura de um dia, entre <see cref="EscalaMinima"/> e <see cref="EscalaMaxima"/>.</summary>
    [ObservableProperty]
    public partial double PixelsPorDia { get; set; } = EscalaInicial;

    /// <summary>
    /// Opção de exibição (só da sessão): falso pinta as tarefas e setas críticas na cor comum. Não
    /// muda o cálculo; <see cref="BarraGantt.EhCritica"/> continua como o CPM calculou.
    /// </summary>
    [ObservableProperty]
    public partial bool MostrarCaminhoCritico { get; set; } = true;

    /// <summary>
    /// A tarefa cuja barra aparece destacada: a selecionada na grid, repassada pela janela principal
    /// (nula quando não há seleção ou quando o destaque de seleção está desligado).
    /// </summary>
    [ObservableProperty]
    public partial Guid? TarefaDestacada { get; set; }

    /// <summary>O usuário clicou na barra de uma tarefa; a janela principal a seleciona.</summary>
    public event Action<Guid>? BarraClicada;

    /// <summary>O que desenhar; nulo quando não há cronograma calculado.</summary>
    [ObservableProperty]
    public partial LayoutGantt? Layout { get; private set; }

    /// <summary>Se a barra vai na cor de crítica: crítica no cálculo e com o destaque ligado.</summary>
    public bool PintarComoCritica(BarraGantt barra) => MostrarCaminhoCritico && barra.EhCritica;

    /// <summary>Se a seta vai na cor de crítica: parte de um caminho crítico e com o destaque ligado.</summary>
    public bool PintarComoCritica(SetaGantt seta) => MostrarCaminhoCritico && seta.EhCritica;

    public bool EstaDestacada(BarraGantt barra) => TarefaDestacada == barra.TarefaId;

    public void ClicarNaBarra(Guid tarefaId) => BarraClicada?.Invoke(tarefaId);

    /// <summary>
    /// Passa a mostrar um cronograma recém-calculado, sem as tarefas <paramref name="ocultas"/> (as de
    /// resumos recolhidos na grid; nulo mantém as de antes).
    /// </summary>
    public void Atualizar(Cronograma cronograma, ResultadoCronograma resultado, IReadOnlySet<Guid>? ocultas = null)
    {
        _cronograma = cronograma;
        _resultado = resultado;
        if (ocultas is not null)
            _ocultas = ocultas;
        RefazerLayout();
    }

    /// <summary>
    /// As tarefas que a grid escondeu (dentro de resumos recolhidos): somem daqui também, para que as
    /// linhas continuem lado a lado. Não recalcula nada.
    /// </summary>
    public void DefinirTarefasOcultas(IReadOnlySet<Guid> ocultas)
    {
        if (_ocultas.SetEquals(ocultas))
            return;
        _ocultas = ocultas;
        RefazerLayout();
    }

    /// <summary>O cálculo falhou: não há o que desenhar.</summary>
    public void Limpar()
    {
        _cronograma = null;
        _resultado = null;
        Layout = null;
    }

    /// <summary>
    /// A área onde o Gantt aparece mudou de tamanho (janela redimensionada, divisor arrastado): o eixo de
    /// dias e a grade passam a preenchê-la.
    /// </summary>
    public void AtualizarAreaVisivel(double largura, double altura)
    {
        largura = double.IsFinite(largura) ? Math.Max(0, largura) : 0;
        altura = double.IsFinite(altura) ? Math.Max(0, altura) : 0;
        if (largura == _larguraVisivel && altura == _alturaVisivel)
            return;
        _larguraVisivel = largura;
        _alturaVisivel = altura;
        RefazerLayout();
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
            : CalculadoraLayoutGantt.Calcular(_cronograma, _resultado, new OpcoesGantt(PixelsPorDia, AlturaLinha)
            {
                LarguraVisivel = _larguraVisivel,
                AlturaVisivel = _alturaVisivel,
                TarefasOcultas = _ocultas,
            });
}
