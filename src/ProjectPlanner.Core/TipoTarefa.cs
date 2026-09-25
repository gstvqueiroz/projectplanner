namespace ProjectPlanner.Core;

/// <summary>
/// O que a tarefa é no cronograma (<see cref="Tarefa.Tipo"/>). Não é escolhido pelo usuário: vem da
/// estrutura e da duração.
/// </summary>
public enum TipoTarefa
{
    // Valores explícitos: são gravados no arquivo. Não reordenar.

    /// <summary>Tarefa comum: duração de pelo menos um dia útil e nenhuma subtarefa.</summary>
    Normal = 0,

    /// <summary>
    /// Marco: duração 0, um ponto no tempo (início = término). Tem vínculos normalmente, mas não
    /// tem subtarefas. Uma tarefa vira marco quando a duração vai para 0, e volta a ser comum quando
    /// ganha duração.
    /// </summary>
    Marco = 1,

    /// <summary>
    /// Resumo: tem subtarefas. Duração, datas e percentual concluído vêm delas, e ele não tem
    /// vínculos. Uma tarefa vira resumo ao ganhar a primeira subtarefa, e volta a ser comum (ou marco)
    /// quando perde a última.
    /// </summary>
    Resumo = 2,
}
