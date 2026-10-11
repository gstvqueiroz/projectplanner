using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class FisicoFinanceiroWindow : Window
{
    public FisicoFinanceiroWindow() => InitializeComponent();

    public FisicoFinanceiroWindow(FisicoFinanceiroViewModel fisicoFinanceiro) : this() => DataContext = fisicoFinanceiro;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
