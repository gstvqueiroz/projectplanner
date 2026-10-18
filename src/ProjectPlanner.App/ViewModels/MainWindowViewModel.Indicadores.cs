using CommunityToolkit.Mvvm.Input;

namespace ProjectPlanner.App.ViewModels;

// Bloco 37: Projeto > Custos realizados… e Indicadores….
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task CustosRealizados() => _dialogos.MostrarCustosAsync(new CustosViewModel(_cronograma, _dialogos, _relogio, CustosMudaram));

    // Os custos entram no Desfazer, sem recalcular o cronograma (não mudam datas nem valores das tarefas).
    private void CustosMudaram(string descricao)
    {
        RegistrarNoHistorico(descricao);
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
    }

    [RelayCommand]
    private Task Indicadores()
    {
        Recalcular(); // o valor agregado e os atrasos vêm do cálculo
        return _dialogos.MostrarIndicadoresAsync(new IndicadoresViewModel(_cronograma, _dialogos, _relogio, _analistaDaObra));
    }
}
