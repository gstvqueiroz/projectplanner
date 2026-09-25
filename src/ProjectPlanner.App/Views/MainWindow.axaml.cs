using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    // Arrastar a borda de um cabeçalho muda a largura muitas vezes por segundo: grava só quando para.
    private readonly DispatcherTimer _larguraParou = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MainWindow()
    {
        InitializeComponent();
        _larguraParou.Tick += (_, _) =>
        {
            _larguraParou.Stop();
            RegistrarColunas();
        };
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
