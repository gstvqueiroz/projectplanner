using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

/// <summary>Bloco 88: a janela do relatório personalizado (ver <see cref="RelatorioPersonalizadoViewModel"/>).</summary>
public partial class RelatorioPersonalizadoWindow : Window
{
    public RelatorioPersonalizadoWindow() => InitializeComponent();

    public RelatorioPersonalizadoWindow(RelatorioPersonalizadoViewModel relatorio) : this()
    {
        DataContext = relatorio;
        Closed += (_, _) => relatorio.Dispose();
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();

    private void AbrirMenuDeSecoes(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RelatorioPersonalizadoViewModel vm && sender is Control botao)
            MenuDeSecoes(vm).Open(botao);
    }

    /// <summary>O menu de "+ Adicionar seção": um item por tipo (cada um entra no fim; a capa, no começo).</summary>
    internal static ContextMenu MenuDeSecoes(RelatorioPersonalizadoViewModel vm)
    {
        var menu = new ContextMenu();
        foreach (var tipo in RelatorioPersonalizadoViewModel.TiposDeSecao)
            menu.Items.Add(new MenuItem { Header = tipo.Nome, Command = vm.AdicionarSecaoCommand, CommandParameter = tipo.Valor });
        return menu;
    }
}
