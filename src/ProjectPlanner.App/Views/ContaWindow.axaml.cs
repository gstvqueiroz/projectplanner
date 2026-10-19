using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class ContaWindow : Window
{
    public ContaWindow()
    {
        InitializeComponent();
        // Fechar no meio de um pedido (ou do login no navegador) cancela.
        Closing += (_, _) => (DataContext as ContaViewModel)?.Fechando();
        Opened += async (_, _) =>
        {
            if (DataContext is ContaViewModel conta)
                await conta.AbrirAsync();
        };
    }

    public ContaWindow(ContaViewModel conta) : this() => DataContext = conta;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
