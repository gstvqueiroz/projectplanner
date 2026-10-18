using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.IA;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 43: uma linha de uma tabela dos indicadores ampliados (as células já em texto).</summary>
/// <param name="Destaque">A linha do total (em negrito).</param>
/// <param name="Alerta">A primeira coluna de alerta em vermelho (o desvio, o medido − físico, o saldo do período).</param>
/// <param name="Alerta2">A segunda (a margem projetada, o saldo acumulado).</param>
public sealed record LinhaDosIndicadores(string[] Celulas, bool Destaque = false, bool Alerta = false, bool Alerta2 = false);

/// <summary>Bloco 43: um item do semáforo na tela.</summary>
public sealed record AlertaNaTela(AlertaDaObra Alerta)
{
    public string Area => Alerta.Area;
    public string Nivel => TextosDaAnalise.Nivel(Alerta.Nivel);
    public string Cor => TextosDaAnalise.Cor(Alerta.Nivel);
    public string Titulo => Alerta.Titulo;
    public string Detalhe => Alerta.Detalhe;
}

// Bloco 43: as abas Custos e margem, Medições e caixa, e Alertas e análise.
public sealed partial class IndicadoresViewModel
{
    public const int AbaVisaoGeral = 0, AbaCustos = 1, AbaMedicoes = 2, AbaAlertas = 3;

    private readonly IAnalistaDaObra _analista;
    private CancellationTokenSource? _analisando;

    [ObservableProperty] public partial int AbaEscolhida { get; set; }

    public AnaliseFinanceira? Analise { get; private set; }

    public IReadOnlyList<CartaoDeIndicador> CartoesDeCusto { get; private set; } = [];

    public IReadOnlyList<LinhaDosIndicadores> Custos { get; private set; } = [];

    public IReadOnlyList<LinhaDosIndicadores> Medicoes { get; private set; } = [];

    public IReadOnlyList<LinhaDoResumoNaTela> Contrato { get; private set; } = [];

    public IReadOnlyList<LinhaDosIndicadores> Caixa { get; private set; } = [];

    /// <summary>O gráfico do fluxo de caixa, em PNG (a tela o mostra com Conversores.Imagem).</summary>
    public byte[]? ImagemDoCaixa { get; private set; }

    public IReadOnlyList<AlertaNaTela> Alertas { get; private set; } = [];

    public bool SemCustoOrcado => Analise is { TemCustoOrcado: false };

    public string AvisoSemCustoOrcado => TextosDaAnalise.SemCustoOrcado;

    private void AtualizarAnalise()
    {
        try
        {
            Analise = Painel is { } p ? AnaliseFinanceira.Montar(_cronograma, p, PeriodicidadeEscolhida.Valor) : null;
        }
        catch (Core.DomainValidationException ex)
        {
            (Analise, Erro) = (null, ex.Message);
        }
        var a = Analise;
        CartoesDeCusto = a is null ? [] : TextosDaAnalise.Cartoes(a);
        // A linha i é a etapa i; depois vem o total (e, nos custos, os custos gerais, sem alerta).
        EtapaFinanceira? Etapa(int i) => a is null ? null : i < a.Etapas.Count ? a.Etapas[i] : i == a.Etapas.Count ? a.Total : null;
        Custos = a is null ? [] : [.. TextosDaAnalise.Custos(a).Select((c, i) => new LinhaDosIndicadores(c, c[0] == "Total",
            Etapa(i)?.DesvioDeCusto < 0, Etapa(i)?.MargemProjetada < 0))];
        Medicoes = a is null ? [] : [.. TextosDaAnalise.Medicoes(a).Select((c, i) => new LinhaDosIndicadores(c, c[0] == "Total",
            Etapa(i)?.DiferencaDaMedicao > AnaliseFinanceira.DiferencaDaMedicaoAtencao))];
        Contrato = a is null ? [] : [.. TextosDaAnalise.Contrato(a).Select(c => new LinhaDoResumoNaTela(c.Rotulo, c.Valor))];
        Caixa = a is null ? [] : [.. TextosDaAnalise.Caixa(a).Select((c, i) => new LinhaDosIndicadores(c, false,
            a.Caixa[i].Realizado && a.Caixa[i].Saldo < 0, a.Caixa[i].Realizado && a.Caixa[i].SaldoAcumulado < 0))];
        ImagemDoCaixa = a is null ? null : Relatorios.ImagemDoCaixa.Png(a, 540, 240);
        Alertas = a is null ? [] : [.. a.Alertas.Select(x => new AlertaNaTela(x))];
        foreach (var nome in new[]
                 {
                     nameof(Analise), nameof(CartoesDeCusto), nameof(Custos), nameof(Medicoes), nameof(Contrato), nameof(Caixa), nameof(ImagemDoCaixa),
                     nameof(Alertas), nameof(SemCustoOrcado),
                 })
            OnPropertyChanged(nome);
        AnalisarCommand.NotifyCanExecuteChanged();
    }

