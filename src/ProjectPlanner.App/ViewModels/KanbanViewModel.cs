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
public sealed partial class ColunaNoKanban(Guid id, Func<IAcoesDasColunas?>? acoes = null, Func<IAcoesDosCards?>? acoesDosCards = null) : ObservableObject
{
    public Guid Id { get; } = id;

    private IAcoesDasColunas? Acoes => acoes?.Invoke();

    /// <summary>Bloco 77: as tarefas de todos os cards da coluna (também os que o filtro esconde), na ordem do quadro.</summary>
    public IReadOnlyList<Guid> TodasAsTarefas { get; internal set; } = [];

    /// <summary>Bloco 77 (K5): "+ Adicionar card" no pé da coluna cria a tarefa no fim do cronograma, com o card nesta coluna.</summary>
    [RelayCommand]
    private Task AdicionarCard() => acoesDosCards?.Invoke()?.AdicionarCardAsync(Id) ?? Task.CompletedTask;

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

/// <summary>Bloco 78: uma raia na tela: o nome, quantos cards e uma célula por coluna.</summary>
public sealed partial class RaiaNoKanban(string chave) : ObservableObject
{
    public string Chave { get; } = chave;

    [ObservableProperty]
    public partial string Nome { get; set; } = "";

    /// <summary>"3 cards".</summary>
    [ObservableProperty]
    public partial string Contagem { get; set; } = "";

    /// <summary>Bloco 87: o andamento das tarefas da raia, ponderado pela duração ("40% concluído"); vazio sem cards.</summary>
    [ObservableProperty]
    public partial string Andamento { get; set; } = "";

    /// <summary>Bloco 87: o % das tarefas da raia (para a barrinha no cabeçalho dela).</summary>
    [ObservableProperty]
    public partial int Percentual { get; set; }

    /// <summary>
    /// Bloco 87: a raia recolhida mostra só uma linha fina, com quantos cards ela tem em cada coluna (dá para soltar cards nela).
    /// Fica assim enquanto o quadro estiver aberto; uma raia nova, com todos os cards concluídos, já aparece recolhida.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Seta), nameof(DicaDaSeta))]
    public partial bool Recolhida { get; set; }

    public string Seta => Recolhida ? "▸" : "▾";

    public string DicaDaSeta => Recolhida ? "Expandir a raia (mostrar os cards)" : "Recolher a raia (só a contagem de cada coluna)";

    [RelayCommand]
    private void Alternar() => Recolhida = !Recolhida;

    public ObservableCollection<CelulaNoKanban> Celulas { get; } = [];
}

/// <summary>Bloco 78: uma célula do quadro com raias: os cards da coluna que são da raia.</summary>
public sealed partial class CelulaNoKanban(ColunaNoKanban coluna, RaiaNoKanban raia) : ObservableObject
{
    public ColunaNoKanban Coluna { get; } = coluna;

    public RaiaNoKanban Raia { get; } = raia;

    public ObservableCollection<CardNoKanban> Cards { get; } = [];

    /// <summary>Bloco 87: na raia recolhida, quantos cards a célula tem ("2 cards"; vazia, "—").</summary>
    [ObservableProperty]
    public partial string Contagem { get; set; } = "—";
}

/// <summary>Bloco 78: uma opção do seletor de raias.</summary>
public sealed record OpcaoDeRaias(string Nome, ModoDasRaias Modo);

/// <summary>Bloco 77: uma opção de um filtro do quadro (Chave nula: todos).</summary>
public sealed record OpcaoDeFiltro(string Nome, string? Chave);

/// <summary>Bloco 77: um card arquivado (fora do quadro), para o menu Arquivados.</summary>
public sealed record CardArquivado(Guid TarefaId, string Nome);

/// <summary>
/// Bloco 75 (o plano "Delete, arrastar e Kanban", K1 a K5): o quadro Kanban, uma visão das mesmas tarefas da grade e do Gantt.
/// Os cards são das tarefas sem subtarefas; as colunas são do <see cref="QuadroDoProjeto"/>. O quadro não muda o cronograma
/// sozinho: clicar num card, soltar cards numa coluna e o Delete pedem ao <see cref="MainWindowViewModel"/>, que aplica as regras
/// (<see cref="RegrasDoQuadro"/>) e registra o Desfazer, como na grade e no Gantt. Bloco 77: o painel do card, os filtros e os
/// arquivados.
/// </summary>
public sealed partial class KanbanViewModel : ObservableObject
{
    private static readonly OpcaoDeFiltro TodosOsResponsaveis = new("Todos os responsáveis", null);
    private static readonly OpcaoDeFiltro TodasAsEtiquetas = new("Todas as etiquetas", null);
    private static readonly OpcaoDeFiltro TodosOsResumos = new("Todos os resumos", null);

