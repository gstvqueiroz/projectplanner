using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 75: uma etiqueta no card (o nome e a cor #RRGGBB).</summary>
public sealed record EtiquetaNoCard(string Nome, string Cor);

/// <summary>
/// Bloco 75 (o plano "Delete, arrastar e Kanban", K4): um card do quadro, com o que ele mostra: o nome (◆ num marco), o resumo
/// em que a tarefa está (como etiqueta), o início e o término, a barra do %, os responsáveis (os profissionais da tarefa), as
/// etiquetas e o checklist (2/5). Atrasada (término antes de hoje e abaixo de 100%): a data em vermelho.
/// </summary>
public sealed partial class CardNoKanban(Guid tarefaId) : ObservableObject
{
    public Guid TarefaId { get; } = tarefaId;

    [ObservableProperty]
    public partial string Nome { get; set; } = "";

    /// <summary>O resumo em que a tarefa está; nulo no primeiro nível.</summary>
    [ObservableProperty]
    public partial string? Resumo { get; set; }

    /// <summary>"12/11 – 20/11"; num marco, o dia só.</summary>
    [ObservableProperty]
    public partial string Datas { get; set; } = "";

    [ObservableProperty]
    public partial int Percentual { get; set; }

    [ObservableProperty]
    public partial bool EhMarco { get; set; }

    /// <summary>Término antes de hoje e abaixo de 100%.</summary>
    [ObservableProperty]
    public partial bool Atrasada { get; set; }

    /// <summary>Os profissionais da tarefa ("Pedreiro, Servente +1"); nulo sem nenhum.</summary>
    [ObservableProperty]
    public partial string? Responsaveis { get; set; }

    /// <summary>"2/5"; nulo sem checklist.</summary>
    [ObservableProperty]
    public partial string? Checklist { get; set; }

    [ObservableProperty]
    public partial bool Selecionado { get; set; }

    /// <summary>Sendo arrastado (fica meio transparente no lugar de origem).</summary>
    [ObservableProperty]
    public partial bool Arrastado { get; set; }

    /// <summary>"1.2 · Escavação · 12/11/2026 a 20/11/2026 · 40%": a dica do card.</summary>
    [ObservableProperty]
    public partial string Dica { get; set; } = "";

    public ObservableCollection<EtiquetaNoCard> Etiquetas { get; } = [];

    /// <summary>A largura da barra do %, de 0 a 1.</summary>
    public double Fracao => Percentual / 100.0;

    partial void OnPercentualChanged(int value) => OnPropertyChanged(nameof(Fracao));
}

/// <summary>
/// Bloco 76 (K2): o que o menu de uma coluna pede ao programa (que aplica as regras de <see cref="RegrasDasColunas"/> e registra o
/// Desfazer).
/// </summary>
public interface IAcoesDasColunas
{
    Task AdicionarColunaAsync();

    Task RenomearColunaAsync(Guid colunaId);

    void ColorirColuna(Guid colunaId, string cor);

    Task DefinirLimiteDaColunaAsync(Guid colunaId);

    void MoverColuna(Guid colunaId, int passo);

    void MudarTipoDaColuna(Guid colunaId, TipoDeColuna tipo);

    Task ExcluirColunaAsync(Guid colunaId);
}

/// <summary>Bloco 75: uma coluna do quadro, com os cards dela na ordem do quadro; Bloco 76: os comandos do menu dela.</summary>
public sealed partial class ColunaNoKanban(Guid id, Func<IAcoesDasColunas?>? acoes = null) : ObservableObject
{
    public Guid Id { get; } = id;

    private IAcoesDasColunas? Acoes => acoes?.Invoke();

    /// <summary>Não é a primeira: dá para levar para a esquerda.</summary>
    [ObservableProperty]
    public partial bool PodeIrParaAEsquerda { get; set; }

    /// <summary>Não é a última: dá para levar para a direita.</summary>
    [ObservableProperty]
    public partial bool PodeIrParaADireita { get; set; }

    [RelayCommand]
    private Task Renomear() => Acoes?.RenomearColunaAsync(Id) ?? Task.CompletedTask;

    [RelayCommand]
    private void Colorir(string cor) => Acoes?.ColorirColuna(Id, cor);

    [RelayCommand]
    private Task DefinirLimite() => Acoes?.DefinirLimiteDaColunaAsync(Id) ?? Task.CompletedTask;

    [RelayCommand]
    private void MoverParaAEsquerda() => Acoes?.MoverColuna(Id, -1);

    [RelayCommand]
    private void MoverParaADireita() => Acoes?.MoverColuna(Id, 1);

    [RelayCommand]
    private void MudarTipo(TipoDeColuna tipo) => Acoes?.MudarTipoDaColuna(Id, tipo);

    [RelayCommand]
    private Task Excluir() => Acoes?.ExcluirColunaAsync(Id) ?? Task.CompletedTask;

    [ObservableProperty]
    public partial string Nome { get; set; } = "";

    [ObservableProperty]
    public partial TipoDeColuna Tipo { get; set; }

    /// <summary>#RRGGBB; nula, a do tema.</summary>
    [ObservableProperty]
    public partial string? Cor { get; set; }

    [ObservableProperty]
    public partial int? Limite { get; set; }

    /// <summary>"3", ou "3/5" com limite de cards.</summary>
    [ObservableProperty]
    public partial string Contagem { get; set; } = "0";

    /// <summary>Mais cards que o limite (WIP): a coluna fica destacada.</summary>
    [ObservableProperty]
    public partial bool PassouDoLimite { get; set; }

    public ObservableCollection<CardNoKanban> Cards { get; } = [];
}

/// <summary>Bloco 75: o arraste em curso no quadro: a coluna e a lacuna entre os cards dela onde os cards cairiam, e o plano.</summary>
public sealed record MovimentoNoKanban(Guid ColunaId, int Lacuna, PlanoDoQuadro Plano);

/// <summary>
/// Bloco 75 (o plano "Delete, arrastar e Kanban", K1 a K5): o quadro Kanban, uma visão das mesmas tarefas da grade e do Gantt.
/// Os cards são das tarefas sem subtarefas; as colunas são do <see cref="QuadroDoProjeto"/>. O quadro não muda o cronograma
/// sozinho: clicar num card, soltar cards numa coluna e o Delete pedem ao <see cref="MainWindowViewModel"/>, que aplica as regras
/// (<see cref="RegrasDoQuadro"/>) e registra o Desfazer, como na grade e no Gantt.
/// </summary>
public sealed partial class KanbanViewModel : ObservableObject
{
    private readonly Dictionary<Guid, CardNoKanban> _cards = [];
    private readonly Dictionary<Guid, ColunaNoKanban> _colunas = [];

    /// <summary>As colunas, da esquerda para a direita.</summary>
    public ObservableCollection<ColunaNoKanban> Colunas { get; } = [];

    /// <summary>O quadro não tem nenhum card (o projeto não tem tarefas, ou só arquivadas).</summary>
    [ObservableProperty]
    public partial bool SemCards { get; private set; } = true;

    /// <summary>Bloco 76: quem aplica o que o menu das colunas pede (o programa).</summary>
    public IAcoesDasColunas? Acoes { get; set; }

    /// <summary>Bloco 76: "+ Adicionar coluna", no fim do quadro.</summary>
    [RelayCommand]
    private Task AdicionarColuna() => Acoes?.AdicionarColunaAsync() ?? Task.CompletedTask;

    /// <summary>O arraste em curso (a janela mostra a guia e a dica); nulo sem arraste.</summary>
    [ObservableProperty]
    public partial MovimentoNoKanban? MovimentoEmCurso { get; set; }

    /// <summary>Um clique num card: a tarefa, com Ctrl (⌘) e com Shift.</summary>
    public event Action<Guid, bool, bool>? CardClicado;

    /// <summary>Um clique fora dos cards: limpa a seleção.</summary>
    public event Action? VazioClicado;

    /// <summary>O que aconteceria ao soltar os cards na coluna, na lacuna dada (sem mudar nada).</summary>
    public Func<IReadOnlyList<Guid>, Guid, int, PlanoDoQuadro>? PreverMovimento { get; set; }

    /// <summary>Solta os cards na coluna, na lacuna dada; devolve se mudou algo.</summary>
    public Func<IReadOnlyList<Guid>, Guid, int, bool>? Mover { get; set; }

    /// <summary>As tarefas dos cards, na ordem do quadro (coluna por coluna): o intervalo do Shift+clique.</summary>
    public IReadOnlyList<Guid> OrdemDosCards => [.. Colunas.SelectMany(c => c.Cards).Select(c => c.TarefaId)];

    /// <summary>O card da tarefa; nulo se ela não está no quadro.</summary>
    public CardNoKanban? CardDe(Guid tarefaId) => _cards.GetValueOrDefault(tarefaId);

    /// <summary>A coluna em que o card está; nula se ele não está no quadro.</summary>
    public ColunaNoKanban? ColunaDe(Guid tarefaId) => Colunas.FirstOrDefault(c => c.Cards.Any(k => k.TarefaId == tarefaId));

    public void ClicarNoCard(Guid tarefaId, bool adicionar, bool intervalo) => CardClicado?.Invoke(tarefaId, adicionar, intervalo);

    public void ClicarNoVazio() => VazioClicado?.Invoke();

    /// <summary>
    /// Refaz o quadro a partir do cronograma (depois de cada alteração e do Desfazer), reaproveitando as colunas e os cards que
    /// já existiam, para a tela não piscar.
    /// </summary>
    public void Atualizar(Cronograma cronograma, DateTime hoje, IReadOnlySet<Guid> selecionadas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var quadro = cronograma.Quadro;
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var edt = cronograma.NumeracaoEdt();
        var porColuna = quadro.CardsPorColuna();
        var vistos = new HashSet<Guid>();
        var colunas = new List<ColunaNoKanban>();
        foreach (var coluna in quadro.Colunas)
        {
            if (!_colunas.TryGetValue(coluna.Id, out var naTela))
                _colunas[coluna.Id] = naTela = new ColunaNoKanban(coluna.Id, () => Acoes);
            (naTela.Nome, naTela.Tipo, naTela.Cor, naTela.Limite) = (coluna.Nome, coluna.Tipo, coluna.Cor, coluna.Limite);
            (naTela.PodeIrParaAEsquerda, naTela.PodeIrParaADireita) = (colunas.Count > 0, colunas.Count < quadro.Colunas.Count - 1);
            var cards = new List<CardNoKanban>();
            foreach (var card in porColuna[coluna.Id])
            {
                if (!_cards.TryGetValue(card.TarefaId, out var cardNaTela))
                    _cards[card.TarefaId] = cardNaTela = new CardNoKanban(card.TarefaId);
                Preencher(cardNaTela, card, porId[card.TarefaId], cronograma, porId, edt, hoje);
                cardNaTela.Selecionado = selecionadas.Contains(card.TarefaId);
                cards.Add(cardNaTela);
                vistos.Add(card.TarefaId);
            }
            Reordenar(naTela.Cards, cards);
            naTela.Contagem = coluna.Limite is { } limite ? $"{cards.Count}/{limite}" : cards.Count.ToString(CultureInfo.CurrentCulture);
            naTela.PassouDoLimite = coluna.Limite is { } l && cards.Count > l;
            colunas.Add(naTela);
        }
        Reordenar(Colunas, colunas);
        foreach (var id in _cards.Keys.Where(id => !vistos.Contains(id)).ToList())
            _cards.Remove(id);
        foreach (var id in _colunas.Keys.Where(id => quadro.Coluna(id) is null).ToList())
            _colunas.Remove(id);
        SemCards = vistos.Count == 0;
    }

    /// <summary>Marca os cards das tarefas selecionadas (a mesma seleção da grade e do Gantt).</summary>
    public void DefinirSelecao(IReadOnlySet<Guid> selecionadas)
    {
        foreach (var (id, card) in _cards)
            card.Selecionado = selecionadas.Contains(id);
    }

    /// <summary>
    /// A tarefa que fica no lugar das que vão ser excluídas (E7, no quadro): o próximo card da coluna do primeiro excluído; se
    /// ele era o último, o de cima; nula se a coluna fica vazia.
    /// </summary>
    public Guid? Substituta(IReadOnlySet<Guid> excluidas)
    {
        foreach (var coluna in Colunas)
        {
            var cards = coluna.Cards.Select(c => c.TarefaId).ToList();
            var primeira = cards.FindIndex(excluidas.Contains);
            if (primeira < 0)
                continue;
            var depois = cards.Skip(primeira).FirstOrDefault(id => !excluidas.Contains(id));
            if (depois != Guid.Empty)
                return depois;
            var antes = cards.Take(primeira).LastOrDefault(id => !excluidas.Contains(id));
            return antes == Guid.Empty ? null : antes;
        }
        return null;
    }

    private static void Preencher(CardNoKanban naTela, CardDoQuadro card, Tarefa tarefa, Cronograma cronograma, IReadOnlyDictionary<Guid, Tarefa> porId,
        IReadOnlyDictionary<Guid, string> edt, DateTime hoje)
    {
        var quadro = cronograma.Quadro;
        var inicio = (tarefa.InicioMaisCedo ?? tarefa.DataInicio).Date;
        var termino = (tarefa.TerminoMaisCedo ?? tarefa.DataTermino).Date;
        naTela.Nome = tarefa.Nome;
        naTela.EhMarco = tarefa.EhMarco;
        naTela.Percentual = tarefa.PercentualConcluido;
        naTela.Resumo = tarefa.TarefaPaiId is { } pai ? porId[pai].Nome : null;
        naTela.Datas = tarefa.EhMarco ? Dia(termino, hoje) : $"{Dia(inicio, hoje)} – {Dia(termino, hoje)}";
        naTela.Atrasada = termino < hoje.Date && tarefa.PercentualConcluido < 100;
        naTela.Responsaveis = Responsaveis(tarefa, cronograma);
        naTela.Checklist = card.Checklist.Count == 0 ? null : $"{card.Checklist.Count(i => i.Feito)}/{card.Checklist.Count}";
        var etiquetas = card.Etiquetas.Select(id => quadro.Etiquetas.FirstOrDefault(e => e.Id == id)).OfType<EtiquetaDoQuadro>()
            .Select(e => new EtiquetaNoCard(e.Nome, e.Cor)).ToList();
        if (!naTela.Etiquetas.SequenceEqual(etiquetas))
        {
            naTela.Etiquetas.Clear();
            foreach (var etiqueta in etiquetas)
                naTela.Etiquetas.Add(etiqueta);
        }
        var periodo = tarefa.EhMarco ? $"marco em {termino:dd/MM/yyyy}" : $"{inicio:dd/MM/yyyy} a {termino:dd/MM/yyyy}";
        naTela.Dica = $"{edt.GetValueOrDefault(tarefa.Id, "")} · {tarefa.Nome} · {periodo} · {tarefa.PercentualConcluido}%"
                      + (naTela.Atrasada ? " · atrasada" : "");
    }

    // "12/11" no ano de hoje; "12/11/27" noutro ano.
    private static string Dia(DateTime data, DateTime hoje) =>
        data.ToString(data.Year == hoje.Year ? "dd/MM" : "dd/MM/yy", CultureInfo.InvariantCulture);

    // Os profissionais da tarefa: até dois nomes e "+N".
    private static string? Responsaveis(Tarefa tarefa, Cronograma cronograma)
    {
        var nomes = tarefa.Atribuicoes.Select(a => cronograma.Recursos.Obter(a.RecursoId))
            .Where(r => r?.Tipo == Core.Recursos.TipoDeRecurso.Profissional).Select(r => r!.Nome).Distinct().ToList();
        return nomes.Count switch
        {
            0 => null,
            <= 2 => string.Join(", ", nomes),
            _ => $"{nomes[0]}, {nomes[1]} +{nomes.Count - 2}",
        };
    }

    // Leva a coleção ao conteúdo e à ordem desejados mexendo só no que mudou.
    private static void Reordenar<T>(ObservableCollection<T> colecao, IReadOnlyList<T> desejada) where T : class
    {
        var manter = desejada.ToHashSet();
        for (var i = colecao.Count - 1; i >= 0; i--)
            if (!manter.Contains(colecao[i]))
                colecao.RemoveAt(i);
        for (var i = 0; i < desejada.Count; i++)
        {
            if (i < colecao.Count && ReferenceEquals(colecao[i], desejada[i]))
                continue;
            var atual = colecao.IndexOf(desejada[i]);
            if (atual >= 0)
                colecao.RemoveAt(atual);
            colecao.Insert(i, desejada[i]);
        }
    }
}
