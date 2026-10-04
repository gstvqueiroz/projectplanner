using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class VisualizadorFotosWindow : Window
{
    public VisualizadorFotosWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, TeclaApertada, RoutingStrategies.Tunnel);
    }

    public VisualizadorFotosWindow(VisualizadorDeFotosViewModel visualizador) : this() => DataContext = visualizador;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();

    private void TeclaApertada(object? sender, KeyEventArgs e)
    {
        if (DataContext is not VisualizadorDeFotosViewModel vm)
            return;
        switch (e.Key)
        {
            case Key.Left when vm.AnteriorCommand.CanExecute(null):
                vm.AnteriorCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when vm.ProximaCommand.CanExecute(null):
                vm.ProximaCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }
}
