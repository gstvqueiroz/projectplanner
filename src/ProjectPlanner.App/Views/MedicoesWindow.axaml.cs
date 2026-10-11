using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class MedicoesWindow : Window
{
    public MedicoesWindow() => InitializeComponent();

    public MedicoesWindow(MedicoesViewModel medicoes) : this() => DataContext = medicoes;

    // Só o medido de um item do rascunho muda (os grupos e as medições aprovadas não).
    private void ItensBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is not ItemDaMedicaoNaTela { PodeMedir: true })
            e.Cancel = true;
    }

    // Num item medido em % já medido, o aditivo só muda o valor.
    private void AditivoBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is ItemDoAditivoNaTela { SoValor: true } && e.Column.Tag is "Quantidade")
        {
            e.Cancel = true;
            if (DataContext is MedicoesViewModel medicoes)
                medicoes.AvisarSoValor();
        }
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
