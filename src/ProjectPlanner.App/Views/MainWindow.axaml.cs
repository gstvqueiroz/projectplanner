using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class MainWindow : Window
{
    private bool _fechamentoConfirmado;
    private ScrollBar? _rolagemDaGrade;

    public MainWindow()
    {
        InitializeComponent();
    }

    // O DataGrid não expõe a posição da rolagem vertical; ela fica na barra interna PART_VerticalScrollbar
    // do template (em pixels, já que as linhas têm altura fixa). O Gantt acompanha essa barra, e a roda
    // do mouse sobre o Gantt é reenviada ao DataGrid, que rola com a lógica dele.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
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
        if (_fechamentoConfirmado || DataContext is not MainWindowViewModel { TemAlteracoesNaoSalvas: true } vm)
            return;

        e.Cancel = true;
        if (await vm.PodeDescartarAlteracoesAsync())
        {
            _fechamentoConfirmado = true;
            Close();
        }
    }
}
