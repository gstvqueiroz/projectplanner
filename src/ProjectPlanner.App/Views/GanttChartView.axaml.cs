using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.Views;

/// <summary>
/// Desenha o <see cref="GanttViewModel.Layout"/>: cabeçalho de tempo, barras (vermelhas as críticas,
/// azuis as demais, com a parte concluída escurecida), losangos dos marcos (pretos, ou vermelhos se
/// críticos), barras "chapéu" dos resumos (escuras, sem cor de crítica, como no MS Project), linhas de
/// folga, setas de dependência e o fundo levemente acinzentado dos fins de semana. A tarefa selecionada
/// ganha um contorno na linha (o mesmo da grid) e na barra; clicar numa barra seleciona a tarefa. Não
/// calcula posição nenhuma; tudo vem pronto da <see cref="CalculadoraLayoutGantt"/>.
/// </summary>
/// <remarks>
/// <para>
/// Arrastar a partir da alça de uma ponta de barra até outra barra cria um vínculo (Bloco 12). O que
/// está sob o mouse vem da geometria do layout (<see cref="LocalizadorGantt"/>), não das formas
/// desenhadas; o tipo vem das bordas (<see cref="InferenciaDependencia"/>); a validação e a criação ficam
/// no <see cref="GanttViewModel"/>. Um clique (apertar e soltar sem andar
/// <see cref="LimiarDoArraste"/> px) continua só selecionando.
/// </para>
/// <para>
/// Durante o arraste: linha guia tracejada da ponta de origem até o cursor; sobre uma barra, contorno
/// verde (válido) ou vermelho (recusado, com cursor de proibido), as alças dela com a que vale
/// preenchida, e um rótulo com o vínculo que será criado ou o motivo da recusa. Soltar fora de qualquer
/// barra, na própria origem ou teclar Esc cancela.
/// </para>
/// </remarks>
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
    // Seleção: o mesmo cinza-azulado do contorno da linha selecionada na grid (MainWindow.axaml).
    private static readonly IBrush ContornoDaLinha = new SolidColorBrush(Color.Parse("#6A8CAF"));
    private static readonly IBrush ContornoDaBarra = new SolidColorBrush(Color.Parse("#263238"));
    private static readonly IBrush Marco = new SolidColorBrush(Color.Parse("#212121"));
    private static readonly IBrush Resumo = new SolidColorBrush(Color.Parse("#37474F"));
    // Ícone de fotos: cinza-azulado de baixo contraste, para não competir com as barras.
    private static readonly IBrush IconeDeFotos = new SolidColorBrush(Color.Parse("#90A4AE"));
    private static readonly IBrush Grade = new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80));
    // Fins de semana: cinza a ~9% de opacidade (#80808018), ≈ #F4F4F4 sobre branco. Translúcido para
    // funcionar também no tema escuro.
    private static readonly IBrush FimDeSemana = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
    private static readonly IBrush GradeForte = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80));

    // Arraste de vínculo: a partir de quanto o mouse andou com o botão apertado deixa de ser clique.
    private const double LimiarDoArraste = 4;

    private static readonly IBrush Valido = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush Neutro = new SolidColorBrush(Color.Parse("#607D8B"));
    private static readonly IBrush FundoDoRotulo = new SolidColorBrush(Color.FromArgb(0xE6, 0x32, 0x32, 0x32));
    private static readonly Cursor CursorDeLigar = new(StandardCursorType.Cross);
    private static readonly Cursor CursorDeSelecionar = new(StandardCursorType.Hand);
    private static readonly Cursor CursorProibido = new(StandardCursorType.No);

    private GanttViewModel? _viewModel;
    private double _deslocamentoVertical;
    private TopLevel? _janela;

    private enum EstadoDoArraste { Parado, Armado, Arrastando }

    private EstadoDoArraste _arraste;
    private AlcaGantt? _origem;
    private IPointer? _ponteiro;
    private Point _pontoDoClique;
    private (Guid TarefaId, BordaDaBarra Borda)? _alcaSobOMouse;
    private (Guid TarefaId, BordaDaBarra Borda)? _destinoPrevisto;
    private PrevisaoDeLigacao? _previsao;

    public GanttChartView()
    {
        InitializeComponent();
        Linhas.RowDefinitions[0].Height = new GridLength(GanttViewModel.AlturaCabecalho);
        RolagemCorpo.ScrollChanged += CorpoRolou;
        RolagemCorpo.SizeChanged += (_, e) => _viewModel?.AtualizarAreaVisivel(e.NewSize.Width, e.NewSize.Height);
        RolagemCorpo.AddHandler(PointerWheelChangedEvent, TratarRodaDoMouse, RoutingStrategies.Tunnel);
        Superficie.PointerPressed += SuperficieApertada;
        Superficie.AddHandler(PointerMovedEvent, (_, e) => _mouseSobreOCorpo = e.GetPosition(Corpo), RoutingStrategies.Tunnel);
        Superficie.AddHandler(PointerPressedEvent, (_, e) => _mouseSobreOCorpo = e.GetPosition(Corpo), RoutingStrategies.Tunnel);
        // Menu de contexto (Bloco 17): só sobre uma barra (ou o ícone de fotos), que fica selecionada.
        _itemGerenciarFotos.Click += (_, _) =>
        {
            if (_tarefaDoMenu is { } id)
                _viewModel?.AbrirFotos(id);
        };
        Superficie.ContextMenu = new ContextMenu { Items = { _itemGerenciarFotos } };

        Superficie.AddHandler(ContextRequestedEvent, PedirMenuDeContexto, RoutingStrategies.Tunnel);
        Superficie.PointerMoved += SuperficieMovida;
        Superficie.PointerReleased += SuperficieSolta;
        Superficie.PointerCaptureLost += (_, _) =>
        {
            if (_arraste != EstadoDoArraste.Parado)
                EncerrarArraste();
        };
        Superficie.PointerExited += (_, _) => _mouseSobreOCorpo = null;
        Superficie.PointerExited += (_, _) =>
        {
            if (_arraste == EstadoDoArraste.Parado)
                LimparAlcaSobOMouse();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _janela = TopLevel.GetTopLevel(this);
        _janela?.AddHandler(KeyDownEvent, TeclaApertada, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _janela?.RemoveHandler(KeyDownEvent, TeclaApertada);
        _janela = null;
        base.OnDetachedFromVisualTree(e);
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
        {
            _viewModel.PropertyChanged -= LayoutMudou;
            _viewModel.RolagemPedida -= RolarAte;
        }
        _viewModel = DataContext as GanttViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += LayoutMudou;
            _viewModel.RolagemPedida += RolarAte;
            _viewModel.AtualizarAreaVisivel(RolagemCorpo.Bounds.Width, RolagemCorpo.Bounds.Height);
        }
        Desenhar();
    }

    // Bloco 25 (P3, P8): leva o X pedido a um terço da largura visível (ou ao começo, para o X 0).
    private void RolarAte(double x)
    {
        RolagemCorpo.UpdateLayout(); // um zoom novo (ajustar ao projeto) acabou de mudar a largura do corpo
        var alvo = Math.Max(0, x - RolagemCorpo.Bounds.Width / 3);
        RolagemCorpo.Offset = new Vector(alvo, RolagemCorpo.Offset.Y);
        RolagemCabecalho.Offset = new Vector(RolagemCorpo.Offset.X, 0);
    }

    private void LayoutMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GanttViewModel.Layout) or nameof(GanttViewModel.MostrarCaminhoCritico)
            or nameof(GanttViewModel.TarefaDestacada) or nameof(GanttViewModel.MostrarFotos)
            or nameof(GanttViewModel.MostrarFolga))
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
        foreach (var barra in layout.Barras.Where(_viewModel.EstaDestacada))
            DesenharContornoDaLinha(barra, layout.Largura);
        foreach (var barra in layout.Barras)
            DesenharBarra(barra, _viewModel);
        foreach (var seta in layout.Setas)
            DesenharSeta(seta, _viewModel);
        if (layout.XDeHoje is { } xDeHoje)
            DesenharHoje(xDeHoje);
    }

    // Bloco 25 (C1): a linha tracejada do dia de hoje, por cima das barras, e o rótulo no cabeçalho.
    private static readonly IBrush LinhaDeHoje = new SolidColorBrush(Color.Parse("#E65100"));

    private void DesenharHoje(double x)
    {
        Corpo.Children.Add(new Line
        {
            Name = "LinhaHoje",
            StartPoint = new Point(x, 0),
            EndPoint = new Point(x, Corpo.Height),
            Stroke = LinhaDeHoje,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
            IsHitTestVisible = false,
        });
        var rotulo = new Border
        {
            Name = "RotuloHoje",
            Background = LinhaDeHoje,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = "Hoje", FontSize = 10, Foreground = Brushes.White },
        };
        rotulo.Measure(Size.Infinity);
        Posicionar(Cabecalho, rotulo, x - rotulo.DesiredSize.Width / 2, GanttViewModel.AlturaCabecalho - rotulo.DesiredSize.Height - 1);
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
        // Divisória de baixo de cada pista, também das vazias até o fim da área visível (Bloco 20), como a grid.
        for (var i = 1; i <= layout.Pistas; i++)
            Corpo.Children.Add(new Line
            {
                StartPoint = new Point(0, i * GanttViewModel.AlturaLinha),
                EndPoint = new Point(layout.Largura, i * GanttViewModel.AlturaLinha),
                Stroke = Grade,
                StrokeThickness = 1,
            });
    }

    // Contorno de 2 px na linha inteira, por dentro dela, continuando o da linha na grid.
    private void DesenharContornoDaLinha(BarraGantt barra, double largura) =>
        Posicionar(Corpo, new Rectangle
        {
            Width = largura,
            Height = GanttViewModel.AlturaLinha,
            Stroke = ContornoDaLinha,
            StrokeThickness = 2,
            IsHitTestVisible = false,
        }, 0, barra.Linha * GanttViewModel.AlturaLinha);

    private void DesenharBarra(BarraGantt barra, GanttViewModel viewModel)
    {
        // A folga (só com Exibir > Folga ligado) corre logo abaixo da barra, como no MS Project, para não
        // cruzar o nome da tarefa.
        var yFolga = barra.Y + barra.Altura + 2;
        if (viewModel.MostrarFolga && barra.FimDaFolga is { } fimDaFolga && fimDaFolga > barra.Direita)
            Corpo.Children.Add(new Line
            {
                StartPoint = new Point(barra.Direita, yFolga),
                EndPoint = new Point(fimDaFolga, yFolga),
                Stroke = LinhaDeFolga,
                StrokeThickness = 2,
            });

        Shape forma = barra.Tipo switch
        {
            TipoTarefa.Marco => Losango(barra, viewModel.PintarComoCritica(barra) ? Critica : Marco),
            TipoTarefa.Resumo => Chapeu(barra),
            _ => new Rectangle
            {
                Width = barra.Largura,
                Height = barra.Altura,
                Fill = viewModel.PintarComoCritica(barra) ? Critica : NaoCritica,
                RadiusX = 2,
                RadiusY = 2,
            },
        };
        forma.Stroke = viewModel.EstaDestacada(barra) ? ContornoDaBarra : null;
        forma.StrokeThickness = 1.5;
        ToolTip.SetTip(forma, barra.Nome);
        if (forma is Rectangle)
            Posicionar(Corpo, forma, barra.X, barra.Y);
        else
            Corpo.Children.Add(forma);

        if (barra.LarguraConcluida > 0)
            Posicionar(Corpo, new Rectangle
            {
                Width = barra.LarguraConcluida,
                Height = barra.Altura / 3,
                Fill = Concluida,
                IsHitTestVisible = false,
            }, barra.X, barra.CentroY - barra.Altura / 6);

        // O nome vem logo depois da barra, além do trecho por onde as setas saem do fim dela; com fotos,
        // a câmera fica nesse lugar e o nome anda para depois dela.
        var xDoNome = barra.Direita + DistanciaDoRotulo;
        if (viewModel.MostraIconeDeFotos(barra))
        {
            DesenharIconeDeFotos(barra);
            xDoNome += LocalizadorGantt.LadoDoIconeDeFotos + 4;
        }
        Posicionar(Corpo, new TextBlock
        {
            Text = barra.Nome,
            FontSize = 11,
            IsHitTestVisible = false,
        }, xDoNome, barra.CentroY - 8);
    }

    // Uma câmera pequena: corpo arredondado, visor em cima e a lente no meio.
    private void DesenharIconeDeFotos(BarraGantt barra)
    {
        var (x, y, lado) = LocalizadorGantt.IconeDeFotos(barra);
        var camera = new Canvas { Width = lado, Height = lado };
        camera.Children.Add(new Rectangle { Width = 4, Height = 2, Fill = IconeDeFotos, [Canvas.LeftProperty] = 4, [Canvas.TopProperty] = 1.5 });
        camera.Children.Add(new Rectangle { Width = lado, Height = 8, RadiusX = 1.5, RadiusY = 1.5, Fill = IconeDeFotos, [Canvas.TopProperty] = 3 });
        camera.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = Brushes.White, [Canvas.LeftProperty] = 3.5, [Canvas.TopProperty] = 4.5 });
        ToolTip.SetTip(camera, barra.QuantidadeDeFotos == 1 ? "1 foto" : $"{barra.QuantidadeDeFotos} fotos");
        Posicionar(Corpo, camera, x, y);
    }

    // O marco: um losango da altura da barra, centrado no instante dele.
    private static Polygon Losango(BarraGantt barra, IBrush cor) => new()
    {
        Points =
        [
            new Point(barra.X, barra.CentroY),
            new Point(barra.X + barra.Largura / 2, barra.Y),
            new Point(barra.Direita, barra.CentroY),
            new Point(barra.X + barra.Largura / 2, barra.Y + barra.Altura),
        ],
        Fill = cor,
    };

    // O resumo: uma barra fina na parte de cima da linha, com as pontas descendo em triângulo (o
    // "chapéu" do MS Project), de ponta a ponta das subtarefas.
    private static Polygon Chapeu(BarraGantt barra)
    {
        var (inicio, fim) = barra.Largura >= 4 ? (barra.X, barra.Direita) : (barra.X - 3, barra.X + 3);
        var topo = barra.Y;
        var espessura = barra.Altura * 0.4;
        var perna = barra.Altura * 0.35;
        var ponta = Math.Min(5, (fim - inicio) / 2);
        return new Polygon
        {
            Points =
            [
                new Point(inicio, topo),
                new Point(fim, topo),
                new Point(fim, topo + espessura + perna),
                new Point(fim - ponta, topo + espessura),
                new Point(inicio + ponta, topo + espessura),
                new Point(inicio, topo + espessura + perna),
            ],
            Fill = Resumo,
        };
    }

    private void DesenharSeta(SetaGantt seta, GanttViewModel viewModel)
    {
        var critica = viewModel.PintarComoCritica(seta);
        var cor = critica ? Critica : SetaNaoCritica;
        Corpo.Children.Add(new Polyline
        {
            Points = seta.Pontos.Select(p => new Point(p.X, p.Y)).ToList(),
            Stroke = cor,
            StrokeThickness = critica ? 1.6 : 1.2,
        });
        Corpo.Children.Add(new Polygon
        {
            Points = seta.Ponta.Select(p => new Point(p.X, p.Y)).ToList(),
            Fill = cor,
        });
    }

    // --- Mouse: seleção e arraste de vínculos ---

    private readonly MenuItem _itemGerenciarFotos = new() { Header = "Gerenciar fotos…" };
    private Guid? _tarefaDoMenu;
    private Point? _mouseSobreOCorpo;

    // Clique direito: sobre uma barra, seleciona a tarefa e deixa o menu abrir para ela; fora das barras
    // (ou pelo teclado, sem posição), não há menu.
    private void PedirMenuDeContexto(object? sender, ContextRequestedEventArgs e)
    {
        _tarefaDoMenu = null;
        // Um pedido sem posição (vindo do teclado ou da plataforma) usa a última posição do mouse sobre o
        // Gantt; se o mouse não está sobre ele, não há menu.
        var ponto = e.TryGetPosition(Corpo, out var posicao) ? posicao : _mouseSobreOCorpo;
        if (_viewModel?.Layout is { } layout && _arraste == EstadoDoArraste.Parado && ponto is { } p
            && (IconeDeFotosEm(layout, p) ?? LocalizadorGantt.BarraEm(layout, p.X, p.Y)) is { } barra)
        {
            _tarefaDoMenu = barra.TarefaId;
            // O menu é aberto aqui, ancorado na superfície (que não é redesenhada), e não pelo Avalonia, que o
            // ancoraria na forma clicada: a seleção redesenha o Gantt, a forma sai da tela e o menu ficava
            // aberto mas invisível.
            var menu = Superficie.ContextMenu!;
            menu.Placement = PlacementMode.Pointer;
            menu.Open(Superficie);
            _viewModel.ClicarNaBarra(barra.TarefaId);
        }
        e.Handled = true;
    }

    private void SuperficieApertada(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.Layout is not { } layout || !e.GetCurrentPoint(Superficie).Properties.IsLeftButtonPressed)
            return;

        var ponto = e.GetPosition(Corpo);
        if (IconeDeFotosEm(layout, ponto) is { } comFotos)
        {
            // O ícone fica além das alças (ver LocalizadorGantt.IconeDeFotos): o clique só abre a galeria.
            _viewModel.AbrirFotos(comFotos.TarefaId);
            e.Handled = true;
            return;
        }
        var alca = LocalizadorGantt.AlcaEm(layout, ponto.X, ponto.Y);
        var barra = alca?.Barra ?? LocalizadorGantt.BarraEm(layout, ponto.X, ponto.Y);
        if (barra is null)
            return;

        _viewModel.ClicarNaBarra(barra.TarefaId); // seleciona, como no Bloco 9
        if (alca is not null)
        {
            // O ponteiro fica com a superfície: o desenho é refeito com a seleção, e o gesto continua
            // mesmo que o mouse saia do Gantt.
            _arraste = EstadoDoArraste.Armado;
            _origem = alca;
            _ponteiro = e.Pointer;
            _pontoDoClique = ponto;
            e.Pointer.Capture(Superficie);
        }
        e.Handled = true;
    }

    private void SuperficieMovida(object? sender, PointerEventArgs e)
    {
        if (_viewModel?.Layout is not { } layout)
            return;

        var ponto = e.GetPosition(Corpo);
        switch (_arraste)
        {
            case EstadoDoArraste.Parado:
                MostrarOQueEstaSobOMouse(layout, ponto);
                break;
            case EstadoDoArraste.Armado when Distancia(ponto, _pontoDoClique) >= LimiarDoArraste:
                _arraste = EstadoDoArraste.Arrastando;
                ToolTip.SetServiceEnabled(Superficie, false);
                AtualizarArraste(layout, ponto);
                break;
            case EstadoDoArraste.Arrastando:
                AtualizarArraste(layout, ponto);
                break;
        }
    }

    private void SuperficieSolta(object? sender, PointerReleasedEventArgs e)
    {
        var (estado, origem) = (_arraste, _origem);
        EncerrarArraste();
        if (estado != EstadoDoArraste.Arrastando || origem is null || _viewModel?.Layout is not { } layout)
            return;

        var ponto = e.GetPosition(Corpo);
        var destino = LocalizadorGantt.DestinoEm(layout, ponto.X, ponto.Y);
        _viewModel.SoltarLigacao(origem.Barra.TarefaId, origem.Borda, destino?.Barra.TarefaId,
            destino?.Borda ?? BordaDaBarra.Inicio);
    }

    private void TeclaApertada(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _arraste == EstadoDoArraste.Parado)
            return;
        EncerrarArraste();
        e.Handled = true;
    }

    // Volta ao estado parado: sem linha guia nem destaque, cursor e dicas normais, ponteiro liberado.
    private void EncerrarArraste()
    {
        var ponteiro = _ponteiro;
        _arraste = EstadoDoArraste.Parado;
        _origem = null;
        _ponteiro = null;
        _destinoPrevisto = null;
        _previsao = null;
        _alcaSobOMouse = null;
        Interacao.Children.Clear();
        ToolTip.SetServiceEnabled(Superficie, true);
        Superficie.Cursor = null;
        ponteiro?.Capture(null);
    }

    // Parado: cruz e o círculo da alça sobre uma ponta de barra; mãozinha sobre o resto da barra.
    private void MostrarOQueEstaSobOMouse(LayoutGantt layout, Point ponto)
    {
        var alca = LocalizadorGantt.AlcaEm(layout, ponto.X, ponto.Y);
        (Guid, BordaDaBarra)? chave = alca is null ? null : (alca.Barra.TarefaId, alca.Borda);
        if (chave != _alcaSobOMouse)
        {
            _alcaSobOMouse = chave;
            Interacao.Children.Clear();
            if (alca is not null)
                DesenharAlca(alca.Ponto, Neutro, preenchida: false);
        }
        Superficie.Cursor = alca is not null ? CursorDeLigar
            : LocalizadorGantt.BarraEm(layout, ponto.X, ponto.Y) is not null || IconeDeFotosEm(layout, ponto) is not null
                ? CursorDeSelecionar
            : null;
    }

    private void LimparAlcaSobOMouse()
    {
        _alcaSobOMouse = null;
        Interacao.Children.Clear();
        Superficie.Cursor = null;
    }

    private void AtualizarArraste(LayoutGantt layout, Point ponto)
    {
        var origem = _origem!;
        var destino = LocalizadorGantt.DestinoEm(layout, ponto.X, ponto.Y);
        (Guid, BordaDaBarra)? chave = destino is null ? null : (destino.Barra.TarefaId, destino.Borda);
        if (chave != _destinoPrevisto)
        {
            _destinoPrevisto = chave;
            _previsao = _viewModel!.PreverLigacao(origem.Barra.TarefaId, origem.Borda, destino?.Barra.TarefaId,
                destino?.Borda ?? BordaDaBarra.Inicio);
        }

        var estado = _previsao?.Estado;
        var cor = estado switch
        {
            EstadoDaLigacao.Valida => Valido,
            EstadoDaLigacao.Invalida => Critica,
            _ => Neutro,
        };
        Interacao.Children.Clear();
        if (destino is not null && estado is EstadoDaLigacao.Valida or EstadoDaLigacao.Invalida)
            DesenharDestino(destino, cor);
        Interacao.Children.Add(new Line
        {
            StartPoint = new Point(origem.Ponto.X, origem.Ponto.Y),
            EndPoint = ponto,
            Stroke = cor,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
        });
        DesenharAlca(origem.Ponto, cor, preenchida: true);
        if (_previsao is not null)
            DesenharRotulo(_previsao, ponto);
        Superficie.Cursor = estado == EstadoDaLigacao.Invalida ? CursorProibido : CursorDeLigar;
    }

    // Contorno em volta da barra de destino e, se ela aceita vínculos, as duas alças, com a que vale preenchida.
    private void DesenharDestino(DestinoGantt destino, IBrush cor)
    {
        var barra = destino.Barra;
        Posicionar(Interacao, new Rectangle
        {
            Width = barra.Largura + 6,
            Height = barra.Altura + 6,
            Stroke = cor,
            StrokeThickness = 2,
            RadiusX = 3,
            RadiusY = 3,
        }, barra.X - 3, barra.Y - 3);
        if (barra.Tipo == TipoTarefa.Resumo)
            return;
        foreach (var borda in new[] { BordaDaBarra.Inicio, BordaDaBarra.Termino })
            DesenharAlca(LocalizadorGantt.Ponta(barra, borda), borda == destino.Borda ? cor : Neutro,
                preenchida: borda == destino.Borda);
    }

    private void DesenharAlca(PontoGantt ponto, IBrush cor, bool preenchida)
    {
        const double diametro = 8;
        Posicionar(Interacao, new Ellipse
        {
            Width = diametro,
            Height = diametro,
            Fill = preenchida ? cor : Brushes.White,
            Stroke = cor,
            StrokeThickness = 1.5,
        }, ponto.X - diametro / 2, ponto.Y - diametro / 2);
    }

    // O rótulo fica um pouco abaixo e à direita do cursor, sem sair da área do Gantt.
    private void DesenharRotulo(PrevisaoDeLigacao previsao, Point ponto)
    {
        var rotulo = new Border
        {
            Background = FundoDoRotulo,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = previsao.Texto,
                Foreground = Brushes.White,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360,
            },
        };
        rotulo.Measure(Size.Infinity);
        var x = Math.Max(0, Math.Min(ponto.X + 14, Corpo.Width - rotulo.DesiredSize.Width - 4));
        Posicionar(Interacao, rotulo, x, ponto.Y + 18);
    }

    // Só conta se o ícone está sendo mostrado (opção "Fotos" ligada).
    private BarraGantt? IconeDeFotosEm(LayoutGantt layout, Point ponto) =>
        _viewModel?.MostrarFotos == true ? LocalizadorGantt.IconeDeFotosEm(layout, ponto.X, ponto.Y) : null;

    private static double Distancia(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static void Posicionar(Canvas canvas, Control controle, double x, double y)
    {
        Canvas.SetLeft(controle, x);
        Canvas.SetTop(controle, y);
        canvas.Children.Add(controle);
    }
}
