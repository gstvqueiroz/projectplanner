namespace ProjectPlanner.App.Models;

/// <summary>Como uma coluna da grid está: visível ou não, largura em pixels e posição (0 = primeira).</summary>
public sealed record EstadoColuna(string Id, bool Visivel, double Largura, int Posicao);

/// <summary>O layout das colunas da grid, como fica gravado nas preferências do usuário.</summary>
public sealed record LayoutColunas(IReadOnlyList<EstadoColuna> Colunas);
