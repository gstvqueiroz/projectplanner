using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 71: um dia da semana na tela do calendário, com a marca de dia de trabalho.</summary>
public sealed partial class DiaDaSemanaNaTela(DayOfWeek dia, bool marcado, Action mudou) : ObservableObject
{
    public DayOfWeek Dia { get; } = dia;

    /// <summary>"Segunda", "Sábado".</summary>
    public string Nome { get; } = char.ToUpperInvariant(CalendarioDoProjeto.NomeDoDia(dia)[0]) + CalendarioDoProjeto.NomeDoDia(dia)[1..];

    [ObservableProperty] public partial bool Marcado { get; set; } = marcado;

    partial void OnMarcadoChanged(bool value) => mudou();
}

/// <summary>Bloco 71: um feriado do projeto na lista ("20/01 (todo ano) — São Sebastião").</summary>
public sealed record FeriadoNaTela(FeriadoDoCalendario Feriado)
{
    public string Texto => Feriado.TodoAno
        ? $"{Feriado.Data.ToString("dd/MM", CultureInfo.InvariantCulture)} (todo ano) — {Feriado.Nome}"
        : $"{Feriado.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} ({CalendarioDoProjeto.NomeDoDia(Feriado.Data.DayOfWeek)}) — {Feriado.Nome}";
}

/// <summary>Bloco 71: um período de exceção na lista ("05/10/2026 a 07/10/2026 — Chuva (sem trabalho)").</summary>
public sealed record ExcecaoNaTela(ExcecaoDoCalendario Excecao)
{
    public string Texto
    {
        get
        {
            var e = Excecao;
            var periodo = e.De == e.Ate ? CalendarioViewModel.Dia(e.De) : $"{CalendarioViewModel.Dia(e.De)} a {CalendarioViewModel.Dia(e.Ate)}";
            return $"{periodo} — {e.Motivo} ({(e.Trabalha ? "com trabalho" : "sem trabalho")})";
        }
    }
}

/// <summary>
/// Bloco 71: Projeto > Calendário de trabalho…: os dias da semana de trabalho, os feriados nacionais (e o Carnaval e o Corpus
/// Christi), os feriados do projeto e os períodos de exceção (chuva, paralisação, recesso, um sábado trabalhado). Mostra o
/// calendário que vai valer, os dias sem trabalho no período do projeto e o que acontece com o término; Aplicar devolve o
/// calendário novo em <see cref="Resultado"/>, e quem abriu a tela o aplica (um passo do Desfazer).
/// </summary>
public sealed partial class CalendarioViewModel : ViewModelBase
{
    public const int DiasNaPrevia = 60;
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly CalendarioDoProjeto _original;
    private readonly Func<CalendarioDoProjeto, DateTime?> _terminoCom;
    private readonly DateTime? _terminoAtual;
    private readonly DateOnly _inicio;
    private bool _montando = true;

    /// <param name="terminoCom">O término do projeto com um calendário (calculado numa cópia); nulo se não calcula.</param>
    public CalendarioViewModel(CalendarioDoProjeto atual, DateTime inicioDoProjeto, DateTime? terminoDoProjeto, Func<CalendarioDoProjeto, DateTime?> terminoCom,
        bool podeUsarNosNovos = true)
    {
        ArgumentNullException.ThrowIfNull(atual);
        ArgumentNullException.ThrowIfNull(terminoCom);
        (_original, _terminoCom, _terminoAtual, _inicio, PodeUsarNosNovos) =
            (atual, terminoCom, terminoDoProjeto, DateOnly.FromDateTime(inicioDoProjeto), podeUsarNosNovos);
        Dias = [.. CalendarioDoProjeto.Semana.Select(d => new DiaDaSemanaNaTela(d, atual.DiasDeTrabalho.Contains(d), Atualizar))];
        FeriadosNacionais = atual.FeriadosNacionais;
        PontosFacultativos = atual.PontosFacultativos;
        foreach (var f in atual.Feriados)
            Feriados.Add(new FeriadoNaTela(f));
        foreach (var e in atual.Excecoes)
            Excecoes.Add(new ExcecaoNaTela(e));
        DataDoFeriado = null;
        _montando = false;
        Atualizar();
    }

    public IReadOnlyList<DiaDaSemanaNaTela> Dias { get; }

    [ObservableProperty] public partial bool FeriadosNacionais { get; set; }

    [ObservableProperty] public partial bool PontosFacultativos { get; set; }

