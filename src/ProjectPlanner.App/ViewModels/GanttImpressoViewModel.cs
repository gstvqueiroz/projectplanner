using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Como na tela: o que o Gantt da janela mostra agora (para começar a impressão igual).</summary>
public sealed record GanttDaTela(bool CaminhoCritico, bool LinhaDeBase, bool Hoje, IReadOnlySet<Guid> Ocultas);

/// <summary>
/// Bloco 57: Arquivo > Imprimir o Gantt…: a folha (A4 a A0, Carta e Ofício; paisagem ou retrato), a escala do tempo, as
/// colunas da tabela, o que entra no desenho (caminho crítico, linha de base, vínculos, hoje, nomes, fins de semana, legenda) e
/// os níveis da EDT, com a prévia de cada página. Salva o PDF ou abre-o no leitor do sistema para imprimir.
/// </summary>
public sealed partial class GanttImpressoViewModel : ViewModelBase
{
    /// <summary>"Como na tela": tira as tarefas dos resumos recolhidos.</summary>
    public const int ComoNaTela = -1;

    private readonly Cronograma _cronograma;
    private readonly ResultadoCronograma _resultado;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly GanttDaTela _tela;
    private readonly IReadOnlyDictionary<Guid, string> _predecessoras;
    private readonly bool _pronta;

