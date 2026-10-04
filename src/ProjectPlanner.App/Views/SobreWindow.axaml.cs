using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class SobreWindow : Window
{
    public SobreWindow() => InitializeComponent();

    public SobreWindow(SobreViewModel sobre) : this() => DataContext = sobre;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
