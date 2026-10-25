using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 58: o filtro da lista de OS.</summary>
public enum FiltroDasOrdens
{
    Todas,
    Abertas,
    Vencidas,
    Encerradas,
}

/// <summary>Uma OS na lista da tela.</summary>
public sealed record OrdemNaLista(OrdemDeServico Ordem, bool Vencida)
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public string Codigo => Ordem.Codigo;

    public string Titulo => Ordem.Dados.Titulo;

    public string Situacao => Vencida ? $"{TextosDaOrdem.Texto(Ordem.Situacao)} · vencida" : TextosDaOrdem.Texto(Ordem.Situacao);

    /// <summary>"Construtora Silva · prazo 30/10 · R$ 4.500,00".</summary>
    public string Detalhe => string.Join(" · ", new[]
    {
        Ordem.Dados.Executante ?? "sem executante",
        Ordem.Dados.Prazo is { } prazo ? $"prazo {prazo.ToString("dd/MM", Brasil)}" : null,
        Moeda.ComSimbolo(Ordem.Total),
    }.OfType<string>());

    public bool Encerrada => Ordem.Situacao is SituacaoDaOrdem.Conferida or SituacaoDaOrdem.Paga or SituacaoDaOrdem.Cancelada;
}

/// <summary>Um item na ficha (os números como a pessoa digitou, lidos ao salvar).</summary>
public sealed partial class ItemNaFicha(Guid? tarefaId, string tarefa) : ObservableObject
{
    public Guid? TarefaId { get; } = tarefaId;

    /// <summary>"2.3" (a EDT da tarefa de onde veio), ou vazio num item avulso.</summary>
    public string Tarefa { get; } = tarefa;

    [ObservableProperty] public partial string Descricao { get; set; } = "";
    [ObservableProperty] public partial string Unidade { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Total))]
    public partial string Quantidade { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Total))]
    public partial string Preco { get; set; } = "";

    /// <summary>O total como está digitado (vazio se ainda não é um número).</summary>
    public string Total
    {
        get
        {
            try
            {
                return Moeda.LerNumero(Quantidade) is { } q && Moeda.Ler(Preco) is { } p ? Moeda.Formatar(Math.Round(q * p, 2, MidpointRounding.AwayFromZero)) : "";
            }
            catch (FormatException)
            {
                return "";
            }
        }
    }

    public static ItemNaFicha De(ItemDaOrdem item, string tarefa) => new(item.TarefaId, tarefa)
    {
        Descricao = item.Descricao, Unidade = item.Unidade, Quantidade = Moeda.FormatarQuantidade(item.Quantidade), Preco = Moeda.Formatar(item.PrecoUnitario),
    };
}

/// <summary>Um ponto da lista de verificação na ficha.</summary>
public sealed partial class VerificacaoNaFicha(string texto, bool feito) : ObservableObject
{
    public string Texto { get; } = texto;

    [ObservableProperty] public partial bool Feito { get; set; } = feito;
}

/// <summary>Uma tarefa para pôr na OS (com o recuo da EDT).</summary>
public sealed partial class TarefaParaEscolher(Guid id, string texto, int nivel, bool ehResumo) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Texto { get; } = texto;

    public bool EhResumo { get; } = ehResumo;

    public Thickness Recuo { get; } = new(nivel * 16, 0, 0, 0);

    [ObservableProperty] public partial bool Marcada { get; set; }
}