    public GanttImpressoViewModel(Cronograma cronograma, ResultadoCronograma resultado, IDialogosService dialogos, TimeProvider relogio,
        GanttDaTela tela, IReadOnlyDictionary<Guid, string> predecessoras)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(tela);
        ArgumentNullException.ThrowIfNull(predecessoras);
        (_cronograma, _resultado, _dialogos, _relogio, _tela, _predecessoras) = (cronograma, resultado, dialogos, relogio, tela, predecessoras);
        (FolhaEscolhida, OrientacaoEscolhida, EscalaEscolhida) = (Folhas[1], Orientacoes[0], Escalas[0]);
        NivelEscolhido = tela.Ocultas.Count > 0 ? Niveis[1] : Niveis[0];
        (CaminhoCritico, LinhaDeBase, Hoje) = (tela.CaminhoCritico, tela.LinhaDeBase && cronograma.LinhaDeBaseEmUso is not null, tela.Hoje);
        _pronta = true;
        PropertyChanged += Mudou;
        Atualizar();
    }

    public string Titulo => $"Imprimir o Gantt — {_cronograma.Nome}";

    // ---- Escolhas ----

    public IReadOnlyList<Escolha<TamanhoDaFolha>> Folhas { get; } =
        [.. Enum.GetValues<TamanhoDaFolha>().Select(f => new Escolha<TamanhoDaFolha>(f, GanttParaImprimir.NomeDa(f)))];

    [ObservableProperty] public partial Escolha<TamanhoDaFolha> FolhaEscolhida { get; set; }

    public IReadOnlyList<Escolha<bool>> Orientacoes { get; } = [new(true, "Paisagem (deitada)"), new(false, "Retrato (em pé)")];

    [ObservableProperty] public partial Escolha<bool> OrientacaoEscolhida { get; set; }

    public IReadOnlyList<Escolha<EscalaDaImpressao>> Escalas { get; } =
    [
        new(EscalaDaImpressao.AjustarALargura, "Ajustar à largura da folha"),
        new(EscalaDaImpressao.Dias, "Dias (várias folhas lado a lado)"),
        new(EscalaDaImpressao.Semanas, "Semanas"),
        new(EscalaDaImpressao.Meses, "Meses"),
    ];

    [ObservableProperty] public partial Escolha<EscalaDaImpressao> EscalaEscolhida { get; set; }

    public IReadOnlyList<Escolha<int?>> Niveis { get; } =
    [
        new(null, "Todas as tarefas"),
        new(ComoNaTela, "Como na tela (sem as tarefas dos resumos recolhidos)"),
        new(1, "Só o 1º nível (etapas)"),
        new(2, "Até o 2º nível"),
        new(3, "Até o 3º nível"),
    ];

    [ObservableProperty] public partial Escolha<int?> NivelEscolhido { get; set; }

    // Colunas da tabela.
    [ObservableProperty] public partial bool ColunaEdt { get; set; } = true;
    [ObservableProperty] public partial bool ColunaNome { get; set; } = true;
    [ObservableProperty] public partial bool ColunaDuracao { get; set; } = true;
    [ObservableProperty] public partial bool ColunaInicio { get; set; } = true;
    [ObservableProperty] public partial bool ColunaTermino { get; set; } = true;
    [ObservableProperty] public partial bool ColunaConcluido { get; set; }
    [ObservableProperty] public partial bool ColunaPredecessoras { get; set; }

    // O desenho.
    [ObservableProperty] public partial bool CaminhoCritico { get; set; }
    [ObservableProperty] public partial bool LinhaDeBase { get; set; }
    [ObservableProperty] public partial bool Vinculos { get; set; } = true;
    [ObservableProperty] public partial bool Hoje { get; set; }
    [ObservableProperty] public partial bool NomesNasBarras { get; set; } = true;
    [ObservableProperty] public partial bool FinsDeSemana { get; set; } = true;
    [ObservableProperty] public partial bool Legenda { get; set; } = true;

    public bool TemLinhaDeBase => _cronograma.LinhaDeBaseEmUso is not null;

    // ---- A prévia ----

    [ObservableProperty] public partial GanttParaImprimir? Impressao { get; private set; }

    /// <summary>A página da prévia (0 = a primeira).</summary>
    [ObservableProperty] public partial int Pagina { get; private set; }

    /// <summary>A página da prévia em PNG.</summary>
    [ObservableProperty] public partial byte[]? Previa { get; private set; }

    /// <summary>"Página 1 de 4".</summary>
    [ObservableProperty] public partial string TextoDaPagina { get; private set; } = "";

    /// <summary>"4 páginas (o tempo em 4 folhas lado a lado) · A4 paisagem".</summary>
    [ObservableProperty] public partial string Resumo { get; private set; } = "";

    [ObservableProperty] public partial string Erro { get; private set; } = "";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    /// <summary>A largura da prévia em pixels (a janela pode mudar).</summary>
    public int LarguraDaPrevia { get; set; } = 1100;

    private static readonly HashSet<string> NaoMudamAImpressao =
        [nameof(Impressao), nameof(Pagina), nameof(Previa), nameof(TextoDaPagina), nameof(Resumo), nameof(Erro), nameof(Mensagem)];

    private void Mudou(object? sender, PropertyChangedEventArgs e)
    {
        if (_pronta && !NaoMudamAImpressao.Contains(e.PropertyName ?? ""))
            Atualizar();
    }

    /// <summary>As opções escolhidas, para o gerador.</summary>
    public OpcoesDaImpressaoDoGantt Opcoes()
    {
        var colunas = new HashSet<ColunaDoGanttImpresso>();
        void Se(bool ligada, ColunaDoGanttImpresso coluna)
        {
            if (ligada)
                colunas.Add(coluna);
        }
        Se(ColunaEdt, ColunaDoGanttImpresso.Edt);
        Se(ColunaNome, ColunaDoGanttImpresso.Nome);
        Se(ColunaDuracao, ColunaDoGanttImpresso.Duracao);
        Se(ColunaInicio, ColunaDoGanttImpresso.Inicio);
        Se(ColunaTermino, ColunaDoGanttImpresso.Termino);
        Se(ColunaConcluido, ColunaDoGanttImpresso.Concluido);
        Se(ColunaPredecessoras, ColunaDoGanttImpresso.Predecessoras);
        var nivel = NivelEscolhido.Valor;
        return new OpcoesDaImpressaoDoGantt
        {
            Folha = FolhaEscolhida.Valor,
            Paisagem = OrientacaoEscolhida.Valor,
            Escala = EscalaEscolhida.Valor,
            Colunas = colunas,
            CaminhoCritico = CaminhoCritico,
            LinhaDeBase = LinhaDeBase,
            Vinculos = Vinculos,
            Hoje = Hoje,
            NomesNasBarras = NomesNasBarras,
            FinsDeSemana = FinsDeSemana,
            Legenda = Legenda,
            NivelMaximo = nivel is > 0 ? nivel : null,
            TarefasOcultas = nivel == ComoNaTela ? _tela.Ocultas : new HashSet<Guid>(),
            Predecessoras = _predecessoras,
            Titulo = _cronograma.Nome,
            Subtitulo = $"gerado em {_relogio.GetLocalNow().DateTime:dd/MM/yyyy HH:mm}",
        };
    }

    /// <summary>Remonta as páginas com as escolhas de agora e volta a prévia à primeira.</summary>
    public void Atualizar()
    {
        try
        {
            Impressao = GanttParaImprimir.Montar(_cronograma, _resultado, Opcoes(), _relogio.GetLocalNow().DateTime);
            (Erro, Resumo) = ("", Impressao.Resumo);
        }
        catch (InvalidOperationException ex)
        {
            (Impressao, Erro, Resumo) = (null, ex.Message, "");
        }
        Pagina = 0;
        Desenhar();
        SalvarPdfCommand.NotifyCanExecuteChanged();
        ImprimirCommand.NotifyCanExecuteChanged();
    }

    private void Desenhar()
    {
        Previa = Impressao?.Previa(Pagina, LarguraDaPrevia);
        TextoDaPagina = Impressao is { } i ? $"Página {Pagina + 1} de {i.Paginas}" : "";
        PaginaAnteriorCommand.NotifyCanExecuteChanged();
        ProximaPaginaCommand.NotifyCanExecuteChanged();
    }

    private bool TemAnterior() => Pagina > 0;

    private bool TemProxima() => Impressao is { } i && Pagina < i.Paginas - 1;

    [RelayCommand(CanExecute = nameof(TemAnterior))]
    private void PaginaAnterior()
    {
        Pagina--;
        Desenhar();
    }

    [RelayCommand(CanExecute = nameof(TemProxima))]
    private void ProximaPagina()
    {
        Pagina++;
        Desenhar();
    }

    // ---- Ações ----

    private bool TemImpressao() => Impressao is not null;

    private string NomeDoArquivo => $"Gantt - {_cronograma.Nome}";

    [RelayCommand(CanExecute = nameof(TemImpressao))]
    private async Task SalvarPdf()
    {
        if (await _dialogos.SalvarComoAsync("Salvar o Gantt em PDF", NomeDoArquivo, "pdf") is not { } caminho)
            return;
        if (await Gravar(Impressao!, caminho))
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
    }

    /// <summary>
    /// Grava o PDF numa pasta temporária e abre-o no leitor do sistema, onde se imprime (o tamanho da folha já vem no PDF;
    /// na impressora, escolher a mesma folha).
    /// </summary>
    [RelayCommand(CanExecute = nameof(TemImpressao))]
    private async Task Imprimir()
    {
        var impressao = Impressao!;
        var pasta = Path.Combine(Path.GetTempPath(), "ProjectPlanner", "Impressao");
        var nome = string.Concat(NomeDoArquivo.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var caminho = Path.Combine(pasta, $"{nome} {_relogio.GetLocalNow().DateTime:yyyyMMdd-HHmmss}.pdf");
        try
        {
            Directory.CreateDirectory(pasta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível preparar a impressão: {ex.Message}";
            return;
        }
        if (!await Gravar(impressao, caminho))
            return;
        await _dialogos.AbrirArquivoAsync(caminho);
        var atalho = AtalhosDeTeclado.EhMac ? "⌘P" : "Ctrl+P";
        Mensagem = $"O PDF abriu no leitor do sistema: imprima com {atalho}, escolhendo a folha {FolhaEscolhida.Texto.Split(' ')[0]} na impressora.";
    }

    // Grava num temporário e troca no fim (um PDF aberto no leitor não fica pela metade).
    private async Task<bool> Gravar(GanttParaImprimir impressao, string caminho)
    {
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    impressao.GerarPdf(destino);
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
