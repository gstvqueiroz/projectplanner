namespace ProjectPlanner.Core.Tests;

/// <summary>
/// Monta cronogramas de teste que começam em <see cref="Datas.Segunda"/> (dia útil 0) e lê o
/// resultado do CPM em números de dia útil, para comparar com cálculos feitos à mão.
/// </summary>
public sealed class MontadorCronograma
{
    private readonly Dictionary<string, Tarefa> _tarefas = [];

    public Cronograma Cronograma { get; } = new(Datas.Segunda);

    public Tarefa this[string nome] => _tarefas[nome];

    /// <summary>Adiciona uma tarefa, ligada por FS com lag 0 a cada predecessora informada.</summary>
    public MontadorCronograma ComTarefa(string nome, int duracao, params string[] predecessorasFS)
    {
        var tarefa = new Tarefa(nome, Datas.Segunda, duracao);
        Cronograma.AdicionarTarefa(tarefa);
        _tarefas[nome] = tarefa;
        foreach (var predecessora in predecessorasFS)
            ComVinculo(predecessora, nome);
        return this;
    }

    public MontadorCronograma ComVinculo(string origem, string destino,
        TipoDependencia tipo = TipoDependencia.FS, int lag = 0)
    {
        Cronograma.AdicionarPredecessora(new Predecessora(_tarefas[origem].Id, _tarefas[destino].Id, tipo, lag));
        return this;
    }

    public ResultadoCronograma Calcular() => CalculadoraCronograma.Calcular(Cronograma);

    /// <summary>Resultado do CPM de cada tarefa, na ordem do cronograma, em números de dia útil.</summary>
    public IEnumerable<LinhaCpm> Linhas() =>
        Cronograma.Tarefas.Select(t => new LinhaCpm(t.Nome,
            Datas.NumeroDoDiaUtil(t.InicioMaisCedo!.Value), Datas.NumeroDoDiaUtil(t.TerminoMaisCedo!.Value),
            Datas.NumeroDoDiaUtil(t.InicioMaisTarde!.Value), Datas.NumeroDoDiaUtil(t.TerminoMaisTarde!.Value),
            t.Folga!.Value, t.EhCritica));

    public LinhaCpm Linha(string nome) => Linhas().Single(l => l.Nome == nome);

    /// <summary>Caminhos críticos escritos como "A → B → C".</summary>
    public static IEnumerable<string> Caminhos(ResultadoCronograma resultado) =>
        resultado.CaminhosCriticos.Select(c => string.Join(" → ", c.Select(t => t.Nome)));
}

/// <summary>Resultado do CPM de uma tarefa, em números de dia útil a partir do início do projeto.</summary>
public readonly record struct LinhaCpm(string Nome, int ES, int EF, int LS, int LF, int Folga, bool Critica);
