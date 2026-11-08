using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 71: Projeto > Calendário de trabalho…: a tela, a troca do calendário (um passo do Desfazer, com o recálculo) e o
// calendário dos projetos novos.
public sealed partial class MainWindowViewModel
{
    private readonly IPreferenciasDoCalendario? _preferenciasDoCalendario;

    [RelayCommand]
    private async Task CalendarioDeTrabalho()
    {
        var tela = new CalendarioViewModel(_cronograma.Calendario, _cronograma.DataInicio, Tarefas.Count == 0 ? null : TerminoProjeto, TerminoCom,
            podeUsarNosNovos: _preferenciasDoCalendario is not null);
        await _dialogos.MostrarCalendarioAsync(tela);
        if (tela.Resultado is not { } novo)
            return;
        if (tela.UsarNosProjetosNovos)
            _preferenciasDoCalendario?.Salvar(novo);
        var mudou = AplicarCalendario(novo);
        if (tela.UsarNosProjetosNovos)
            Mensagem = mudou ? $"{Mensagem} Vale também nos projetos novos." : "O calendário passa a valer nos projetos novos.";
    }

    /// <summary>
    /// Troca o calendário do projeto e recalcula: numa cópia, que entra no lugar se o cálculo fecha (um passo do Desfazer,
    /// "Calendário de trabalho"). Também pela IA (as atualizações do tipo calendario).
    /// </summary>
    /// <returns>Se mudou.</returns>
    public bool AplicarCalendario(CalendarioDoProjeto novo)
    {
        ArgumentNullException.ThrowIfNull(novo);
        if (novo.Equals(_cronograma.Calendario))
        {
            Mensagem = "O calendário de trabalho não mudou.";
            return false;
        }
        var copia = _cronograma.Copiar();
        copia.DefinirCalendario(novo);
        try
        {
            _calculadora.Calcular(copia);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = $"O calendário não foi aplicado: {ex.Message}";
            return false;
        }
        var antes = Tarefas.Count == 0 ? null : TerminoProjeto;
        TrocarCronograma(copia, registrarComo: "Calendário de trabalho");
        Mensagem = $"Calendário de trabalho: {novo.Descricao}." + (antes is { } a && TerminoProjeto is { } d && a.Date != d.Date
            ? $" O término passou de {a:dd/MM/yyyy} para {d:dd/MM/yyyy}."
            : "");
        return true;
    }

    /// <summary>
    /// Para o retrato da IA: o calendário e os dias fora da regra do início do projeto até 60 dias depois do término (no máximo
    /// <see cref="DiasDoCalendarioNoRetrato"/> de cada tipo), para ela não contar um feriado como dia de trabalho.
    /// </summary>
    public static IReadOnlyList<string> CalendarioParaOAssistente(CalendarioDoProjeto calendario, DateOnly inicio, DateOnly termino)
    {
        var ate = termino.AddDays(60) > inicio.AddDays(365) ? termino.AddDays(60) : inicio.AddDays(365);
        static string Dia((DateOnly Dia, string Motivo) d) => $"{d.Dia:dd/MM/yyyy} ({CalendarioDoProjeto.NomeDoDia(d.Dia.DayOfWeek)[..3]}) {d.Motivo}";
        var linhas = new List<string> { calendario.Descricao };
        var sem = calendario.DiasSemTrabalho(inicio, ate);
        if (sem.Count > 0)
            linhas.Add("Dias sem trabalho no período (além das folgas da semana): " + string.Join("; ", sem.Take(DiasDoCalendarioNoRetrato).Select(Dia)) +
                       (sem.Count > DiasDoCalendarioNoRetrato ? $"; e mais {sem.Count - DiasDoCalendarioNoRetrato}" : "") + ".");
        var extras = calendario.DiasExtras(inicio, ate);
        if (extras.Count > 0)
            linhas.Add("Dias extras de trabalho: " + string.Join("; ", extras.Take(DiasDoCalendarioNoRetrato).Select(Dia)) + ".");
        return linhas;
    }

    public const int DiasDoCalendarioNoRetrato = 40;

    /// <summary>
    /// Bloco 83: o calendário em dados no retrato (para o servidor contar os dias úteis como o programa nos comandos do Telegram):
    /// os dias da semana e as exceções do início do projeto até um ano depois do término ou de hoje, o que vier depois.
    /// </summary>
    public static CalendarioNoContexto DiasUteisParaOAssistente(CalendarioDoProjeto calendario, DateOnly inicio, DateOnly termino, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(calendario);
        var ate = (termino > hoje ? termino : hoje).AddDays(365);
        return new CalendarioNoContexto([.. calendario.DiasDeTrabalho], [.. calendario.DiasSemTrabalho(inicio, ate).Select(d => d.Dia)],
            [.. calendario.DiasExtras(inicio, ate).Select(d => d.Dia)]);
    }

    // O término do projeto com outro calendário, numa cópia (para a prévia da tela); nulo sem tarefas ou se não fecha.
    private DateTime? TerminoCom(CalendarioDoProjeto calendario)
    {
        if (Tarefas.Count == 0)
            return null;
        var copia = _cronograma.Copiar();
        copia.DefinirCalendario(calendario);
        try
        {
            return CalculadoraCronograma.Calcular(copia).DataTermino;
        }
        catch (DomainValidationException)
        {
            return null;
        }
    }
}
