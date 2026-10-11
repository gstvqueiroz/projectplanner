using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class FotosDoProjetoWindow : Window
{
    public FotosDoProjetoWindow()
    {
        InitializeComponent();
        // Fotos soltas em qualquer lugar da janela vão para a importação em lote.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, ArrastandoPorCima);
        AddHandler(DragDrop.DropEvent, Soltou);
    }

    public FotosDoProjetoWindow(FotosDoProjetoViewModel fotos) : this() => DataContext = fotos;

    private FotosDoProjetoViewModel? Fotos => DataContext as FotosDoProjetoViewModel;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();

    private void MiniaturaApertada(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: FotoNoProjeto item } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Fotos?.Selecionar(item);
    }

    private void MiniaturaDuploClique(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FotoNoProjeto item } && Fotos?.AmpliarCommand.CanExecute(item) == true)
            Fotos.AmpliarCommand.Execute(item);
    }

    private void ArrastandoPorCima(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void Soltou(object? sender, DragEventArgs e)
    {
        var arquivos = e.DataTransfer.TryGetFiles();
        if (arquivos is null || Fotos is not { } fotos)
            return;
        await fotos.LerLoteAsync(arquivos.Select(a => a.TryGetLocalPath()).OfType<string>());
    }
}
