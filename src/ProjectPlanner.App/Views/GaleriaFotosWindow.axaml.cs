using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class GaleriaFotosWindow : Window
{
    public GaleriaFotosWindow()
    {
        InitializeComponent();
        // Soltar fotos do Finder/Explorer em qualquer lugar da janela.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, ArrastandoPorCima);
        AddHandler(DragDrop.DropEvent, Soltou);
        AddHandler(KeyDownEvent, TeclaApertada, RoutingStrategies.Tunnel);
    }

    public GaleriaFotosWindow(GaleriaFotosViewModel galeria) : this() => DataContext = galeria;

    private GaleriaFotosViewModel? Galeria => DataContext as GaleriaFotosViewModel;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();

    // Clique seleciona só a foto; com Cmd (Mac) ou Ctrl (Windows), põe ou tira da seleção.
    private void MiniaturaApertada(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: FotoNaGaleria item } && Galeria is { } galeria
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            galeria.Selecionar(item, e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
    }

    private void MiniaturaDuploClique(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FotoNaGaleria item } && Galeria?.AmpliarCommand.CanExecute(item) == true)
            Galeria.AmpliarCommand.Execute(item);
    }

    // Delete exclui as selecionadas; Cmd/Ctrl+A seleciona todas (fora de um campo de texto).
    private void TeclaApertada(object? sender, KeyEventArgs e)
    {
        if (Galeria is not { } galeria || e.Source is TextBox)
            return;
        if (e.Key is Key.Delete or Key.Back && galeria.ExcluirCommand.CanExecute(null))
        {
            galeria.ExcluirCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            galeria.SelecionarTodasCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ArrastandoPorCima(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void Soltou(object? sender, DragEventArgs e)
    {
        var arquivos = e.DataTransfer.TryGetFiles();
        if (arquivos is null || Galeria is not { } galeria)
            return;
        await galeria.AdicionarArquivosAsync(arquivos.Select(a => a.TryGetLocalPath()).OfType<string>());
    }
}
