using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma legenda sugerida pela IA, para revisar: o texto pode ser editado, e só as marcadas são aplicadas.</summary>
public sealed partial class LegendaParaRevisar(FotoTarefa foto, byte[]? miniatura, string tarefa, string sugerida) : ObservableObject
{
    public FotoTarefa Foto { get; } = foto;

    public byte[]? Miniatura { get; } = miniatura;

    /// <summary>"1 Fundação · 15/09/2026".</summary>
    public string Descricao { get; } = $"{tarefa} · {foto.DataFoto.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    [ObservableProperty] public partial string Legenda { get; set; } = sugerida;

    [ObservableProperty] public partial bool Aplicar { get; set; } = true;
}

/// <summary>Um % sugerido pela IA para uma tarefa, para revisar.</summary>
public sealed partial class AvancoParaRevisar(Tarefa tarefa, string nome, int sugerido, string justificativa, bool diverge) : ObservableObject
{
    public Tarefa Tarefa { get; } = tarefa;

    /// <summary>"3 Alvenaria".</summary>
    public string Nome { get; } = nome;

    public int Atual { get; } = tarefa.PercentualConcluido;

    public string Justificativa { get; } = justificativa;

    /// <summary>As fotos não batem com o % informado (diferença grande): vem marcada para aplicar.</summary>
    public bool Diverge { get; } = diverge;

    [ObservableProperty] public partial decimal? Sugerido { get; set; } = sugerido;

    [ObservableProperty] public partial bool Aplicar { get; set; } = diverge;
}

// Bloco 29: a aba Sugestões da IA: legendas para as fotos sem legenda e o % concluído conferido pelas fotos recentes.
// Nada muda sem revisar: as sugestões vêm numa lista para editar, marcar e aplicar (cada aplicação é um passo do Desfazer).
public sealed partial class FotosDoProjetoViewModel
{
    /// <summary>Até quantos dias atrás uma foto conta como recente para conferir o %.</summary>
    public const int DiasDeFotosRecentes = 30;

    private readonly IAnalistaDeFotos _analista;
    private CancellationTokenSource? _cancelamentoDaIa;

    public bool IaConfigurada => _analista.EstaConfigurado;

