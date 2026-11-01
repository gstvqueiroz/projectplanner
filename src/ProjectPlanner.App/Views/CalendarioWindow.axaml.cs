using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class CalendarioWindow : Window
{
    public CalendarioWindow() => InitializeComponent();

    public CalendarioWindow(CalendarioViewModel calendario) : this()
    {
        DataContext = calendario;
        calendario.FecharPedido += Close;
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
