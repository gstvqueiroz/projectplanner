namespace ProjectPlanner.Core.Edicao;

/// <summary>
/// Bloco 72: a regra do resumo que fica vazio (E4): se todas as subtarefas de um resumo forem excluídas ou movidas para fora,
/// ele vira tarefa comum e mantém o início e o término que tinha (a duração em dias úteis e o "não iniciar antes de" no
/// início). Guarde as datas antes de mexer na estrutura e aplique depois.
/// </summary>
public sealed class ResumosAntesDaMudanca
{
    private readonly Dictionary<Guid, (DateTime Inicio, DateTime Termino)> _datas;

    private ResumosAntesDaMudanca(Dictionary<Guid, (DateTime, DateTime)> datas) => _datas = datas;

    public static ResumosAntesDaMudanca Guardar(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        return new(cronograma.Tarefas.Where(t => t.EhResumo && t.InicioMaisCedo is not null && t.TerminoMaisCedo is not null)
            .ToDictionary(t => t.Id, t => (t.InicioMaisCedo!.Value.Date, t.TerminoMaisCedo!.Value.Date)));
    }

    /// <summary>Os resumos de antes que viraram tarefa comum ficam com a duração e o início que tinham.</summary>
    /// <returns>Os nomes dos que viraram tarefa.</returns>
    public IReadOnlyList<string> Aplicar(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var viraram = new List<string>();
        foreach (var tarefa in cronograma.Tarefas.Where(t => !t.EhResumo && _datas.ContainsKey(t.Id)))
        {
            var (inicio, termino) = _datas[tarefa.Id];
            tarefa.AlterarDuracao(Math.Max(1, tarefa.Calendario.ContarDiasUteis(inicio, termino)));
            if (inicio > cronograma.DataInicio)
                tarefa.DefinirRestricaoDeInicio(inicio);
            viraram.Add(tarefa.Nome);
        }
        return viraram;
    }
}