    partial void OnFeriadosNacionaisChanged(bool value) => Atualizar();

    partial void OnPontosFacultativosChanged(bool value) => Atualizar();

    public ObservableCollection<FeriadoNaTela> Feriados { get; } = [];

    public ObservableCollection<ExcecaoNaTela> Excecoes { get; } = [];

    // ---- Um feriado novo ----

    [ObservableProperty] public partial DateTime? DataDoFeriado { get; set; }

    [ObservableProperty] public partial string NomeDoFeriado { get; set; } = "";

    [ObservableProperty] public partial bool FeriadoTodoAno { get; set; }

    [RelayCommand]
    private void AcrescentarFeriado()
    {
        if (DataDoFeriado is not { } data)
        {
            Mensagem = "Escolha a data do feriado.";
            return;
        }
        if (Tentar(c => c.ComFeriado(new FeriadoDoCalendario(DateOnly.FromDateTime(data), NomeDoFeriado, FeriadoTodoAno))) is { } novo)
        {
            Trocar(Feriados, novo.Feriados.Select(f => new FeriadoNaTela(f)));
            (DataDoFeriado, NomeDoFeriado, FeriadoTodoAno) = (null, "", false);
        }
    }

    [RelayCommand]
    private void RemoverFeriado(FeriadoNaTela? feriado)
    {
        if (feriado is not null && Feriados.Remove(feriado))
            Atualizar();
    }

    // ---- Um período de exceção novo ----

    [ObservableProperty] public partial DateTime? DeDaExcecao { get; set; }

    [ObservableProperty] public partial DateTime? AteDaExcecao { get; set; }

    [ObservableProperty] public partial string MotivoDaExcecao { get; set; } = "";

    /// <summary>Falso: sem trabalho (chuva, paralisação, recesso); verdadeiro: com trabalho (um sábado ou feriado trabalhado).</summary>
    [ObservableProperty] public partial bool ExcecaoComTrabalho { get; set; }

    [RelayCommand]
    private void AcrescentarExcecao()
    {
        if (DeDaExcecao is not { } de)
        {
            Mensagem = "Escolha o primeiro dia do período.";
            return;
        }
        var ate = AteDaExcecao ?? de;
        if (Tentar(c => c.ComExcecao(new ExcecaoDoCalendario(DateOnly.FromDateTime(de), DateOnly.FromDateTime(ate), MotivoDaExcecao, ExcecaoComTrabalho))) is { } novo)
        {
            Trocar(Excecoes, novo.Excecoes.Select(e => new ExcecaoNaTela(e)));
            (DeDaExcecao, AteDaExcecao, MotivoDaExcecao, ExcecaoComTrabalho) = (null, null, "", false);
        }
    }

    [RelayCommand]
    private void RemoverExcecao(ExcecaoNaTela? excecao)
    {
        if (excecao is not null && Excecoes.Remove(excecao))
            Atualizar();
    }