    private readonly Dictionary<Guid, CardNoKanban> _cards = [];
    private readonly Dictionary<Guid, ColunaNoKanban> _colunas = [];
    private readonly Dictionary<string, RaiaNoKanban> _raias = [];
    private readonly Dictionary<(string Raia, Guid Coluna), CelulaNoKanban> _celulas = [];
    private Dictionary<Guid, string> _raiaDoCard = [];
    private Cronograma? _cronograma;
    private DateTime _hoje;
    private IReadOnlySet<Guid> _selecionadas = new HashSet<Guid>();
    private bool _refazendo;

    /// <summary>As colunas, da esquerda para a direita (com os cards que o filtro deixa ver).</summary>
    public ObservableCollection<ColunaNoKanban> Colunas { get; } = [];

    /// <summary>O quadro não tem nenhum card (o projeto não tem tarefas, ou só arquivadas).</summary>
    [ObservableProperty]
    public partial bool SemCards { get; private set; } = true;

    /// <summary>Bloco 77: há cards, mas nenhum passa nos filtros.</summary>
    [ObservableProperty]
    public partial bool NenhumNoFiltro { get; private set; }

    /// <summary>Bloco 76: quem aplica o que o menu das colunas pede (o programa).</summary>
    public IAcoesDasColunas? Acoes { get; set; }

    /// <summary>Bloco 77: quem aplica o que o painel e o menu dos cards pedem (o programa).</summary>
    public IAcoesDosCards? AcoesDosCards { get; set; }

    /// <summary>Bloco 77: a linha da tarefa na grade (o painel edita os campos dela).</summary>
    public Func<Guid, TarefaViewModel?>? LinhaDa { get; set; }

    /// <summary>Bloco 78: "Resumo da semana (IA)": o assistente escreve o resumo do quadro.</summary>
    [RelayCommand]
    private Task ResumoDaSemana() => AcoesDosCards?.ResumoDaSemanaAsync() ?? Task.CompletedTask;

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

    /// <summary>O que aconteceria ao soltar os cards na coluna, na lacuna dada entre todos os cards dela (sem mudar nada).</summary>
    public Func<IReadOnlyList<Guid>, Guid, int, PlanoDoQuadro>? PreverMovimento { get; set; }

    /// <summary>Solta os cards na coluna, na lacuna dada entre todos os cards dela; devolve se mudou algo.</summary>
    public Func<IReadOnlyList<Guid>, Guid, int, bool>? Mover { get; set; }

    /// <summary>As tarefas dos cards que aparecem, na ordem do quadro (coluna por coluna): o intervalo do Shift+clique.</summary>
    public IReadOnlyList<Guid> OrdemDosCards => [.. Colunas.SelectMany(c => c.Cards).Select(c => c.TarefaId)];

    /// <summary>O card da tarefa; nulo se ela não está no quadro (ou o filtro o esconde).</summary>
    public CardNoKanban? CardDe(Guid tarefaId) => _cards.GetValueOrDefault(tarefaId);

    /// <summary>A coluna em que o card está; nula se ele não está no quadro.</summary>
    public ColunaNoKanban? ColunaDe(Guid tarefaId) => Colunas.FirstOrDefault(c => c.Cards.Any(k => k.TarefaId == tarefaId));

    public void ClicarNoCard(Guid tarefaId, bool adicionar, bool intervalo) => CardClicado?.Invoke(tarefaId, adicionar, intervalo);

