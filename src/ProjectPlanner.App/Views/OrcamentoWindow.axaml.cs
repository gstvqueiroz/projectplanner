using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class OrcamentoWindow : Window
{
    public OrcamentoWindow() => InitializeComponent();

    public OrcamentoWindow(OrcamentoViewModel orcamento) : this() => DataContext = orcamento;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
