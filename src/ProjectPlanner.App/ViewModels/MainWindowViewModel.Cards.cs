using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.App.ViewModels;

// Bloco 77 (o plano "Delete, arrastar e Kanban", K4 e K5): o conteúdo dos cards (checklist, etiquetas), arquivar, "+ Adicionar
// card" e as etiquetas do projeto. Cada mudança é um passo do Desfazer; o checklist e as etiquetas não mudam o %, e não recalculam.
public sealed partial class MainWindowViewModel : IAcoesDosCards
{
    void IAcoesDosCards.DefinirChecklist(Guid tarefaId, IReadOnlyList<ItemDoChecklist> itens, string descricao) =>
        MudarOQuadro($"{descricao} de \"{NomeDa(tarefaId)}\"", () =>
        {
            RegrasDosCards.DefinirChecklist(_cronograma, tarefaId, itens);
            var feitos = itens.Count(i => i.Feito);
            return (itens.Count == 0 ? $"O checklist de \"{NomeDa(tarefaId)}\" ficou vazio." : $"Checklist de \"{NomeDa(tarefaId)}\": {feitos} de {itens.Count}.", false);
        });

    void IAcoesDosCards.MarcarEtiqueta(Guid tarefaId, Guid etiquetaId, bool marcada)
    {
        var etiqueta = _cronograma.Quadro.Etiquetas.FirstOrDefault(e => e.Id == etiquetaId)?.Nome ?? "";
        MudarOQuadro(marcada ? $"Etiqueta \"{etiqueta}\" em \"{NomeDa(tarefaId)}\"" : $"Tirar a etiqueta \"{etiqueta}\" de \"{NomeDa(tarefaId)}\"", () =>
        {
            RegrasDosCards.MarcarEtiqueta(_cronograma, tarefaId, etiquetaId, marcada);
            return (marcada ? $"\"{NomeDa(tarefaId)}\" com a etiqueta \"{etiqueta}\"." : $"\"{NomeDa(tarefaId)}\" sem a etiqueta \"{etiqueta}\".", false);
        });
    }

    /// <summary>K5: arquivar esconde o card do quadro; a tarefa continua no cronograma (e na grade e no Gantt).</summary>
    void IAcoesDosCards.Arquivar(IReadOnlyList<Guid> tarefas)
    {
        var nomes = tarefas.Where(id => _cronograma.Quadro.CardDe(id) is { Arquivado: false }).Select(NomeDa).ToList();
        if (nomes.Count == 0)
            return;
        var descricao = nomes.Count == 1 ? $"Arquivar \"{nomes[0]}\"" : $"Arquivar {nomes.Count} cards";
        if (MudarOQuadro(descricao, () =>
            {
                RegrasDosCards.Arquivar(_cronograma, tarefas);
                return ((nomes.Count == 1 ? $"Card \"{nomes[0]}\" arquivado" : $"{nomes.Count} cards arquivados")
                        + ": a tarefa continua no cronograma (Arquivados, no alto do quadro, traz de volta).", false);
            }))
            AvisoComDesfazer = Mensagem;
    }

    void IAcoesDosCards.Desarquivar(IReadOnlyList<Guid> tarefas)
    {
        var nomes = tarefas.Where(id => _cronograma.Quadro.CardDe(id) is { Arquivado: true }).Select(NomeDa).ToList();
        if (nomes.Count == 0)
            return;
        MudarOQuadro(nomes.Count == 1 ? $"Desarquivar \"{nomes[0]}\"" : $"Desarquivar {nomes.Count} cards", () =>
        {
            RegrasDosCards.Desarquivar(_cronograma, tarefas);
            return (nomes.Count == 1 ? $"Card \"{nomes[0]}\" de volta ao quadro." : $"{nomes.Count} cards de volta ao quadro.", false);
        });
    }

    /// <summary>
    /// K5: "+ Adicionar card" no pé da coluna cria a tarefa no fim do cronograma (no primeiro nível, com 1 dia, como Adicionar tarefa)
    /// e põe o card no fim da coluna, com a regra do % dela (numa Concluída, a tarefa nasce com 100%).
    /// </summary>
    async Task IAcoesDosCards.AdicionarCardAsync(Guid colunaId)
    {
        if (_cronograma.Quadro.Coluna(colunaId) is not { } coluna)
            return;
        var nome = await _dialogos.PedirTextoAsync("Novo card",
            $"Nome da tarefa (ela entra no fim do cronograma, com 1 dia, e o card no fim de \"{coluna.Nome}\"):", "Nova tarefa");
        if (string.IsNullOrWhiteSpace(nome))
            return;
        var tarefa = new Tarefa(nome, _cronograma.DataInicio, 1);
        _cronograma.AdicionarTarefa(tarefa);
        _cronograma.Quadro.Sincronizar();
        var plano = RegrasDoQuadro.Planejar(_cronograma, [tarefa.Id], colunaId, int.MaxValue);
        if (plano is { Valido: true, MudaAlgo: true })
            RegrasDoQuadro.Aplicar(_cronograma, plano);
        SincronizarLinhas();
        TarefaSelecionada = LinhaDe(tarefa.Id);
        MarcarAlteracao($"Adicionar card \"{tarefa.Nome}\"");
        Mensagem = $"Card \"{tarefa.Nome}\" criado em \"{coluna.Nome}\": é a tarefa {LinhaDe(tarefa.Id).Numero} do cronograma"
                   + (tarefa.PercentualConcluido == 100 ? ", já com 100%." : ".");
    }

