using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 33: Projeto > Orçamento….
public sealed partial class MainWindowViewModel
{
    private readonly IBibliotecaDeReferencias _biblioteca;

    [RelayCommand]
    private Task Orcamento()
    {
        Recalcular();
        return _dialogos.MostrarOrcamentoAsync(new OrcamentoViewModel(_cronograma, _dialogos, _biblioteca, _relogio, descricao => MarcarAlteracao(descricao)));
    }
}
