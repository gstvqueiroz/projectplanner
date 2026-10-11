using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class IndicadoresWindow : Window
{
    public IndicadoresWindow() => InitializeComponent();

    public IndicadoresWindow(IndicadoresViewModel indicadores) : this() => DataContext = indicadores;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
