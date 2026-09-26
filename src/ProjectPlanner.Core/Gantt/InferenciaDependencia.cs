namespace ProjectPlanner.Core.Gantt;

/// <summary>Uma das pontas de uma barra do Gantt: o início (esquerda) ou o término (direita) da tarefa.</summary>
public enum BordaDaBarra
{
    Inicio,
    Termino,
}

/// <summary>
/// Tipo de dependência criado ao arrastar, no Gantt, de uma ponta da predecessora até uma ponta da
/// sucessora: o tipo liga o ponto de origem ao ponto de destino, como no próprio nome dele.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Término → Início: FS (TI), o caso comum.</item>
/// <item>Início → Início: SS (II).</item>
/// <item>Término → Término: FF (TT).</item>
/// <item>Início → Término: SF (IT).</item>
/// </list>
/// No MS Project o arraste cria sempre TI; aqui o gesto comum (da ponta direita da predecessora para
/// qualquer ponto da sucessora) também dá TI, e os outros tipos saem das alças.
/// </remarks>
public static class InferenciaDependencia
{
    /// <exception cref="ArgumentOutOfRangeException">Alguma das bordas não existe.</exception>
    public static TipoDependencia Tipo(BordaDaBarra origem, BordaDaBarra destino) => (origem, destino) switch
    {
        (BordaDaBarra.Termino, BordaDaBarra.Inicio) => TipoDependencia.FS,
        (BordaDaBarra.Inicio, BordaDaBarra.Inicio) => TipoDependencia.SS,
        (BordaDaBarra.Termino, BordaDaBarra.Termino) => TipoDependencia.FF,
        (BordaDaBarra.Inicio, BordaDaBarra.Termino) => TipoDependencia.SF,
        _ => throw new ArgumentOutOfRangeException(nameof(origem), $"Borda inválida: {origem} → {destino}."),
    };
}
