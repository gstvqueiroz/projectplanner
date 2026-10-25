using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// Bloco 64: Projeto > Relatório das ordens de serviço…: o filtro (o período pela emissão, as situações e o executante), os números
/// do que entra e o relatório em PDF (salvar ou imprimir) ou a lista em CSV.
/// </summary>
public sealed partial class RelatorioDasOrdensViewModel : ViewModelBase
{
    public const string TodosOsExecutantes = "Todos os executantes";

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Func<Core.Relatorios.CabecalhoDoRelatorio> _cabecalho;
    private readonly IReadOnlyDictionary<Guid, string> _tarefas;

    /// <param name="cabecalho">Bloco 65: a obra e quem emite, com o logotipo.</param>
    public RelatorioDasOrdensViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio,
        Func<Core.Relatorios.CabecalhoDoRelatorio>? cabecalho = null)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        (_cronograma, _dialogos, _relogio) = (cronograma, dialogos, relogio);
        _cabecalho = cabecalho ?? (() => new Core.Relatorios.CabecalhoDoRelatorio(cronograma.Nome, cronograma.Obra, null, null, null));
        var edt = cronograma.NumeracaoEdt();
        _tarefas = cronograma.Tarefas.ToDictionary(t => t.Id, t => $"{edt[t.Id]} {t.Nome}");
        Executantes = [TodosOsExecutantes, .. cronograma.OrdensDeServico.Todas.Select(o => o.Dados.Executante).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Order()];
        (SituacaoEscolhida, ExecutanteEscolhido) = (Situacoes[0], TodosOsExecutantes);
        PropertyChanged += Mudou;
        Atualizar();
    }

    public string Titulo => $"Relatório das ordens de serviço — {_cronograma.Nome}";

    private DateTime Agora => _relogio.GetLocalNow().DateTime;

    public IReadOnlyList<Escolha<SituacoesDoRelatorio>> Situacoes { get; } =
    [
        new(SituacoesDoRelatorio.Todas, "Todas"),
        new(SituacoesDoRelatorio.Abertas, "Abertas (rascunho e em andamento)"),
        new(SituacoesDoRelatorio.Encerradas, "Encerradas (conferidas, pagas e canceladas)"),
        new(SituacoesDoRelatorio.Vencidas, "Vencidas"),
    ];

    public IReadOnlyList<string> Executantes { get; }

    [ObservableProperty] public partial DateTime? De { get; set; }
    [ObservableProperty] public partial DateTime? Ate { get; set; }
    [ObservableProperty] public partial Escolha<SituacoesDoRelatorio> SituacaoEscolhida { get; set; }
    [ObservableProperty] public partial string ExecutanteEscolhido { get; set; }

    [ObservableProperty] public partial RelatorioDasOrdens Relatorio { get; private set; } = null!;

    /// <summary>"8 OS · contratado R$ 24.000,00 · medido R$ 6.000,00 · pago R$ 0,00 · a pagar R$ 6.000,00 · 4 vencidas".</summary>
    [ObservableProperty] public partial string Resumo { get; private set; } = "";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    /// <summary>Bloco 65: o PDF traz, no fim, o detalhe de cada OS (os itens com o medido, as medições e os pagamentos).</summary>
    [ObservableProperty] public partial bool Detalhar { get; set; }

    public FiltroDoRelatorioDasOrdens Filtro() => new(
        De is { } de ? DateOnly.FromDateTime(de) : null, Ate is { } ate ? DateOnly.FromDateTime(ate) : null, SituacaoEscolhida.Valor,
        ExecutanteEscolhido == TodosOsExecutantes ? null : ExecutanteEscolhido);

    private void Mudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(De) or nameof(Ate) or nameof(SituacaoEscolhida) or nameof(ExecutanteEscolhido))
            Atualizar();
    }

    private void Atualizar()
    {
        var r = Relatorio = RelatorioDasOrdens.Montar(_cronograma.OrdensDeServico.Todas, Filtro(), DateOnly.FromDateTime(Agora));
        Resumo = r.Vazio ? "Nenhuma OS neste filtro." : string.Join(" · ", new[]
        {
            r.Ordens.Count == 1 ? "1 OS" : $"{r.Ordens.Count} OS",
            $"contratado {Moeda.ComSimbolo(r.Valor)}", $"medido {Moeda.ComSimbolo(r.Medido)}", $"pago {Moeda.ComSimbolo(r.Pago)}",
            $"a pagar {Moeda.ComSimbolo(r.APagar)}",
            r.Vencidas.Count == 0 ? null : r.Vencidas.Count == 1 ? "1 vencida" : $"{r.Vencidas.Count} vencidas",
            r.ParaConferir.Count == 0 ? null : $"{r.ParaConferir.Count} para conferir",
        }.OfType<string>());
    }

    private string NomeDoArquivo => string.Concat($"Relatório das OS - {_cronograma.Nome}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [RelayCommand]
    private async Task SalvarPdf()
    {
        if (await _dialogos.SalvarComoAsync("Salvar o relatório das OS em PDF", NomeDoArquivo, "pdf") is not { } caminho)
            return;
        if (await Gravar(caminho, GerarPdf))
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
    }

    /// <summary>Grava o PDF numa pasta temporária e abre no leitor do sistema, para imprimir.</summary>
    [RelayCommand]
    private async Task Imprimir()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "ProjectPlanner", "Impressao");
        var caminho = Path.Combine(pasta, $"{NomeDoArquivo} {Agora:yyyyMMdd-HHmmss}.pdf");
        try
        {
            Directory.CreateDirectory(pasta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível preparar a impressão: {ex.Message}";
            return;
        }
        if (!await Gravar(caminho, GerarPdf))
            return;
        await _dialogos.AbrirArquivoAsync(caminho);
        Mensagem = $"O relatório abriu no leitor do sistema: imprima com {(AtalhosDeTeclado.EhMac ? "⌘P" : "Ctrl+P")}.";
    }

    [RelayCommand]
    private async Task ExportarCsv()
    {
        if (await _dialogos.SalvarComoAsync("Exportar as OS do relatório em CSV", NomeDoArquivo, "csv") is not { } caminho)
            return;
        var texto = OrdensDeServicoViewModel.Csv(Relatorio.Ordens, Relatorio.Hoje);
        if (await Gravar(caminho, destino =>
            {
                using var escrita = new StreamWriter(destino, TabelaCsv.Codificacao);
                escrita.Write(texto);
            }))
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
    }

    private void GerarPdf(Stream destino) =>
        new GeradorRelatorioDasOrdensPdf().Gerar(Relatorio, _cabecalho(), _tarefas, Agora.ToString("dd/MM/yyyy HH:mm", Brasil), destino, Detalhar);

    /// <summary>Bloco 65: o relatório em Excel, com as abas Resumo, OS, Itens, Medições e Por mês.</summary>
    [RelayCommand]
    private async Task ExportarExcel()
    {
        if (await _dialogos.SalvarComoAsync("Exportar o relatório das OS em Excel", NomeDoArquivo, "xlsx") is not { } caminho)
            return;
        var (relatorio, cabecalho, agora) = (Relatorio, _cabecalho(), Agora);
        if (await Gravar(caminho, destino => new GeradorOrdensExcel().Relatorio(relatorio, cabecalho, _tarefas, agora, destino)))
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
    }

    // Grava num temporário e troca no fim (um arquivo aberto no leitor não fica pela metade).
    private async Task<bool> Gravar(string caminho, Action<Stream> gravar)
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
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar: {ex.Message}";
            return false;
        }
    }
}