/// <summary>
/// Bloco 58: Projeto > Ordens de serviço…: a lista das OS da obra (com o filtro e os números: em andamento, vencidas e o valor
/// em aberto) e a ficha da escolhida: em rascunho, edita-se tudo (as tarefas, os itens com o preço do orçamento, o executante,
/// as datas, a verificação); depois, os botões levam a OS pelo fluxo (emitir, aceitar, iniciar, concluir, conferir ou
/// reprovar, cancelar), cada passo no histórico e no Desfazer. Gera o PDF para assinar.
/// </summary>
public sealed partial class OrdensDeServicoViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Action<string, bool> _mudou;
    private readonly Func<OrdemDeServico, bool, Task<int>>? _publicar;
    private readonly Func<Core.Relatorios.CabecalhoDoRelatorio> _cabecalho;
    private readonly Func<RelatorioFotograficoViewModel>? _relatorioFotografico;
    private readonly IReadOnlyDictionary<Guid, string> _nomes;
    private readonly IReadOnlyDictionary<Guid, string> _edt;
    private bool _carregando;
    private bool _fichaMudou;

    /// <param name="mudou">As OS mudaram, com o nome da mudança para o Desfazer e se as tarefas mudaram (para recalcular).</param>
    /// <param name="tarefaSelecionada">A tarefa selecionada na grid: a OS nova começa com ela.</param>
    /// <param name="publicar">
    /// Bloco 59: põe a OS nos grupos da obra no Telegram (com os valores ou não) e diz em quantos; nulo quando a obra não está no
    /// Telegram.
    /// </param>
    public OrdensDeServicoViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, Action<string, bool> mudou,
        Guid? tarefaSelecionada = null, Func<OrdemDeServico, bool, Task<int>>? publicar = null, Guid? ordemInicial = null, bool criarNova = false,
        Func<Core.Relatorios.CabecalhoDoRelatorio>? cabecalho = null, Func<RelatorioFotograficoViewModel>? relatorioFotografico = null)
    {
        _relatorioFotografico = relatorioFotografico;
        _publicar = publicar;
        _cabecalho = cabecalho ?? (() => new Core.Relatorios.CabecalhoDoRelatorio(cronograma.Nome, cronograma.Obra, null, null, null));
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(mudou);
        (_cronograma, _dialogos, _relogio, _mudou) = (cronograma, dialogos, relogio, mudou);
        _edt = cronograma.NumeracaoEdt();
        _nomes = cronograma.Tarefas.ToDictionary(t => t.Id, t => $"{_edt[t.Id]} {t.Nome}");
        TarefaSelecionadaNaGrid = tarefaSelecionada;
        Tarefas = [.. cronograma.Tarefas.Select(t => new TarefaParaEscolher(t.Id, _nomes[t.Id], cronograma.NivelDe(t.Id), t.EhResumo))];
        Sugestoes = [.. cronograma.Recursos.Lista.Where(r => r.Tipo == Core.Recursos.TipoDeRecurso.Profissional).Select(r => r.Nome)
            .Concat(cronograma.Recursos.Lista.Select(r => r.Fornecedor).OfType<string>())
            .Concat(cronograma.OrdensDeServico.Todas.Select(o => o.Dados.Executante).OfType<string>())
            .Where(n => n != Core.Orcamento.AplicacaoDeSugestoes.OrigemDaEquipe).Distinct().Order()];
        FiltroEscolhido = Filtros[0];
        PropertyChanged += FichaMudou;
        Atualizar();
        Selecionada = Lista.FirstOrDefault(l => l.Ordem.Id == ordemInicial) ?? Lista.FirstOrDefault(l => !l.Encerrada) ?? Lista.FirstOrDefault();
        if (criarNova)
            NovaOrdem(); // Bloco 64: o botão "Nova OS" da barra
    }

    /// <summary>Bloco 66: o relatório fotográfico da OS escolhida (as fotos das tarefas dela, no período dela).</summary>
    public bool TemRelatorioFotografico => _relatorioFotografico is not null && Ordem is { Situacao: not SituacaoDaOrdem.Rascunho };

    public RelatorioFotograficoViewModel? RelatorioFotograficoDaOrdem()
    {
        if (_relatorioFotografico is null || Ordem is not { } o)
            return null;
        SalvarSePreciso();
        return _relatorioFotografico().ParaAOrdem(o.Id);
    }

    /// <summary>Bloco 64: o relatório das OS (a janela abre a partir daqui, por cima desta).</summary>
    public RelatorioDasOrdensViewModel NovoRelatorio()
    {
        SalvarSePreciso();
        return new RelatorioDasOrdensViewModel(_cronograma, _dialogos, _relogio, _cabecalho);
    }

    public string Titulo => $"Ordens de serviço — {_cronograma.Nome}";

    public Guid? TarefaSelecionadaNaGrid { get; }

    private DateTime Agora => _relogio.GetLocalNow().DateTime;

    private DateOnly Hoje => DateOnly.FromDateTime(Agora);

    // ---- A lista ----

    public IReadOnlyList<Escolha<FiltroDasOrdens>> Filtros { get; } =
    [
        new(FiltroDasOrdens.Todas, "Todas"),
        new(FiltroDasOrdens.Abertas, "Abertas (rascunho e em andamento)"),
        new(FiltroDasOrdens.Vencidas, "Vencidas"),
        new(FiltroDasOrdens.Encerradas, "Encerradas (conferidas, pagas e canceladas)"),
    ];

    [ObservableProperty] public partial Escolha<FiltroDasOrdens> FiltroEscolhido { get; set; }

    partial void OnFiltroEscolhidoChanged(Escolha<FiltroDasOrdens> value) => Atualizar();

    public ObservableCollection<OrdemNaLista> Lista { get; } = [];

    /// <summary>"2 em andamento · 1 vencida · R$ 8.300,00 em aberto · 1 rascunho".</summary>
    [ObservableProperty] public partial string Resumo { get; private set; } = "";

    public bool SemOrdens => _cronograma.OrdensDeServico.Vazio;

    public const string TextoSemOrdens =
        "Nenhuma OS ainda. Selecione uma tarefa (ou uma etapa) na grid e clique em Nova OS: os itens, as quantidades, os preços e as datas vêm do cronograma e do orçamento.";

    [ObservableProperty] public partial OrdemNaLista? Selecionada { get; set; }

    /// <summary>Bloco 62: "Prazos: 3 de 4 concluídas no prazo (75%); atraso médio de 2 dias".</summary>
    public string Prazos { get; private set; } = "";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    private void Atualizar()
    {
        var todas = _cronograma.OrdensDeServico.Todas;
        var filtradas = todas.Select(o => new OrdemNaLista(o, o.Vencida(Hoje))).Where(l => FiltroEscolhido.Valor switch
        {
            FiltroDasOrdens.Abertas => l.Ordem.Situacao == SituacaoDaOrdem.Rascunho || TextosDaOrdem.EmAndamento(l.Ordem.Situacao),
            FiltroDasOrdens.Vencidas => l.Vencida,
            FiltroDasOrdens.Encerradas => l.Encerrada,
            _ => true,
        }).ToList();
        var id = Selecionada?.Ordem.Id;
        _carregando = true;
        try
        {
            Lista.Clear();
            foreach (var l in filtradas)
                Lista.Add(l);
        }
        finally
        {
            _carregando = false;
        }
        var andamento = todas.Count(o => TextosDaOrdem.EmAndamento(o.Situacao));
        var vencidas = todas.Count(o => o.Vencida(Hoje));
        var rascunhos = todas.Count(o => o.Situacao == SituacaoDaOrdem.Rascunho);
        // Bloco 60: a pagar é o medido e não pago; por medir, o resto do contratado das OS que estão na rua.
        var validas = todas.Where(o => o.Situacao != SituacaoDaOrdem.Cancelada).ToList();
        var aPagar = validas.Sum(o => o.APagar);
        var porMedir = validas.Where(o => TextosDaOrdem.EmAndamento(o.Situacao)).Sum(o => o.PorMedir);
        Resumo = todas.Count == 0 ? "" : string.Join(" · ", new[]
        {
            Plural(andamento, "em andamento", "em andamento"),
            vencidas == 0 ? null : Plural(vencidas, "vencida", "vencidas"),
            aPagar == 0 ? null : $"{Moeda.ComSimbolo(aPagar)} medido a pagar",
            porMedir == 0 ? null : $"{Moeda.ComSimbolo(porMedir)} por medir",
            rascunhos == 0 ? null : Plural(rascunhos, "rascunho", "rascunhos"),
        }.OfType<string>());
        PorExecutante = [.. validas.Where(o => o.Situacao != SituacaoDaOrdem.Rascunho).GroupBy(o => o.Dados.Executante ?? "sem executante").OrderBy(g => g.Key)
            .Select(g => new LinhaDoResumoNaTela($"{g.Key} ({Plural(g.Count(), "OS", "OS")})",
                $"{Moeda.ComSimbolo(g.Sum(o => o.Total))} · medido {Moeda.ComSimbolo(g.Sum(o => o.TotalMedido))} · pago {Moeda.ComSimbolo(g.Sum(o => o.TotalPago))}" +
                (TextosDaOrdem.Prazos(g) is { } prazos ? $" · {prazos[..1].ToLowerInvariant()}{prazos[1..^1]}" : "")))];
        Prazos = TextosDaOrdem.Prazos(todas) ?? "";
        OnPropertyChanged(nameof(PorExecutante));
        OnPropertyChanged(nameof(Prazos));
        OnPropertyChanged(nameof(SemOrdens));
        if (id is { } atual)
        {
            var mesma = Lista.FirstOrDefault(l => l.Ordem.Id == atual);
            _carregando = true;
            try
            {
                Selecionada = mesma;
            }
            finally
            {
                _carregando = false;
            }
            Carregar(mesma?.Ordem);
        }
    }

    private static string Plural(int n, string um, string varios) => n == 1 ? $"1 {um}" : $"{n} {varios}";

    /// <summary>Ao fechar a janela: grava o que ficou na ficha de um rascunho.</summary>
    public void AoFechar()
    {
        if (Ordem is { PodeEditar: true } o && _fichaMudou && !Gravar(o, out _))
            _fichaMudou = false; // não dá para avisar com a janela fechando: o rascunho fica como estava
    }
}
