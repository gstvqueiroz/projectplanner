namespace ProjectPlanner.Core;

/// <summary>
/// Tarefa do cronograma.
/// </summary>
/// <remarks>
/// Regras de datas (calendário padrão de segunda a sexta; feriados ficam para um bloco futuro):
/// <list type="bullet">
/// <item>As datas não têm hora: qualquer horário informado é descartado.</item>
/// <item>Um início que cai no fim de semana vai para a segunda-feira seguinte; um término
/// que cai no fim de semana volta para a sexta-feira anterior.</item>
/// <item><see cref="Duracao"/> é um número inteiro de dias úteis, maior que zero.</item>
/// <item><see cref="DataTermino"/> é inclusiva, ou seja, é o último dia útil de trabalho:
/// <see cref="DataInicio"/> + (<see cref="Duracao"/> − 1) dias úteis. Uma tarefa de 1 dia
/// termina no mesmo dia em que começa.</item>
/// <item>Alterar o início mantém a duração e desloca o término. Alterar a duração recalcula o
/// término. Alterar o término recalcula a duração, e um término anterior ao início é rejeitado.</item>
/// </list>
/// Toda violação lança <see cref="DomainValidationException"/> e deixa a tarefa inalterada.
/// <para>
/// <see cref="DataInicio"/> e <see cref="DataTermino"/> são as datas planejadas, informadas pelo
/// usuário. As datas do caminho crítico (<see cref="InicioMaisCedo"/>, <see cref="TerminoMaisCedo"/>,
/// <see cref="InicioMaisTarde"/>, <see cref="TerminoMaisTarde"/>) e a <see cref="Folga"/> são o
/// resultado do último <see cref="CalculadoraCronograma.Calcular"/>. Elas ficam nulas até o primeiro
/// cálculo e não se atualizam sozinhas quando a tarefa ou o cronograma mudam: é preciso recalcular.
/// </para>
/// </remarks>
public class Tarefa
{
    public Tarefa(string nome, DateTime dataInicio, int duracao)
    {
        Id = Guid.NewGuid();
        Nome = ValidarNome(nome);
        ValidarDuracao(duracao);
        DataInicio = NormalizarInicio(dataInicio);
        Duracao = duracao;
        DataTermino = CalcularTermino(DataInicio, duracao);
    }

    public Guid Id { get; }

    public string Nome { get; private set; }

    /// <summary>Duração em dias úteis (inteiro maior que zero).</summary>
    public int Duracao { get; private set; }

    /// <summary>Primeiro dia útil de trabalho.</summary>
    public DateTime DataInicio { get; private set; }

    /// <summary>Último dia útil de trabalho (inclusivo).</summary>
    public DateTime DataTermino { get; private set; }

    /// <summary>Percentual concluído, de 0 a 100.</summary>
    public int PercentualConcluido { get; private set; }

    /// <summary>Início mais cedo (ES), do último cálculo do cronograma.</summary>
    public DateTime? InicioMaisCedo { get; private set; }

    /// <summary>Término mais cedo (EF), do último cálculo do cronograma.</summary>
    public DateTime? TerminoMaisCedo { get; private set; }

    /// <summary>Início mais tarde (LS), do último cálculo do cronograma.</summary>
    public DateTime? InicioMaisTarde { get; private set; }

    /// <summary>Término mais tarde (LF), do último cálculo do cronograma.</summary>
    public DateTime? TerminoMaisTarde { get; private set; }

    /// <summary>Folga total em dias úteis: LS − ES (igual a LF − EF). Nunca é negativa.</summary>
    public int? Folga { get; private set; }

    /// <summary>Se a tarefa é crítica (folga zero). Falso antes do primeiro cálculo.</summary>
    public bool EhCritica => Folga == 0;

    public void Renomear(string nome) => Nome = ValidarNome(nome);

    /// <summary>Altera a duração e recalcula o término, mantendo o início.</summary>
    public void AlterarDuracao(int duracao)
    {
        ValidarDuracao(duracao);
        DataTermino = CalcularTermino(DataInicio, duracao);
        Duracao = duracao;
    }

    /// <summary>Move o início mantendo a duração; o término é deslocado junto.</summary>
    public void AlterarDataInicio(DateTime dataInicio)
    {
        var inicio = NormalizarInicio(dataInicio);
        DataTermino = CalcularTermino(inicio, Duracao);
        DataInicio = inicio;
    }

    /// <summary>Altera o término e recalcula a duração, mantendo o início.</summary>
    public void AlterarDataTermino(DateTime dataTermino)
    {
        var termino = DiasUteis.AnteriorOuMesmo(dataTermino.Date);
        if (termino < DataInicio)
            throw new DomainValidationException(nameof(DataTermino),
                $"A data de término ({dataTermino:d}) não pode ser anterior à data de início ({DataInicio:d}).");

        Duracao = DiasUteis.ContarInclusivo(DataInicio, termino);
        DataTermino = termino;
    }

    public void AtualizarPercentualConcluido(int percentual)
    {
        if (percentual is < 0 or > 100)
            throw new DomainValidationException(nameof(PercentualConcluido),
                $"O percentual concluído deve estar entre 0 e 100 (informado: {percentual}).");

        PercentualConcluido = percentual;
    }

    internal void DefinirDatasCalculadas(DateTime inicioMaisCedo, DateTime terminoMaisCedo,
        DateTime inicioMaisTarde, DateTime terminoMaisTarde, int folga)
    {
        InicioMaisCedo = inicioMaisCedo;
        TerminoMaisCedo = terminoMaisCedo;
        InicioMaisTarde = inicioMaisTarde;
        TerminoMaisTarde = terminoMaisTarde;
        Folga = folga;
    }

    private static string ValidarNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new DomainValidationException(nameof(Nome), "O nome da tarefa é obrigatório.");

        return nome.Trim();
    }

    private static void ValidarDuracao(int duracao)
    {
        if (duracao <= 0)
            throw new DomainValidationException(nameof(Duracao),
                $"A duração deve ser maior que zero (informado: {duracao}).");
    }

    private static DateTime NormalizarInicio(DateTime dataInicio) =>
        DiasUteis.ProximoOuMesmo(dataInicio.Date);

    private static DateTime CalcularTermino(DateTime inicio, int duracao) =>
        DiasUteis.Adicionar(inicio, duracao - 1)
        ?? throw new DomainValidationException(nameof(DataTermino),
            "A data de término calculada ultrapassa a maior data suportada (31/12/9999).");
}
