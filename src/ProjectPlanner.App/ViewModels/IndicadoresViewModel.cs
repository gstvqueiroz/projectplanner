using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma etapa na tabela do painel.</summary>
public sealed record EtapaDoPainelNaTela(string Nome, string Orcamento, string Previsto, string Realizado, string Medido, string CustoReal, string Desvio,
    bool Atrasada);

/// <summary>
/// Bloco 37: Projeto > Indicadores…: numa data de status, o valor agregado (VP, VA, CR, IDP, IDC, estimativa no término e
/// término pela tendência), o físico × o financeiro, a Curva S (prevista × atual × medida × custo real, com o realizado), as
/// etapas e as tarefas mais atrasadas; exporta em PDF e Word. Só mostra. Bloco 43: as abas Custos e margem, Medições e
/// caixa, e Alertas e análise (com o resumo escrito pela IA).
/// </summary>
public sealed partial class IndicadoresViewModel : ViewModelBase
{
    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly bool _pronta;

    /// <param name="analista">Bloco 43: a IA que escreve a análise (sem ela, o botão avisa que falta configurar).</param>
    public IndicadoresViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, IA.IAnalistaDaObra? analista = null)
    {
        _analista = analista ?? new IA.AnalistaDaObraIndisponivel();
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        (_cronograma, _dialogos, _relogio) = (cronograma, dialogos, relogio);
        DataDeStatus = relogio.GetLocalNow().Date;
        PeriodicidadeEscolhida = Periodicidades[2];
        _pronta = true;
        Atualizar();
    }

    public string Titulo => $"Indicadores — {_cronograma.Nome}";

    // ---- Escolhas ----

    /// <summary>A data de status (o fim do dia): hoje, ou outra para ver como estava.</summary>
    [ObservableProperty] public partial DateTime? DataDeStatus { get; set; }

    public IReadOnlyList<Escolha<Periodicidade>> Periodicidades { get; } =
    [
        new(Periodicidade.Semanal, "Semanal"),
        new(Periodicidade.Quinzenal, "Quinzenal"),
        new(Periodicidade.Mensal, "Mensal"),
    ];

    [ObservableProperty] public partial Escolha<Periodicidade> PeriodicidadeEscolhida { get; set; }

    partial void OnDataDeStatusChanged(DateTime? value) => Atualizar();

    partial void OnPeriodicidadeEscolhidaChanged(Escolha<Periodicidade> value) => Atualizar();

    [RelayCommand]
    private void Hoje() => DataDeStatus = _relogio.GetLocalNow().Date;

    // ---- O que a tela mostra ----

    public PainelDeIndicadores? Painel { get; private set; }

    public string Cabecalho => Painel is { } p ? TextosDosIndicadores.Titulo(p) : "";

    public string Subtitulo => Painel is { } p ? TextosDosIndicadores.Subtitulo(p) : "";

    public IReadOnlyList<CartaoDeIndicador> Cartoes { get; private set; } = [];

    public IReadOnlyList<EtapaDoPainelNaTela> Etapas { get; private set; } = [];

    public IReadOnlyList<string> Atrasos { get; private set; } = [];

    public string TituloDosAtrasos => Painel is { } p && p.TotalDeAtrasadas > Atrasos.Count ? $"Mais atrasadas ({Atrasos.Count} de {p.TotalDeAtrasadas})" : "Atrasadas";

    public bool SemAtrasos => Atrasos.Count == 0;

    public CurvaS? Curva => Painel?.Curva;

    public bool SemValores => Painel is { TemValores: false };

    public string AvisoSemValores => TextosDosIndicadores.SemValores;

    [ObservableProperty] public partial string Erro { get; private set; } = "";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    /// <summary>Refaz o painel (depois de mudar a data ou a periodicidade).</summary>
    public void Atualizar()
    {
        if (!_pronta)
            return;
        try
        {
            Painel = DataDeStatus is { } data
                ? PainelDeIndicadores.Montar(_cronograma, DateOnly.FromDateTime(data), PeriodicidadeEscolhida.Valor)
                : throw new DomainValidationException(nameof(DataDeStatus), "Escolha a data de status.");
            Erro = "";
        }
        catch (DomainValidationException ex)
        {
            (Painel, Erro) = (null, ex.Message);
        }
        AtualizarAnalise();
        Cartoes = Painel is { } p ? TextosDosIndicadores.Cartoes(p) : [];
        Etapas = Painel is { } q
            ? [.. TextosDosIndicadores.Etapas(q).Select((e, i) => new EtapaDoPainelNaTela(e[0], e[1], e[2], e[3], e[4], e[5], e[6], i < q.Etapas.Count && q.Etapas[i].Desvio < 0))]
            : [];
        Atrasos = Painel is { } r ? TextosDosIndicadores.Atrasos(r) : [];
        foreach (var nome in new[]
                 {
                     nameof(Painel), nameof(Cabecalho), nameof(Subtitulo), nameof(Cartoes), nameof(Etapas), nameof(Atrasos), nameof(TituloDosAtrasos),
                     nameof(SemAtrasos), nameof(Curva), nameof(SemValores),
                 })
            OnPropertyChanged(nome);
        ExportarPdfCommand.NotifyCanExecuteChanged();
        ExportarWordCommand.NotifyCanExecuteChanged();
    }

    // ---- Exportar ----

    private bool TemPainel() => Painel is not null;

    private string NomeDoArquivo => $"Indicadores {Painel!.Data:yyyy-MM-dd} - {_cronograma.Nome}";

    [RelayCommand(CanExecute = nameof(TemPainel))]
    private Task ExportarPdf() => Exportar("pdf", "PDF", (p, destino) =>
        new GeradorIndicadoresPdf().Gerar(p, _cronograma.Nome, _relogio.GetLocalNow().DateTime, destino, Analise, EscritaParaExportar));

    [RelayCommand(CanExecute = nameof(TemPainel))]
    private Task ExportarWord() => Exportar("docx", "Word", (p, destino) =>
        new GeradorIndicadoresWord().Gerar(p, _cronograma.Nome, _relogio.GetLocalNow().DateTime, destino, Analise, EscritaParaExportar));

    private async Task Exportar(string extensao, string formato, Action<PainelDeIndicadores, Stream> gerar)
    {
        var painel = Painel!;
        if (await _dialogos.SalvarComoAsync($"Exportar os indicadores em {formato}", NomeDoArquivo, extensao) is not { } caminho)
            return;
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    gerar(painel, destino);
                File.Move(temporario, caminho, overwrite: true);
            });
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar: {ex.Message}";
        }
    }
}
