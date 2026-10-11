using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma linha de base na lista, com o término previsto e se é a em uso.</summary>
public sealed record LinhaDeBaseNaLista(LinhaDeBase Linha, bool EmUso)
{
    public string Descricao => Linha.Descricao;

    /// <summary>"Término previsto: 15/12/2026 · 37 tarefas".</summary>
    public string Detalhe =>
        $"Término previsto: {Linha.Termino:dd/MM/yyyy} · " + (Linha.Tarefas.Count == 1 ? "1 tarefa" : $"{Linha.Tarefas.Count} tarefas");
}

/// <summary>
/// Bloco 31: Projeto > Linhas de base…: as linhas salvas, qual é a em uso (a que a grid, o Gantt e a Curva S comparam), e
/// remover as que não servem mais. Cada mudança é um passo do Desfazer.
/// </summary>
public sealed partial class LinhasDeBaseViewModel : ViewModelBase
{
    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly Action<string> _mudou;

    /// <param name="mudou">A em uso mudou ou uma foi removida, com o nome da alteração para o Desfazer.</param>
    public LinhasDeBaseViewModel(Cronograma cronograma, IDialogosService dialogos, Action<string> mudou)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(mudou);
        (_cronograma, _dialogos, _mudou) = (cronograma, dialogos, mudou);
        Atualizar();
    }

    public ObservableCollection<LinhaDeBaseNaLista> Linhas { get; } = [];

    public bool Vazia => Linhas.Count == 0;

    public const string SemLinhas = "Nenhuma linha de base ainda. Use Projeto > Salvar linha de base… para guardar o cronograma de agora.";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    private void Atualizar()
    {
        Linhas.Clear();
        foreach (var linha in _cronograma.LinhasDeBase)
            Linhas.Add(new LinhaDeBaseNaLista(linha, linha == _cronograma.LinhaDeBaseEmUso));
        OnPropertyChanged(nameof(Vazia));
        UsarNenhumaCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Passa a comparar com esta linha de base.</summary>
    [RelayCommand]
    private void Usar(LinhaDeBaseNaLista? item)
    {
        if (item is null || item.EmUso)
            return;
        _cronograma.UsarLinhaDeBase(item.Linha.Numero);
        _mudou($"Usar a linha de base LB{item.Linha.Numero}");
        Mensagem = $"Comparando com a LB{item.Linha.Numero}.";
        Atualizar();
    }

    /// <summary>Não comparar com nenhuma (as linhas continuam guardadas).</summary>
    [RelayCommand(CanExecute = nameof(TemEmUso))]
    private void UsarNenhuma()
    {
        _cronograma.UsarLinhaDeBase(null);
        _mudou("Não comparar com linha de base");
        Mensagem = "Sem comparação com linha de base.";
        Atualizar();
    }

    private bool TemEmUso() => _cronograma.LinhaDeBaseEmUso is not null;

    [RelayCommand]
    private async Task Remover(LinhaDeBaseNaLista? item)
    {
        if (item is null || !await _dialogos.ConfirmarAsync("Remover linha de base",
                $"Remover a {item.Descricao}? Dá para desfazer com Editar > Desfazer.", "Remover"))
            return;
        _cronograma.RemoverLinhaDeBase(item.Linha.Numero);
        _mudou($"Remover a linha de base LB{item.Linha.Numero}");
        Mensagem = $"LB{item.Linha.Numero} removida.";
        Atualizar();
    }
}
