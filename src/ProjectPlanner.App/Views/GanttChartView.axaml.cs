using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.Views;

/// <summary>
/// Desenha o <see cref="GanttViewModel.Layout"/>: cabeçalho de tempo, barras (vermelhas as críticas,
/// azuis as demais, com a parte concluída escurecida), linhas de folga, setas de dependência e o fundo
/// levemente acinzentado dos fins de semana. Não
/// calcula posição nenhuma; tudo vem pronto da <see cref="CalculadoraLayoutGantt"/>.
/// </summary>
public partial class GanttChartView : UserControl
{
    // Folga no fim do corpo, para o Gantt conseguir acompanhar a grid até a última linha.
    private const double SobraNoFim = 60;

    // Quanto a roda do mouse rola na horizontal por unidade (o mesmo passo do DataGrid).
    private const double PassoDaRoda = 50;

    // Distância do nome ao fim da barra: passa do trecho horizontal (8) por onde as setas saem.
    private const double DistanciaDoRotulo = 12;

    private static readonly IBrush Critica = new SolidColorBrush(Color.Parse("#D32F2F"));
    private static readonly IBrush NaoCritica = new SolidColorBrush(Color.Parse("#1E88E5"));
    private static readonly IBrush Concluida = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));
    private static readonly IBrush LinhaDeFolga = new SolidColorBrush(Color.Parse("#9E9E9E"));
    private static readonly IBrush SetaNaoCritica = new SolidColorBrush(Color.Parse("#607D8B"));
    private static readonly IBrush Grade = new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80));
    // Fins de semana: cinza a ~9% de opacidade (#80808018), ≈ #F4F4F4 sobre branco. Translúcido para
    // funcionar também no tema escuro.
    private static readonly IBrush FimDeSemana = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
    private static readonly IBrush GradeForte = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80));

    private GanttViewModel? _viewModel;
    private double _deslocamentoVertical;

    public GanttChartView()
    {
        InitializeComponent();
        Linhas.RowDefinitions[0].Height = new GridLength(GanttViewModel.AlturaCabecalho);
        RolagemCorpo.ScrollChanged += CorpoRolou;
        RolagemCorpo.SizeChanged += (_, e) => _viewModel?.AtualizarAreaVisivel(e.NewSize.Width, e.NewSize.Height);
        RolagemCorpo.AddHandler(PointerWheelChangedEvent, TratarRodaDoMouse, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// A roda do mouse girou na vertical sobre o Gantt (argumentos originais e o delta vertical). Quem
    /// manda na rolagem vertical é a grid; a janela repassa o giro a ela.
    /// </summary>
    public event Action<PointerWheelEventArgs, double>? RodaDoMouseVertical;

    /// <summary>Acompanha a rolagem vertical da grid.</summary>
    public void RolarVerticalmentePara(double deslocamento)
    {
        _deslocamentoVertical = deslocamento;
        RolagemCorpo.Offset = new Vector(RolagemCorpo.Offset.X, deslocamento);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= LayoutMudou;
        _viewModel = DataContext as GanttViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += LayoutMudou;
            _viewModel.AtualizarAreaVisivel(RolagemCorpo.Bounds.Width, RolagemCorpo.Bounds.Height);
        }
        Desenhar();
    }

    private void LayoutMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GanttViewModel.Layout))
            Desenhar();
    }

    // A grid é a única dona da rolagem vertical: se o corpo se mexer na vertical por qualquer outro
    // caminho (trackpad, teclado, acessibilidade), ele volta para onde a grid está.
    private void CorpoRolou(object? sender, ScrollChangedEventArgs e)
    {
        RolagemCabecalho.Offset = new Vector(RolagemCorpo.Offset.X, 0);
        if (Math.Abs(RolagemCorpo.Offset.Y - _deslocamentoVertical) > 0.5)
            RolagemCorpo.Offset = new Vector(RolagemCorpo.Offset.X, _deslocamentoVertical);
    }

    // O giro horizontal (ou Shift + giro vertical) rola o Gantt; o vertical vai para a grid.
    private void TratarRodaDoMouse(object? sender, PointerWheelEventArgs e)
    {
        var (horizontal, vertical) = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Delta.X == 0
            ? (e.Delta.Y, 0d)
            : (e.Delta.X, e.Delta.Y);
        if (horizontal != 0)
            RolagemCorpo.Offset = new Vector(RolagemCorpo.Offset.X - horizontal * PassoDaRoda, RolagemCorpo.Offset.Y);
        if (vertical != 0)
            RodaDoMouseVertical?.Invoke(e, vertical);
        e.Handled = true;
    }

    private void Desenhar()
    {
        Cabecalho.Children.Clear();
        Corpo.Children.Clear();
        if (_viewModel?.Layout is not { } layout)
        {
            Cabecalho.Width = Corpo.Width = Corpo.Height = 0;
            return;
        }

        Cabecalho.Width = Corpo.Width = layout.Largura;
        Cabecalho.Height = GanttViewModel.AlturaCabecalho;
        Corpo.Height = layout.Altura + SobraNoFim;

        DesenharCabecalho(layout.Escala);
        DesenharGradeDeFundo(layout);
        foreach (var barra in layout.Barras)
            DesenharBarra(barra);
        foreach (var seta in layout.Setas)
            DesenharSeta(seta);
    }

    private void DesenharCabecalho(EscalaGantt escala)
    {
        var alturaFaixa = GanttViewModel.AlturaCabecalho / 2;
        DesenharFaixa(escala.Superior, 0, alturaFaixa, FontWeight.SemiBold);
        DesenharFaixa(escala.Inferior, alturaFaixa, alturaFaixa, FontWeight.Normal);
    }

    private void DesenharFaixa(IReadOnlyList<MarcaEscala> marcas, double y, double altura, FontWeight peso)
    {
        foreach (var marca in marcas)
        {
            var celula = new Border
            {
                Width = marca.Largura,
                Height = altura,
                BorderBrush = GradeForte,
                BorderThickness = new Thickness(0, 0, 1, 1),
                ClipToBounds = true,
                Child = new TextBlock
                {
                    Text = marca.Rotulo,
                    FontSize = 11,
                    FontWeight = peso,
                    Margin = new Thickness(4, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            };
            Posicionar(Cabecalho, celula, marca.X, y);
        }
    }

    private void DesenharGradeDeFundo(LayoutGantt layout)
    {
        foreach (var coluna in layout.FinsDeSemana)
            Posicionar(Corpo, new Rectangle
            {
                Width = coluna.Largura,
                Height = Corpo.Height,
                Fill = FimDeSemana,
                IsHitTestVisible = false,
            }, coluna.X, 0);
        foreach (var marca in layout.Escala.Inferior)
            Corpo.Children.Add(new Line
            {
                StartPoint = new Point(marca.X, 0),
                EndPoint = new Point(marca.X, Corpo.Height),
                Stroke = Grade,
                StrokeThickness = 1,
            });
        for (var i = 1; i <= layout.Barras.Count; i++)
            Corpo.Children.Add(new Line
            {
                StartPoint = new Point(0, i * GanttViewModel.AlturaLinha),
                EndPoint = new Point(layout.Largura, i * GanttViewModel.AlturaLinha),
                Stroke = Grade,
                StrokeThickness = 1,
            });
    }

    private void DesenharBarra(BarraGantt barra)
    {
        // A folga corre logo abaixo da barra (como no MS Project), para não cruzar o nome da tarefa.
        var yFolga = barra.Y + barra.Altura + 2;
        if (barra.FimDaFolga is { } fimDaFolga)
            Corpo.Children.Add(new Line
            {
                StartPoint = new Point(barra.Direita, yFolga),
                EndPoint = new Point(fimDaFolga, yFolga),
                Stroke = LinhaDeFolga,
                StrokeThickness = 2,
            });

        var retangulo = new Rectangle
        {
            Width = barra.Largura,
            Height = barra.Altura,
            Fill = barra.EhCritica ? Critica : NaoCritica,
            RadiusX = 2,
            RadiusY = 2,
        };
        ToolTip.SetTip(retangulo, barra.Nome);
        Posicionar(Corpo, retangulo, barra.X, barra.Y);

        if (barra.LarguraConcluida > 0)
            Posicionar(Corpo, new Rectangle
            {
                Width = barra.LarguraConcluida,
                Height = barra.Altura / 3,
                Fill = Concluida,
                IsHitTestVisible = false,
            }, barra.X, barra.CentroY - barra.Altura / 6);

        // O nome vem logo depois da barra, além do trecho por onde as setas saem do fim dela.
        Posicionar(Corpo, new TextBlock
        {
            Text = barra.Nome,
            FontSize = 11,
            IsHitTestVisible = false,
        }, barra.Direita + DistanciaDoRotulo, barra.CentroY - 8);
    }

    private void DesenharSeta(SetaGantt seta)
    {
        var cor = seta.EhCritica ? Critica : SetaNaoCritica;
        Corpo.Children.Add(new Polyline
        {
            Points = seta.Pontos.Select(p => new Point(p.X, p.Y)).ToList(),
            Stroke = cor,
            StrokeThickness = seta.EhCritica ? 1.6 : 1.2,
        });
        Corpo.Children.Add(new Polygon
        {
            Points = seta.Ponta.Select(p => new Point(p.X, p.Y)).ToList(),
            Fill = cor,
        });
    }

    private static void Posicionar(Canvas canvas, Control controle, double x, double y)
    {
        Canvas.SetLeft(controle, x);
        Canvas.SetTop(controle, y);
        canvas.Children.Add(controle);
    }
}
