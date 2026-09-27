using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    // Arrastar a borda de um cabeçalho muda a largura muitas vezes por segundo: grava só quando para.
    private readonly DispatcherTimer _larguraParou = new() { Interval = TimeSpan.FromMilliseconds(400) };

    // Num resumo, estas colunas vêm das subtarefas (ver AvisarValorCalculadoNoResumo).
    private static readonly HashSet<string> CalculadasNoResumo =
        ["Duracao", "Inicio", "Termino", "PercentualConcluido", "Predecessoras"];

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, AtalhosDaEstrutura, RoutingStrategies.Tunnel);
        // Tamanho da janela, colunas (largura, ordem, visíveis) e tarefas mudam o layout da grid.
        Grade.LayoutUpdated += (_, _) => AtualizarLinhasVazias();
        Grade.AddHandler(ContextRequestedEvent, PedirMenuDaGrade, RoutingStrategies.Tunnel);
        LigarArrasteDaGrade();
        _larguraParou.Tick += (_, _) =>
        {
            _larguraParou.Stop();
            RegistrarColunas();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelMudou;
        base.OnDataContextChanged(e);
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is null)
            return;
        MontarMenuDeColunas(_viewModel.Colunas);
        _viewModel.PropertyChanged += ViewModelMudou;
        AplicarModoVisualizacao();
    }

    private void ViewModelMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ModoVisualizacao))
            AplicarModoVisualizacao();
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
        PainelGantt.IsVisible = _viewModel.MostrarGantt;
        Grade.IsHitTestVisible = LinhasVazias.IsVisible = _viewModel.MostrarTabela;
        Grade.Opacity = _viewModel.MostrarTabela ? 1 : 0;
        _modoAplicado = modo;
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
    // menu, para dar para escolher várias de uma vez, como no painel "Colunas ▾" da barra antiga.
    private void MontarMenuDeColunas(ColunasViewModel colunas)
    {
        MenuColunas.Items.Clear();
        foreach (var coluna in colunas.Colunas)
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
            MenuColunas.Items.Add(item);
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

    private void GradeBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is TarefaViewModel { EhResumo: true } && e.Column.Tag is string coluna
            && CalculadasNoResumo.Contains(coluna) && DataContext is MainWindowViewModel vm)
        {
            e.Cancel = true;
            vm.AvisarValorCalculadoNoResumo();
        }
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
