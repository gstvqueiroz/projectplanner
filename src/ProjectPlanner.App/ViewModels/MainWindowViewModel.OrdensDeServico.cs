using CommunityToolkit.Mvvm.Input;

namespace ProjectPlanner.App.ViewModels;

// Bloco 58: Projeto > Ordens de serviço….
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task OrdensDeServico()
    {
        Recalcular(); // as datas da OS nova vêm do cronograma calculado
        return _dialogos.MostrarOrdensDeServicoAsync(new OrdensDeServicoViewModel(_cronograma, _dialogos, _relogio, OrdensMudaram,
            TarefaSelecionada?.Tarefa.Id));
    }

    // As OS entram no Desfazer; quando mexem nas tarefas (o % da conferência), o cronograma é recalculado.
    private void OrdensMudaram(string descricao, bool tarefasMudaram)
    {
        if (tarefasMudaram)
        {
            MarcarAlteracao(descricao);
            return;
        }
        RegistrarNoHistorico(descricao);
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
    }
}
