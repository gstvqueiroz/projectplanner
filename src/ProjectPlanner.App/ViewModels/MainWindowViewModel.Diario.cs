using CommunityToolkit.Mvvm.Input;

namespace ProjectPlanner.App.ViewModels;

// Bloco 81: Projeto > Diário de obra… (o RDO).
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task DiarioDeObra()
    {
        Recalcular(); // o dia novo vem do cronograma calculado
        return _dialogos.MostrarDiarioDeObraAsync(new DiarioDeObraViewModel(_cronograma, _dialogos, _relogio, descricao => MarcarAlteracao(descricao),
            CabecalhoDosRelatorios, fotos => _fotos.Miniaturas(fotos)));
    }
}
