using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Relatorios;
using ProjectPlanner.IA;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

// Bloco 29: o texto do andamento do período (escrito pelo usuário ou sugerido pela IA, sempre editável) e as páginas de
// antes e depois por local.
public sealed partial class RelatorioFotograficoViewModel
{
    private readonly IAnalistaDeFotos _analista;
    private CancellationTokenSource? _cancelamentoDaIa;

    /// <summary>Um ou mais parágrafos (uma linha em branco separa os parágrafos).</summary>
    [ObservableProperty] public partial string TextoDoAndamento { get; set; } = "";

    /// <summary>Os pontos de atenção, um por linha.</summary>
    [ObservableProperty] public partial string TextoDosPontos { get; set; } = "";

    [ObservableProperty] public partial bool ComAntesEDepois { get; set; }

    public bool IaConfigurada => _analista.EstaConfigurado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SugerirTextoCommand), nameof(CancelarIaCommand))]
    public partial bool IaOcupada { get; private set; }

    /// <summary>O andamento do pedido à IA, o resultado ou o erro.</summary>
    [ObservableProperty] public partial string AvisoDaIa { get; private set; } = "";

    /// <summary>
    /// Pede à IA o texto do andamento e os pontos de atenção, com os dados do período, as etapas, as legendas das fotos e
    /// algumas fotos (as de destaque primeiro). O texto que já estava escrito só é trocado depois de confirmar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeSugerir))]
    private async Task SugerirTexto()
    {
        if (!_analista.EstaConfigurado)
        {
            AvisoDaIa = AnalistaIndisponivel.Mensagem;
            return;
        }
        if (_relatorio is not { } relatorio)
        {
            AvisoDaIa = "Corrija as escolhas do relatório antes de pedir o texto.";
            return;
        }
        if ((TextoDoAndamento.Length > 0 || TextoDosPontos.Length > 0)
            && !await _dialogos.ConfirmarAsync("Sugerir o texto", "Trocar o texto que já está escrito pelo texto sugerido pela IA?", "Trocar"))
            return;

        var pedido = PedidoDoTexto(relatorio);
        IaOcupada = true;
        AvisoDaIa = $"Pedindo o texto à IA ({(pedido.Fotos.Count == 1 ? "1 foto" : $"{pedido.Fotos.Count} fotos")})…";
        using var cancelamento = _cancelamentoDaIa = new CancellationTokenSource();
        try
        {
            var texto = await _analista.RedigirRelatorioAsync(pedido, cancelamento.Token);
            TextoDoAndamento = texto.Andamento.Replace("\n", Environment.NewLine + Environment.NewLine);
            TextoDosPontos = string.Join(Environment.NewLine, texto.PontosDeAtencao);
            AvisoDaIa = "Texto sugerido pela IA. Revise e ajuste antes de gerar o relatório.";
        }
        catch (OperationCanceledException)
        {
            AvisoDaIa = "Pedido cancelado.";
        }
        catch (ErroDoAssistenteException ex)
        {
            AvisoDaIa = ex.Message;
        }
        finally
        {
            _cancelamentoDaIa = null;
            IaOcupada = false;
        }
    }

    private bool PodeSugerir() => !IaOcupada;

    [RelayCommand(CanExecute = nameof(IaOcupada))]
    private void CancelarIa() => _cancelamentoDaIa?.Cancel();

    // O andamento (o mesmo da capa), as etapas do período com a situação e as legendas das fotos, e até
    // LimitesDaAnalise.FotosNoTexto fotos do relatório (destaques primeiro).
    private PedidoDeTextoDoRelatorio PedidoDoTexto(RelatorioFotografico relatorio)
    {
        var (de, ate) = (relatorio.Opcoes.De, relatorio.Opcoes.Ate);
        var andamento = AcompanhamentoDoCronograma.Calcular(_cronograma, ate.ToDateTime(TimeOnly.MinValue));
        var edt = _cronograma.NumeracaoEdt();
        var fotosDoRelatorio = relatorio.Fotos.ToList();
        var etapas = _cronograma.Tarefas
            .Where(t => !t.EhResumo)
            .Where(t => fotosDoRelatorio.Any(f => f.Foto.TarefaId == t.Id)
                        || ((t.InicioMaisCedo ?? t.DataInicio) <= ate.ToDateTime(TimeOnly.MinValue)
                            && (t.TerminoMaisCedo ?? t.DataTermino) >= de.ToDateTime(TimeOnly.MinValue)))
            .Select(t => new EtapaParaTexto(edt[t.Id], t.Nome, DateOnly.FromDateTime(t.InicioMaisCedo ?? t.DataInicio),
                DateOnly.FromDateTime(t.TerminoMaisCedo ?? t.DataTermino), t.PercentualConcluido,
                andamento.TryGetValue(t.Id, out var a) ? a.PercentualPrevisto : 0,
                andamento.TryGetValue(t.Id, out var b) ? b.Texto : "",
                [.. fotosDoRelatorio.Where(f => f.Foto.TarefaId == t.Id).Select(f => f.Texto ?? f.Titulo).OfType<string>().Take(5)]))
            .ToList();
        var fotos = fotosDoRelatorio
            .OrderByDescending(f => f.Destaque).ThenBy(f => f.Numero)
            .Select(f => _fotos.Imagem(f.Foto.Id) is { } jpeg
                ? new FotoParaIA(f.Foto.Id.ToString("N"), jpeg, f.Tarefa, DateOnly.FromDateTime(f.Foto.DataFoto), f.Titulo, f.Texto,
                    f.Foto.Local, f.Foto.PercentualConcluidoNaData)
                : null)
            .OfType<FotoParaIA>()
            .Take(LimitesDaAnalise.FotosNoTexto)
            .ToList();
        var resumo = TextosDoRelatorio.Andamento(relatorio)
            .Concat(TextosDoRelatorio.Atrasadas(relatorio).Select(a => "Atrasada: " + a))
            .Concat(TextosDoRelatorio.ProximosMarcos(relatorio).Select(m => "Próximo marco: " + m))
            .ToList();
        return new PedidoDeTextoDoRelatorio(_cronograma.Nome, de, ate, resumo, etapas, fotos);
    }
}