    public void ClicarNoVazio() => VazioClicado?.Invoke();

    // ---- Bloco 77: soltar com filtro ----

    /// <summary>
    /// A lacuna entre todos os cards da coluna que corresponde à lacuna <paramref name="lacunaVisivel"/> entre os que aparecem (com
    /// filtro, cards escondidos ficam entre os visíveis): antes do card visível daquela posição; depois do último visível.
    /// </summary>
    public int LacunaEntreTodos(Guid colunaId, int lacunaVisivel)
    {
        if (!_colunas.TryGetValue(colunaId, out var coluna))
            return lacunaVisivel;
        var todas = coluna.TodasAsTarefas.ToList();
        if (coluna.Cards.Count == 0)
            return todas.Count;
        return lacunaVisivel < coluna.Cards.Count
            ? Math.Max(0, todas.IndexOf(coluna.Cards[Math.Max(0, lacunaVisivel)].TarefaId))
            : todas.IndexOf(coluna.Cards[^1].TarefaId) + 1;
    }

    /// <summary>O plano de soltar na lacuna entre os cards que aparecem (a tela usa esta).</summary>
    public PlanoDoQuadro? PreverSoltar(IReadOnlyList<Guid> tarefas, Guid colunaId, int lacunaVisivel) =>
        PreverMovimento?.Invoke(tarefas, colunaId, LacunaEntreTodos(colunaId, lacunaVisivel));

    /// <summary>Solta na lacuna entre os cards que aparecem (a tela usa esta).</summary>
    public bool Soltar(IReadOnlyList<Guid> tarefas, Guid colunaId, int lacunaVisivel) =>
        Mover?.Invoke(tarefas, colunaId, LacunaEntreTodos(colunaId, lacunaVisivel)) ?? false;

    // ---- Bloco 78 (K6): as raias ----

    public IReadOnlyList<OpcaoDeRaias> OpcoesDeRaias { get; } =
    [
        new("Sem raias", ModoDasRaias.Nenhuma), new("Raias por resumo", ModoDasRaias.PorResumo), new("Raias por responsável", ModoDasRaias.PorResponsavel),
    ];

    /// <summary>Como o quadro agrupa os cards (o seletor no alto do quadro); vem do projeto e vai para ele.</summary>
    [ObservableProperty]
    public partial OpcaoDeRaias? Raias { get; set; }

    /// <summary>O quadro está em raias (a tela troca as colunas pela grade de raias × colunas).</summary>
    public bool ComRaias => Raias?.Modo is ModoDasRaias.PorResumo or ModoDasRaias.PorResponsavel;

    /// <summary>As raias, de cima para baixo (vazio sem raias).</summary>
    public ObservableCollection<RaiaNoKanban> RaiasNaTela { get; } = [];

    /// <summary>Muda o modo das raias do projeto (um passo do Desfazer).</summary>
    public Action<ModoDasRaias>? DefinirModoDasRaias { get; set; }

    /// <summary>Leva os cards para outra raia (muda o resumo ou o responsável) e para a coluna, na lacuna entre todos os cards dela.</summary>
    public Func<IReadOnlyList<Guid>, string, Guid, int, bool>? MoverParaRaia { get; set; }

    partial void OnRaiasChanged(OpcaoDeRaias? value)
    {
        OnPropertyChanged(nameof(ComRaias));
        if (!_refazendo && value is { } opcao && _cronograma is { } cronograma && opcao.Modo != cronograma.Quadro.Raias)
            DefinirModoDasRaias?.Invoke(opcao.Modo);
    }

    /// <summary>A raia em que o card está (a chave); nula sem raias.</summary>
    public string? RaiaDoCard(Guid tarefaId) => _raiaDoCard.GetValueOrDefault(tarefaId);

    /// <summary>Bloco 87: recolhe todas as raias (só a linha com as contagens de cada uma).</summary>
    [RelayCommand]
    private void RecolherRaias()
    {
        foreach (var raia in RaiasNaTela)
            raia.Recolhida = true;
    }

