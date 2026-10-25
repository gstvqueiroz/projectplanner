using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class OrdensDeServicoWindow : Window
{
    public OrdensDeServicoWindow() => InitializeComponent();

    public OrdensDeServicoWindow(OrdensDeServicoViewModel ordens) : this()
    {
        DataContext = ordens;
        Closing += (_, _) => ordens.AoFechar();
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
