using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

/// <summary>Galeria das fotos de uma tarefa, só para ver (Bloco 14).</summary>
public partial class GaleriaFotosWindow : Window
{
    // Exigido pelo carregador de XAML e pelo designer.
    public GaleriaFotosWindow()
    {
        InitializeComponent();
    }

    public GaleriaFotosWindow(GaleriaFotosViewModel galeria) : this()
    {
        DataContext = galeria;
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
