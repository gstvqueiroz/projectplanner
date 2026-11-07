namespace ProjectPlanner.App.ViewModels;

// Bloco 72: a seleção, a mesma na grade, no Gantt (e no Kanban): várias tarefas, com a principal em TarefaSelecionada (a
// última clicada, que os comandos de uma tarefa só usam). Ctrl+clique (⌘ no Mac) acrescenta ou tira; Shift+clique seleciona o
// intervalo desde a selecionada.
public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<TarefaViewModel> _selecionadas = [];
    private bool _definindoSelecao;

    /// <summary>As tarefas selecionadas, na ordem do cronograma; só a principal quando não há seleção múltipla.</summary>
    public IReadOnlyList<TarefaViewModel> Selecionadas => _selecionadas.Count > 0 ? _selecionadas : TarefaSelecionada is { } t ? [t] : [];

    /// <summary>A seleção está sendo posta pelo programa (o Gantt, o Kanban): a grade não deve devolvê-la enquanto isso.</summary>
    public bool AplicandoSelecao { get; private set; }

    /// <summary>A seleção mudou fora da grade: a janela põe a mesma seleção nela.</summary>
    public event Action? SelecaoMudou;

    /// <summary>Seleciona as tarefas (a principal é a última, se não for dada); <paramref name="daGrade"/>: veio da grade.</summary>
    public void DefinirSelecao(IEnumerable<TarefaViewModel> linhas, TarefaViewModel? principal = null, bool daGrade = false)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        var existentes = Tarefas.ToHashSet();
        var lista = linhas.Where(existentes.Contains).Distinct().OrderBy(l => l.Numero).ToList();
        principal = principal is not null && lista.Contains(principal) ? principal : lista.LastOrDefault();
        _definindoSelecao = true;
        AplicandoSelecao = !daGrade;
        try
        {
            _selecionadas = lista;
            TarefaSelecionada = principal;
        }
        finally
        {
            _definindoSelecao = false;
            AplicandoSelecao = false;
        }
        SelecaoAtualizada(avisarAGrade: !daGrade);
    }

    /// <summary>Um clique numa tarefa (no Gantt ou num card): sozinho seleciona só ela; com Ctrl acrescenta ou tira; com Shift, o intervalo.</summary>
    public void ClicarNaTarefa(Guid tarefaId, bool adicionar, bool intervalo)
    {
        if (LinhasVisiveis.FirstOrDefault(l => l.Tarefa.Id == tarefaId) is not { } linha)
        {
            TarefaSelecionada = null; // uma tarefa que não está mais na grade (o layout ainda era o de antes)
            return;
        }
        if (adicionar)
        {
            var lista = Selecionadas.ToList();
            if (!lista.Remove(linha))
                lista.Add(linha);
            DefinirSelecao(lista, lista.Contains(linha) ? linha : null);
        }
        else if (intervalo && TarefaSelecionada is { } ancora && LinhasVisiveis.IndexOf(ancora) is var de and >= 0)
        {
            var ate = LinhasVisiveis.IndexOf(linha);
            DefinirSelecao(LinhasVisiveis.Skip(Math.Min(de, ate)).Take(Math.Abs(ate - de) + 1), linha);
        }
        else
            DefinirSelecao([linha], linha); // também quando ela já era a principal de várias
    }

    /// <summary>Clique com o botão direito: numa tarefa fora da seleção, ela passa a ser a seleção; dentro, a seleção fica.</summary>
    public void ClicarComBotaoDireito(Guid tarefaId)
    {
        if (!Selecionadas.Any(l => l.Tarefa.Id == tarefaId))
            ClicarNaTarefa(tarefaId, adicionar: false, intervalo: false);
    }

    private void SelecaoAtualizada(bool avisarAGrade)
    {
        AtualizarDestaqueNoGantt();
        RemoverTarefaCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Selecionadas));
        if (avisarAGrade)
            SelecaoMudou?.Invoke();
    }
}

// Bloco 72: o aviso com o botão Desfazer ("Tarefa excluída · Desfazer"), que a janela mostra por alguns segundos.
public sealed partial class MainWindowViewModel
{
    /// <summary>Quanto tempo o aviso fica na tela.</summary>
    public static readonly TimeSpan DuracaoDoAviso = TimeSpan.FromSeconds(8);

    /// <summary>
    /// O texto do aviso com o Desfazer; nulo sem aviso. Some na próxima alteração, no Desfazer (o botão do aviso é o mesmo
    /// Desfazer do menu Editar) e quando a janela o fecha.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial string? AvisoComDesfazer { get; set; }
}
