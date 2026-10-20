using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class ConfiguracaoAssistenteWindow : Window
{
    public ConfiguracaoAssistenteWindow()
    {
        InitializeComponent();
        // Fechar no meio de um teste de conexão cancela o teste.
        Closing += (_, _) => (DataContext as ConfiguracaoAssistenteViewModel)?.Fechando();
        // Bloco 46: os níveis da conta vêm do servidor ao abrir.
        Opened += async (_, _) =>
        {
            if (DataContext is ConfiguracaoAssistenteViewModel configuracao)
                await configuracao.CarregarAsync();
        };
    }

    public ConfiguracaoAssistenteWindow(ConfiguracaoAssistenteViewModel configuracao) : this() => DataContext = configuracao;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
