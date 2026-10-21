using Microsoft.Extensions.Options;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 46: os modelos liberados, um por nível. Vêm da configuração (<c>Modelos__0__Id</c>, …); sem ela, os padrão. Trocar
/// o modelo de um nível (quando o Google aposentar um, por exemplo) é só mudar a configuração.
/// </summary>
public sealed class CatalogoDeModelos(IOptions<OpcoesDoServidor> opcoes)
{
    /// <summary>
    /// 0.47.2 (decisão do usuário): o Padrão é o Claude Haiku 5.5 (o mais barato, com preço por faixa) e o Máximo, o Claude
    /// Sonnet 5.5 (metade do Opus). O Avançado continua o Gemini 3.1 Pro (o 2.5 Pro responde 404 para chaves de projetos
    /// novos, 0.45.2). A reserva do Padrão é o Gemini 2.5 Flash: ver <see cref="Reserva"/> e docs/SERVIDOR.md. Bloco 50: o
    /// Intermediário, Gemini 3 Flash (cerca de 19 créditos por pedido típico), entre o Haiku (4) e o Sonnet (70). No Gemini, o
    /// cache é automático e não cobra a gravação (<see cref="ModeloDeIa.FatorDaGravacaoNoCache"/> = 1).
    /// </summary>
    public static IReadOnlyList<ModeloDeIa> Padrao { get; } =
    [
        new() { Id = "claude-haiku-5-5", Nivel = Niveis.Padrao, Provedor = Provedores.Anthropic, Nome = "Claude Haiku 5.5",
            DolaresPorMilhaoDeEntrada = 0.10m, DolaresPorMilhaoDeSaida = 0.50m,
            LimiteDaFaixa = 100_000, DolaresPorMilhaoDeEntradaAcima = 0.50m, DolaresPorMilhaoDeSaidaAcima = 2.50m },
        new() { Id = "gemini-3-flash-preview", Nivel = Niveis.Intermediario, Provedor = Provedores.Gemini, Nome = "Gemini 3 Flash",
            DolaresPorMilhaoDeEntrada = 0.50m, DolaresPorMilhaoDeSaida = 3.00m, FatorDaGravacaoNoCache = 1m },
        new() { Id = "gemini-3.1-pro-preview", Nivel = Niveis.Avancado, Provedor = Provedores.Gemini, Nome = "Gemini 3.1 Pro",
            DolaresPorMilhaoDeEntrada = 2.00m, DolaresPorMilhaoDeSaida = 12.00m, FatorDaGravacaoNoCache = 1m },
        new() { Id = "claude-sonnet-5-5", Nivel = Niveis.Maximo, Provedor = Provedores.Anthropic, Nome = "Claude Sonnet 5.5",
            DolaresPorMilhaoDeEntrada = 2.00m, DolaresPorMilhaoDeSaida = 10.00m },
    ];

    /// <summary>A reserva do Padrão, se o Haiku der problema: o Gemini 2.5 Flash (pela configuração, ver docs/SERVIDOR.md).</summary>
    public static ModeloDeIa Reserva { get; } = new()
    {
        Id = "gemini-2.5-flash", Nivel = Niveis.Padrao, Provedor = Provedores.Gemini, Nome = "Gemini 2.5 Flash",
        DolaresPorMilhaoDeEntrada = 0.30m, DolaresPorMilhaoDeSaida = 2.50m, FatorDaLeituraDoCache = 0.25m, FatorDaGravacaoNoCache = 1m,
    };

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