    /// <summary>Bloco 87: expande todas as raias.</summary>
    [RelayCommand]
    private void ExpandirRaias()
    {
        foreach (var raia in RaiasNaTela)
            raia.Recolhida = false;
    }

    // A lacuna entre os cards da coluna que aparecem, correspondente à lacuna entre os cards da célula.
    private static int LacunaNaColuna(CelulaNoKanban celula, int lacunaNaCelula)
    {
        var visiveis = celula.Coluna.Cards;
        if (celula.Cards.Count == 0)
            return visiveis.Count;
        return lacunaNaCelula < celula.Cards.Count
            ? Math.Max(0, visiveis.IndexOf(celula.Cards[Math.Max(0, lacunaNaCelula)]))
            : visiveis.IndexOf(celula.Cards[^1]) + 1;
    }

    /// <summary>O plano de soltar na célula: na mesma raia, o da coluna; noutra, também muda o resumo ou o responsável.</summary>
    public PlanoDoQuadro? PreverNaCelula(IReadOnlyList<Guid> tarefas, CelulaNoKanban celula, int lacunaNaCelula)
    {
        ArgumentNullException.ThrowIfNull(tarefas);
        ArgumentNullException.ThrowIfNull(celula);
        var plano = PreverSoltar(tarefas, celula.Coluna.Id, LacunaNaColuna(celula, lacunaNaCelula));
        if (plano is not { Valido: true } || tarefas.All(id => RaiaDoCard(id) == celula.Raia.Chave))
            return plano;
        var raia = $"Mover para a raia \"{celula.Raia.Nome}\"";
        return plano with
        {
            MudaAlgo = true,
            Dica = plano.Tarefas.Any(id => ColunaDe(id) != celula.Coluna) ? $"{raia} · {plano.Dica}" : raia,
        };
    }

    /// <summary>Solta na célula: na mesma raia, como numa coluna; noutra, muda também o resumo ou o responsável.</summary>
    public bool SoltarNaCelula(IReadOnlyList<Guid> tarefas, CelulaNoKanban celula, int lacunaNaCelula)
    {
        ArgumentNullException.ThrowIfNull(tarefas);
        ArgumentNullException.ThrowIfNull(celula);
        var lacuna = LacunaNaColuna(celula, lacunaNaCelula);
        if (tarefas.All(id => RaiaDoCard(id) == celula.Raia.Chave))
            return Soltar(tarefas, celula.Coluna.Id, lacuna);
        return MoverParaRaia?.Invoke(tarefas, celula.Raia.Chave, celula.Coluna.Id, LacunaEntreTodos(celula.Coluna.Id, lacuna)) ?? false;
    }

