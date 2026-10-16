using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma superalocação na tela.</summary>
public sealed record SuperalocacaoNaTela(string Recurso, string Periodo, string Pico, string Tarefas);

/// <summary>Uma necessidade de um período na tela.</summary>
public sealed record NecessidadeNaTela(string Periodo, string Recurso, string Tipo, string Quantidade, string Custo);

/// <summary>Uma tarefa que o nivelamento muda, na tela.</summary>
public sealed record MudancaNaTela(string Tarefa, string InicioAtual, string InicioNovo, string Atraso, string Motivo);

/// <summary>
/// Bloco 34: Projeto > Uso de recursos…: o histograma de cada recurso (por dia útil, com o disponível), as superalocações e
/// o nivelamento sugerido (só aplica se você aprovar: um passo do Desfazer), e as necessidades por período (equipes,
/// equipamentos e compras, com o custo), exportáveis em CSV.
/// </summary>
public sealed partial class UsoDeRecursosViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly Action<string> _mudou;
    private readonly bool _pronta;
    private IReadOnlyList<UsoDoRecurso> _usos = [];
    private NivelamentoSugerido? _sugestao;

    /// <param name="mudou">O nivelamento foi aplicado (o cronograma recalcula), com o nome para o Desfazer.</param>
    public UsoDeRecursosViewModel(Cronograma cronograma, IDialogosService dialogos, Action<string> mudou)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(mudou);
        (_cronograma, _dialogos, _mudou) = (cronograma, dialogos, mudou);
        PeriodicidadeEscolhida = Periodicidades[0];
        TipoEscolhido = Tipos[0];
        _pronta = true;
        Atualizar();
    }

    public string Titulo => $"Uso de recursos — {_cronograma.Nome}";

    [ObservableProperty] public partial string Erro { get; private set; } = "";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    public bool SemRecursos => _cronograma.Recursos.Vazio;

    public const string TextoSemRecursos = "O projeto não tem recursos: cadastre-os em Projeto > Recursos… e atribua-os às tarefas em Tarefa > Recursos da tarefa….";

    private void Atualizar()
    {
        try
        {
            _usos = UsoDeRecursos.Calcular(_cronograma);
            Erro = "";
        }
        catch (DomainValidationException ex)
        {
            (_usos, Erro) = ([], ex.Message);
        }
        var anterior = RecursoEscolhido?.Valor;
        Recursos = [.. _usos.Select(u => new Escolha<Guid>(u.Recurso.Id,
            $"{u.Recurso.Nome}{(u.AcimaDoLimite.Count > 0 ? " ⚠" : "")}"))];
        RecursoEscolhido = Recursos.FirstOrDefault(r => r.Valor == anterior)
                           ?? Recursos.FirstOrDefault(r => _usos.First(u => u.Recurso.Id == r.Valor).AcimaDoLimite.Count > 0)
                           ?? Recursos.FirstOrDefault();
        Superalocacoes = Erro.Length > 0 ? [] : [.. UsoDeRecursos.Superalocacoes(_cronograma).Select(s => new SuperalocacaoNaTela(s.Recurso.Nome,
            s.De == s.Ate ? s.De.ToString("dd/MM/yyyy", Brasil) : $"{s.De:dd/MM} a {s.Ate:dd/MM/yyyy} ({s.Dias} dias úteis)",
            $"{Numero(s.Pico)} por dia (disponível {Numero(s.Limite)})", string.Join(", ", s.Tarefas)))];
        foreach (var nome in new[] { nameof(Recursos), nameof(Superalocacoes), nameof(SemSuperalocacoes), nameof(SemRecursos) })
            OnPropertyChanged(nome);
        AtualizarNecessidades();
    }

    private static string Numero(decimal valor) => RecursosDoProjetoTexto.Numero(Math.Round(valor, 2, MidpointRounding.AwayFromZero));

    // ---- Histograma ----

    public IReadOnlyList<Escolha<Guid>> Recursos { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Uso), nameof(ResumoDoRecurso))]
    public partial Escolha<Guid>? RecursoEscolhido { get; set; }

    public UsoDoRecurso? Uso => RecursoEscolhido is { } e ? _usos.FirstOrDefault(u => u.Recurso.Id == e.Valor) : null;

    /// <summary>"Pico de 3 por dia · disponível 2 · 3 dias acima · 13 dias de trabalho no projeto".</summary>
    public string ResumoDoRecurso
    {
        get
        {
            if (Uso is not { } uso)
                return "";
            var r = uso.Recurso;
            if (!r.PorDia)
                return $"{Numero(uso.Dias.Sum(d => d.Quantidade))} {r.Unidade} no projeto, de {uso.Dias.FirstOrDefault()?.Dia:dd/MM/yyyy} a {uso.Dias.LastOrDefault()?.Dia:dd/MM/yyyy}";
            return $"Pico de {Numero(uso.Pico)} por dia · " + (r.Disponivel is { } d ? $"disponível {Numero(d)} · " : "sem limite · ") +
                   (uso.AcimaDoLimite.Count == 1 ? "1 dia acima · " : uso.AcimaDoLimite.Count > 1 ? $"{uso.AcimaDoLimite.Count} dias acima · " : "") +
                   $"{Numero(uso.Dias.Sum(x => x.Quantidade))} dias de trabalho no projeto";
        }
    }

    // ---- Superalocações e nivelamento ----

    public IReadOnlyList<SuperalocacaoNaTela> Superalocacoes { get; private set; } = [];

    public bool SemSuperalocacoes => Superalocacoes.Count == 0 && Erro.Length == 0;

    public IReadOnlyList<MudancaNaTela> Mudancas { get; private set; } = [];

    public string TextoDaSugestao { get; private set; } = "";

    public bool TemSugestao => _sugestao is { NadaAMudar: false };

    [RelayCommand]
    private void SugerirNivelamento()
    {
        try
        {
            _sugestao = UsoDeRecursos.Sugerir(_cronograma);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        Mudancas = [.. _sugestao.Mudancas.Select(m => new MudancaNaTela($"{m.Edt} {m.Nome}", m.InicioAtual.ToString("dd/MM/yyyy", Brasil),
            m.InicioNovo.ToString("dd/MM/yyyy", Brasil), m.Dias == 1 ? "1 dia útil" : $"{m.Dias} dias úteis",
            m.Direta ? "adiada para liberar o recurso" : "acompanha a predecessora"))];
        var termino = _sugestao.TerminoAtual == _sugestao.TerminoNovo
            ? $"o término continua em {_sugestao.TerminoNovo:dd/MM/yyyy}"
            : $"o término passa de {_sugestao.TerminoAtual:dd/MM/yyyy} para {_sugestao.TerminoNovo:dd/MM/yyyy}";
        TextoDaSugestao = _sugestao.NadaAMudar
            ? "Nada a mudar: " + (_sugestao.NaoResolvidos.Count == 0 ? "nenhum recurso passa do disponível." : "o que passa não dá para resolver adiando tarefas.")
            : $"{(_sugestao.Mudancas.Count == 1 ? "1 tarefa muda" : $"{_sugestao.Mudancas.Count} tarefas mudam")}; {termino}.";
        if (_sugestao.NaoResolvidos.Count > 0)
            TextoDaSugestao += " Fica sem resolver: " + string.Join("; ", _sugestao.NaoResolvidos) + ".";
        foreach (var nome in new[] { nameof(Mudancas), nameof(TextoDaSugestao), nameof(TemSugestao) })
            OnPropertyChanged(nome);
        AplicarNivelamentoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(TemSugestao))]
    private async Task AplicarNivelamento()
    {
        var sugestao = _sugestao!;
        if (!await _dialogos.ConfirmarAsync("Aplicar o nivelamento",
                $"As tarefas adiadas ganham \"não iniciar antes de\" no novo início. {TextoDaSugestao} Dá para desfazer com Editar > Desfazer.", "Aplicar"))
            return;
        var aplicadas = UsoDeRecursos.Aplicar(_cronograma, sugestao);
        _mudou("Nivelar recursos");
        (_sugestao, Mudancas, TextoDaSugestao) = (null, [], "");
        foreach (var nome in new[] { nameof(Mudancas), nameof(TextoDaSugestao), nameof(TemSugestao) })
            OnPropertyChanged(nome);
        AplicarNivelamentoCommand.NotifyCanExecuteChanged();
        Atualizar();
        Mensagem = $"Nivelamento aplicado: {(aplicadas == 1 ? "1 tarefa ganhou" : $"{aplicadas} tarefas ganharam")} \"não iniciar antes de\".";
    }

    // ---- Necessidades por período ----

    public IReadOnlyList<Escolha<Periodicidade>> Periodicidades { get; } =
    [
        new(Periodicidade.Semanal, "Semanal"),
        new(Periodicidade.Quinzenal, "Quinzenal"),
        new(Periodicidade.Mensal, "Mensal"),
    ];

    [ObservableProperty] public partial Escolha<Periodicidade> PeriodicidadeEscolhida { get; set; }

    public IReadOnlyList<Escolha<TipoDeRecurso?>> Tipos { get; } =
    [
        new(null, "Todos"),
        new(TipoDeRecurso.Profissional, "Profissionais"),
        new(TipoDeRecurso.Material, "Materiais"),
        new(TipoDeRecurso.Equipamento, "Equipamentos"),
    ];

    [ObservableProperty] public partial Escolha<TipoDeRecurso?> TipoEscolhido { get; set; }

    public IReadOnlyList<NecessidadeNaTela> Necessidades { get; private set; } = [];

    public string TotalDasNecessidades { get; private set; } = "";

    partial void OnPeriodicidadeEscolhidaChanged(Escolha<Periodicidade> value) => AtualizarNecessidades();

    partial void OnTipoEscolhidoChanged(Escolha<TipoDeRecurso?> value) => AtualizarNecessidades();

    private IReadOnlyList<NecessidadeNoPeriodo> _necessidades = [];

    private void AtualizarNecessidades()
    {
        if (!_pronta)
            return;
        try
        {
            var dias = _usos.SelectMany(u => u.Dias).Select(d => d.Dia).ToList();
            _necessidades = dias.Count == 0 ? [] :
                UsoDeRecursos.Necessidades(_cronograma, PeriodosFinanceiros.Gerar(dias.Min(), dias.Max(), PeriodicidadeEscolhida.Valor))
                    .Where(n => TipoEscolhido.Valor is not { } tipo || n.Recurso.Tipo == tipo).ToList();
        }
        catch (DomainValidationException)
        {
            _necessidades = [];
        }
        Necessidades = [.. _necessidades.Select(n => new NecessidadeNaTela(n.Periodo.Rotulo, n.Recurso.Nome, TiposDeRecurso.Texto(n.Recurso.Tipo),
            $"{Numero(n.Quantidade)} {(n.Recurso.PorDia ? (n.Quantidade == 1 ? "dia" : "dias") : n.Recurso.Unidade)}", Moeda.Formatar(n.Custo)))];
        TotalDasNecessidades = $"Total: {Moeda.ComSimbolo(_necessidades.Sum(n => n.Custo))}";
        OnPropertyChanged(nameof(Necessidades));
        OnPropertyChanged(nameof(TotalDasNecessidades));
    }

    [RelayCommand]
    private async Task ExportarNecessidades()
    {
        if (await _dialogos.SalvarComoAsync("Exportar as necessidades em CSV", $"Necessidades de recursos - {_cronograma.Nome}", "csv") is not { } caminho)
            return;
        var linhas = _necessidades.Select(n => (IReadOnlyList<string>)[n.Periodo.Rotulo, n.Periodo.Inicio.ToString("dd/MM/yyyy", Brasil),
            n.Periodo.Fim.ToString("dd/MM/yyyy", Brasil), n.Recurso.Nome, TiposDeRecurso.Texto(n.Recurso.Tipo), n.Recurso.Unidade,
            n.Quantidade.ToString("0.####", Brasil), n.Custo.ToString("0.00", Brasil), n.Recurso.Fornecedor ?? ""]);
        try
        {
            await File.WriteAllTextAsync(caminho,
                TabelaCsv.Gerar(["Período", "De", "Até", "Recurso", "Tipo", "Unidade", "Quantidade", "Custo (R$)", "Fornecedor"], linhas), TabelaCsv.Codificacao);
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível salvar: {ex.Message}";
        }
    }
}
