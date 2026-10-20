using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// Bloco 46: um nível de modelo como as telas mostram: "Padrão: Gemini 2.5 Flash", com o gasto de um pedido típico ou, fora do
/// plano, "do plano Profissional".
/// </summary>
public sealed record OpcaoDeNivel(ModeloDaConta Modelo)
{
    public string Id => Modelo.Id;

    public string Titulo => $"{Modelo.NomeDoNivel}: {Modelo.Nome}";

    public bool Habilitado => Modelo.Liberado && Modelo.Disponivel;

    /// <summary>Os níveis fora do plano aparecem apagados.</summary>
    public double Opacidade => Habilitado ? 1 : 0.5;

    public string Detalhe => !Modelo.Liberado ? "do plano Profissional"
        : !Modelo.Disponivel ? "indisponível no momento"
        : $"cerca de {Modelo.CreditosPorPedido} créditos por pedido";

    /// <summary>"Padrão: Gemini 2.5 Flash (cerca de 14 créditos por pedido)", para listas de uma linha.</summary>
    public string TextoCompleto => $"{Titulo} ({Detalhe})";

    public static IReadOnlyList<OpcaoDeNivel> De(IReadOnlyList<ModeloDaConta> modelos) => [.. modelos.Select(m => new OpcaoDeNivel(m))];

    /// <summary>A opção do modelo escolhido; se ele não está liberado (ou saiu da lista), a primeira liberada.</summary>
    public static OpcaoDeNivel? Escolhida(IReadOnlyList<OpcaoDeNivel> opcoes, string modelo) =>
        opcoes.FirstOrDefault(o => o.Id == modelo && o.Habilitado) ?? opcoes.FirstOrDefault(o => o.Habilitado);
}
