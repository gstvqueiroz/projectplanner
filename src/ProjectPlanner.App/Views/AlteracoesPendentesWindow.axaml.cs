using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Views;

/// <summary>Pergunta o que fazer com as alterações não salvas: Salvar, Não salvar ou Cancelar.</summary>
public partial class AlteracoesPendentesWindow : Window
{
    // Exigido pelo carregador de XAML e pelo designer.
    public AlteracoesPendentesWindow()
    {
        InitializeComponent();
    }

    public AlteracoesPendentesWindow(string nomeProjeto) : this()
    {
        Pergunta.Text = $"Deseja salvar as alterações em \"{nomeProjeto}\"? Se não salvar, elas serão perdidas.";
    }

    private void Salvar(object? sender, RoutedEventArgs e) => Close(RespostaAlteracoesPendentes.Salvar);

    private void NaoSalvar(object? sender, RoutedEventArgs e) => Close(RespostaAlteracoesPendentes.NaoSalvar);

    private void Cancelar(object? sender, RoutedEventArgs e) => Close(RespostaAlteracoesPendentes.Cancelar);
}