    async Task IAcoesDosCards.NovaEtiquetaAsync(Guid? marcarNoCard)
    {
        var nome = await _dialogos.PedirTextoAsync("Nova etiqueta", "Nome da etiqueta (por exemplo, Elétrica, Urgente, Terceiro):", "");
        if (string.IsNullOrWhiteSpace(nome))
            return;
        MudarOQuadro($"Nova etiqueta \"{nome.Trim()}\"", () =>
        {
            var etiqueta = RegrasDosCards.CriarEtiqueta(_cronograma, nome);
            if (marcarNoCard is { } id && _cronograma.Quadro.CardDe(id) is not null)
                RegrasDosCards.MarcarEtiqueta(_cronograma, id, etiqueta.Id, true);
            return ($"Etiqueta \"{etiqueta.Nome}\" criada" + (marcarNoCard is { } t ? $" e marcada em \"{NomeDa(t)}\"." : "."), false);
        });
    }

    async Task IAcoesDosCards.RenomearEtiquetaAsync(Guid etiquetaId)
    {
        if (_cronograma.Quadro.Etiquetas.FirstOrDefault(e => e.Id == etiquetaId) is not { } etiqueta)
            return;
        var nome = await _dialogos.PedirTextoAsync("Renomear etiqueta", "Nome da etiqueta:", etiqueta.Nome);
        if (nome is null || nome.Trim() == etiqueta.Nome)
            return;
        MudarOQuadro($"Renomear etiqueta \"{etiqueta.Nome}\"", () =>
        {
            RegrasDosCards.RenomearEtiqueta(_cronograma, etiquetaId, nome);
            return ($"A etiqueta \"{etiqueta.Nome}\" agora é \"{nome.Trim()}\".", false);
        });
    }

    void IAcoesDosCards.ColorirEtiqueta(Guid etiquetaId, string cor)
    {
        if (_cronograma.Quadro.Etiquetas.FirstOrDefault(e => e.Id == etiquetaId) is not { } etiqueta || etiqueta.Cor == cor)
            return;
        MudarOQuadro($"Cor da etiqueta \"{etiqueta.Nome}\"", () =>
        {
            RegrasDosCards.ColorirEtiqueta(_cronograma, etiquetaId, cor);
            return ($"Cor da etiqueta \"{etiqueta.Nome}\" mudada.", false);
        });
    }

    void IAcoesDosCards.ExcluirEtiqueta(Guid etiquetaId)
    {
        if (_cronograma.Quadro.Etiquetas.FirstOrDefault(e => e.Id == etiquetaId) is not { } etiqueta)
            return;
        if (MudarOQuadro($"Excluir etiqueta \"{etiqueta.Nome}\"", () =>
            {
                var cards = RegrasDosCards.ExcluirEtiqueta(_cronograma, etiquetaId);
                return ($"Etiqueta \"{etiqueta.Nome}\" excluída" + cards switch { 0 => ".", 1 => " (saiu de 1 card).", _ => $" (saiu de {cards} cards)." }, false);
            }))
            AvisoComDesfazer = Mensagem;
    }

    /// <summary>"Excluir tarefa" no painel ou no menu do card: as mesmas regras do Delete (E1 a E8).</summary>
    async Task IAcoesDosCards.ExcluirTarefasAsync(IReadOnlyList<Guid> tarefas)
    {
        var linhas = tarefas.Select(id => Tarefas.FirstOrDefault(l => l.Tarefa.Id == id)).OfType<TarefaViewModel>().ToList();
        if (linhas.Count == 0)
            return;
        DefinirSelecao(linhas, linhas[0]);
        await RemoverTarefaCommand.ExecuteAsync(null);
    }

    Task IAcoesDosCards.RecursosDaTarefaAsync(Guid tarefaId)
    {
        if (Tarefas.FirstOrDefault(l => l.Tarefa.Id == tarefaId) is not { } linha)
            return Task.CompletedTask;
        DefinirSelecao([linha], linha);
        return RecursosDaTarefaCommand.ExecuteAsync(null);
    }
}
