namespace ProjectPlanner.IA;

/// <summary>
/// Bloco 83: o calendário de trabalho do retrato em dados: os dias da semana de trabalho e, no período do projeto, os dias sem
/// trabalho que fogem da semana (feriados, paralisações) e os dias extras (um sábado ou um feriado trabalhado). Com ele, o
/// servidor conta os dias úteis como o programa. Fora do período mandado, vale só a semana.
/// </summary>
public sealed record CalendarioNoContexto(IReadOnlyList<DayOfWeek> DiasDaSemana, IReadOnlyList<DateOnly> SemTrabalho, IReadOnlyList<DateOnly> Extras)
{
    /// <summary>De segunda a sexta, sem feriados: o de um retrato de antes do Bloco 83.</summary>
    public static CalendarioNoContexto SegundaASexta { get; } =
        new([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], [], []);

    private HashSet<DateOnly>? _semTrabalho;
    private HashSet<DateOnly>? _extras;

    public bool EhDiaUtil(DateOnly dia)
    {
        _semTrabalho ??= [.. SemTrabalho];
        _extras ??= [.. Extras];
        return _extras.Contains(dia) || (DiasDaSemana.Contains(dia.DayOfWeek) && !_semTrabalho.Contains(dia));
    }

    /// <summary>Os dias úteis de <paramref name="de"/> a <paramref name="ate"/>, os dois inclusive (zero se ate vem antes de de).</summary>
    public int ContarDiasUteis(DateOnly de, DateOnly ate)
    {
        var n = 0;
        for (var dia = de; dia <= ate; dia = dia.AddDays(1))
            if (EhDiaUtil(dia))
                n++;
        return n;
    }
}

/// <summary>
/// Bloco 83: os números da obra que só o programa calcula, no retrato: o contrato e o medido (as medições aprovadas), as ordens
/// de serviço em andamento e as vencidas e o término da linha de base em uso.
/// </summary>
public sealed record AndamentoNoContexto(decimal ValorContratado, decimal Medido, int OrdensEmAndamento, int OrdensVencidas, DateOnly? TerminoDaLinhaDeBase);

/// <summary>Bloco 83: como uma tarefa do retrato está num dia (as mesmas regras do AcompanhamentoDoCronograma do programa).</summary>
public enum SituacaoNoDia
{
    NaoIniciada,
    EmDia,
    Atrasada,
    Concluida,
}

/// <summary>
/// Bloco 83: o andamento das tarefas do retrato num dia, pelas regras do programa (AcompanhamentoDoCronograma): o previsto conta
/// os dias úteis decorridos até o começo do dia (no primeiro dia de uma tarefa, o previsto ainda é 0%); a tarefa está atrasada
/// quando o % feito está abaixo do previsto; um marco, quando o dia dele já passou e ele não foi concluído.
/// </summary>
public static class AndamentoNoDia
{
    /// <summary>O % que a tarefa devia ter no começo do dia.</summary>
    public static int Previsto(TarefaNoContexto tarefa, CalendarioNoContexto calendario, DateOnly dia)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        ArgumentNullException.ThrowIfNull(calendario);
        if (tarefa.Tipo == TipoNoContexto.Marco || tarefa.DuracaoDias <= 0)
            return dia > tarefa.Inicio ? 100 : 0;
        if (dia <= tarefa.Inicio)
            return 0;
        if (dia > tarefa.Termino)
            return 100;
        return Math.Min(100, (int)Math.Floor(100.0 * calendario.ContarDiasUteis(tarefa.Inicio, dia.AddDays(-1)) / tarefa.DuracaoDias));
    }

    public static SituacaoNoDia Situacao(TarefaNoContexto tarefa, CalendarioNoContexto calendario, DateOnly dia)
    {
        var previsto = Previsto(tarefa, calendario, dia);
        var feito = tarefa.PercentualConcluido;
        return feito >= 100 ? SituacaoNoDia.Concluida
            : feito < previsto ? SituacaoNoDia.Atrasada
            : feito == 0 && previsto == 0 ? SituacaoNoDia.NaoIniciada
            : SituacaoNoDia.EmDia;
    }
}