    public string AvisoDeConfiguracao => AnalistaIndisponivel.Mensagem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SugerirLegendasCommand), nameof(ConferirAvancoCommand), nameof(CancelarIaCommand),
        nameof(AplicarLegendasCommand), nameof(AplicarAvancoCommand))]
    public partial bool IaOcupada { get; private set; }

    /// <summary>O andamento do pedido, o resultado ou o erro.</summary>
    [ObservableProperty] public partial string AvisoDaIa { get; private set; } = "";

    public ObservableCollection<LegendaParaRevisar> LegendasSugeridas { get; } = [];

    public ObservableCollection<AvancoParaRevisar> AvancosSugeridos { get; } = [];

    /// <summary>As fotos da linha do tempo (com os filtros) sem legenda, que irão no próximo pedido (até o limite).</summary>
    public IReadOnlyList<FotoTarefa> FotosParaLegendar =>
        _filtradas.Where(f => f.Descricao is null).Take(LimitesDaAnalise.FotosPorPedidoDeLegendas).ToList();

    /// <summary>"Sugerir legendas (5 fotos sem legenda)".</summary>
    public string TextoDeSugerirLegendas => FotosParaLegendar.Count switch
    {
        0 => "Sugerir legendas (nenhuma foto sem legenda)",
        1 => "Sugerir legenda (1 foto sem legenda)",
        var n => $"Sugerir legendas ({n} fotos sem legenda)",
    };

    /// <summary>As tarefas em andamento com fotos recentes, as mais recentemente fotografadas primeiro (até o limite).</summary>
    public IReadOnlyList<Tarefa> TarefasParaConferir
    {
        get
        {
            var hoje = Hoje.Date;
            return _cronograma.Tarefas
                .Where(t => !t.EhResumo && !t.EhMarco && t.PercentualConcluido < 100)
                .Select(t => (Tarefa: t, Ultima: t.FotosNaOrdemDeCadastro.Where(f => f.DataFoto.Date <= hoje && f.DataFoto.Date >= hoje.AddDays(-DiasDeFotosRecentes))
                    .Select(f => f.DataFoto).DefaultIfEmpty(DateTime.MinValue).Max()))
                .Where(x => x.Ultima != DateTime.MinValue)
                .OrderByDescending(x => x.Ultima)
                .Take(LimitesDaAnalise.TarefasPorPedidoDeAvanco)
                .Select(x => x.Tarefa)
                .ToList();
        }
    }

    public string TextoDeConferirAvanco => TarefasParaConferir.Count switch
    {
        0 => $"Conferir o % pelas fotos (nenhuma tarefa em andamento com fotos dos últimos {DiasDeFotosRecentes} dias)",
        1 => "Conferir o % pelas fotos (1 tarefa)",
        var n => $"Conferir o % pelas fotos ({n} tarefas)",
    };

    private void AtualizarTextosDaIa()
    {
        OnPropertyChanged(nameof(FotosParaLegendar));
        OnPropertyChanged(nameof(TextoDeSugerirLegendas));
        OnPropertyChanged(nameof(TarefasParaConferir));
        OnPropertyChanged(nameof(TextoDeConferirAvanco));
        SugerirLegendasCommand.NotifyCanExecuteChanged();
        ConferirAvancoCommand.NotifyCanExecuteChanged();
    }

    // ---- Legendas ----

    [RelayCommand(CanExecute = nameof(PodeSugerirLegendas))]
    private async Task SugerirLegendas()
    {
        var fotos = FotosParaLegendar;
        var paraIa = fotos.Select(f => ParaIa(f)).OfType<FotoParaIA>().ToList();
        if (paraIa.Count == 0)
        {
            AvisoDaIa = "As imagens dessas fotos não estão disponíveis (o arquivo do projeto foi movido?).";
            return;
        }
        var sugestoes = await PedirAsync($"Pedindo legendas para {Quantas(paraIa.Count)} à IA…",
            c => _analista.SugerirLegendasAsync(new PedidoDeLegendas(_cronograma.Nome, paraIa), c));
        if (sugestoes is null)
            return;
        var porId = fotos.ToDictionary(f => f.Id.ToString("N"));
        var miniaturas = _fotos.Miniaturas(fotos.Select(f => f.Id));
        LegendasSugeridas.Clear();
        foreach (var sugestao in sugestoes.Where(s => porId.ContainsKey(s.Id)))
        {
            var foto = porId[sugestao.Id];
            LegendasSugeridas.Add(new LegendaParaRevisar(foto, miniaturas.GetValueOrDefault(foto.Id), _nomes[foto.TarefaId], sugestao.Legenda));
        }
        AvisoDaIa = LegendasSugeridas.Count == 0
            ? "A IA não sugeriu nenhuma legenda. Tente de novo."
            : $"{(LegendasSugeridas.Count == 1 ? "1 legenda sugerida" : $"{LegendasSugeridas.Count} legendas sugeridas")}. Revise, ajuste e aplique as marcadas.";
        AplicarLegendasCommand.NotifyCanExecuteChanged();
    }

    private bool PodeSugerirLegendas() => !IaOcupada && FotosParaLegendar.Count > 0;

    /// <summary>Põe as legendas marcadas nas fotos (as que continuam sem legenda), num passo só do Desfazer.</summary>
    [RelayCommand(CanExecute = nameof(PodeAplicarLegendas))]
    private void AplicarLegendas()
    {
        var tarefas = _cronograma.Tarefas.ToDictionary(t => t.Id);
        var (aplicadas, mudaram, erros) = (0, new HashSet<Guid>(), new List<string>());
        foreach (var item in LegendasSugeridas.Where(l => l.Aplicar && !string.IsNullOrWhiteSpace(l.Legenda)).ToList())
        {
            if (!tarefas.TryGetValue(item.Foto.TarefaId, out var tarefa) || tarefa.Foto(item.Foto.Id) is not { } atual)
                continue; // a foto saiu do projeto enquanto isso
            try
            {
                var m = atual.Metadados;
                _fotos.Editar(tarefa, atual.Id, new MetadadosFoto(m.DataFoto, m.Titulo, item.Legenda.Trim(), m.Autor, m.Local,
                    m.PercentualConcluidoNaData, m.Tags));
                aplicadas++;
                mudaram.Add(tarefa.Id);
                LegendasSugeridas.Remove(item);
            }
            catch (DomainValidationException ex)
            {
                erros.Add(ex.Message);
            }
        }
        if (mudaram.Count > 0)
            _fotosMudaram(mudaram, aplicadas == 1 ? "Legenda sugerida pela IA" : "Legendas sugeridas pela IA");
        AvisoDaIa = (aplicadas == 1 ? "1 legenda aplicada." : $"{aplicadas} legendas aplicadas.") + (erros.Count == 0 ? "" : " " + string.Join(" ", erros));
        Atualizar();
    }

    private bool PodeAplicarLegendas() => !IaOcupada && LegendasSugeridas.Count > 0;

    // ---- % pelas fotos ----

    [RelayCommand(CanExecute = nameof(PodeConferirAvanco))]
    private async Task ConferirAvanco()
    {
        var hoje = DateOnly.FromDateTime(Hoje);
        var andamento = AcompanhamentoDoCronograma.Calcular(_cronograma, Hoje.Date);
        var edt = _cronograma.NumeracaoEdt();
        var tarefas = TarefasParaConferir;
        var pedido = tarefas.Select(t => new TarefaParaAvanco(t.Id.ToString("N"), edt[t.Id], t.Nome,
                DateOnly.FromDateTime(t.InicioMaisCedo ?? t.DataInicio), DateOnly.FromDateTime(t.TerminoMaisCedo ?? t.DataTermino),
                t.PercentualConcluido, andamento.TryGetValue(t.Id, out var a) ? a.PercentualPrevisto : 0,
                t.FotosNaOrdemDeCadastro.Where(f => DateOnly.FromDateTime(f.DataFoto) <= hoje)
                    .OrderByDescending(f => f.DataFoto).Take(LimitesDaAnalise.FotosPorTarefa)
                    .Select(f => ParaIa(f)).OfType<FotoParaIA>().ToList()))
            .Where(t => t.Fotos.Count > 0)
            .ToList();
        if (pedido.Count == 0)
        {
            AvisoDaIa = "As imagens das fotos recentes não estão disponíveis (o arquivo do projeto foi movido?).";
            return;
        }
        var sugestoes = await PedirAsync($"Pedindo à IA para conferir {(pedido.Count == 1 ? "1 tarefa" : $"{pedido.Count} tarefas")}…",
            c => _analista.SugerirAvancoAsync(new PedidoDeAvanco(_cronograma.Nome, hoje, pedido), c));
        if (sugestoes is null)
            return;
        var porId = tarefas.ToDictionary(t => t.Id.ToString("N"));
        AvancosSugeridos.Clear();
        foreach (var s in sugestoes.Where(s => porId.ContainsKey(s.Id)))
            AvancosSugeridos.Add(new AvancoParaRevisar(porId[s.Id], _nomes[porId[s.Id].Id], s.Percentual, s.Justificativa, s.Diverge));
        var divergem = AvancosSugeridos.Count(a => a.Diverge);
        AvisoDaIa = AvancosSugeridos.Count == 0
            ? "A IA não conferiu nenhuma tarefa. Tente de novo."
            : divergem == 0
                ? "As fotos batem com o % informado em todas as tarefas conferidas."
                : $"{(divergem == 1 ? "1 tarefa" : $"{divergem} tarefas")} com o % diferente do que as fotos mostram (vêm marcadas). Revise e aplique.";
        AplicarAvancoCommand.NotifyCanExecuteChanged();
    }

    private bool PodeConferirAvanco() => !IaOcupada && TarefasParaConferir.Count > 0;

    /// <summary>Muda o % concluído das tarefas marcadas para o sugerido (ou o editado), num passo só do Desfazer.</summary>
    [RelayCommand(CanExecute = nameof(PodeAplicarAvanco))]
    private void AplicarAvanco()
    {
        var (mudaram, erros) = (new HashSet<Guid>(), new List<string>());
        foreach (var item in AvancosSugeridos.Where(a => a.Aplicar && a.Sugerido is not null).ToList())
        {
            if (!_cronograma.Tarefas.Contains(item.Tarefa))
                continue;
            try
            {
                var percentual = (int)Math.Round(item.Sugerido!.Value);
                if (percentual != item.Tarefa.PercentualConcluido)
                {
                    item.Tarefa.AtualizarPercentualConcluido(percentual);
                    mudaram.Add(item.Tarefa.Id);
                }
                AvancosSugeridos.Remove(item);
            }
            catch (DomainValidationException ex)
            {
                erros.Add($"{item.Nome}: {ex.Message}");
            }
        }
        if (mudaram.Count > 0)
            _fotosMudaram(mudaram, mudaram.Count == 1 ? "% sugerido pela IA" : "% sugeridos pela IA");
        AvisoDaIa = (mudaram.Count == 1 ? "1 tarefa atualizada." : $"{mudaram.Count} tarefas atualizadas.") + (erros.Count == 0 ? "" : " " + string.Join(" ", erros));
        Atualizar();
    }

    private bool PodeAplicarAvanco() => !IaOcupada && AvancosSugeridos.Count > 0;

    [RelayCommand(CanExecute = nameof(IaOcupada))]
    private void CancelarIa() => _cancelamentoDaIa?.Cancel();

    // ---- Comum ----

    private async Task<T?> PedirAsync<T>(string andamento, Func<CancellationToken, Task<T>> pedido) where T : class
    {
        if (!_analista.EstaConfigurado)
        {
            AvisoDaIa = AnalistaIndisponivel.Mensagem;
            return null;
        }
        IaOcupada = true;
        AvisoDaIa = andamento;
        using var cancelamento = _cancelamentoDaIa = new CancellationTokenSource();
        try
        {
            return await pedido(cancelamento.Token);
        }
        catch (OperationCanceledException)
        {
            AvisoDaIa = "Pedido cancelado.";
            return null;
        }
        catch (ErroDoAssistenteException ex)
        {
            AvisoDaIa = ex.Message;
            return null;
        }
        finally
        {
            _cancelamentoDaIa = null;
            IaOcupada = false;
        }
    }

    private FotoParaIA? ParaIa(FotoTarefa foto) =>
        _fotos.Imagem(foto.Id) is { } jpeg
            ? new FotoParaIA(foto.Id.ToString("N"), jpeg, _nomes[foto.TarefaId], DateOnly.FromDateTime(foto.DataFoto), foto.Titulo,
                foto.Descricao, foto.Local, foto.PercentualConcluidoNaData)
            : null;

    private static string Quantas(int n) => n == 1 ? "1 foto" : $"{n} fotos";
}
