using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class UsoDeRecursosWindow : Window
{
    public UsoDeRecursosWindow() => InitializeComponent();

    public UsoDeRecursosWindow(UsoDeRecursosViewModel uso) : this() => DataContext = uso;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