    // As raias × colunas: cada célula com os cards da coluna (os que aparecem) que são da raia.
    private void MontarAsRaias(Cronograma cronograma, IReadOnlyDictionary<Guid, Tarefa> porId, IReadOnlyList<ColunaNoKanban> colunas)
    {
        var modo = cronograma.Quadro.Raias;
        Raias = OpcoesDeRaias.First(o => o.Modo == modo);
        if (modo == ModoDasRaias.Nenhuma)
        {
            _raiaDoCard = [];
            RaiasNaTela.Clear();
            _raias.Clear();
            _celulas.Clear();
            return;
        }
        _raiaDoCard = colunas.SelectMany(c => c.TodasAsTarefas).ToDictionary(id => id, id => RegrasDasRaias.RaiaDe(cronograma, modo, porId[id]));
        var raias = new List<RaiaNoKanban>();
        var todasAsChaves = new HashSet<string>(); // também as escondidas pelo filtro: elas guardam se estavam recolhidas
        foreach (var raia in RegrasDasRaias.Raias(cronograma, modo))
        {
            todasAsChaves.Add(raia.Chave);
            var nova = !_raias.TryGetValue(raia.Chave, out var naTela);
            if (nova)
                _raias[raia.Chave] = naTela = new RaiaNoKanban(raia.Chave);
            naTela!.Nome = raia.Nome;
            var celulas = new List<CelulaNoKanban>();
            var cards = 0;
            foreach (var coluna in colunas)
            {
                if (!_celulas.TryGetValue((raia.Chave, coluna.Id), out var celula))
                    _celulas[(raia.Chave, coluna.Id)] = celula = new CelulaNoKanban(coluna, naTela);
                var daRaia = coluna.Cards.Where(k => _raiaDoCard.GetValueOrDefault(k.TarefaId) == raia.Chave).ToList();
                Reordenar(celula.Cards, daRaia);
                celula.Contagem = daRaia.Count == 0 ? "—" : daRaia.Count == 1 ? "1 card" : $"{daRaia.Count} cards";
                cards += daRaia.Count;
                celulas.Add(celula);
            }
            Reordenar(naTela.Celulas, celulas);
            naTela.Contagem = cards == 1 ? "1 card" : $"{cards} cards";
            // Bloco 87: o andamento da raia, por todas as tarefas dela (com ou sem filtro), ponderado pela duração.
            var tarefas = colunas.SelectMany(c => c.TodasAsTarefas).Where(id => _raiaDoCard.GetValueOrDefault(id) == raia.Chave).Select(id => porId[id]).ToList();
            var pesos = tarefas.Sum(t => (long)t.Duracao);
            naTela.Percentual = tarefas.Count == 0 ? 0
                : (int)Math.Round(pesos == 0 ? tarefas.Average(t => t.PercentualConcluido) : tarefas.Sum(t => (double)t.Duracao * t.PercentualConcluido) / pesos,
                    MidpointRounding.AwayFromZero);
            naTela.Andamento = tarefas.Count == 0 ? "" : $"{naTela.Percentual}% concluído";
            // Bloco 87: uma raia nova com tudo concluído já vem recolhida (as etapas prontas não tomam o quadro).
            if (nova)
                naTela.Recolhida = tarefas.Count > 0 && tarefas.All(t => t.PercentualConcluido == 100);
            // Com um filtro, as raias sem nenhum card que passe nele saem (voltam ao limpar o filtro).
            if (HaFiltro && cards == 0)
                continue;
            raias.Add(naTela);
        }
        Reordenar(RaiasNaTela, raias);
        var chaves = todasAsChaves;
        foreach (var chave in _raias.Keys.Where(c => !chaves.Contains(c)).ToList())
            _raias.Remove(chave);
        foreach (var chave in _celulas.Keys.Where(c => !chaves.Contains(c.Raia) || !_colunas.ContainsKey(c.Coluna)).ToList())
            _celulas.Remove(chave);
    }

    // ---- Bloco 77: o painel do card ----

    /// <summary>O painel lateral do card aberto; nulo com ele fechado.</summary>
    [ObservableProperty]
    public partial PainelDoCard? Painel { get; private set; }

    /// <summary>K4: duplo clique num card abre o painel com todos os campos, a descrição e o checklist.</summary>
    public void AbrirCard(Guid tarefaId)
    {
        if (_cronograma?.Quadro.CardDe(tarefaId) is null)
            return;
        Painel = new PainelDoCard(tarefaId, AcoesDosCards, () => Painel = null);
        AtualizarPainel();
    }

    [RelayCommand]
    private void FecharCard() => Painel = null;

    private void AtualizarPainel()
    {
        if (Painel is not { } painel || _cronograma is not { } cronograma)
            return;
        var tarefa = cronograma.Tarefas.FirstOrDefault(t => t.Id == painel.TarefaId);
        if (tarefa is null || tarefa.EhResumo || cronograma.Quadro.CardDe(tarefa.Id) is not { } card || LinhaDa?.Invoke(tarefa.Id) is not { } linha)
        {
            Painel = null; // a tarefa saiu, ou virou resumo
            return;
        }
        painel.Atualizar(linha, card, cronograma, Responsaveis(tarefa, cronograma, todos: true));
    }

    // ---- Bloco 77: os arquivados ----

    /// <summary>Os cards arquivados (fora do quadro), na ordem do cronograma.</summary>
    public ObservableCollection<CardArquivado> Arquivados { get; } = [];

    /// <summary>"Arquivados (3)".</summary>
    [ObservableProperty]
    public partial string TextoDosArquivados { get; private set; } = "Arquivados (0)";

