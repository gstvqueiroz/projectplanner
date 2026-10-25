using CommunityToolkit.Mvvm.Input;

namespace ProjectPlanner.App.ViewModels;

// Bloco 57: Arquivo > Imprimir o Gantt….
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task ImprimirGantt() => NovaImpressaoDoGantt() is { } impressao ? _dialogos.MostrarImpressaoDoGanttAsync(impressao) : Task.CompletedTask;

    /// <summary>A impressão do Gantt com o cronograma calculado; nula se o cálculo falhou (a mensagem já está na barra).</summary>
    internal GanttImpressoViewModel? NovaImpressaoDoGantt()
    {
        if (!Recalcular())
            return null;
        var resultado = _calculadora.Calcular(_cronograma);
        var edicao = (IEdicaoCronograma)this;
        var predecessoras = _cronograma.Tarefas.ToDictionary(t => t.Id, edicao.TextoPredecessoras);
        var tela = new GanttDaTela(Gantt.MostrarCaminhoCritico, Gantt.MostrarLinhaDeBase, Gantt.MostrarHoje, _ocultas);
        return new GanttImpressoViewModel(_cronograma, resultado, _dialogos, _relogio, tela, predecessoras);
    }
}
