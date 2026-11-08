using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.App.ViewModels;

// Bloco 75 (o plano "Delete, arrastar e Kanban", K1 a K7): o quadro Kanban, uma visão das mesmas tarefas. Clicar num card
// seleciona a tarefa (a mesma seleção da grade e do Gantt); soltar cards numa coluna aplica as regras do quadro (o % segue o
// card) num passo só do Desfazer; o Delete num card exclui a tarefa com as regras de sempre.
public sealed partial class MainWindowViewModel
{
    /// <summary>O quadro Kanban (Exibir > Kanban), refeito a cada alteração, como o Gantt.</summary>
    public KanbanViewModel Kanban { get; } = new();

    private void LigarOQuadro()
    {
        Kanban.CardClicado += ClicarNoCard;
        Kanban.VazioClicado += () => TarefaSelecionada = null;
        Kanban.PreverMovimento = PreverMovimentoNoQuadro;
        Kanban.Mover = MoverCards;
    }

    /// <summary>O que aconteceria ao soltar os cards das tarefas na coluna, na lacuna dada (0 é antes do primeiro card). Não muda nada.</summary>
    public PlanoDoQuadro PreverMovimentoNoQuadro(IReadOnlyList<Guid> tarefas, Guid colunaId, int lacuna) =>
        RegrasDoQuadro.Planejar(_cronograma, tarefas, colunaId, lacuna);

    /// <summary>
    /// K3 e K5: solta os cards na coluna. Entrar numa Concluída põe 100%; sair dela volta ao % de antes; entrar numa A fazer
    /// põe 0% (com o Desfazer no aviso); reordenar não muda o cronograma (nem recalcula). Um passo só do Desfazer.
    /// </summary>
    /// <returns>Se mudou algo.</returns>
    public bool MoverCards(IReadOnlyList<Guid> tarefas, Guid colunaId, int lacuna)
    {
        ArgumentNullException.ThrowIfNull(tarefas);
        var plano = PreverMovimentoNoQuadro(tarefas, colunaId, lacuna);
        if (!plano.Valido)
        {
            if (plano.Motivo is { } motivo)
                Mensagem = motivo;
            return false;
        }
        if (!plano.MudaAlgo)
            return false;
        try
        {
            RegrasDoQuadro.Aplicar(_cronograma.Copiar(), plano); // primeiro numa cópia: se o domínio recusar, nada muda
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        var resultado = RegrasDoQuadro.Aplicar(_cronograma, plano);
        DefinirSelecao(plano.Tarefas.Select(LinhaDe), LinhaDe(plano.Tarefas[0]));
        if (plano.SoOQuadro)
        {
            // Só os cards mudaram de lugar: nenhuma data nem % muda, e o cronograma não é recalculado.
            AvisoComDesfazer = null;
            RegistrarNoHistorico(resultado.Descricao);
            PossuiAlteracoesNaoSalvas = true;
            OnPropertyChanged(nameof(Titulo));
            AtualizarQuadro();
        }
        else
            MarcarAlteracao(resultado.Descricao); // o % dos resumos, a Curva S, os indicadores...
        Mensagem = resultado.Texto;
        if (plano.ZeraProgresso)
            AvisoComDesfazer = resultado.Texto;
        return true;
    }

    /// <summary>
    /// Um clique num card: sozinho seleciona só a tarefa; com Ctrl (⌘) acrescenta ou tira; com Shift, o intervalo na ordem do
    /// quadro. Vale também para uma tarefa que a grade não mostra (dentro de um resumo recolhido).
    /// </summary>
    public void ClicarNoCard(Guid tarefaId, bool adicionar, bool intervalo)
    {
        if (Tarefas.FirstOrDefault(l => l.Tarefa.Id == tarefaId) is not { } linha)
        {
            TarefaSelecionada = null;
            return;
        }
        if (adicionar)
        {
            var lista = Selecionadas.ToList();
            if (!lista.Remove(linha))
                lista.Add(linha);
            DefinirSelecao(lista, lista.Contains(linha) ? linha : null);
            return;
        }
        var ordem = Kanban.OrdemDosCards.ToList();
        if (intervalo && TarefaSelecionada is { } ancora && ordem.IndexOf(ancora.Tarefa.Id) is var de and >= 0 && ordem.IndexOf(tarefaId) is var ate and >= 0)
            DefinirSelecao(ordem.Skip(Math.Min(de, ate)).Take(Math.Abs(ate - de) + 1).Select(LinhaDe), linha);
        else
            DefinirSelecao([linha], linha);
    }

    private void AtualizarQuadro() =>
        Kanban.Atualizar(_cronograma, _relogio.GetLocalNow().Date, Selecionadas.Select(l => l.Tarefa.Id).ToHashSet());
}
