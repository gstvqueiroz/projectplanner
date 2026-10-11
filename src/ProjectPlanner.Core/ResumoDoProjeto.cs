using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.Core;

/// <summary>Uma tarefa citada no resumo do projeto (atrasada ou próximo marco).</summary>
public sealed record TarefaNoResumo(string Edt, string Nome, DateTime Data, int PercentualConcluido, int PercentualPrevisto);

/// <summary>
/// O resumo do projeto numa data (Bloco 25, C7): andamento geral, contagens, atrasadas e próximos marcos. Conta só as
/// tarefas de trabalho (não os resumos), e o % geral é a média ponderada pela duração, como o de um resumo.
/// </summary>
public sealed record ResumoDoProjeto(
    string Nome,
    DateTime Inicio,
    DateTime? Termino,
    int DiasUteis,
    int PercentualConcluido,
    int PercentualPrevisto,
    int Tarefas,
    int Resumos,
    int Marcos,
    int Concluidas,
    int EmDia,
    int NaoIniciadas,
    int Atrasadas,
    int Criticas,
    int Fotos,
    IReadOnlyList<TarefaNoResumo> ListaDeAtrasadas,
    IReadOnlyList<TarefaNoResumo> ProximosMarcos)
{
    /// <summary>Bloco 31: as tarefas (e resumos) que terminam depois do prazo, na ordem da EDT; a data é o prazo.</summary>
    public IReadOnlyList<TarefaNoResumo> ForaDoPrazo { get; init; } = [];

    /// <summary>Bloco 31: o término do projeto na linha de base em uso; nulo sem linha de base.</summary>
    public DateTime? TerminoDaLinhaDeBase { get; init; }

    /// <summary>Bloco 31: dias úteis do término previsto (linha de base) ao término atual; positivo: atrasou.</summary>
    public int? DesvioDoTermino { get; init; }

    /// <summary>Bloco 28 (FT5): as tarefas em andamento sem foto recente, na ordem da EDT.</summary>
    public IReadOnlyList<TarefaSemFotoRecente> SemFotoRecente { get; init; } = [];

    /// <summary>Até quantos dias à frente entram os próximos marcos.</summary>
    public const int DiasDosProximosMarcos = 30;

    /// <summary>
    /// O resumo do cronograma já calculado, na data de status. <paramref name="andamento"/> é o de
    /// <see cref="AcompanhamentoDoCronograma.Calcular"/> na mesma data.
    /// </summary>
    public static ResumoDoProjeto Calcular(Cronograma cronograma, IReadOnlyDictionary<Guid, AndamentoDaTarefa> andamento, DateTime dataDeStatus)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(andamento);
        var hoje = dataDeStatus.Date;
        var edt = cronograma.NumeracaoEdt();
        var folhas = cronograma.Tarefas.Where(t => !t.EhResumo).ToList();
        var trabalho = folhas.Where(t => !t.EhMarco).ToList();
        var duracao = trabalho.Sum(t => t.Duracao);
        int Ponderado(Func<Tarefa, int> percentual) =>
            duracao == 0 ? 0 : (int)Math.Floor((double)trabalho.Sum(t => t.Duracao * percentual(t)) / duracao);
        SituacaoDaTarefa? Situacao(Tarefa t) => andamento.TryGetValue(t.Id, out var a) ? a.Situacao : null;
        TarefaNoResumo Citada(Tarefa t) => new(edt[t.Id], t.Nome, (t.EhMarco ? t.InicioMaisCedo : t.TerminoMaisCedo) ?? t.DataTermino,
            t.PercentualConcluido, andamento.TryGetValue(t.Id, out var a) ? a.PercentualPrevisto : 0);

        var termino = folhas.Select(t => t.TerminoMaisCedo).Where(d => d is not null).DefaultIfEmpty(null).Max();
        var atrasadas = folhas.Where(t => Situacao(t) == SituacaoDaTarefa.Atrasada).ToList();
        var marcos = folhas.Where(t => t.EhMarco).ToList();
        var proximos = marcos
            .Where(m => m.PercentualConcluido < 100 && m.InicioMaisCedo is { } dia && dia.Date >= hoje && dia.Date <= hoje.AddDays(DiasDosProximosMarcos))
            .OrderBy(m => m.InicioMaisCedo)
            .Select(Citada).ToList();

        return new ResumoDoProjeto(
            cronograma.Nome,
            cronograma.DataInicio,
            termino,
            termino is { } fim ? CalendarioDeTrabalho.ContarDiasUteis(cronograma.DataInicio, fim) : 0,
            Ponderado(t => t.PercentualConcluido),
            Ponderado(t => andamento.TryGetValue(t.Id, out var a) ? a.PercentualPrevisto : 0),
            trabalho.Count,
            cronograma.Tarefas.Count(t => t.EhResumo),
            marcos.Count,
            folhas.Count(t => Situacao(t) == SituacaoDaTarefa.Concluida),
            folhas.Count(t => Situacao(t) == SituacaoDaTarefa.EmDia),
            folhas.Count(t => Situacao(t) == SituacaoDaTarefa.NaoIniciada),
            atrasadas.Count,
            folhas.Count(t => t.EhCritica && t.PercentualConcluido < 100),
            cronograma.Tarefas.Sum(t => t.Fotos.Count),
            atrasadas.OrderBy(t => (t.EhMarco ? t.InicioMaisCedo : t.TerminoMaisCedo) ?? DateTime.MaxValue).Select(Citada).ToList(),
            proximos)
        {
            SemFotoRecente = AlertaDeFotos.Calcular(cronograma, hoje),
            ForaDoPrazo = [.. cronograma.Tarefas.Where(t => t.ForaDoPrazo)
                .Select(t => new TarefaNoResumo(edt[t.Id], t.Nome, t.Prazo!.Value, t.PercentualConcluido,
                    andamento.TryGetValue(t.Id, out var a) ? a.PercentualPrevisto : 0))],
            TerminoDaLinhaDeBase = cronograma.LinhaDeBaseEmUso?.Termino,
            DesvioDoTermino = cronograma.LinhaDeBaseEmUso?.Termino is { } previsto && termino is { } atual
                ? (int)Core.DiasUteis.Indice(Core.DiasUteis.ProximoOuMesmo(previsto.Date), Core.DiasUteis.ProximoOuMesmo(atual.Date))
                : null,
        };
    }
}
