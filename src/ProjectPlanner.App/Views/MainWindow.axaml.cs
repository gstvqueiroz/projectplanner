using System.ComponentModel;
using Avalonia.Controls;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class MainWindow : Window
{
    private bool _fechamentoConfirmado;

    public MainWindow()
    {
        InitializeComponent();
    }

    // O primeiro Esc cancela só a célula (o DataGrid relê o valor, que ainda é o recusado); o segundo
    // cancelaria a linha e chamaria IEditableObject.CancelEdit. Aqui o primeiro já basta.
    private void GradeCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel && e.Row.DataContext is IEditableObject linha)
            linha.CancelEdit();
    }

    // Com alterações não salvas, segura o fechamento, pergunta o que fazer e só então fecha.
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_fechamentoConfirmado || DataContext is not MainWindowViewModel { TemAlteracoesNaoSalvas: true } vm)
            return;

        e.Cancel = true;
        if (await vm.PodeDescartarAlteracoesAsync())
        {
            _fechamentoConfirmado = true;
            Close();
        }
    }
}
