using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ProjectPlanner.App.Views;

/// <summary>Pergunta se o usuário quer mesmo fazer uma ação que não dá para desfazer; devolve se confirmou.</summary>
public partial class ConfirmacaoWindow : Window
{
    // Exigido pelo carregador de XAML e pelo designer.
    public ConfirmacaoWindow()
    {
        InitializeComponent();
    }

    public ConfirmacaoWindow(string titulo, string mensagem, string textoDoBotao) : this()
    {
        Title = titulo;
        Mensagem.Text = mensagem;
        Confirmar.Content = textoDoBotao;
    }

    private void Confirmado(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancelar(object? sender, RoutedEventArgs e) => Close(false);
}
