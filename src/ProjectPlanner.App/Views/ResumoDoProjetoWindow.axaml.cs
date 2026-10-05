using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class ResumoDoProjetoWindow : Window
{
    public ResumoDoProjetoWindow() => InitializeComponent();

    public ResumoDoProjetoWindow(ResumoDoProjetoViewModel resumo) : this() => DataContext = resumo;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
