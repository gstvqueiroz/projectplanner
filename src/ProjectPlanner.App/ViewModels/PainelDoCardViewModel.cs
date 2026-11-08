using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// Bloco 77 (K4 e K5): o que o painel do card e o menu dos cards pedem ao programa, que aplica as regras de
/// <see cref="RegrasDosCards"/> e registra o Desfazer.
/// </summary>
public interface IAcoesDosCards
{
    void DefinirChecklist(Guid tarefaId, IReadOnlyList<ItemDoChecklist> itens, string descricao);

    void MarcarEtiqueta(Guid tarefaId, Guid etiquetaId, bool marcada);

    void Arquivar(IReadOnlyList<Guid> tarefas);

    void Desarquivar(IReadOnlyList<Guid> tarefas);

    Task AdicionarCardAsync(Guid colunaId);

    /// <summary>Uma etiqueta nova no projeto (e marcada no card, se for dado).</summary>
    Task NovaEtiquetaAsync(Guid? marcarNoCard);

    Task RenomearEtiquetaAsync(Guid etiquetaId);

    void ColorirEtiqueta(Guid etiquetaId, string cor);

    void ExcluirEtiqueta(Guid etiquetaId);

    Task ExcluirTarefasAsync(IReadOnlyList<Guid> tarefas);

    Task RecursosDaTarefaAsync(Guid tarefaId);
}

/// <summary>Bloco 77: um item do checklist no painel (marcar ou mudar o texto vira um passo do Desfazer).</summary>
public sealed partial class ItemNoPainel(PainelDoCard painel, string texto, bool feito) : ObservableObject
{
    [RelayCommand]
    private void Remover() => painel.RemoverItemCommand.Execute(this);

    [ObservableProperty]
    public partial string Texto { get; set; } = texto;

    [ObservableProperty]
    public partial bool Feito { get; set; } = feito;

    partial void OnTextoChanged(string value) => painel.ChecklistMudou(this, textoMudou: true);

    partial void OnFeitoChanged(bool value) => painel.ChecklistMudou(this, textoMudou: false);
}

/// <summary>Bloco 77: uma etiqueta do projeto no painel, marcada ou não no card.</summary>
public sealed partial class EtiquetaNoPainel(PainelDoCard painel, Guid id, string nome, string cor, bool marcada) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    public partial string Nome { get; set; } = nome;

    [ObservableProperty]
    public partial string Cor { get; set; } = cor;

    [ObservableProperty]
    public partial bool Marcada { get; set; } = marcada;

    partial void OnMarcadaChanged(bool value) => painel.EtiquetaMudou(this);
}

/// <summary>
/// Bloco 77 (K4): o painel lateral do card (duplo clique num card): os campos da tarefa (os mesmos da grade, com as mesmas
/// validações e o mesmo Desfazer: o painel edita a própria linha da tarefa), a descrição (as notas), as etiquetas, o checklist
/// (que não muda o %) e arquivar. O que muda ali muda no cronograma; depois de cada mudança (e do Desfazer) o painel é refeito a
/// partir do cronograma.
/// </summary>
public sealed partial class PainelDoCard(Guid tarefaId, IAcoesDosCards? acoes, Action? aoFechar = null) : ObservableObject
{
    [RelayCommand]
    private void Fechar() => aoFechar?.Invoke();

    private bool _atualizando;

    public Guid TarefaId { get; } = tarefaId;

    /// <summary>A linha da tarefa na grade (os campos do painel são os dela).</summary>
    [ObservableProperty]
    public partial TarefaViewModel? Linha { get; private set; }

    /// <summary>"1.2 · Nº 3".</summary>
    [ObservableProperty]
    public partial string Identificacao { get; private set; } = "";

    [ObservableProperty]
    public partial string? Resumo { get; private set; }

    [ObservableProperty]
    public partial string? Responsaveis { get; private set; }

    [ObservableProperty]
    public partial string Coluna { get; private set; } = "";

    [ObservableProperty]
    public partial bool Arquivado { get; private set; }

    /// <summary>"2 de 5"; vazio sem itens.</summary>
    [ObservableProperty]
    public partial string ProgressoDoChecklist { get; private set; } = "";

    /// <summary>O texto do item novo do checklist.</summary>
    [ObservableProperty]
    public partial string NovoItem { get; set; } = "";

