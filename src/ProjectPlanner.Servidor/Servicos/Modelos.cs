using Microsoft.Extensions.Options;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 46: os modelos liberados, um por nível. Vêm da configuração (<c>Modelos__0__Id</c>, …); sem ela, os padrão. Trocar
/// o modelo de um nível (quando o Google aposentar um, por exemplo) é só mudar a configuração.
/// </summary>
public sealed class CatalogoDeModelos(IOptions<OpcoesDoServidor> opcoes)
{
    public static IReadOnlyList<ModeloDeIa> Padrao { get; } =
    [
        new() { Id = "gemini-2.5-flash", Nivel = Niveis.Padrao, Provedor = Provedores.Gemini, Nome = "Gemini 2.5 Flash",
            DolaresPorMilhaoDeEntrada = 0.30m, DolaresPorMilhaoDeSaida = 2.50m },
        // 0.45.2: o 2.5 Pro responde 404 para chaves de projetos novos; o 3.1 Pro (prévia) é o Pro que a chave do servidor tem.
        new() { Id = "gemini-3.1-pro-preview", Nivel = Niveis.Avancado, Provedor = Provedores.Gemini, Nome = "Gemini 3.1 Pro",
            DolaresPorMilhaoDeEntrada = 2.00m, DolaresPorMilhaoDeSaida = 12.00m },
        new() { Id = "claude-opus-5-5", Nivel = Niveis.Maximo, Provedor = Provedores.Anthropic, Nome = "Claude Opus 5.5",
            DolaresPorMilhaoDeEntrada = 4.00m, DolaresPorMilhaoDeSaida = 20.00m },
    ];

    /// <summary>Um pedido típico do programa (a pergunta com o retrato do cronograma), para estimar os créditos.</summary>
    public const long EntradaTipica = 10_000;
    public const long SaidaTipica = 1_500;

    public IReadOnlyList<ModeloDeIa> Todos => opcoes.Value.Modelos is { Count: > 0 } configurados
        ? configurados.Where(m => !string.IsNullOrWhiteSpace(m.Id)).DistinctBy(m => m.Id).ToList()
        : Padrao;

    public ModeloDeIa? Achar(string id) => Todos.FirstOrDefault(m => m.Id == id);

    /// <summary>A chave do provedor do modelo está configurada?</summary>
    public bool Disponivel(ModeloDeIa modelo) => modelo.Provedor switch
    {
        Provedores.Anthropic => !string.IsNullOrWhiteSpace(opcoes.Value.Anthropic.Chave),
        _ => !string.IsNullOrWhiteSpace(opcoes.Value.Gemini.Chave),
    };
}
