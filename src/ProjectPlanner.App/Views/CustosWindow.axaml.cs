using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class CustosWindow : Window
{
    public CustosWindow() => InitializeComponent();

    public CustosWindow(CustosViewModel custos) : this() => DataContext = custos;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
