using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class OrcamentoWindow : Window
{
    public OrcamentoWindow() => InitializeComponent();

    public OrcamentoWindow(OrcamentoViewModel orcamento) : this()
    {
        DataContext = orcamento;
        Closed += (_, _) =>
        {
            orcamento.Fechando(); // a consulta à IA em andamento é cancelada
            orcamento.CancelarDownload(); // Bloco 40: e o download da SINAPI
        };
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