    // ---- Bloco 77 (K5): os filtros ----

    /// <summary>Busca por texto no nome e na descrição (sem diferença de maiúsculas e acentos).</summary>
    [ObservableProperty]
    public partial string Busca { get; set; } = "";

    [ObservableProperty]
    public partial OpcaoDeFiltro? FiltroResponsavel { get; set; }

    [ObservableProperty]
    public partial OpcaoDeFiltro? FiltroEtiqueta { get; set; }

    [ObservableProperty]
    public partial OpcaoDeFiltro? FiltroResumo { get; set; }

    /// <summary>Só as atrasadas (término antes de hoje e abaixo de 100%).</summary>
    [ObservableProperty]
    public partial bool SoAtrasadas { get; set; }

    /// <summary>Só as que vencem nesta semana (término de hoje até domingo, abaixo de 100%).</summary>
    [ObservableProperty]
    public partial bool SoDestaSemana { get; set; }

    public ObservableCollection<OpcaoDeFiltro> OpcoesDeResponsavel { get; } = [TodosOsResponsaveis];

    public ObservableCollection<OpcaoDeFiltro> OpcoesDeEtiqueta { get; } = [TodasAsEtiquetas];

    public ObservableCollection<OpcaoDeFiltro> OpcoesDeResumo { get; } = [TodosOsResumos];

    /// <summary>As etiquetas do projeto (o menu Etiquetas).</summary>
    public IReadOnlyList<EtiquetaDoQuadro> EtiquetasDoProjeto { get; private set; } = [];

    /// <summary>Algum filtro está ligado.</summary>
    public bool HaFiltro => !string.IsNullOrWhiteSpace(Busca) || FiltroResponsavel?.Chave is not null || FiltroEtiqueta?.Chave is not null
                            || FiltroResumo?.Chave is not null || SoAtrasadas || SoDestaSemana;

    [RelayCommand]
    private void LimparFiltros()
    {
        _refazendo = true;
        try
        {
            (Busca, FiltroResponsavel, FiltroEtiqueta, FiltroResumo, SoAtrasadas, SoDestaSemana) =
                ("", TodosOsResponsaveis, TodasAsEtiquetas, TodosOsResumos, false, false);
        }
        finally
        {
            _refazendo = false;
        }
        Refazer();
    }

    partial void OnBuscaChanged(string value) => Refazer();

    partial void OnFiltroResponsavelChanged(OpcaoDeFiltro? value) => Refazer();

    partial void OnFiltroEtiquetaChanged(OpcaoDeFiltro? value) => Refazer();

    partial void OnFiltroResumoChanged(OpcaoDeFiltro? value) => Refazer();

    partial void OnSoAtrasadasChanged(bool value) => Refazer();

    partial void OnSoDestaSemanaChanged(bool value) => Refazer();

    /// <summary>
    /// Refaz o quadro a partir do cronograma (depois de cada alteração e do Desfazer), reaproveitando as colunas e os cards que
    /// já existiam, para a tela não piscar.
    /// </summary>
    public void Atualizar(Cronograma cronograma, DateTime hoje, IReadOnlySet<Guid> selecionadas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        (_cronograma, _hoje, _selecionadas) = (cronograma, hoje.Date, selecionadas);
        AtualizarOpcoes(cronograma);
        Refazer();
    }

    private void Refazer()
    {
        if (_refazendo || _cronograma is not { } cronograma)
            return;
        _refazendo = true;
        try
        {
            Montar(cronograma);
        }
        finally
        {
            _refazendo = false;
        }
        OnPropertyChanged(nameof(HaFiltro));
        AtualizarPainel();
    }

