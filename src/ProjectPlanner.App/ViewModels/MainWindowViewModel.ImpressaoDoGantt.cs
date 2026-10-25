using CommunityToolkit.Mvvm.Input;

namespace ProjectPlanner.App.ViewModels;

// Bloco 57: Arquivo > Imprimir o Gantt….
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task ImprimirGantt()
    {
        if (!Recalcular())
            return Task.CompletedTask; // a mensagem do cálculo já está na barra
        var resultado = _calculadora.Calcular(_cronograma);
        var edicao = (IEdicaoCronograma)this;
        var predecessoras = _cronograma.Tarefas.ToDictionary(t => t.Id, edicao.TextoPredecessoras);
        var tela = new GanttDaTela(Gantt.MostrarCaminhoCritico, Gantt.MostrarLinhaDeBase, Gantt.MostrarHoje, _ocultas);
        return _dialogos.MostrarImpressaoDoGanttAsync(new GanttImpressoViewModel(_cronograma, resultado, _dialogos, _relogio, tela, predecessoras));
    }
}
