using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class LinhasDeBaseWindow : Window
{
    public LinhasDeBaseWindow() => InitializeComponent();

    public LinhasDeBaseWindow(LinhasDeBaseViewModel linhas) : this() => DataContext = linhas;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
