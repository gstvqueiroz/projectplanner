using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 79: as abas de atalho no rodapé, à esquerda, como as planilhas do Excel. Gantt e Kanban trocam a visão na própria janela
// (a aba da visão atual fica marcada); as outras abrem as telas (Orçamento, Recursos, Medições...), com os mesmos comandos do
// menu. Exibir > Abas no rodapé liga e desliga a faixa (fica em interface.json).
public sealed partial class MainWindowViewModel
{
    private ModoVisualizacao _ultimoModoDoCronograma = ModoVisualizacao.Dividido;

    /// <summary>A visão atual é a do cronograma (a tabela e o Gantt, juntos ou um só): a aba Gantt fica marcada.</summary>
    public bool EhCronograma => ModoVisualizacao != ModoVisualizacao.Kanban;

    /// <summary>
    /// A visão do cronograma para onde a aba Gantt volta: a última usada antes do Kanban (a tabela e o Gantt lado a lado, no
    /// começo). A aba usa o mesmo comando dos modos do menu Exibir.
    /// </summary>
    public ModoVisualizacao ModoDoCronograma => _ultimoModoDoCronograma;

    partial void OnModoVisualizacaoChanged(ModoVisualizacao value)
    {
        if (value == ModoVisualizacao.Kanban || value == _ultimoModoDoCronograma)
            return;
        _ultimoModoDoCronograma = value;
        OnPropertyChanged(nameof(ModoDoCronograma));
    }

    /// <summary>As abas de atalho aparecem no rodapé.</summary>
    [ObservableProperty]
    public partial bool MostrarAbas { get; private set; }

    /// <summary>Exibir > Abas no rodapé: mostra ou esconde a faixa e guarda a escolha para as próximas sessões.</summary>
    [RelayCommand]
    private void AlternarAbas()
    {
        MostrarAbas = !MostrarAbas;
        // "with": os outros campos (o tema, o painel do assistente, os botões tirados da barra) ficam como estavam.
        _preferenciasInterface.Salvar((_preferenciasInterface.Carregar() ?? new PreferenciasInterface(Assistente.Visivel, Tema)) with
        {
            OcultarAbas = !MostrarAbas,
        });
    }
}