    private void Montar(Cronograma cronograma)
    {
        var quadro = cronograma.Quadro;
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var edt = cronograma.NumeracaoEdt();
        var porColuna = quadro.CardsPorColuna();
        var passa = Filtro(cronograma, porId);
        var vistos = new HashSet<Guid>();
        var total = 0;
        var colunas = new List<ColunaNoKanban>();
        foreach (var coluna in quadro.Colunas)
        {
            if (!_colunas.TryGetValue(coluna.Id, out var naTela))
                _colunas[coluna.Id] = naTela = new ColunaNoKanban(coluna.Id, () => Acoes, () => AcoesDosCards);
            (naTela.Nome, naTela.Tipo, naTela.Cor, naTela.Limite) = (coluna.Nome, coluna.Tipo, coluna.Cor, coluna.Limite);
            (naTela.PodeIrParaAEsquerda, naTela.PodeIrParaADireita) = (colunas.Count > 0, colunas.Count < quadro.Colunas.Count - 1);
            var todos = porColuna[coluna.Id];
            naTela.TodasAsTarefas = [.. todos.Select(c => c.TarefaId)];
            total += todos.Count;
            var cards = new List<CardNoKanban>();
            foreach (var card in todos.Where(c => passa(porId[c.TarefaId], c)))
            {
                if (!_cards.TryGetValue(card.TarefaId, out var cardNaTela))
                    _cards[card.TarefaId] = cardNaTela = new CardNoKanban(card.TarefaId);
                Preencher(cardNaTela, card, porId[card.TarefaId], cronograma, porId, edt, _hoje);
                cardNaTela.Selecionado = _selecionadas.Contains(card.TarefaId);
                cards.Add(cardNaTela);
                vistos.Add(card.TarefaId);
            }
            Reordenar(naTela.Cards, cards);
            naTela.Contagem = HaFiltro ? $"{cards.Count} de {todos.Count}"
                : coluna.Limite is { } limite ? $"{todos.Count}/{limite}" : todos.Count.ToString(CultureInfo.CurrentCulture);
            naTela.PassouDoLimite = coluna.Limite is { } l && todos.Count > l; // o limite (WIP) conta todos, com ou sem filtro
            colunas.Add(naTela);
        }
        Reordenar(Colunas, colunas);
        MontarAsRaias(cronograma, porId, colunas); // Bloco 78
        foreach (var id in _cards.Keys.Where(id => !vistos.Contains(id)).ToList())
            _cards.Remove(id);
        foreach (var id in _colunas.Keys.Where(id => quadro.Coluna(id) is null).ToList())
            _colunas.Remove(id);
        SemCards = total == 0;
        NenhumNoFiltro = total > 0 && vistos.Count == 0;

        var arquivados = cronograma.Tarefas.Where(t => !t.EhResumo && quadro.CardDe(t.Id) is { Arquivado: true })
            .Select(t => new CardArquivado(t.Id, t.Nome)).ToList();
        if (!Arquivados.SequenceEqual(arquivados))
        {
            Arquivados.Clear();
            foreach (var a in arquivados)
                Arquivados.Add(a);
        }
        TextoDosArquivados = $"Arquivados ({arquivados.Count})";
    }

    // As opções dos filtros (os responsáveis, as etiquetas e os resumos do projeto), mantendo a escolhida se ela continua lá.
    private void AtualizarOpcoes(Cronograma cronograma)
    {
        var responsaveis = cronograma.Tarefas.Where(t => !t.EhResumo).SelectMany(t => NomesDosProfissionais(t, cronograma)).Distinct()
            .Order(StringComparer.CurrentCulture).Select(n => new OpcaoDeFiltro(n, n));
        EtiquetasDoProjeto = cronograma.Quadro.Etiquetas;
        var etiquetas = cronograma.Quadro.Etiquetas.Select(e => new OpcaoDeFiltro(e.Nome, e.Id.ToString()));
        var edt = cronograma.NumeracaoEdt();
        var resumos = cronograma.Tarefas.Where(t => t.EhResumo).Select(t => new OpcaoDeFiltro($"{edt.GetValueOrDefault(t.Id, "")} {t.Nome}", t.Id.ToString()));
        _refazendo = true;
        try
        {
            FiltroResponsavel = Trocar(OpcoesDeResponsavel, TodosOsResponsaveis, responsaveis, FiltroResponsavel);
            FiltroEtiqueta = Trocar(OpcoesDeEtiqueta, TodasAsEtiquetas, etiquetas, FiltroEtiqueta);
            FiltroResumo = Trocar(OpcoesDeResumo, TodosOsResumos, resumos, FiltroResumo);
        }
        finally
        {
            _refazendo = false;
        }
    }

