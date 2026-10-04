using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace ProjectPlanner.App.Views;

public partial class AvisoDeErroWindow : Window
{
    public AvisoDeErroWindow() => InitializeComponent();

    public AvisoDeErroWindow(string mensagem) : this() => Mensagem.Text = mensagem;

    /// <summary>Mostra o aviso por cima da janela principal (ou sozinho, se ela ainda não existe).</summary>
    public static void Mostrar(string mensagem)
    {
        var aviso = new AvisoDeErroWindow(mensagem);
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { IsVisible: true } principal)
            _ = aviso.ShowDialog(principal);
        else
            aviso.Show();
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
