namespace ProjectPlanner.Core;

/// <summary>Como uma tarefa está numa data de status (Bloco 25, C2).</summary>
public enum SituacaoDaTarefa
{
    /// <summary>Ainda não devia ter começado (ou começa no próprio dia) e está em 0%.</summary>
    NaoIniciada,

    /// <summary>O % concluído está em dia com o previsto.</summary>
    EmDia,

    /// <summary>O % concluído está abaixo do previsto para a data (inclui o que já devia ter terminado).</summary>
    Atrasada,

    Concluida,
}

/// <summary>A situação de uma tarefa e o % que ela devia ter na data de status.</summary>
public sealed record AndamentoDaTarefa(SituacaoDaTarefa Situacao, int PercentualPrevisto)
{
    public string Texto => Situacao switch
    {
        SituacaoDaTarefa.NaoIniciada => "Não iniciada",
        SituacaoDaTarefa.EmDia => "Em dia",
        SituacaoDaTarefa.Atrasada => "Atrasada",
        _ => "Concluída",
    };
}

/// <summary>
/// O andamento do cronograma numa data de status (Bloco 25, C2), a partir das datas calculadas e do % concluído
/// informado. O previsto conta os dias úteis decorridos até o começo do dia de status: no primeiro dia de uma tarefa,
/// o previsto ainda é 0%, e ela não aparece atrasada só por não ter começado de manhã.
/// </summary>
/// <remarks>
/// Tarefa comum: concluída em 100%; atrasada se o informado está abaixo do previsto; não iniciada em 0% com previsto 0%;
/// senão, em dia. Marco: concluído em 100%; atrasado se o dia dele já passou; senão, não iniciado. Resumo: concluído se
/// todas as subtarefas estão; atrasado se alguma está; não iniciado se nenhuma começou; senão, em dia (o previsto é a
/// média das subtarefas comuns, ponderada pela duração).
/// </remarks>
public static class AcompanhamentoDoCronograma
{
    /// <summary>O andamento de cada tarefa calculada; tarefas sem cálculo ficam de fora.</summary>
    public static IReadOnlyDictionary<Guid, AndamentoDaTarefa> Calcular(Cronograma cronograma, DateTime dataDeStatus)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var data = dataDeStatus.Date;
        var andamento = new Dictionary<Guid, AndamentoDaTarefa>();
        foreach (var tarefa in cronograma.Tarefas.Where(t => !t.EhResumo && t.InicioMaisCedo is not null))
            andamento[tarefa.Id] = tarefa.EhMarco ? DoMarco(tarefa, data) : DaTarefa(tarefa, data);

        // De trás para a frente (pré-ordem invertida), cada resumo vem depois das subtarefas dele.
        foreach (var resumo in cronograma.Tarefas.Reverse().Where(t => t.EhResumo))
        {
            var filhas = cronograma.Tarefas.Where(t => t.TarefaPaiId == resumo.Id && andamento.ContainsKey(t.Id)).ToList();
            if (filhas.Count > 0)
                andamento[resumo.Id] = DoResumo(filhas.Select(f => (f, andamento[f.Id])).ToList());
        }
        return andamento;
    }

    private static AndamentoDaTarefa DaTarefa(Tarefa tarefa, DateTime data)
    {
        var (inicio, termino) = (tarefa.InicioMaisCedo!.Value.Date, tarefa.TerminoMaisCedo!.Value.Date);
        var previsto = data <= inicio ? 0
            : data > termino ? 100
            : (int)Math.Floor(100.0 * CalendarioDeTrabalho.ContarDiasUteis(inicio, data.AddDays(-1)) / tarefa.Duracao);
        var informado = tarefa.PercentualConcluido;
        var situacao = informado == 100 ? SituacaoDaTarefa.Concluida
            : informado < previsto ? SituacaoDaTarefa.Atrasada
            : informado == 0 && previsto == 0 ? SituacaoDaTarefa.NaoIniciada
            : SituacaoDaTarefa.EmDia;
        return new AndamentoDaTarefa(situacao, previsto);
    }

    private static AndamentoDaTarefa DoMarco(Tarefa marco, DateTime data)
    {
        var passou = data > marco.InicioMaisCedo!.Value.Date;
        if (marco.PercentualConcluido == 100)
            return new AndamentoDaTarefa(SituacaoDaTarefa.Concluida, passou ? 100 : 0);
        return passou ? new AndamentoDaTarefa(SituacaoDaTarefa.Atrasada, 100) : new AndamentoDaTarefa(SituacaoDaTarefa.NaoIniciada, 0);
    }

    private static AndamentoDaTarefa DoResumo(IReadOnlyList<(Tarefa Tarefa, AndamentoDaTarefa Andamento)> filhas)
    {
        var comuns = filhas.Where(f => f.Tarefa.Duracao > 0).ToList();
        var duracao = comuns.Sum(f => f.Tarefa.Duracao);
        var previsto = duracao == 0 ? 0 : (int)Math.Floor((double)comuns.Sum(f => f.Tarefa.Duracao * f.Andamento.PercentualPrevisto) / duracao);
        var situacoes = filhas.Select(f => f.Andamento.Situacao).ToList();
        var situacao = situacoes.All(s => s == SituacaoDaTarefa.Concluida) ? SituacaoDaTarefa.Concluida
            : situacoes.Contains(SituacaoDaTarefa.Atrasada) ? SituacaoDaTarefa.Atrasada
            : situacoes.All(s => s == SituacaoDaTarefa.NaoIniciada) ? SituacaoDaTarefa.NaoIniciada
            : SituacaoDaTarefa.EmDia;
        return new AndamentoDaTarefa(situacao, previsto);
    }
}