    // ---- A análise escrita (pela IA, editável) ----

    /// <summary>O resumo executivo (escrito pela IA ou à mão); vai no PDF e no Word quando preenchido.</summary>
    [ObservableProperty] public partial string ResumoDaAnalise { get; set; } = "";

    /// <summary>Os pontos de atenção, um por linha.</summary>
    [ObservableProperty] public partial string PontosDaAnalise { get; set; } = "";

    /// <summary>As recomendações, uma por linha.</summary>
    [ObservableProperty] public partial string RecomendacoesDaAnalise { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalisarCommand), nameof(CancelarAnaliseCommand))]
    public partial bool Analisando { get; private set; }

    [ObservableProperty] public partial string AvisoDaAnalise { get; private set; } = "";

    public bool IaConfigurada => _analista.EstaConfigurado;

    private static IReadOnlyList<string> Linhas(string texto) =>
        [.. texto.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim().TrimStart('•', '-', '*').Trim()).Where(l => l.Length > 0)];

    /// <summary>A análise para exportar; nula se nada foi escrito.</summary>
    public AnaliseEscrita? EscritaParaExportar =>
        new AnaliseEscrita(ResumoDaAnalise.Trim(), Linhas(PontosDaAnalise), Linhas(RecomendacoesDaAnalise)) is { Vazia: false } e ? e : null;

    private bool PodeAnalisar() => Painel is not null && Analise is not null && !Analisando;

    /// <summary>Pede à IA o resumo, os pontos e as recomendações (substituem o que estiver nos campos, que ficam editáveis).</summary>
    [RelayCommand(CanExecute = nameof(PodeAnalisar))]
    private async Task Analisar()
    {
        if (!_analista.EstaConfigurado)
        {
            AvisoDaAnalise = AnalistaIndisponivel.Mensagem;
            return;
        }
        var pedido = new PedidoDeAnaliseDaObra(_cronograma.Nome, Painel!.Data, TextosDaAnalise.ParaAIA(Painel, Analise!),
            [.. _cronograma.Assistente.Ficha.Itens.Select(i => $"{i.Campo}: {i.Valor}")]);
        using var cancelamento = _analisando = new CancellationTokenSource();
        Analisando = true;
        AvisoDaAnalise = "Pedindo a análise à IA…";
        try
        {
            var r = await _analista.AnalisarAsync(pedido, cancelamento.Token);
            (ResumoDaAnalise, PontosDaAnalise, RecomendacoesDaAnalise) = (r.Resumo, string.Join("\n", r.Pontos), string.Join("\n", r.Recomendacoes));
            AvisoDaAnalise = "Análise escrita pela IA: revise e ajuste antes de exportar. Ela vai no PDF e no Word.";
        }
        catch (OperationCanceledException)
        {
            AvisoDaAnalise = "Análise cancelada.";
        }
        catch (ErroDoAssistenteException ex)
        {
            AvisoDaAnalise = ex.Message;
        }
        finally
        {
            _analisando = null;
            Analisando = false;
        }
    }

    [RelayCommand(CanExecute = nameof(Analisando))]
    private void CancelarAnalise() => _analisando?.Cancel();

    /// <summary>A janela fechou: a análise em andamento é cancelada.</summary>
    public void Fechando() => _analisando?.Cancel();
}
