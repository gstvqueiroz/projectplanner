using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 43: a análise dos indicadores pelo Gemini falso.
public sealed class AnalistaDaObraGeminiTests : IDisposable
{
    private const string Chave = "AIzaChaveDeTesteSoParaOsTestes123456789";

    private readonly ManipuladorFalso _servidor = new();
    private readonly HttpClient _http;

    public AnalistaDaObraGeminiTests() => _http = new HttpClient(_servidor) { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose() => _http.Dispose();

    private AnalistaDaObraGemini Analista() => new(_http, Chave, "gemini-2.5-flash");

    private static PedidoDeAnaliseDaObra Pedido() => new("Residência", new DateOnly(2026, 10, 9),
        ["Prazo: IDP 0,80; 1 tarefa atrasada.", "Alerta Custo (Crítico): Índice de custo 0,38."], ["Área construída: 120 m²"]);

    private void Responder(JsonObject json, string fim = "STOP") =>
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(json.ToJsonString(), fim)));

    [Fact]
    public async Task Pedido_ComOsDadosEAFicha_EOFormato()
    {
        Responder(new JsonObject { ["resumo"] = "Ok.", ["pontos"] = new JsonArray(), ["recomendacoes"] = new JsonArray() });

        await Analista().AnalisarAsync(Pedido(), CancellationToken.None);

        var corpo = _servidor.UltimoCorpo;
        corpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be(AnalistaDaObraGemini.Instrucoes);
        corpo["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be(
            "Obra: Residência. Data de status: 09/10/2026.\nFicha da obra:\n- Área construída: 120 m²\nIndicadores:\n" +
            "- Prazo: IDP 0,80; 1 tarefa atrasada.\n- Alerta Custo (Crítico): Índice de custo 0,38.\n");
        corpo["generationConfig"]!["responseSchema"]!["required"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().Equal("resumo", "pontos", "recomendacoes");
    }

    [Fact]
    public async Task Resposta_AparadaSemRepetidosELimitada()
    {
        Responder(new JsonObject
        {
            ["resumo"] = "  A obra está atrasada e gastando acima do orçado.  ",
            ["pontos"] = new JsonArray("Sapatas atrasadas.", "Sapatas atrasadas.", "  ", 3, "Custo da fundação 2,6× o orçado.",
                "a", "b", "c", "d", "e"),
            ["recomendacoes"] = new JsonArray(new string('x', 500)),
        });

        var r = await Analista().AnalisarAsync(Pedido(), CancellationToken.None);

        r.Resumo.Should().Be("A obra está atrasada e gastando acima do orçado.");
        r.Pontos.Should().Equal("Sapatas atrasadas.", "Custo da fundação 2,6× o orçado.", "a", "b", "c", "d");
        (r.Recomendacoes.Single().Length, r.Recomendacoes.Single()[^1]).Should().Be((AnalistaDaObraGemini.TamanhoDoItem, '…'));
    }

    [Fact]
    public async Task SemResumo_OuCortada_Erro()
    {
        Responder(new JsonObject { ["resumo"] = " ", ["pontos"] = new JsonArray(), ["recomendacoes"] = new JsonArray() });
        await ((Func<Task>)(() => Analista().AnalisarAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage("*não escreveu a análise*");

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("{\"resumo\": \"cort", "MAX_TOKENS")));
        await ((Func<Task>)(() => Analista().AnalisarAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage("*cortada*");
    }

    [Fact]
    public async Task SemChave_Indisponivel_ComChave_PeloProvedor()
    {
        var configuracao = new ConfiguracaoFixa();
        var provedor = new ProvedorDeTeste();
        var analista = new AnalistaDaObraConformeConfiguracao(configuracao, provedor);

        analista.EstaConfigurado.Should().BeFalse();
        await ((Func<Task>)(() => analista.AnalisarAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage(AnalistaIndisponivel.Mensagem);

        configuracao.ChaveApi = "chave-1";
        await ((Func<Task>)(() => analista.AnalisarAsync(Pedido(), CancellationToken.None))).Should().ThrowAsync<ErroDoAssistenteException>();
        provedor.AnalistasDaObraCriados.Should().Equal(("chave-1", "gemini-2.5-flash"));
        new ProvedorGemini(_http).CriarAnalistaDaObra(Chave, "gemini-2.5-flash").Should().BeOfType<AnalistaDaObraGemini>();
    }
}
