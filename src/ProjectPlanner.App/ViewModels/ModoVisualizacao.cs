namespace ProjectPlanner.App.ViewModels;

/// <summary>Quais painéis a janela mostra (Exibir e a faixa de acesso rápido). Vale só na sessão.</summary>
public enum ModoVisualizacao
{
    /// <summary>Grid e Gantt lado a lado, com o divisor entre eles (o padrão).</summary>
    Dividido,

    /// <summary>Só a grid de tarefas, na largura toda.</summary>
    SomenteTabela,

    /// <summary>Só o Gantt, na largura toda.</summary>
    SomenteGantt,
}
