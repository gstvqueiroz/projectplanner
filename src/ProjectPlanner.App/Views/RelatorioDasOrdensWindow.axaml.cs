using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class RelatorioDasOrdensWindow : Window
{
    public RelatorioDasOrdensWindow() => InitializeComponent();

    public RelatorioDasOrdensWindow(RelatorioDasOrdensViewModel relatorio) : this() => DataContext = relatorio;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
