using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class RecursosDaTarefaWindow : Window
{
    public RecursosDaTarefaWindow() => InitializeComponent();

    public RecursosDaTarefaWindow(RecursosDaTarefaViewModel vm) : this() => DataContext = vm;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
