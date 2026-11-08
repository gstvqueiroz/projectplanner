using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

// Bloco 72: a seleção múltipla na grade (Ctrl+clique e Shift+clique, como no MS Project), a mesma do Gantt: a janela leva a
// seleção da grade para o MainWindowViewModel e a do programa (o Gantt, o Kanban, os comandos) para a grade. A tecla Delete
// (⌫ no Mac) exclui as selecionadas, e o aviso "Desfazer" fica alguns segundos.
public partial class MainWindow
{
    private bool _aplicandoNaGrade;
    private readonly DispatcherTimer _tempoDoAviso = new() { Interval = MainWindowViewModel.DuracaoDoAviso };

    private void LigarSelecao()
    {
        Grade.SelectionChanged += GradeSelecaoMudou;
        AddHandler(KeyDownEvent, TeclaDeExcluir, RoutingStrategies.Tunnel);
        _tempoDoAviso.Tick += (_, _) => FecharOAviso(null, null);
    }

    private void LigarSelecaoAoViewModel(MainWindowViewModel vm, bool ligar)
    {
        if (ligar)
            vm.SelecaoMudou += AplicarSelecaoNaGrade;
        else
            vm.SelecaoMudou -= AplicarSelecaoNaGrade;
    }

    private void GradeSelecaoMudou(object? sender, SelectionChangedEventArgs e)
    {
        if (_aplicandoNaGrade || _viewModel is null)
            return;
        _viewModel.DefinirSelecao(Grade.SelectedItems.OfType<TarefaViewModel>(), Grade.SelectedItem as TarefaViewModel, daGrade: true);
    }

    // A seleção do programa na grade (só as linhas que a grade mostra: uma tarefa dentro de um resumo recolhido não tem linha).
    private void AplicarSelecaoNaGrade()
    {
        if (_viewModel is null)
            return;
        var visiveis = _viewModel.LinhasVisiveis.ToHashSet();
        var desejadas = _viewModel.Selecionadas.Where(visiveis.Contains).ToList();
        var atuais = Grade.SelectedItems.OfType<TarefaViewModel>().ToList();
        var principal = _viewModel.TarefaSelecionada is { } p && visiveis.Contains(p) ? p : desejadas.LastOrDefault();
        if (atuais.Count == desejadas.Count && atuais.All(desejadas.Contains) && Equals(Grade.SelectedItem, principal))
            return;
        _aplicandoNaGrade = true;
        try
        {
            Grade.SelectedItem = principal;
            foreach (var linha in desejadas.Where(l => l != principal))
                Grade.SelectedItems.Add(linha);
        }
        finally
        {
            _aplicandoNaGrade = false;
        }
    }

    // E1: Delete (⌫ no Mac) exclui as selecionadas quando nenhuma célula está em edição e o foco está na grade, no Gantt ou no
    // Kanban (editando um texto, num filtro ou noutro painel, a tecla continua sendo do texto).
    private void TeclaDeExcluir(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || e.KeyModifiers != KeyModifiers.None || !(e.Key == Key.Delete || (AtalhosDeTeclado.EhMac && e.Key == Key.Back))
            || e.Source is TextBox || !NaAreaDasTarefas(e.Source as Avalonia.Visual))
            return;
        e.Handled = true;
        if (_viewModel.RemoverTarefaCommand.CanExecute(null))
            _viewModel.RemoverTarefaCommand.Execute(null);
    }

    private bool NaAreaDasTarefas(Avalonia.Visual? origem) =>
        origem is not null && origem.GetSelfAndVisualAncestors().Any(v => ReferenceEquals(v, Grade) || ReferenceEquals(v, Gantt) || ReferenceEquals(v, QuadroKanban));

    private void FecharOAviso(object? sender, RoutedEventArgs? e)
    {
        _tempoDoAviso.Stop();
        if (_viewModel is not null)
            _viewModel.AvisoComDesfazer = null;
    }

    // O aviso com o Desfazer some sozinho depois de alguns segundos (e na próxima alteração).
    private void AvisoMudou()
    {
        _tempoDoAviso.Stop();
        if (_viewModel?.AvisoComDesfazer is not null)
            _tempoDoAviso.Start();
    }
}
