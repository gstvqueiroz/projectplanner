using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

// Bloco 81: o Diário de Obra (RDO). Ao fechar a tela, o que está no formulário é salvo; com um número que não vale, a tela
// pergunta se fecha sem salvar as mudanças do dia.
public partial class DiarioDeObraWindow : Window
{
    private bool _podeFechar;

    public DiarioDeObraWindow() => InitializeComponent();

    public DiarioDeObraWindow(DiarioDeObraViewModel diario) : this()
    {
        DataContext = diario;
        Closing += async (_, e) =>
        {
            if (_podeFechar || diario.SalvarSePreciso())
                return;
            e.Cancel = true;
            if (await diario.FecharSemSalvarAsync())
            {
                _podeFechar = true;
                Dispatcher.UIThread.Post(Close);
            }
        };
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
