using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class GanttImpressoWindow : Window
{
    public GanttImpressoWindow() => InitializeComponent();

    public GanttImpressoWindow(GanttImpressoViewModel impressao) : this()
    {
        DataContext = impressao;
        impressao.PropertyChanged += Mudou;
        Closed += (_, _) => impressao.PropertyChanged -= Mudou;
        Mostrar(impressao.Previa);
    }

    private void Mudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GanttImpressoViewModel.Previa))
            Mostrar(((GanttImpressoViewModel)sender!).Previa);
    }

    // A prévia vem em PNG do gerador do PDF (o mesmo desenho).
    private void Mostrar(byte[]? png)
    {
        var antiga = Previa.Source as Bitmap;
        Previa.Source = png is null ? null : new Bitmap(new MemoryStream(png));
        antiga?.Dispose();
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
