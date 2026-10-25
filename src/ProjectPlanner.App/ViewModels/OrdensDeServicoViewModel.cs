using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

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
    private readonly IReadOnlyDictionary<Guid, string> _nomes;
    private readonly IReadOnlyDictionary<Guid, string> _edt;
    private bool _carregando;
    private bool _fichaMudou;

    /// <param name="mudou">As OS mudaram, com o nome da mudança para o Desfazer e se as tarefas mudaram (para recalcular).</param>
    /// <param name="tarefaSelecionada">A tarefa selecionada na grid: a OS nova começa com ela.</param>
    public OrdensDeServicoViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, Action<string, bool> mudou,
        Guid? tarefaSelecionada = null)
    {
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
        Selecionada = Lista.FirstOrDefault(l => !l.Encerrada) ?? Lista.FirstOrDefault();
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
        var emAberto = todas.Where(o => TextosDaOrdem.EmAndamento(o.Situacao) || o.Situacao == SituacaoDaOrdem.Conferida).Sum(o => o.Total);
        Resumo = todas.Count == 0 ? "" : string.Join(" · ", new[]
        {
            Plural(andamento, "em andamento", "em andamento"),
            vencidas == 0 ? null : Plural(vencidas, "vencida", "vencidas"),
            emAberto == 0 ? null : $"{Moeda.ComSimbolo(emAberto)} a pagar",
            rascunhos == 0 ? null : Plural(rascunhos, "rascunho", "rascunhos"),
        }.OfType<string>());
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

    // ---- A ficha ----

    public IReadOnlyList<Escolha<TipoDeExecutante>> Tipos { get; } =
        [.. Enum.GetValues<TipoDeExecutante>().Select(t => new Escolha<TipoDeExecutante>(t, TextosDaOrdem.Texto(t)))];

    public IReadOnlyList<Escolha<FormaDePagamentoDaOrdem>> Formas { get; } =
        [.. Enum.GetValues<FormaDePagamentoDaOrdem>().Select(f => new Escolha<FormaDePagamentoDaOrdem>(f, TextosDaOrdem.Texto(f)))];

    public IReadOnlyList<Escolha<PrecoDosItens>> Precos { get; } =
    [
        new(PrecoDosItens.MaoDeObra, "Só mão de obra (o material é da obra)"),
        new(PrecoDosItens.ServicoCompleto, "Serviço completo (com material)"),
    ];

    /// <summary>Os profissionais, os fornecedores e os executantes de outras OS, para o executante.</summary>
    public IReadOnlyList<string> Sugestoes { get; }

    public IReadOnlyList<TarefaParaEscolher> Tarefas { get; }

    public bool TemSelecionada => Selecionada is not null;

    public OrdemDeServico? Ordem => Selecionada?.Ordem;

    /// <summary>"OS-003 · Em execução".</summary>
    public string Cabecalho => Ordem is { } o ? $"{o.Codigo} · {TextosDaOrdem.Texto(o.Situacao)}" : "";

    public string AvisoDeVencida => Ordem is { } o && o.Vencida(Hoje) ? $"Passou do prazo ({o.Dados.Prazo!.Value.ToString("dd/MM/yyyy", Brasil)})." : "";

    public bool PodeEditar => Ordem?.PodeEditar == true;

    /// <summary>A verificação se marca no rascunho (a lista) e durante a execução e a conferência.</summary>
    public bool PodeMarcarVerificacao => PodeEditar || Ordem?.Situacao is SituacaoDaOrdem.Aceita or SituacaoDaOrdem.EmExecucao or SituacaoDaOrdem.Concluida;

    [ObservableProperty] public partial string TituloDaOrdem { get; set; } = "";
    [ObservableProperty] public partial Escolha<TipoDeExecutante> TipoEscolhido { get; set; } = null!;
    [ObservableProperty] public partial string Executante { get; set; } = "";
    [ObservableProperty] public partial string Contato { get; set; } = "";
    [ObservableProperty] public partial DateTime? InicioPrevisto { get; set; }
    [ObservableProperty] public partial DateTime? Prazo { get; set; }
    [ObservableProperty] public partial Escolha<FormaDePagamentoDaOrdem> FormaEscolhida { get; set; } = null!;
    [ObservableProperty] public partial string Observacoes { get; set; } = "";
    [ObservableProperty] public partial Escolha<PrecoDosItens> PrecoEscolhido { get; set; } = null!;
    [ObservableProperty] public partial string NovaVerificacao { get; set; } = "";

    public ObservableCollection<ItemNaFicha> Itens { get; } = [];

    public ObservableCollection<VerificacaoNaFicha> Verificacao { get; } = [];

    /// <summary>"25/10/2026 14:00 · Aceita (João, pelo Telegram)", do mais novo ao mais antigo.</summary>
    public IReadOnlyList<string> Historico => Ordem is { } o
        ? [.. o.Historico.Reverse().Select(e => $"{e.Quando.ToString("dd/MM/yyyy HH:mm", Brasil)} · {e.Descricao}{(e.Autor is null ? "" : $" ({e.Autor})")}")]
        : [];

    public string TotalDaFicha
    {
        get
        {
            try
            {
                return Moeda.ComSimbolo(Itens.Sum(i => (Moeda.LerNumero(i.Quantidade) ?? 0) * (Moeda.Ler(i.Preco) ?? 0)));
            }
            catch (FormatException)
            {
                return "—";
            }
        }
    }

    private static readonly HashSet<string> CamposDaFicha =
    [
        nameof(TituloDaOrdem), nameof(TipoEscolhido), nameof(Executante), nameof(Contato), nameof(InicioPrevisto), nameof(Prazo),
        nameof(FormaEscolhida), nameof(Observacoes),
    ];

    private void FichaMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (!_carregando && CamposDaFicha.Contains(e.PropertyName ?? ""))
            _fichaMudou = true;
    }

    private void ItemMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (_carregando)
            return;
        _fichaMudou = true;
        OnPropertyChanged(nameof(TotalDaFicha));
    }

    // A verificação: no rascunho é só a lista; depois, marcar um ponto muda a OS na hora.
    private void VerificacaoMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (_carregando || sender is not VerificacaoNaFicha v || e.PropertyName != nameof(VerificacaoNaFicha.Feito))
            return;
        if (PodeEditar)
        {
            _fichaMudou = true;
            return;
        }
        if (Ordem is not { } o)
            return;
        if (!Executar(() => o.MarcarVerificacao(Verificacao.IndexOf(v), v.Feito),
                $"{o.Codigo}: {(v.Feito ? "feito" : "desmarcado")} \"{v.Texto}\"", $"{(v.Feito ? "Marcado" : "Desmarcado")}: {v.Texto}."))
            Carregar(o); // a caixa volta a mostrar o que vale
    }

    partial void OnSelecionadaChanged(OrdemNaLista? oldValue, OrdemNaLista? newValue)
    {
        if (_carregando)
            return;
        if (oldValue is { Ordem.PodeEditar: true } anterior && _fichaMudou && !Gravar(anterior.Ordem, out var erro))
            Mensagem = $"As alterações da {anterior.Ordem.Codigo} não foram salvas: {erro}";
        Carregar(newValue?.Ordem);
    }

    // Põe a OS na ficha (ou limpa).
    private void Carregar(OrdemDeServico? o)
    {
        _carregando = true;
        try
        {
            foreach (var item in Itens)
                item.PropertyChanged -= ItemMudou;
            foreach (var v in Verificacao)
                v.PropertyChanged -= VerificacaoMudou;
            Itens.Clear();
            Verificacao.Clear();
            var d = o?.Dados;
            TituloDaOrdem = d?.Titulo ?? "";
            TipoEscolhido = Tipos.First(t => t.Valor == (d?.Tipo ?? TipoDeExecutante.Terceiro));
            Executante = d?.Executante ?? "";
            Contato = d?.Contato ?? "";
            InicioPrevisto = d?.InicioPrevisto?.ToDateTime(TimeOnly.MinValue);
            Prazo = d?.Prazo?.ToDateTime(TimeOnly.MinValue);
            FormaEscolhida = Formas.First(f => f.Valor == (d?.FormaDePagamento ?? FormaDePagamentoDaOrdem.Empreitada));
            PrecoEscolhido ??= Precos[0];
            Observacoes = d?.Observacoes ?? "";
            NovaVerificacao = "";
            foreach (var item in d?.Itens ?? [])
                Itens.Add(ItemNaFicha.De(item, EdtDe(item.TarefaId)));
            foreach (var v in d?.Verificacao ?? [])
                Verificacao.Add(new VerificacaoNaFicha(v.Texto, v.Feito));
            foreach (var item in Itens)
                item.PropertyChanged += ItemMudou;
            foreach (var v in Verificacao)
                v.PropertyChanged += VerificacaoMudou;
            var tarefasDaOrdem = d?.Tarefas.ToHashSet() ?? [];
            foreach (var t in Tarefas)
                t.Marcada = tarefasDaOrdem.Contains(t.Id);
        }
        finally
        {
            _carregando = false;
        }
        _fichaMudou = false;
        foreach (var nome in new[] { nameof(TemSelecionada), nameof(Ordem), nameof(Cabecalho), nameof(AvisoDeVencida), nameof(PodeEditar),
                     nameof(PodeMarcarVerificacao), nameof(Historico), nameof(TotalDaFicha) })
            OnPropertyChanged(nome);
        AtualizarBotoes();
    }

    private string EdtDe(Guid? tarefaId) => tarefaId is { } id && _edt.TryGetValue(id, out var edt) ? edt : "";

    /// <summary>Os dados como estão na ficha.</summary>
    /// <exception cref="DomainValidationException">Algum campo não vale.</exception>
    /// <exception cref="FormatException">Uma quantidade ou um preço que não é número.</exception>
    private DadosDaOrdem DadosDaFicha()
    {
        var itens = Itens.Select(i => new ItemDaOrdem(i.TarefaId, i.Descricao, i.Unidade,
            Moeda.LerNumero(i.Quantidade) ?? throw new DomainValidationException(nameof(Itens), $"Informe a quantidade de \"{i.Descricao}\"."),
            Moeda.Ler(i.Preco) ?? 0)).ToList();
        var recurso = _cronograma.Recursos.Lista.FirstOrDefault(r => string.Equals(r.Nome, Executante.Trim(), StringComparison.OrdinalIgnoreCase));
        return new DadosDaOrdem(TituloDaOrdem, TipoEscolhido.Valor, Executante, recurso?.Id, Contato,
            Tarefas.Where(t => t.Marcada).Select(t => t.Id), itens,
            InicioPrevisto is { } inicio ? DateOnly.FromDateTime(inicio) : null, Prazo is { } prazo ? DateOnly.FromDateTime(prazo) : null,
            FormaEscolhida.Valor, Observacoes, Verificacao.Select(v => new ItemDeVerificacao(v.Texto, v.Feito)));
    }

    // Grava a ficha na OS em rascunho (sem mensagem); falso com o motivo.
    private bool Gravar(OrdemDeServico ordem, out string erro)
    {
        try
        {
            var nova = ordem.Editar(DadosDaFicha());
            _cronograma.OrdensDeServico.Substituir(nova);
            _mudou($"Alterar a {nova.Codigo}", false);
            _fichaMudou = false;
            erro = "";
            return true;
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            erro = ex.Message;
            return false;
        }
    }

    // ---- Ações da ficha (rascunho) ----

    /// <summary>Uma OS nova em rascunho, com a tarefa selecionada na grid (e as de dentro, se for uma etapa), ou em branco.</summary>
    [RelayCommand]
    private void NovaOrdem()
    {
        if (!SalvarSePreciso())
            return;
        DadosDaOrdem dados;
        try
        {
            dados = TarefaSelecionadaNaGrid is { } id && _cronograma.Tarefas.Any(t => t.Id == id)
                ? MontagemDaOrdem.Rascunho(_cronograma, [id], PrecoEscolhido?.Valor ?? PrecoDosItens.MaoDeObra)
                : new DadosDaOrdem("Nova ordem de serviço", TipoDeExecutante.Terceiro, null, null, null, null, null, Hoje, null,
                    FormaDePagamentoDaOrdem.Empreitada, null, null);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        var ordem = OrdemDeServico.Nova(Guid.NewGuid(), _cronograma.OrdensDeServico.ProximoNumero, dados, Agora);
        _cronograma.OrdensDeServico.Adicionar(ordem);
        _mudou($"Nova {ordem.Codigo}", false);
        if (FiltroEscolhido.Valor is FiltroDasOrdens.Vencidas or FiltroDasOrdens.Encerradas)
            FiltroEscolhido = Filtros[0];
        Selecionada = null;
        Atualizar();
        Selecionada = Lista.First(l => l.Ordem.Id == ordem.Id);
        Mensagem = TarefaSelecionadaNaGrid is null
            ? $"{ordem.Codigo} criada. Marque as tarefas e clique em Trazer itens das tarefas, ou adicione os itens à mão."
            : $"{ordem.Codigo} criada com {Plural(dados.Itens.Count, "item", "itens")} da tarefa selecionada. Confira os preços e escolha o executante.";
    }

    private bool SalvarSePreciso()
    {
        if (Ordem is not { PodeEditar: true } o || !_fichaMudou)
            return true;
        if (Gravar(o, out var erro))
        {
            Atualizar();
            return true;
        }
        Mensagem = erro;
        return false;
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void Salvar()
    {
        if (Ordem is not { } o)
            return;
        if (!Gravar(o, out var erro))
        {
            Mensagem = erro;
            return;
        }
        Atualizar();
        Mensagem = $"{o.Codigo} salva.";
    }

    /// <summary>Troca os itens que vieram de tarefas pelos das tarefas marcadas (os avulsos ficam), com o preço escolhido.</summary>
    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void TrazerItensDasTarefas()
    {
        var marcadas = Tarefas.Where(t => t.Marcada).Select(t => t.Id).ToList();
        if (marcadas.Count == 0)
        {
            Mensagem = "Marque as tarefas da OS (na lista de tarefas) e clique de novo.";
            return;
        }
        DadosDaOrdem montada;
        try
        {
            montada = MontagemDaOrdem.Rascunho(_cronograma, marcadas, PrecoEscolhido.Valor);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        var avulsos = Itens.Where(i => i.TarefaId is null).ToList();
        foreach (var item in Itens)
            item.PropertyChanged -= ItemMudou;
        Itens.Clear();
        foreach (var item in montada.Itens.Select(i => ItemNaFicha.De(i, EdtDe(i.TarefaId))).Concat(avulsos))
        {
            item.PropertyChanged += ItemMudou;
            Itens.Add(item);
        }
        (InicioPrevisto, Prazo) = (montada.InicioPrevisto?.ToDateTime(TimeOnly.MinValue), montada.Prazo?.ToDateTime(TimeOnly.MinValue));
        _fichaMudou = true;
        OnPropertyChanged(nameof(TotalDaFicha));
        Mensagem = $"{Plural(montada.Itens.Count, "item veio", "itens vieram")} das tarefas, com as datas do cronograma. Confira e salve.";
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void AdicionarItem()
    {
        var item = new ItemNaFicha(null, "") { Unidade = MontagemDaOrdem.UnidadeSemQuantidade, Quantidade = "1" };
        item.PropertyChanged += ItemMudou;
        Itens.Add(item);
        _fichaMudou = true;
    }

    [RelayCommand]
    private void RemoverItem(ItemNaFicha? item)
    {
        if (!PodeEditar || item is null)
            return;
        item.PropertyChanged -= ItemMudou;
        Itens.Remove(item);
        _fichaMudou = true;
        OnPropertyChanged(nameof(TotalDaFicha));
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void AdicionarVerificacao()
    {
        try
        {
            var v = new ItemDeVerificacao(NovaVerificacao);
            var naFicha = new VerificacaoNaFicha(v.Texto, false);
            naFicha.PropertyChanged += VerificacaoMudou;
            Verificacao.Add(naFicha);
            NovaVerificacao = "";
            _fichaMudou = true;
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
        }
    }

    [RelayCommand]
    private void RemoverVerificacao(VerificacaoNaFicha? v)
    {
        if (!PodeEditar || v is null)
            return;
        v.PropertyChanged -= VerificacaoMudou;
        Verificacao.Remove(v);
        _fichaMudou = true;
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private async Task ExcluirRascunho()
    {
        var o = Ordem!;
        if (!await _dialogos.ConfirmarAsync("Excluir rascunho", $"Excluir a {o.Codigo} (\"{o.Dados.Titulo}\")? Dá para desfazer com Editar > Desfazer.", "Excluir"))
            return;
        _cronograma.OrdensDeServico.Remover(o.Id);
        _mudou($"Excluir a {o.Codigo}", false);
        _fichaMudou = false;
        Selecionada = null;
        Atualizar();
        Selecionada = Lista.FirstOrDefault();
        Mensagem = $"{o.Codigo} excluída.";
    }

    // ---- O fluxo ----

    public bool PodeEmitir => Ordem?.PodeIrPara(SituacaoDaOrdem.Emitida) == true;
    public bool PodeAceitar => Ordem?.PodeIrPara(SituacaoDaOrdem.Aceita) == true;
    public bool PodeIniciar => Ordem?.PodeIrPara(SituacaoDaOrdem.EmExecucao) == true;
    public bool PodeConcluir => Ordem?.PodeIrPara(SituacaoDaOrdem.Concluida) == true;
    public bool PodeConferir => Ordem?.PodeIrPara(SituacaoDaOrdem.Conferida) == true;
    public bool PodeVoltarARascunho => Ordem?.PodeIrPara(SituacaoDaOrdem.Rascunho) == true;
    public bool PodeCancelar => Ordem?.PodeIrPara(SituacaoDaOrdem.Cancelada) == true;

    private void AtualizarBotoes()
    {
        foreach (var nome in new[] { nameof(PodeEmitir), nameof(PodeAceitar), nameof(PodeIniciar), nameof(PodeConcluir), nameof(PodeConferir),
                     nameof(PodeVoltarARascunho), nameof(PodeCancelar) })
            OnPropertyChanged(nome);
        foreach (var comando in new IRelayCommand[] { SalvarCommand, TrazerItensDasTarefasCommand, AdicionarItemCommand, AdicionarVerificacaoCommand,
                     ExcluirRascunhoCommand, EmitirCommand, AceitarCommand, RecusarCommand, IniciarCommand, ConcluirCommand, ConferirCommand,
                     ReprovarCommand, VoltarARascunhoCommand, CancelarCommand, SalvarPdfCommand, ImprimirCommand })
            comando.NotifyCanExecuteChanged();
    }

    // Aplica um passo à OS escolhida (troca na lista, entra no Desfazer) e mostra a mensagem.
    private bool Executar(Func<OrdemDeServico> passo, string descricao, string mensagem, bool tarefasMudaram = false)
    {
        OrdemDeServico nova;
        try
        {
            nova = passo();
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        _cronograma.OrdensDeServico.Substituir(nova);
        _mudou(descricao, tarefasMudaram);
        Atualizar();
        Mensagem = mensagem;
        return true;
    }

    [RelayCommand(CanExecute = nameof(PodeEmitir))]
    private void Emitir()
    {
        if (!SalvarSePreciso())
            return;
        var o = Ordem!;
        Executar(() => o.Emitir(Agora), $"Emitir a {o.Codigo}",
            $"{o.Codigo} emitida para {o.Dados.Executante}. Gere o PDF para assinar (ou mande pelo Telegram).");
    }

    [RelayCommand(CanExecute = nameof(PodeAceitar))]
    private void Aceitar()
    {
        var o = Ordem!;
        Executar(() => o.Aceitar(Agora), $"{o.Codigo} aceita", $"{o.Codigo} aceita pelo executante.");
    }

    [RelayCommand(CanExecute = nameof(PodeAceitar))]
    private async Task Recusar()
    {
        var o = Ordem!;
        if (await _dialogos.PedirTextoAsync("Recusar a OS", $"Por que a {o.Codigo} foi recusada? (fica no histórico)") is not { } motivo)
            return;
        Executar(() => o.Recusar(Agora, motivo), $"{o.Codigo} recusada",
            $"{o.Codigo} recusada. Para corrigir e emitir de novo, clique em Voltar a rascunho.");
    }

    [RelayCommand(CanExecute = nameof(PodeIniciar))]
    private void Iniciar()
    {
        var o = Ordem!;
        Executar(() => o.Iniciar(Agora), $"Iniciar a {o.Codigo}", $"{o.Codigo} em execução.");
    }

    [RelayCommand(CanExecute = nameof(PodeConcluir))]
    private void Concluir()
    {
        var o = Ordem!;
        Executar(() => o.Concluir(Agora), $"Concluir a {o.Codigo}", $"{o.Codigo} concluída pelo executante: falta a conferência.");
    }

    [RelayCommand(CanExecute = nameof(PodeConferir))]
    private void Conferir()
    {
        var o = Ordem!;
        Executar(() => o.Conferir(Agora), $"Conferir a {o.Codigo}", $"{o.Codigo} conferida e aprovada.");
    }

    [RelayCommand(CanExecute = nameof(PodeConferir))]
    private async Task Reprovar()
    {
        var o = Ordem!;
        if (await _dialogos.PedirTextoAsync("Reprovar na conferência", $"O que precisa ser refeito na {o.Codigo}? (fica no histórico)") is not { } motivo)
            return;
        Executar(() => o.Reprovar(Agora, motivo), $"Reprovar a {o.Codigo}", $"{o.Codigo} reprovada: voltou à execução.");
    }

    [RelayCommand(CanExecute = nameof(PodeVoltarARascunho))]
    private void VoltarARascunho()
    {
        var o = Ordem!;
        Executar(() => o.VoltarARascunho(Agora), $"{o.Codigo} de volta a rascunho", $"{o.Codigo} voltou a rascunho: corrija e emita de novo.");
    }

    [RelayCommand(CanExecute = nameof(PodeCancelar))]
    private async Task Cancelar()
    {
        var o = Ordem!;
        if (await _dialogos.PedirTextoAsync("Cancelar a OS", $"Por que cancelar a {o.Codigo}? (fica no histórico)") is not { } motivo)
            return;
        Executar(() => o.Cancelar(Agora, motivo), $"Cancelar a {o.Codigo}", $"{o.Codigo} cancelada.");
    }

    // ---- PDF ----

    private bool TemSelecionadaParaPdf() => Ordem is not null;

    private CabecalhoDaOrdem CabecalhoDoPdf() =>
        new(_cronograma.Nome, _cronograma.Obra, _nomes, Agora.ToString("dd/MM/yyyy HH:mm", Brasil));

    private string NomeDoArquivo(OrdemDeServico o) =>
        string.Concat($"{o.Codigo} - {o.Dados.Titulo}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [RelayCommand(CanExecute = nameof(TemSelecionadaParaPdf))]
    private async Task SalvarPdf()
    {
        if (!SalvarSePreciso())
            return;
        var o = Ordem!;
        if (await _dialogos.SalvarComoAsync($"Salvar a {o.Codigo} em PDF", NomeDoArquivo(o), "pdf") is not { } caminho)
            return;
        if (await GravarPdf(o, caminho))
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
    }

    /// <summary>Grava o PDF numa pasta temporária e abre no leitor do sistema, para imprimir.</summary>
    [RelayCommand(CanExecute = nameof(TemSelecionadaParaPdf))]
    private async Task Imprimir()
    {
        if (!SalvarSePreciso())
            return;
        var o = Ordem!;
        var pasta = Path.Combine(Path.GetTempPath(), "ProjectPlanner", "Impressao");
        var caminho = Path.Combine(pasta, $"{NomeDoArquivo(o)} {Agora:yyyyMMdd-HHmmss}.pdf");
        try
        {
            Directory.CreateDirectory(pasta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível preparar a impressão: {ex.Message}";
            return;
        }
        if (!await GravarPdf(o, caminho))
            return;
        await _dialogos.AbrirArquivoAsync(caminho);
        Mensagem = $"O PDF da {o.Codigo} abriu no leitor do sistema: imprima com {(AtalhosDeTeclado.EhMac ? "⌘P" : "Ctrl+P")}.";
    }

    // Grava num temporário e troca no fim (um PDF aberto no leitor não fica pela metade).
    private async Task<bool> GravarPdf(OrdemDeServico ordem, string caminho)
    {
        var cabecalho = CabecalhoDoPdf();
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    new GeradorOrdemDeServicoPdf().Gerar(ordem, cabecalho, destino);
                File.Move(temporario, caminho, overwrite: true);
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar: {ex.Message}";
            return false;
        }
    }

    /// <summary>Ao fechar a janela: grava o que ficou na ficha de um rascunho.</summary>
    public void AoFechar()
    {
        if (Ordem is { PodeEditar: true } o && _fichaMudou && !Gravar(o, out _))
            _fichaMudou = false; // não dá para avisar com a janela fechando: o rascunho fica como estava
    }
}
