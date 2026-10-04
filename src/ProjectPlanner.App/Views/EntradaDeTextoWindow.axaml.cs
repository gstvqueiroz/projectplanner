using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ProjectPlanner.App.Views;

public partial class EntradaDeTextoWindow : Window
{
    public EntradaDeTextoWindow() => InitializeComponent();

    public EntradaDeTextoWindow(string titulo, string pergunta, string? valorInicial) : this()
    {
        Title = titulo;
        Pergunta.Text = pergunta;
        Resposta.Text = valorInicial ?? "";
        Opened += (_, _) =>
        {
            Resposta.Focus();
            Resposta.SelectAll();
        };
    }

    private void Confirmado(object? sender, RoutedEventArgs e) => Close(Resposta.Text);

    private void Cancelar(object? sender, RoutedEventArgs e) => Close(null);
}
