using System.Globalization;

namespace ProjectPlanner.Core.Planejamento;

/// <summary>Uma tarefa como estava quando a linha de base foi salva: as datas calculadas, a duração e o valor.</summary>
public sealed record TarefaNaLinhaDeBase(DateTime Inicio, DateTime Termino, int Duracao, decimal? Valor);

/// <summary>
/// Bloco 31: o retrato do cronograma num momento (as datas calculadas, a duração e o valor de cada tarefa), para comparar
/// o previsto com o atual. Imutável: refazer a linha de base é salvar outra (LB0, LB1…).
/// </summary>
public sealed class LinhaDeBase
{
    public const int TamanhoMaximoDoNome = 80;

    /// <exception cref="DomainValidationException">Número negativo ou nome longo demais.</exception>
    public LinhaDeBase(int numero, string? nome, DateTime salvaEm, IReadOnlyDictionary<Guid, TarefaNaLinhaDeBase> tarefas)
    {
        ArgumentNullException.ThrowIfNull(tarefas);
        if (numero < 0)
            throw new DomainValidationException(nameof(Numero), "O número da linha de base não pode ser negativo.");
        var limpo = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
        if (limpo?.Length > TamanhoMaximoDoNome)
            throw new DomainValidationException(nameof(Nome), $"O nome da linha de base tem {limpo.Length} caracteres; o máximo é {TamanhoMaximoDoNome}.");
        (Numero, Nome, SalvaEm, Tarefas) = (numero, limpo, salvaEm, new Dictionary<Guid, TarefaNaLinhaDeBase>(tarefas));
    }

    public int Numero { get; }

    /// <summary>"Contrato assinado", "Aditivo 1"…; nulo sem nome.</summary>
    public string? Nome { get; }

    public DateTime SalvaEm { get; }

    /// <summary>As tarefas que existiam quando ela foi salva (as criadas depois não estão aqui).</summary>
    public IReadOnlyDictionary<Guid, TarefaNaLinhaDeBase> Tarefas { get; }

    /// <summary>"LB0 · Contrato assinado · salva em 01/09/2026 10:00".</summary>
    public string Descricao =>
        string.Join(" · ", new[] { $"LB{Numero}", Nome, $"salva em {SalvaEm.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}" }.OfType<string>());

    /// <summary>O término da linha de base: o maior término entre as tarefas.</summary>
    public DateTime? Termino => Tarefas.Count == 0 ? null : Tarefas.Values.Max(t => t.Termino);

    public override string ToString() => Descricao;
}

/// <summary>Bloco 31: o desvio de uma tarefa em relação à linha de base em uso, em dias úteis (positivo: atrasou).</summary>
public static class DesvioDaLinhaDeBase
{
    /// <summary>Dias úteis do início previsto até o início atual; nulo se a tarefa não está na linha de base ou não foi calculada.</summary>
    public static int? DoInicio(Tarefa tarefa, LinhaDeBase? linhaDeBase) =>
        linhaDeBase?.Tarefas.GetValueOrDefault(tarefa.Id) is { } prevista && tarefa.InicioMaisCedo is { } atual
            ? Dias(prevista.Inicio, atual)
            : null;

    /// <summary>Dias úteis do término previsto até o término atual.</summary>
    public static int? DoTermino(Tarefa tarefa, LinhaDeBase? linhaDeBase) =>
        linhaDeBase?.Tarefas.GetValueOrDefault(tarefa.Id) is { } prevista && tarefa.TerminoMaisCedo is { } atual
            ? Dias(prevista.Termino, atual)
            : null;

    private static int Dias(DateTime previsto, DateTime atual) =>
        (int)DiasUteis.Indice(DiasUteis.ProximoOuMesmo(previsto.Date), DiasUteis.ProximoOuMesmo(atual.Date));
}
