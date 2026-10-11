using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class AssistenteView : UserControl
{
    private AssistenteViewModel? _viewModel;

    public AssistenteView()
    {
        InitializeComponent();
        // Enter envia; Shift+Enter quebra a linha (o TextBox aceita Enter).
        Entrada.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _viewModel is { } vm)
            {
                if (vm.EnviarCommand.CanExecute(null))
                    vm.EnviarCommand.Execute(null);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Mensagens.CollectionChanged -= MensagensMudaram;
            _viewModel.FocoNaEntradaPedido -= FocarNaEntrada;
        }
        base.OnDataContextChanged(e);
        _viewModel = DataContext as AssistenteViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Mensagens.CollectionChanged += MensagensMudaram;
            _viewModel.FocoNaEntradaPedido += FocarNaEntrada;
        }
    }

    // Bloco 26 (Ajustar o plano): o cursor vai para o fim do texto começado na caixa.
    private void FocarNaEntrada()
    {
        Entrada.Focus();
        Entrada.CaretIndex = Entrada.Text?.Length ?? 0;
    }

    // A conversa desce sozinha a cada mensagem nova.
    private void MensagensMudaram(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => Rolagem.ScrollToEnd(), DispatcherPriority.Background);
}
