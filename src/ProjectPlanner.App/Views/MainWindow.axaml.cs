using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class MainWindow : Window
{
    private bool _fechamentoConfirmado;
    private ScrollBar? _rolagemDaGrade;
    private ColunasViewModel? _colunas;
    private bool _aplicandoColunas;
    private MainWindowViewModel? _viewModel;
    private ModoVisualizacao _modoAplicado = ModoVisualizacao.Dividido;
    private GridLength _larguraDaTabela = new(620);
    private GridLength _larguraDoAssistente = new(340);

    // Arrastar a borda de um cabeçalho muda a largura muitas vezes por segundo: grava só quando para.
    private readonly DispatcherTimer _larguraParou = new() { Interval = TimeSpan.FromMilliseconds(400) };

    // Num resumo, estas colunas vêm das subtarefas (ver AvisarValorCalculadoNoResumo).
    private static readonly HashSet<string> CalculadasNoResumo =
        ["Duracao", "Inicio", "Termino", "PercentualConcluido", "Predecessoras", "NaoIniciarAntesDe", "Valor"];

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, AtalhosDaEstrutura, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, AtalhosDoTeclado, RoutingStrategies.Tunnel);
        MostrarAtalhos(AtalhosDeTeclado.EhMac);
        // Tamanho da janela, colunas (largura, ordem, visíveis) e tarefas mudam o layout da grid.
        Grade.LayoutUpdated += (_, _) => AtualizarLinhasVazias();
        Grade.AddHandler(ContextRequestedEvent, PedirMenuDaGrade, RoutingStrategies.Tunnel);
        LigarArrasteDaGrade();
        Paineis.SizeChanged += (_, _) => LimitarLarguraDaTabela();
        _larguraParou.Tick += (_, _) =>
        {
            _larguraParou.Stop();
            RegistrarColunas();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LinhaParaMostrar -= RolarAteALinha;
            _viewModel.PropertyChanged -= ViewModelMudou;
            _viewModel.Assistente.PropertyChanged -= AssistenteMudou;
            _viewModel.Barra.Mudou -= AjustarBarra;
        }
        base.OnDataContextChanged(e);
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is null)
            return;
        MontarMenuDeColunas(_viewModel.Colunas);
        MontarMenuDaBarra(_viewModel.Barra);
        _viewModel.Barra.Mudou += AjustarBarra;
        AjustarBarra();
        MostrarContadorDeFotos(_viewModel);
        _viewModel.LinhaParaMostrar += RolarAteALinha;
        _viewModel.PropertyChanged += ViewModelMudou;
        _viewModel.Assistente.PropertyChanged += AssistenteMudou;
        AplicarModoVisualizacao();
        AplicarPainelDoAssistente();
        AplicarTema();
    }

    // Bloco 25 (V3): o tema vale para o programa inteiro (as outras janelas também), por isso vai no Application.
    private void AplicarTema()
    {
        if (_viewModel is null || Application.Current is not { } aplicacao)
            return;
        aplicacao.RequestedThemeVariant = _viewModel.Tema switch
        {
            TemaDaInterface.Claro => ThemeVariant.Light,
            TemaDaInterface.Escuro => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    // Bloco 25 (P3): a grid rola até a linha pedida (Ir para a tarefa, Rolar até a tarefa).
    private void RolarAteALinha(TarefaViewModel linha) => Grade.ScrollIntoView(linha, null);

    /// <summary>O número no canto do botão de fotos (Bloco 25, F1): quantas fotos tem a tarefa selecionada.</summary>
    internal Border? ContadorDeFotos { get; private set; }

    // Um adorno, e não o conteúdo do botão: os botões da barra são só o ícone.
    private void MostrarContadorDeFotos(MainWindowViewModel viewModel)
    {
        ContadorDeFotos ??= new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1565C0")),
            CornerRadius = new CornerRadius(7),
            MinWidth = 14,
            Height = 14,
            Padding = new Thickness(3, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 1, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        ContadorDeFotos.DataContext = viewModel;
        ContadorDeFotos.Bind(IsVisibleProperty, new Binding(nameof(MainWindowViewModel.FotosDaSelecionada))
        {
            Converter = new Avalonia.Data.Converters.FuncValueConverter<int, bool>(n => n > 0),
        });
        ((TextBlock)ContadorDeFotos.Child!).Bind(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.FotosDaSelecionada)));
        AdornerLayer.SetAdorner(BotaoFotos, ContadorDeFotos);
    }

    private void AssistenteMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistenteViewModel.Visivel))
            AplicarPainelDoAssistente();
    }

    // Painel do assistente: colunas 3 (divisor) e 4 (painel) de Paineis. A largura que o usuário deu arrastando o
    // divisor é guardada ao ocultar e devolvida ao mostrar (só na sessão).
    private void AplicarPainelDoAssistente()
    {
        if (_viewModel is null)
            return;
        var colunas = Paineis.ColumnDefinitions;
        if (colunas[4].Width.IsAbsolute && colunas[4].Width.Value > 0)
            _larguraDoAssistente = colunas[4].Width;
        var visivel = _viewModel.Assistente.Visivel;
        colunas[3].Width = new GridLength(visivel ? 6 : 0);
        colunas[4].Width = visivel ? _larguraDoAssistente : new GridLength(0);
        colunas[4].MinWidth = visivel ? 260 : 0;
        PainelAssistente.IsVisible = DivisorDoAssistente.IsVisible = visivel;
        LimitarLarguraDaTabela();
    }

    private void ViewModelMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ModoVisualizacao))
            AplicarModoVisualizacao();
        else if (e.PropertyName == nameof(MainWindowViewModel.Tema))
            AplicarTema();
    }

    // Clique direito numa linha da grid: seleciona a linha e deixa o menu de contexto abrir para ela. Fora
    // das linhas (cabeçalho, linhas vazias) não há menu.
    private void PedirMenuDaGrade(object? sender, ContextRequestedEventArgs e)
    {
        var linha = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        if (linha?.DataContext is TarefaViewModel tarefa && _viewModel is not null)
        {
            _viewModel.TarefaSelecionada = tarefa;
            return;
        }
        e.Handled = true;
    }

    // Modos de visualização: só as larguras e a visibilidade das colunas de Paineis mudam; a grid e o Gantt
    // continuam os mesmos, com os dados que já têm. A largura da grid no modo Dividido (que o usuário pode
    // ter mudado arrastando o divisor) é guardada ao sair dele e devolvida ao voltar. No Somente Gantt a
    // grid fica com largura zero, mas não some: é ela que comanda a rolagem vertical do Gantt.
    private void AplicarModoVisualizacao()
    {
        if (_viewModel is null)
            return;
        var colunas = Paineis.ColumnDefinitions;
        if (_modoAplicado == ModoVisualizacao.Dividido && colunas[0].Width.IsAbsolute && colunas[0].Width.Value > 0)
            _larguraDaTabela = colunas[0].Width;

        var modo = _viewModel.ModoVisualizacao;
        (colunas[0].Width, colunas[1].Width, colunas[2].Width) = modo switch
        {
            ModoVisualizacao.SomenteTabela => (GridLength.Star, new GridLength(0), new GridLength(0)),
            ModoVisualizacao.SomenteGantt => (new GridLength(0), new GridLength(0), GridLength.Star),
            _ => (_larguraDaTabela, new GridLength(6), GridLength.Star),
        };
        Divisor.IsVisible = modo == ModoVisualizacao.Dividido;
        LimitarLarguraDaTabela();
        PainelGantt.IsVisible = _viewModel.MostrarGantt;
        Grade.IsHitTestVisible = LinhasVazias.IsVisible = _viewModel.MostrarTabela;
        Grade.Opacity = _viewModel.MostrarTabela ? 1 : 0;
        _modoAplicado = modo;
    }

    /// <summary>A menor largura do Gantt no modo Dividido, numa janela estreita (Bloco 25, B3).</summary>
    public const double LarguraMinimaDoGantt = 340;

    /// <summary>A grid não fica mais estreita que isto só para dar lugar ao Gantt.</summary>
    public const double LarguraMinimaDaTabela = 260;

    // Bloco 25 (B3): numa janela estreita, a grid (620 px, ou o que o usuário arrastou) não espreme o Gantt até sumir: a
    // largura dela fica limitada (MaxWidth) para sobrar LarguraMinimaDoGantt. A largura escolhida não muda; quando a
    // janela volta a ter espaço, ela volta inteira.
    private void LimitarLarguraDaTabela()
    {
        var colunas = Paineis.ColumnDefinitions;
        if (_viewModel?.ModoVisualizacao != ModoVisualizacao.Dividido || Paineis.Bounds.Width <= 0)
        {
            colunas[0].MaxWidth = double.PositiveInfinity;
            return;
        }
        static double Largura(ColumnDefinition coluna) => coluna.Width.IsAbsolute ? coluna.Width.Value : coluna.ActualWidth;
        var disponivel = Paineis.Bounds.Width - Largura(colunas[1]) - Largura(colunas[3]) - Largura(colunas[4]);
        colunas[0].MaxWidth = Math.Max(LarguraMinimaDaTabela, disponivel - LarguraMinimaDoGantt);
    }

    // Recalcula as linhas vazias da grid a partir do que o DataGrid mostra de fato: a área das linhas (sem
    // cabeçalho nem barra de rolagem), quantas linhas há e onde terminam as colunas visíveis (pelos cabeçalhos,
    // que já refletem ordem, largura e rolagem horizontal).
    private void AtualizarLinhasVazias()
    {
        var linhas = Grade.GetVisualDescendants().OfType<DataGridRowsPresenter>().FirstOrDefault();
        if (linhas is null || _viewModel is null || linhas.TranslatePoint(default, LinhasVazias) is not { } origem)
            return;
        var area = new Rect(origem, linhas.Bounds.Size);
        var bordas = Grade.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .Where(h => h.IsVisible && h.Bounds.Width > 0 && h.DataContext is not null && h.TranslatePoint(default, LinhasVazias) is not null)
            .Select(h => h.TranslatePoint(new Point(h.Bounds.Width, 0), LinhasVazias)!.Value.X)
            .Where(x => x > area.Left && x <= area.Right)
            .OrderBy(x => x)
            .ToList();
        var cor = Grade.VerticalGridLinesBrush ?? Grade.HorizontalGridLinesBrush ?? Brushes.LightGray;
        LinhasVazias.Atualizar(area, _viewModel.LinhasVisiveis.Count, GanttViewModel.AlturaLinha, bordas, cor);
    }

    // Exibir > Colunas: uma caixa por coluna (a do Nome fica marcada e desabilitada, porque não pode
    // sumir) e, depois de uma linha, "Restaurar layout padrão de colunas". Marcar uma coluna não fecha o
    // menu, para dar para escolher várias de uma vez, como no painel "Colunas ▾" da barra antiga. Bloco 36: as
    // colunas que só servem a quem usa linha de base e prazos, ou valores e medição, ficam em dois submenus (a lista
    // inteira passava da altura da tela).
    internal static readonly IReadOnlyList<(string Titulo, string[] Colunas)> GruposDeColunas =
    [
        ("Linha de base e prazos", ["NaoIniciarAntesDe", "Prazo", "InicioLinhaDeBase", "TerminoLinhaDeBase", "DesvioDoInicio", "DesvioDoTermino"]),
        ("Valores e medição", ["Valor", "Unidade", "Quantidade", "PrecoUnitario"]),
        ("Recursos e custos", ["Recursos", "CustoOrcado", "Margem"]),
    ];

    // ---- Bloco 39: a barra de ícones ----

    /// <summary>Exibir > Barra de ícones: uma caixa por botão, por grupo, e "Mostrar todos".</summary>
    private void MontarMenuDaBarra(BarraDeIconesViewModel barra)
    {
        MenuBarra.Items.Clear();
        foreach (var grupo in barra.Botoes.GroupBy(b => b.Grupo))
        {
            var submenu = new MenuItem { Header = BarraDeIconesViewModel.NomeDoGrupo(grupo.Key) };
            foreach (var botao in grupo)
            {
                var item = new MenuItem { Header = botao.Nome, ToggleType = MenuItemToggleType.CheckBox, StaysOpenOnClick = true, DataContext = botao };
                item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(BotaoDaBarra.Visivel)) { Mode = BindingMode.TwoWay });
                submenu.Items.Add(item);
            }
            MenuBarra.Items.Add(submenu);
        }
        MenuBarra.Items.Add(new Separator());
        MenuBarra.Items.Add(new MenuItem { Header = "Mostrar todos os botões", Command = barra.RestaurarCommand });
    }

    private const double LarguraDoBotaoDaBarra = 28, LarguraDoDivisor = 9, LarguraDoMais = 34;

    // A largura de cada botão, medida quando aparece (escondido, o Avalonia não mede).
    private readonly Dictionary<Button, double> _larguraDosBotoes = [];
    private double _larguraDaBarra = -1;

    /// <summary>Os botões da barra, na ordem, com o id do catálogo (o Tag).</summary>
    internal IEnumerable<Button> BotoesDaBarra => AcessoRapido.Children.OfType<Button>().Where(b => b.Tag is string);

    // O menu do "»" (um flyout não entra no escopo de nomes da janela).
    internal MenuFlyout MenuMais => (MenuFlyout)BotaoMais.Flyout!;

    /// <summary>Os que foram para o "»" por falta de largura.</summary>
    internal IReadOnlyList<Button> BotoesNoMais { get; private set; } = [];

    private void BarraMudouDeTamanho(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty && sender is Control c && Math.Abs(c.Bounds.Width - _larguraDaBarra) > 0.5)
            AjustarBarra();
    }

    /// <summary>
    /// Mostra os botões escolhidos em Exibir > Barra de ícones; os que não cabem na largura vão, na ordem, para o "»". Os
    /// divisores aparecem só entre dois grupos com botões à vista.
    /// </summary>
    internal void AjustarBarra()
    {
        if (_viewModel is null || AcessoRapido.Parent is not Control area)
            return;
        if (_larguraDaBarra < 0)
            area.PropertyChanged += BarraMudouDeTamanho;
        _larguraDaBarra = area.Bounds.Width;
        foreach (var b in BotoesDaBarra.Where(b => b.IsVisible && b.DesiredSize.Width > 0))
            _larguraDosBotoes[b] = b.DesiredSize.Width;
        double Largura(Button b) => _larguraDosBotoes.GetValueOrDefault(b, LarguraDoBotaoDaBarra) + AcessoRapido.Spacing;

        var escolhidos = BotoesDaBarra.Where(b => _viewModel.Barra.Botao((string)b.Tag!).Visivel).ToList();
        var disponivel = _larguraDaBarra <= 0 ? double.MaxValue : _larguraDaBarra;
        var total = escolhidos.Sum(Largura) + 8 * LarguraDoDivisor;
        var cabem = new HashSet<Button>(escolhidos);
        if (total > disponivel)
        {
            cabem.Clear();
            var usado = LarguraDoMais;
            GrupoDaBarra? grupoAnterior = null;
            foreach (var b in escolhidos)
            {
                var grupo = _viewModel.Barra.Botao((string)b.Tag!).Grupo;
                var largura = Largura(b) + (grupoAnterior is { } g && g != grupo ? LarguraDoDivisor : 0);
                if (usado + largura > disponivel)
                    break;
                usado += largura;
                cabem.Add(b);
                grupoAnterior = grupo;
            }
        }
        foreach (var b in BotoesDaBarra)
            b.IsVisible = cabem.Contains(b);

        // Divisores: só entre botões à vista.
        Border? pendente = null;
        var haBotaoAntes = false;
        foreach (var filho in AcessoRapido.Children)
            if (filho is Border divisor)
            {
                divisor.IsVisible = false;
                if (haBotaoAntes)
                    (pendente, haBotaoAntes) = (divisor, false);
            }
            else if (filho.IsVisible)
            {
                if (pendente is not null)
                    (pendente.IsVisible, pendente) = (true, null);
                haBotaoAntes = true;
            }

        // O "»": os que não couberam, separados por grupo.
        BotoesNoMais = [.. escolhidos.Where(b => !cabem.Contains(b))];
        MenuMais.Items.Clear();
        GrupoDaBarra? ultimo = null;
        foreach (var b in BotoesNoMais)
        {
            var grupo = _viewModel.Barra.Botao((string)b.Tag!).Grupo;
            if (ultimo is { } u && u != grupo)
                MenuMais.Items.Add(new Separator());
            ultimo = grupo;
            MenuMais.Items.Add(new MenuItem
            {
                Header = Avalonia.Automation.AutomationProperties.GetName(b),
                Command = b.Command,
                CommandParameter = b.CommandParameter,
            });
        }
        BotaoMais.IsVisible = BotoesNoMais.Count > 0;
    }

    private void MontarMenuDeColunas(ColunasViewModel colunas)
    {
        static MenuItem Caixa(ColunaViewModel coluna)
        {
            var item = new MenuItem
            {
                Header = coluna.Titulo,
                ToggleType = MenuItemToggleType.CheckBox,
                StaysOpenOnClick = true,
                IsEnabled = coluna.PodeOcultar,
                DataContext = coluna,
            };
            item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(ColunaViewModel.Visivel)) { Mode = BindingMode.TwoWay });
            return item;
        }

        MenuColunas.Items.Clear();
        var agrupadas = GruposDeColunas.SelectMany(g => g.Colunas).ToHashSet();
        foreach (var coluna in colunas.Colunas.Where(c => !agrupadas.Contains(c.Id)))
            MenuColunas.Items.Add(Caixa(coluna));
        MenuColunas.Items.Add(new Separator());
        foreach (var (titulo, ids) in GruposDeColunas)
        {
            var grupo = new MenuItem { Header = titulo };
            foreach (var id in ids)
                grupo.Items.Add(Caixa(colunas[id]));
            MenuColunas.Items.Add(grupo);
        }
        MenuColunas.Items.Add(new Separator());
        MenuColunas.Items.Add(new MenuItem
        {
            Header = "Restaurar layout padrão de colunas",
            Command = colunas.RestaurarPadraoCommand,
        });
    }

    // As colunas do DataGrid seguem o ColunasViewModel (visibilidade, ordem e largura), e o que o usuário
    // muda arrastando cabeçalhos volta para ele. Cada coluna é identificada pelo Tag, o id do ColunasViewModel.
    private void LigarColunas(ColunasViewModel colunas)
    {
        _colunas = colunas;
        AplicarColunas();
        foreach (var coluna in colunas.Colunas)
            coluna.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ColunaViewModel.Visivel) && ColunaDaGrade(coluna.Id) is { } daGrade)
                    daGrade.IsVisible = coluna.Visivel;
            };
        colunas.LayoutRestaurado += (_, _) => AplicarColunas();
        Grade.ColumnReordered += (_, _) => RegistrarColunas();
        foreach (var daGrade in Grade.Columns)
            daGrade.PropertyChanged += (_, e) =>
            {
                if (e.Property == DataGridColumn.WidthProperty && !_aplicandoColunas)
                    _larguraParou.Start();
            };
    }

    private void AplicarColunas()
    {
        if (_colunas is null)
            return;
        _aplicandoColunas = true;
        try
        {
            // DisplayIndex em ordem crescente: cada coluna vai para o seu lugar sem desarrumar as anteriores.
            foreach (var coluna in _colunas.Colunas.OrderBy(c => c.Posicao))
            {
                if (ColunaDaGrade(coluna.Id) is not { } daGrade)
                    continue;
                daGrade.IsVisible = coluna.Visivel;
                daGrade.Width = new DataGridLength(coluna.Largura);
                daGrade.DisplayIndex = coluna.Posicao;
            }
        }
        finally
        {
            _aplicandoColunas = false;
        }
    }

    private void RegistrarColunas() => _colunas?.RegistrarLayoutDaGrade(Grade.Columns
        .Where(c => c.Tag is string)
        .Select(c => ((string)c.Tag!, c.DisplayIndex, c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth)));

    private DataGridColumn? ColunaDaGrade(string id) => Grade.Columns.FirstOrDefault(c => Equals(c.Tag, id));

    protected override void OnClosed(EventArgs e)
    {
        _larguraParou.Stop();
        RegistrarColunas();
        base.OnClosed(e);
    }

    // O DataGrid não expõe a posição da rolagem vertical; ela fica na barra interna PART_VerticalScrollbar
    // do template (em pixels, já que as linhas têm altura fixa). O Gantt acompanha essa barra, e a roda
    // do mouse sobre o Gantt é reenviada ao DataGrid, que rola com a lógica dele.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is MainWindowViewModel vm)
            LigarColunas(vm.Colunas);
        _rolagemDaGrade = Grade.GetVisualDescendants().OfType<ScrollBar>()
            .FirstOrDefault(barra => barra.Name == "PART_VerticalScrollbar");
        if (_rolagemDaGrade is null)
            return;

        _rolagemDaGrade.PropertyChanged += (_, args) =>
        {
            if (args.Property == RangeBase.ValueProperty)
                Gantt.RolarVerticalmentePara(_rolagemDaGrade.Value);
        };
        Gantt.RodaDoMouseVertical += (roda, vertical) => Grade.RaiseEvent(new PointerWheelEventArgs(
            Grade, roda.Pointer, this, roda.GetPosition(this), roda.Timestamp,
            roda.GetCurrentPoint(this).Properties, KeyModifiers.None, new Avalonia.Vector(0, vertical))
        {
            RoutedEvent = PointerWheelChangedEvent,
        });
    }

    // Bloco 36: num resumo, a unidade e a quantidade não existem (são das tarefas dentro dele).
    private static readonly HashSet<string> SemUnidadeNoResumo = ["Unidade", "Quantidade"];

    private void GradeBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        // Bloco 32: a célula Recursos não edita: abre Tarefa > Recursos da tarefa….
        if (e.Column.Tag is "Recursos" && e.Row.DataContext is TarefaViewModel linha && DataContext is MainWindowViewModel principal)
        {
            e.Cancel = true;
            principal.TarefaSelecionada = linha;
            principal.RecursosDaTarefaCommand.Execute(null);
            return;
        }
        if (e.Row.DataContext is not TarefaViewModel { EhResumo: true } || e.Column.Tag is not string coluna || DataContext is not MainWindowViewModel vm)
            return;
        if (CalculadasNoResumo.Contains(coluna))
        {
            e.Cancel = true;
            vm.AvisarValorCalculadoNoResumo();
        }
        else if (SemUnidadeNoResumo.Contains(coluna))
        {
            e.Cancel = true;
            vm.AvisarSemUnidadeNoResumo();
        }
    }

    // Bloco 25 (P1): os atalhos aparecem nos menus (o texto do gesto) e nas dicas dos botões da barra.
    private void MostrarAtalhos(bool mac)
    {
        foreach (var (item, acao) in new (MenuItem, AcaoComAtalho)[]
                 {
                     (ItemNovo, AcaoComAtalho.Novo), (ItemAbrir, AcaoComAtalho.Abrir), (ItemSalvar, AcaoComAtalho.Salvar),
                     (ItemSalvarComo, AcaoComAtalho.SalvarComo), (ItemRecalcular, AcaoComAtalho.Recalcular),
                     (ItemFotos, AcaoComAtalho.FotosDaTarefa), (ItemDividido, AcaoComAtalho.Dividido),
                     (ItemSomenteTabela, AcaoComAtalho.SomenteTabela), (ItemSomenteGantt, AcaoComAtalho.SomenteGantt),
                     (ItemAssistente, AcaoComAtalho.Assistente), (ItemIrParaTarefa, AcaoComAtalho.IrParaTarefa),
                     (ItemDesfazer, AcaoComAtalho.Desfazer), (ItemRefazer, AcaoComAtalho.Refazer),
                     (ItemCopiar, AcaoComAtalho.Copiar), (ItemColar, AcaoComAtalho.Colar), (ItemDuplicar, AcaoComAtalho.Duplicar),
                     (ItemImprimirGantt, AcaoComAtalho.ImprimirGantt),
                 })
            item.InputGesture = AtalhosDeTeclado.Gesto(acao, mac);

        foreach (var (nome, acao) in new (string, AcaoComAtalho)[]
                 {
                     ("Novo projeto", AcaoComAtalho.Novo), ("Abrir projeto", AcaoComAtalho.Abrir), ("Ir para a tarefa", AcaoComAtalho.IrParaTarefa),
                     ("Salvar", AcaoComAtalho.Salvar), ("Recalcular cronograma", AcaoComAtalho.Recalcular),
                     ("Dividido", AcaoComAtalho.Dividido), ("Somente Tabela", AcaoComAtalho.SomenteTabela),
                     ("Somente Gantt", AcaoComAtalho.SomenteGantt), ("Assistente", AcaoComAtalho.Assistente),
                     ("Imprimir o Gantt", AcaoComAtalho.ImprimirGantt),
                 })
        {
            var botao = AcessoRapido.Children.OfType<Button>()
                .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == nome);
            ToolTip.SetTip(botao, AtalhosDeTeclado.Dica((string)ToolTip.GetTip(botao)!, acao, mac));
        }
        // Bloco 30: "Desfazer Duração de "Escavação" (⌘Z)".
        foreach (var (botao, propriedade, acao) in new (Button, string, AcaoComAtalho)[]
                 {
                     (BotaoDesfazer, nameof(MainWindowViewModel.TextoDesfazer), AcaoComAtalho.Desfazer),
                     (BotaoRefazer, nameof(MainWindowViewModel.TextoRefazer), AcaoComAtalho.Refazer),
                 })
            botao.Bind(ToolTip.TipProperty, new Binding(propriedade)
            {
                StringFormat = "{0} (" + AtalhosDeTeclado.Texto(acao, mac).Replace("{", "{{").Replace("}", "}}") + ")",
            });
        BotaoFotos.Bind(ToolTip.TipProperty, new Binding(nameof(MainWindowViewModel.DicaFotosDaSelecionada))
        {
            StringFormat = "{0} (" + AtalhosDeTeclado.Texto(AcaoComAtalho.FotosDaTarefa, mac).Replace("{", "{{").Replace("}", "}}") + ")",
        });
    }

    // Bloco 25 (P1): os atalhos de teclado valem na janela toda, inclusive editando uma célula, que é confirmada antes
    // (salvar com a célula ainda aberta deixaria de fora o que acabou de ser digitado).
    private void AtalhosDoTeclado(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || AtalhosDeTeclado.Achar(e.Key, e.KeyModifiers, AtalhosDeTeclado.EhMac) is not { } acao
            || (AtalhosDeTeclado.De(acao).ForaDoTexto && e.Source is TextBox))
            return;
        e.Handled = true;
        Grade.CommitEdit();
        var (comando, parametro) = acao switch
        {
            AcaoComAtalho.Novo => (vm.NovoProjetoCommand, null),
            AcaoComAtalho.Abrir => (vm.AbrirProjetoCommand, null),
            AcaoComAtalho.Salvar => (vm.SalvarProjetoCommand, null),
            AcaoComAtalho.SalvarComo => (vm.SalvarComoProjetoCommand, null),
            AcaoComAtalho.Recalcular => (vm.RecalcularCronogramaCommand, null),
            AcaoComAtalho.FotosDaTarefa => (vm.GerenciarFotosCommand, null),
            AcaoComAtalho.Dividido => (vm.DefinirModoVisualizacaoCommand, ModoVisualizacao.Dividido),
            AcaoComAtalho.SomenteTabela => (vm.DefinirModoVisualizacaoCommand, ModoVisualizacao.SomenteTabela),
            AcaoComAtalho.SomenteGantt => (vm.DefinirModoVisualizacaoCommand, ModoVisualizacao.SomenteGantt),
            AcaoComAtalho.Assistente => ((System.Windows.Input.ICommand)vm.Assistente.AlternarVisibilidadeCommand, (object?)null),
            AcaoComAtalho.IrParaTarefa => (vm.IrParaTarefaCommand, null),
            AcaoComAtalho.Desfazer => (vm.DesfazerAlteracaoCommand, null),
            AcaoComAtalho.Refazer => (vm.RefazerAlteracaoCommand, null),
            AcaoComAtalho.Copiar => (vm.CopiarTarefaCommand, null),
            AcaoComAtalho.Colar => (vm.ColarTarefaCommand, null),
            AcaoComAtalho.Duplicar => (vm.DuplicarTarefaCommand, null),
            AcaoComAtalho.ImprimirGantt => (vm.ImprimirGanttCommand, null),
            _ => ((System.Windows.Input.ICommand?)null, (object?)null),
        };
        if (comando?.CanExecute(parametro) == true)
            comando.Execute(parametro);
    }

    // Alt+Shift+→ e Alt+Shift+← (Option no Mac) rebaixam e promovem a tarefa selecionada, como no MS
    // Project. Durante a edição de um texto, a combinação continua sendo do texto (seleção por palavra).
    private void AtalhosDaEstrutura(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != (KeyModifiers.Alt | KeyModifiers.Shift) || e.Key is not (Key.Left or Key.Right)
            || e.Source is TextBox || DataContext is not MainWindowViewModel vm)
            return;

        var comando = e.Key == Key.Right ? vm.RebaixarCommand : vm.PromoverCommand;
        if (comando.CanExecute(null))
            comando.Execute(null);
        e.Handled = true;
    }

    // O primeiro Esc cancela só a célula (o DataGrid relê o valor, que ainda é o recusado); o segundo
    // cancelaria a linha e chamaria IEditableObject.CancelEdit. Aqui o primeiro já basta.
    private void GradeCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel && e.Row.DataContext is IEditableObject linha)
            linha.CancelEdit();
    }

    // Com alterações não salvas, segura o fechamento, pergunta o que fazer e só então fecha.
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_fechamentoConfirmado || DataContext is not MainWindowViewModel { PossuiAlteracoesNaoSalvas: true } vm)
            return;

        e.Cancel = true;
        if (await vm.PodeDescartarAlteracoesAsync())
        {
            _fechamentoConfirmado = true;
            Close();
        }
    }
}
