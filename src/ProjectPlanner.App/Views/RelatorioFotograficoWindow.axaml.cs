using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class RelatorioFotograficoWindow : Window
{
    public RelatorioFotograficoWindow() => InitializeComponent();

    public RelatorioFotograficoWindow(RelatorioFotograficoViewModel relatorio) : this() => DataContext = relatorio;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
