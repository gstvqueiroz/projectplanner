using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class RecursosWindow : Window
{
    public RecursosWindow() => InitializeComponent();

    public RecursosWindow(RecursosViewModel vm) : this() => DataContext = vm;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