    private static OpcaoDeFiltro Trocar(ObservableCollection<OpcaoDeFiltro> opcoes, OpcaoDeFiltro todos, IEnumerable<OpcaoDeFiltro> novas,
        OpcaoDeFiltro? escolhida)
    {
        var lista = novas.Prepend(todos).ToList();
        if (!opcoes.SequenceEqual(lista))
        {
            opcoes.Clear();
            foreach (var opcao in lista)
                opcoes.Add(opcao);
        }
        return opcoes.FirstOrDefault(o => o.Chave is not null && o.Chave == escolhida?.Chave) ?? opcoes[0];
    }

    // O que passa nos filtros ligados.
    private Func<Tarefa, CardDoQuadro, bool> Filtro(Cronograma cronograma, IReadOnlyDictionary<Guid, Tarefa> porId)
    {
        if (!HaFiltro)
            return (_, _) => true;
        var busca = Busca.Trim();
        var comparacao = CultureInfo.CurrentCulture.CompareInfo;
        bool Contem(string? texto) => texto is not null && comparacao.IndexOf(texto, busca, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        var dentroDoResumo = FiltroResumo?.Chave is { } resumo && Guid.TryParse(resumo, out var resumoId)
            ? cronograma.DescendentesDe(resumoId).Select(t => t.Id).ToHashSet()
            : null;
        var etiqueta = FiltroEtiqueta?.Chave is { } e && Guid.TryParse(e, out var etiquetaId) ? etiquetaId : (Guid?)null;
        var fimDaSemana = _hoje.AddDays(((int)DayOfWeek.Sunday - (int)_hoje.DayOfWeek + 7) % 7);
        return (tarefa, card) =>
        {
            var termino = (tarefa.TerminoMaisCedo ?? tarefa.DataTermino).Date;
            return (busca.Length == 0 || Contem(tarefa.Nome) || Contem(tarefa.Notas))
                   && (FiltroResponsavel?.Chave is not { } responsavel || NomesDosProfissionais(tarefa, cronograma).Contains(responsavel))
                   && (etiqueta is not { } id || card.Etiquetas.Contains(id))
                   && (dentroDoResumo is null || dentroDoResumo.Contains(tarefa.Id))
                   && (!SoAtrasadas || (termino < _hoje && tarefa.PercentualConcluido < 100))
                   && (!SoDestaSemana || (termino >= _hoje && termino <= fimDaSemana && tarefa.PercentualConcluido < 100));
        };
    }

    /// <summary>Marca os cards das tarefas selecionadas (a mesma seleção da grade e do Gantt).</summary>
    public void DefinirSelecao(IReadOnlySet<Guid> selecionadas)
    {
        _selecionadas = selecionadas;
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
        naTela.Responsaveis = Responsaveis(tarefa, cronograma, todos: false);
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
                      + (naTela.Atrasada ? " · atrasada" : "") + " (duplo clique abre o card)";
    }

    // "12/11" no ano de hoje; "12/11/27" noutro ano.
    private static string Dia(DateTime data, DateTime hoje) =>
        data.ToString(data.Year == hoje.Year ? "dd/MM" : "dd/MM/yy", CultureInfo.InvariantCulture);

    private static List<string> NomesDosProfissionais(Tarefa tarefa, Cronograma cronograma) =>
        [.. tarefa.Atribuicoes.Select(a => cronograma.Recursos.Obter(a.RecursoId))
            .Where(r => r?.Tipo == Core.Recursos.TipoDeRecurso.Profissional).Select(r => r!.Nome).Distinct()];

    // Os profissionais da tarefa: no card, até dois nomes e "+N"; no painel, todos.
    private static string? Responsaveis(Tarefa tarefa, Cronograma cronograma, bool todos)
    {
        var nomes = NomesDosProfissionais(tarefa, cronograma);
        return nomes.Count switch
        {
            0 => null,
            _ when todos || nomes.Count <= 2 => string.Join(", ", nomes),
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