    /// <summary>O recesso de fim de ano (24/12 a 01/01) de cada virada de ano do projeto (ao menos a do primeiro ano).</summary>
    [RelayCommand]
    private void AcrescentarRecesso()
    {
        var fim = DateOnly.FromDateTime(_terminoAtual ?? _inicio.ToDateTime(TimeOnly.MinValue));
        var calendario = Calendario ?? _original;
        try
        {
            for (var ano = _inicio.Year; ano <= Math.Max(_inicio.Year, fim.Year) && ano < 9999; ano++)
            {
                var recesso = new ExcecaoDoCalendario(new DateOnly(ano, 12, 24), new DateOnly(ano + 1, 1, 1), "Recesso de fim de ano", false);
                if (!calendario.Excecoes.Contains(recesso))
                    calendario = calendario.ComExcecao(recesso);
            }
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        Trocar(Excecoes, calendario.Excecoes.Select(e => new ExcecaoNaTela(e)));
    }

    // ---- O calendário que vai valer ----

    /// <summary>O calendário da tela; nulo enquanto não vale (sem nenhum dia de trabalho).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AplicarCommand))]
    public partial CalendarioDoProjeto? Calendario { get; private set; }

    [ObservableProperty] public partial string Descricao { get; private set; } = "";

    /// <summary>"O término do projeto passa de 30/11/2026 para 04/12/2026."</summary>
    [ObservableProperty] public partial string Impacto { get; private set; } = "";

    /// <summary>Os feriados, as paralisações e os dias extras de trabalho no período do projeto, em ordem.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> DiasForaDaRegra { get; private set; } = [];

    [ObservableProperty] public partial string Mensagem { get; set; } = "";

    [ObservableProperty] public partial bool UsarNosProjetosNovos { get; set; }

    public bool PodeUsarNosNovos { get; }

    public bool Mudou => Calendario is { } c && !c.Equals(_original);

    /// <summary>O calendário aplicado; nulo se a tela foi fechada sem aplicar.</summary>
    public CalendarioDoProjeto? Resultado { get; private set; }

    /// <summary>Pede à janela para fechar (depois de aplicar).</summary>
    public event Action? FecharPedido;

    private bool PodeAplicar() => Calendario is not null;

    [RelayCommand(CanExecute = nameof(PodeAplicar))]
    private void Aplicar()
    {
        Resultado = Calendario;
        FecharPedido?.Invoke();
    }

    /// <summary>Volta para o padrão de segunda a sexta com os feriados nacionais (sem os do projeto e sem exceções).</summary>
    [RelayCommand]
    private void UsarOCalendarioDoBrasil()
    {
        _montando = true;
        foreach (var d in Dias)
            d.Marcado = CalendarioDoProjeto.Brasil.DiasDeTrabalho.Contains(d.Dia);
        (FeriadosNacionais, PontosFacultativos) = (true, true);
        Feriados.Clear();
        Excecoes.Clear();
        _montando = false;
        Atualizar();
    }

    private void Atualizar()
    {
        if (_montando)
            return;
        try
        {
            Calendario = CalendarioDoProjeto.Criar(Dias.Where(d => d.Marcado).Select(d => d.Dia), FeriadosNacionais, PontosFacultativos,
                Feriados.Select(f => f.Feriado), Excecoes.Select(e => e.Excecao));
            Mensagem = "";
        }
        catch (DomainValidationException ex)
        {
            Calendario = null;
            Descricao = "";
            Impacto = "";
            DiasForaDaRegra = [];
            Mensagem = ex.Message;
            return;
        }
        var calendario = Calendario;
        Descricao = calendario.Descricao;
        var novo = _terminoCom(calendario);
        Impacto = _terminoAtual is not { } antes || novo is not { } depois ? ""
            : antes.Date == depois.Date ? $"O término do projeto continua em {Dia(antes)}."
            : $"O término do projeto passa de {Dia(antes)} para {Dia(depois)}.";
        var ate = DateOnly.FromDateTime(new[] { novo ?? DateTime.MinValue, _terminoAtual ?? DateTime.MinValue, _inicio.AddDays(365).ToDateTime(TimeOnly.MinValue) }.Max());
        var lista = calendario.DiasSemTrabalho(_inicio, ate).Select(d => (d.Dia, Texto: $"{Dia(d.Dia)} ({CalendarioDoProjeto.NomeDoDia(d.Dia.DayOfWeek)}) — {d.Motivo}"))
            .Concat(calendario.DiasExtras(_inicio, ate).Select(d => (d.Dia, Texto: $"{Dia(d.Dia)} ({CalendarioDoProjeto.NomeDoDia(d.Dia.DayOfWeek)}) — {d.Motivo} (com trabalho)")))
            .OrderBy(d => d.Dia).Select(d => d.Texto).ToList();
        DiasForaDaRegra = lista.Count <= DiasNaPrevia ? lista : [.. lista.Take(DiasNaPrevia), $"… e mais {lista.Count - DiasNaPrevia}"];
        OnPropertyChanged(nameof(Mudou));
    }

    // Aplica uma mudança no calendário de agora; se o domínio recusar, só mostra o motivo.
    private CalendarioDoProjeto? Tentar(Func<CalendarioDoProjeto, CalendarioDoProjeto> mudanca)
    {
        try
        {
            return mudanca(Calendario ?? CalendarioDoProjeto.Criar(CalendarioDoProjeto.Semana, FeriadosNacionais, PontosFacultativos,
                Feriados.Select(f => f.Feriado), Excecoes.Select(e => e.Excecao)));
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return null;
        }
    }

    private void Trocar<T>(ObservableCollection<T> lista, IEnumerable<T> itens)
    {
        _montando = true;
        lista.Clear();
        foreach (var item in itens)
            lista.Add(item);
        _montando = false;
        Atualizar();
    }

    internal static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", Brasil);

    internal static string Dia(DateTime dia) => dia.ToString("dd/MM/yyyy", Brasil);
}
