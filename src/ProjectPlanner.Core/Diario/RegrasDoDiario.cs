using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Diario;

/// <summary>
/// Bloco 81: o diário ligado ao resto do projeto: o dia sugerido pelo cronograma (os serviços previstos, o efetivo e os
/// equipamentos deles, e o clima pelo calendário), levar o % dos serviços ao cronograma e marcar no calendário um dia que não deu
/// para trabalhar.
/// </summary>
public static class RegrasDoDiario
{
    /// <summary>
    /// O dia como o cronograma prevê: os serviços em execução nele (as tarefas sem subtarefas abaixo de 100% cujas datas calculadas
    /// passam pelo dia, com o % de agora), o efetivo e os equipamentos delas (as atribuições por dia, somadas por recurso, com o
    /// fornecedor como empresa) e o clima: um dia parado no calendário fica impraticável (chuva, se o motivo fala de chuva).
    /// </summary>
    public static DiaDaObra Sugerir(Cronograma cronograma, DateOnly data)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var dia = data.ToDateTime(TimeOnly.MinValue);
        var edt = cronograma.NumeracaoEdt();
        var tarefas = cronograma.Tarefas.Where(t => !t.EhResumo && t.PercentualConcluido < 100
                                                    && (t.InicioMaisCedo ?? t.DataInicio).Date <= dia && (t.TerminoMaisCedo ?? t.DataTermino).Date >= dia).ToList();
        var porRecurso = tarefas.SelectMany(t => t.Atribuicoes).GroupBy(a => a.RecursoId)
            .Select(g => (Recurso: cronograma.Recursos.Obter(g.Key), Quantidade: g.Sum(a => a.Quantidade)))
            .Where(x => x.Recurso is not null).ToList();
        List<T> Dos<T>(TipoDeRecurso tipo, Func<Recurso, int, T> linha) =>
            [.. porRecurso.Where(x => x.Recurso!.Tipo == tipo).Select(x => linha(x.Recurso!, Math.Clamp((int)Math.Ceiling(x.Quantidade), 1, 999)))];

        var motivo = cronograma.Calendario.EhDiaUtil(data) ? null : cronograma.Calendario.MotivoSemTrabalho(data);
        var clima = motivo is null ? ClimaDoPeriodo.Bom
            : new ClimaDoPeriodo(motivo.Contains("chuv", StringComparison.OrdinalIgnoreCase) ? TempoNoPeriodo.Chuva : TempoNoPeriodo.Bom, Praticavel: false);
        return new DiaDaObra(data)
        {
            Manha = clima,
            Tarde = clima,
            Atividades = [.. tarefas.Select(t => new AtividadeNoDia(t.Id, $"{edt[t.Id]} {t.Nome}", t.PercentualConcluido))],
            Efetivo = Dos(TipoDeRecurso.Profissional, (r, q) => new EfetivoNoDia(r.Nome, q, r.Fornecedor)),
            Equipamentos = Dos(TipoDeRecurso.Equipamento, (r, q) => new EquipamentoNoDia(r.Nome, q)),
            Ocorrencias = motivo is null ? null : $"Sem trabalho no calendário: {motivo}.",
        };
    }

    /// <summary>
    /// Leva ao cronograma o % dos serviços do dia que são tarefas (sem subtarefas) e têm %; os outros ficam como estão.
    /// </summary>
    /// <returns>As tarefas que mudaram ("2.1 Alvenaria: 40% → 55%").</returns>
    public static IReadOnlyList<string> LevarOPercentual(Cronograma cronograma, DateOnly data)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var dia = cronograma.Diario.Dia(data) ?? throw new DomainValidationException(nameof(DiarioDaObra.Dias), $"O diário não tem o dia {data:dd/MM/yyyy}.");
        var edt = cronograma.NumeracaoEdt();
        var mudaram = new List<string>();
        foreach (var atividade in dia.Atividades)
        {
            if (atividade is not { TarefaId: { } id, Percentual: { } percentual }
                || cronograma.Tarefas.FirstOrDefault(t => t.Id == id) is not { EhResumo: false } tarefa || tarefa.PercentualConcluido == percentual)
                continue;
            mudaram.Add($"{edt[tarefa.Id]} {tarefa.Nome}: {tarefa.PercentualConcluido}% → {percentual}%");
            tarefa.AtualizarPercentualConcluido(percentual);
        }
        return mudaram;
    }

    /// <summary>Marca o dia sem trabalho no calendário (o cronograma anda um dia útil), com o motivo e o número do RDO.</summary>
    /// <exception cref="DomainValidationException">O dia não está no diário, ou já não é dia de trabalho.</exception>
    public static void MarcarSemTrabalho(Cronograma cronograma, DateOnly data)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var dia = cronograma.Diario.Dia(data) ?? throw new DomainValidationException(nameof(DiarioDaObra.Dias), $"O diário não tem o dia {data:dd/MM/yyyy}.");
        if (!cronograma.Calendario.EhDiaUtil(data))
            throw new DomainValidationException(nameof(Cronograma.Calendario), $"{data:dd/MM/yyyy} já não é dia de trabalho no calendário.");
        var motivo = dia.Manha.Tempo == TempoNoPeriodo.Chuva || dia.Tarde.Tempo == TempoNoPeriodo.Chuva ? "Chuva" : "Sem trabalho";
        cronograma.DefinirCalendario(cronograma.Calendario.ComExcecao(
            new ExcecaoDoCalendario(data, data, $"{motivo} (RDO nº {cronograma.Diario.NumeroDe(data)})", Trabalha: false)));
    }
}