    public ObservableCollection<ItemNoPainel> Checklist { get; } = [];

    public ObservableCollection<EtiquetaNoPainel> Etiquetas { get; } = [];

    [RelayCommand]
    private void AdicionarItem()
    {
        if (string.IsNullOrWhiteSpace(NovoItem))
            return;
        var itens = Itens().Append(new ItemDoChecklist(NovoItem.Trim(), false)).ToList();
        NovoItem = "";
        acoes?.DefinirChecklist(TarefaId, itens, "Adicionar item do checklist");
    }

    [RelayCommand]
    private void RemoverItem(ItemNoPainel item) =>
        acoes?.DefinirChecklist(TarefaId, Checklist.Where(i => i != item).Select(i => new ItemDoChecklist(i.Texto, i.Feito)).ToList(),
            "Remover item do checklist");

    [RelayCommand]
    private void Arquivar() => acoes?.Arquivar([TarefaId]);

    [RelayCommand]
    private void Desarquivar() => acoes?.Desarquivar([TarefaId]);

    [RelayCommand]
    private Task NovaEtiqueta() => acoes?.NovaEtiquetaAsync(TarefaId) ?? Task.CompletedTask;

    [RelayCommand]
    private Task Excluir() => acoes?.ExcluirTarefasAsync([TarefaId]) ?? Task.CompletedTask;

    [RelayCommand]
    private Task Recursos() => acoes?.RecursosDaTarefaAsync(TarefaId) ?? Task.CompletedTask;

    internal void ChecklistMudou(ItemNoPainel item, bool textoMudou)
    {
        if (_atualizando)
            return;
        acoes?.DefinirChecklist(TarefaId, Itens().ToList(),
            textoMudou ? "Mudar item do checklist" : item.Feito ? "Marcar item do checklist" : "Desmarcar item do checklist");
    }

    internal void EtiquetaMudou(EtiquetaNoPainel etiqueta)
    {
        if (!_atualizando)
            acoes?.MarcarEtiqueta(TarefaId, etiqueta.Id, etiqueta.Marcada);
    }

    private IEnumerable<ItemDoChecklist> Itens() => Checklist.Select(i => new ItemDoChecklist(i.Texto, i.Feito));

    /// <summary>Refaz o painel a partir do cronograma (depois de cada mudança e do Desfazer), mexendo só no que mudou.</summary>
    internal void Atualizar(TarefaViewModel linha, CardDoQuadro card, Cronograma cronograma, string? responsaveis)
    {
        _atualizando = true;
        try
        {
            Linha = linha;
            Identificacao = $"{linha.Edt} · Nº {linha.Numero}";
            Resumo = linha.Tarefa.TarefaPaiId is { } pai ? cronograma.Tarefas.First(t => t.Id == pai).Nome : null;
            Responsaveis = responsaveis;
            Coluna = cronograma.Quadro.Coluna(card.ColunaId)?.Nome ?? "";
            Arquivado = card.Arquivado;
            ProgressoDoChecklist = card.Checklist.Count == 0 ? "" : $"{card.Checklist.Count(i => i.Feito)} de {card.Checklist.Count}";

            while (Checklist.Count > card.Checklist.Count)
                Checklist.RemoveAt(Checklist.Count - 1);
            for (var i = 0; i < card.Checklist.Count; i++)
            {
                if (i < Checklist.Count)
                    (Checklist[i].Texto, Checklist[i].Feito) = (card.Checklist[i].Texto, card.Checklist[i].Feito);
                else
                    Checklist.Add(new ItemNoPainel(this, card.Checklist[i].Texto, card.Checklist[i].Feito));
            }

            var etiquetas = cronograma.Quadro.Etiquetas;
            if (!Etiquetas.Select(e => e.Id).SequenceEqual(etiquetas.Select(e => e.Id)))
            {
                Etiquetas.Clear();
                foreach (var e in etiquetas)
                    Etiquetas.Add(new EtiquetaNoPainel(this, e.Id, e.Nome, e.Cor, card.Etiquetas.Contains(e.Id)));
            }
            else
                foreach (var (naTela, e) in Etiquetas.Zip(etiquetas))
                    (naTela.Nome, naTela.Cor, naTela.Marcada) = (e.Nome, e.Cor, card.Etiquetas.Contains(e.Id));
        }
        finally
        {
            _atualizando = false;
        }
    }
}
