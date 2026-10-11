using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma célula de período na tela: o valor e o % da linha (vazia quando nada cai no período).</summary>
public sealed record CelulaFisicoFinanceira(string Valor, string Percentual)
{
    public bool Vazia => Valor.Length == 0 && Percentual.Length == 0;
}

/// <summary>Uma linha da tabela na tela.</summary>
public sealed record LinhaFisicoFinanceiraNaTela(string Nome, string Valor, IReadOnlyList<CelulaFisicoFinanceira> Celulas, bool Total = false);

/// <summary>
/// Bloco 35: Projeto > Cronograma físico-financeiro…: a tabela etapa × período (R$ e %, do período e acumulado) do
/// cronograma atual ou da linha de base em uso, a Curva S (prevista × atual, com o realizado de hoje) e as exportações em
/// PDF e CSV. Só mostra: os valores são editados na grid (coluna Valor) ou importados de uma planilha.
/// </summary>
public sealed partial class FisicoFinanceiroViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Action _mostrarColunaValor;
    private readonly Func<Task> _importarValores;
    private readonly bool _pronta;

    /// <param name="mostrarColunaValor">Liga a coluna Valor da grid (para quem ainda não preencheu os valores).</param>
    /// <param name="importarValores">Projeto > Importar valores… (a tabela se refaz depois).</param>
    public FisicoFinanceiroViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, Action mostrarColunaValor,
        Func<Task> importarValores)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(mostrarColunaValor);
        ArgumentNullException.ThrowIfNull(importarValores);
        (_cronograma, _dialogos, _relogio, _mostrarColunaValor, _importarValores) = (cronograma, dialogos, relogio, mostrarColunaValor, importarValores);
        Bases = cronograma.LinhaDeBaseEmUso is { } linha
            ? [new(BaseDoFisicoFinanceiro.Atual, "Cronograma atual"), new(BaseDoFisicoFinanceiro.LinhaDeBase, $"Linha de base (LB{linha.Numero})")]
            : [new(BaseDoFisicoFinanceiro.Atual, "Cronograma atual")];
        (PeriodicidadeEscolhida, BaseEscolhida, LinhasEscolhidas) = (Periodicidades[2], Bases[0], OpcoesDeLinhas[0]);
        _pronta = true;
        PropertyChanged += Mudou;
        Atualizar();
    }

    public string Titulo => $"Cronograma físico-financeiro — {_cronograma.Nome}";

    // ---- Escolhas ----

    public IReadOnlyList<Escolha<Periodicidade>> Periodicidades { get; } =
    [
        new(Periodicidade.Semanal, "Semanal"),
        new(Periodicidade.Quinzenal, "Quinzenal"),
        new(Periodicidade.Mensal, "Mensal"),
        new(Periodicidade.DatasDeCorte, "Datas de corte"),
    ];

    [ObservableProperty] public partial Escolha<Periodicidade> PeriodicidadeEscolhida { get; set; }

    /// <summary>As datas de corte (o último dia de cada período), separadas por vírgula ou uma por linha.</summary>
    [ObservableProperty] public partial string TextoDosCortes { get; set; } = "";

    public bool MostraCortes => PeriodicidadeEscolhida.Valor == Periodicidade.DatasDeCorte;

    public IReadOnlyList<Escolha<BaseDoFisicoFinanceiro>> Bases { get; }

    [ObservableProperty] public partial Escolha<BaseDoFisicoFinanceiro> BaseEscolhida { get; set; }

    public IReadOnlyList<Escolha<LinhasDoFisicoFinanceiro>> OpcoesDeLinhas { get; } =
    [
        new(LinhasDoFisicoFinanceiro.Etapas, "Etapas (1º nível)"),
        new(LinhasDoFisicoFinanceiro.Tarefas, "Todas as tarefas"),
    ];

    [ObservableProperty] public partial Escolha<LinhasDoFisicoFinanceiro> LinhasEscolhidas { get; set; }

    private static readonly HashSet<string> NaoMudamATabela =
        [nameof(Tabela), nameof(Curva), nameof(Cabecalhos), nameof(RotuloDoValor), nameof(RotulosDosPeriodos), nameof(Linhas), nameof(Erro), nameof(Aviso), nameof(Resumo), nameof(Mensagem),
         nameof(SemValores), nameof(MostraCortes)];

    private void Mudou(object? sender, PropertyChangedEventArgs e)
    {
        if (!_pronta || NaoMudamATabela.Contains(e.PropertyName ?? ""))
            return;
        if (e.PropertyName == nameof(PeriodicidadeEscolhida))
            OnPropertyChanged(nameof(MostraCortes));
        Atualizar();
    }

    // ---- O que a tela mostra ----

    [ObservableProperty] public partial CronogramaFisicoFinanceiro? Tabela { get; private set; }

    [ObservableProperty] public partial CurvaS? Curva { get; private set; }

    /// <summary>"Etapa", "Valor (R$)" e um rótulo por período.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDoValor), nameof(RotulosDosPeriodos))]
    public partial IReadOnlyList<string> Cabecalhos { get; private set; } = [];

    public string RotuloDoValor => Cabecalhos.Count > 1 ? Cabecalhos[1] : "";

    public IReadOnlyList<string> RotulosDosPeriodos => [.. Cabecalhos.Skip(2)];

    /// <summary>As linhas e, no fim, os totais (do período e acumulados).</summary>
    [ObservableProperty] public partial IReadOnlyList<LinhaFisicoFinanceiraNaTela> Linhas { get; private set; } = [];

    /// <summary>"Valor total: R$ 1.234,00 · 6 períodos · realizado em 11/10/2026: 23,5% (R$ 290,00)".</summary>
    [ObservableProperty] public partial string Resumo { get; private set; } = "";

    /// <summary>Escolha que não vale (datas de corte com erro, cronograma que não calcula).</summary>
    [ObservableProperty] public partial string Erro { get; private set; } = "";

    /// <summary>Sem valores, o físico vai pela duração: o aviso diz como preencher.</summary>
    [ObservableProperty] public partial string Aviso { get; private set; } = "";

    [ObservableProperty] public partial bool SemValores { get; private set; }

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    public const string AvisoSemValores =
        "Nenhuma tarefa tem valor: o % físico está pesado pela duração, e não há R$. Preencha a coluna Valor (R$) na grid ou importe uma planilha com a EDT e o valor de cada tarefa.";

    private DateOnly Hoje => DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);

    /// <summary>Refaz a tabela e a curva (depois de mudar as escolhas ou de importar valores).</summary>
    public void Atualizar()
    {
        try
        {
            var (inicio, fim) = CalculadoraFisicoFinanceira.Intervalo(_cronograma);
            var periodos = PeriodosFinanceiros.Gerar(inicio, fim, PeriodicidadeEscolhida.Valor,
                MostraCortes ? PeriodosFinanceiros.LerCortes(TextoDosCortes) : null);
            Tabela = CalculadoraFisicoFinanceira.Calcular(_cronograma, BaseEscolhida.Valor, periodos, LinhasEscolhidas.Valor);
            Curva = CurvaS.Montar(_cronograma, periodos, Hoje);
            Erro = "";
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            (Tabela, Curva, Erro) = (null, null, ex.Message);
        }
        MontarTela();
        ExportarPdfCommand.NotifyCanExecuteChanged();
        ExportarCsvCommand.NotifyCanExecuteChanged();
    }

    private void MontarTela()
    {
        if (Tabela is not { } t)
        {
            (Cabecalhos, Linhas, Resumo, Aviso, SemValores) = ([], [], "", "", false);
            return;
        }
        SemValores = !t.PorValor;
        Aviso = SemValores ? AvisoSemValores : "";
        Cabecalhos = ["Etapa", t.PorValor ? "Valor (R$)" : "Peso (dias)", .. t.Periodos.Select(p => p.Rotulo)];
        var linhas = t.Linhas.Select(l => new LinhaFisicoFinanceiraNaTela($"{l.Edt} {l.Nome}",
                t.PorValor ? Moeda.Formatar(l.Valor) : l.Peso.ToString("0", Brasil),
                [.. l.PesoPorPeriodo.Select((p, i) => p == 0 ? new CelulaFisicoFinanceira("", "")
                    : new CelulaFisicoFinanceira(t.PorValor ? Moeda.Formatar(l.ValorPorPeriodo[i]) : "", Percentual(l.PercentualPorPeriodo[i])))]))
            .ToList();
        if (t.PorValor)
        {
            linhas.Add(new LinhaFisicoFinanceiraNaTela("Total do período", Moeda.Formatar(t.ValorTotal),
                [.. t.ValorDoPeriodo.Select((v, i) => new CelulaFisicoFinanceira(Moeda.Formatar(v), Percentual(t.PercentualDoPeriodo[i])))], Total: true));
            linhas.Add(new LinhaFisicoFinanceiraNaTela("Acumulado", "",
                [.. t.ValorAcumulado.Select((v, i) => new CelulaFisicoFinanceira(Moeda.Formatar(v), Percentual(t.PercentualAcumulado[i])))], Total: true));
        }
        else
        {
            linhas.Add(new LinhaFisicoFinanceiraNaTela("% do período", "", [.. t.PercentualDoPeriodo.Select(p => new CelulaFisicoFinanceira("", Percentual(p)))], Total: true));
            linhas.Add(new LinhaFisicoFinanceiraNaTela("% acumulado", "", [.. t.PercentualAcumulado.Select(p => new CelulaFisicoFinanceira("", Percentual(p)))], Total: true));
        }
        Linhas = linhas;
        var periodos = t.Periodos.Count == 1 ? "1 período" : $"{t.Periodos.Count} períodos";
        var realizado = Curva?.Realizado is { } r
            ? $" · realizado em {r.Data:dd/MM/yyyy}: {Percentual(r.Percentual)}" + (t.PorValor ? $" (R$ {Moeda.Formatar(r.Valor)})" : "")
            : "";
        Resumo = (t.PorValor ? $"Valor total: R$ {Moeda.Formatar(t.ValorTotal)} · " : "") + periodos + realizado;
    }

    private static string Percentual(double valor) => valor.ToString("0.0", Brasil) + "%";

    // ---- Ações ----

    [RelayCommand]
    private void MostrarColunaValor()
    {
        _mostrarColunaValor();
        Mensagem = "A coluna Valor (R$) está na grid: preencha o valor de cada tarefa, e a tabela usa os valores na próxima vez.";
    }

    [RelayCommand]
    private async Task ImportarValores()
    {
        await _importarValores();
        Atualizar();
    }

    private bool TemTabela() => Tabela is not null;

    private string NomeDoArquivo => $"Físico-financeiro - {_cronograma.Nome}";

    private string Subtitulo => $"{BaseEscolhida.Texto} · {PeriodicidadeEscolhida.Texto.ToLower(Brasil)} · gerado em {_relogio.GetLocalNow().DateTime:dd/MM/yyyy HH:mm}";

    [RelayCommand(CanExecute = nameof(TemTabela))]
    private async Task ExportarPdf()
    {
        if (await _dialogos.SalvarComoAsync("Exportar o físico-financeiro em PDF", NomeDoArquivo, "pdf") is not { } caminho)
            return;
        var (tabela, curva) = (Tabela!, Curva!);
        await Salvar(caminho, destino => new GeradorFisicoFinanceiroPdf().Gerar(tabela, curva, _cronograma.Nome, Subtitulo, destino));
    }

    [RelayCommand(CanExecute = nameof(TemTabela))]
    private async Task ExportarCsv()
    {
        if (await _dialogos.SalvarComoAsync("Exportar o físico-financeiro em CSV", NomeDoArquivo, "csv") is not { } caminho)
            return;
        var texto = Csv(Tabela!);
        await Salvar(caminho, destino =>
        {
            using var escrita = new StreamWriter(destino, TabelaCsv.Codificacao);
            escrita.Write(texto);
        });
    }

    /// <summary>O CSV: por linha, o valor e, por período, o R$ e o %; no fim, os totais e o acumulado.</summary>
    internal static string Csv(CronogramaFisicoFinanceiro t)
    {
        static string Numero(decimal v) => v.ToString("0.00", Brasil);
        static string Pct(double v) => v.ToString("0.00", Brasil);
        var cabecalho = new List<string> { "EDT", "Etapa", t.PorValor ? "Valor (R$)" : "Peso (dias)" };
        foreach (var p in t.Periodos)
            cabecalho.AddRange([$"{p.Rotulo} (R$)", $"{p.Rotulo} (%)"]);
        var linhas = t.Linhas.Select(l => (IReadOnlyList<string>)[l.Edt, TabelaCsv.TextoLivre(l.Nome), t.PorValor ? Numero(l.Valor) : l.Peso.ToString("0", Brasil),
            .. l.ValorPorPeriodo.SelectMany((v, i) => new[] { Numero(v), Pct(l.PercentualPorPeriodo[i]) })]).ToList();
        linhas.Add(["", "Total do período", Numero(t.ValorTotal), .. t.ValorDoPeriodo.SelectMany((v, i) => new[] { Numero(v), Pct(t.PercentualDoPeriodo[i]) })]);
        linhas.Add(["", "Acumulado", "", .. t.ValorAcumulado.SelectMany((v, i) => new[] { Numero(v), Pct(t.PercentualAcumulado[i]) })]);
        return TabelaCsv.Gerar(cabecalho, linhas);
    }

    // Grava num temporário e troca no fim (um arquivo aberto no Excel ou no leitor de PDF não fica pela metade).
    private async Task Salvar(string caminho, Action<Stream> gravar)
    {
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    gravar(destino);
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
