using CommunityToolkit.Mvvm.Input;

namespace ProjectPlanner.App.ViewModels;

// Bloco 39: a barra de ícones (os botões escolhidos em Exibir > Barra de ícones) e as opções do Gantt que viraram botões.
public sealed partial class MainWindowViewModel
{
    public BarraDeIconesViewModel Barra { get; }

    // Bloco 43: a IA que escreve a análise dos indicadores.
    private readonly IA.IAnalistaDaObra _analistaDaObra;

    [RelayCommand]
    private void AlternarCaminhoCritico() => Gantt.MostrarCaminhoCritico = !Gantt.MostrarCaminhoCritico;

    [RelayCommand]
    private void AlternarLinhaDeBase() => Gantt.MostrarLinhaDeBase = !Gantt.MostrarLinhaDeBase;

    [RelayCommand]
    private void AlternarLinhaHoje() => Gantt.MostrarHoje = !Gantt.MostrarHoje;
}
