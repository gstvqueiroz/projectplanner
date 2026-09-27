namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// Onde uma tarefa arrastada na grid cairia (Bloco 18).
/// </summary>
/// <param name="PosicaoEntreIrmas">A posição que ela passa a ocupar entre as irmãs (0 = a primeira).</param>
/// <param name="LinhaDaGuia">
/// Em cima de qual linha visível a linha guia é desenhada (a quantidade de linhas = depois da última).
/// </param>
public sealed record DestinoDeReordenacao(int PosicaoEntreIrmas, int LinhaDaGuia);
