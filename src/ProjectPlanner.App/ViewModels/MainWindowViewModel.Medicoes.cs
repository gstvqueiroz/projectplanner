using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.App.ViewModels;

// Bloco 36: Projeto > Medições… (medições, aditivos, regras do contrato e plano de medições).
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task Medicoes()
    {
        Recalcular(); // o plano e o avanço físico vêm do cálculo
        return _dialogos.MostrarMedicoesAsync(new MedicoesViewModel(_cronograma, _dialogos, _relogio, _preferenciasRelatorio, ContratoMudou,
            RelatorioDoPeriodoAsync));
    }

    // Uma mudança do contrato entra no Desfazer; só um aditivo (que muda os valores das tarefas) recalcula o cronograma.
    private void ContratoMudou(string descricao, bool recalcular)
    {
        if (recalcular)
        {
            MarcarAlteracao(descricao);
            return;
        }
        RegistrarNoHistorico(descricao);
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
    }

    // O relatório fotográfico com o período da medição e as tarefas em execução nele.
    private Task RelatorioDoPeriodoAsync(DateOnly de, DateOnly ate)
    {
        Recalcular();
        var relatorio = new RelatorioFotograficoViewModel(_cronograma, _fotos, _preferenciasRelatorio, _dialogos, _relogio, MarcarSemRecalcular, _analista);
        (relatorio.De, relatorio.Ate) = (de.ToDateTime(TimeOnly.MinValue), ate.ToDateTime(TimeOnly.MinValue));
        relatorio.TarefasEscolhidas = relatorio.OpcoesDeTarefas.First(o => o.Valor == TarefasDoRelatorio.EmExecucaoNoPeriodo);
        return _dialogos.MostrarRelatorioFotograficoAsync(relatorio);
    }
}
