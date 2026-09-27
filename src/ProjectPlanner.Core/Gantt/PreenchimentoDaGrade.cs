namespace ProjectPlanner.Core.Gantt;

/// <summary>
/// Quantas linhas vazias a grid de tarefas desenha abaixo das reais para preencher a área visível, como o
/// Gantt faz desde o Bloco 8 (<see cref="CalculadoraLayoutGantt"/> estica o corpo até a altura visível).
/// As linhas vazias são só desenho: não são tarefas, não se editam nem se selecionam.
/// </summary>
public static class PreenchimentoDaGrade
{
    /// <param name="linhasReais">Quantas linhas de tarefa a grid mostra.</param>
    /// <param name="alturaDisponivel">Altura da área das linhas (sem o cabeçalho), em pixels.</param>
    /// <param name="alturaLinha">Altura de cada linha, em pixels.</param>
    /// <returns>
    /// As linhas que faltam para cobrir a altura, contando a última mesmo que só apareça em parte; zero
    /// quando as reais já cobrem tudo (aí a grid rola) ou quando não há altura.
    /// </returns>
    public static int LinhasVazias(int linhasReais, double alturaDisponivel, double alturaLinha)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(linhasReais);
        if (!double.IsFinite(alturaLinha) || alturaLinha <= 0)
            throw new ArgumentOutOfRangeException(nameof(alturaLinha), alturaLinha, "A altura da linha precisa ser positiva.");
        if (!double.IsFinite(alturaDisponivel) || alturaDisponivel <= 0)
            return 0;

        var cabem = (int)Math.Min(Math.Ceiling(alturaDisponivel / alturaLinha), int.MaxValue);
        return Math.Max(0, cabem - linhasReais);
    }
}
