using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 29: a IA olhando as fotos, contra o Gemini falso (nenhum teste chama a API de verdade).
public sealed class AnalistaGeminiTests : IDisposable
{
    private const string Chave = "AIzaChaveDeTesteSoParaOsTestes123456789";

    private readonly ManipuladorFalso _servidor = new();
    private readonly HttpClient _http;

    public AnalistaGeminiTests() => _http = new HttpClient(_servidor) { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose() => _http.Dispose();

    private AnalistaGemini Analista() => new(_http, Chave, "gemini-2.5-flash");

    private void Responder(JsonObject json, string fim = "STOP") =>
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(json.ToJsonString(), fim)));

    private static FotoParaIA Foto(string id, string tarefa = "1 Fundação", string? legenda = null) =>
        new(id, [0xFF, 0xD8, 0xFF, (byte)id.Length], tarefa, new DateOnly(2026, 9, 28), Legenda: legenda, Local: "Bloco A", PercentualNaData: 40);

    private static string Instrucoes(JsonNode corpo) => corpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();

    private static IEnumerable<string> Partes(JsonNode corpo) =>
        corpo["contents"]![0]!["parts"]!.AsArray().Select(p => p!["text"]?.GetValue<string>() ?? $"[{p!["inlineData"]!["mimeType"]}]");

    // ---- Legendas ----

    [Fact]
    public async Task Legendas_PedidoComAsFotosEOsDados_EOFormatoObrigatorio()
    {
        Responder(new JsonObject { ["legendas"] = new JsonArray(new JsonObject { ["id"] = "f1", ["legenda"] = "Armadura das sapatas pronta." }) });

        var legendas = await Analista().SugerirLegendasAsync(new PedidoDeLegendas("Residência", [Foto("f1"), Foto("f2", legenda: "antiga")]), CancellationToken.None);

        legendas.Should().Equal(new LegendaSugerida("f1", "Armadura das sapatas pronta."));
        var (pedido, _) = _servidor.Pedidos.Single();
        (pedido.RequestUri!.ToString(), pedido.Headers.GetValues("x-goog-api-key").Single())
            .Should().Be(("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", Chave));
        var corpo = _servidor.UltimoCorpo;
        Instrucoes(corpo).Should().StartWith(AnalistaGemini.InstrucoesGerais).And.Contain(AnalistaGemini.InstrucoesDasLegendas);
        Partes(corpo).Should().Equal(
            "Obra: Residência. 2 foto(s) para legendar.",
            "Foto id=f1 · 1 Fundação · 28/09/2026 · local: Bloco A · 40% na data", "[image/jpeg]",
            "Foto id=f2 · 1 Fundação · 28/09/2026 · local: Bloco A · 40% na data · legenda atual: antiga", "[image/jpeg]");
        corpo["contents"]![0]!["parts"]![2]!["inlineData"]!["data"]!.GetValue<string>().Should().Be(Convert.ToBase64String([0xFF, 0xD8, 0xFF, 2]));
        corpo["generationConfig"]!["responseMimeType"]!.GetValue<string>().Should().Be("application/json");
        corpo["generationConfig"]!["responseSchema"]!["properties"]!["legendas"]!["items"]!["required"]!.AsArray()
            .Select(n => n!.GetValue<string>()).Should().Equal("id", "legenda");
    }

    [Fact]
    public async Task Legendas_RespostaConferida_IdsDesconhecidosRepetidosEVaziosFicamDeFora_ELongaCortada()
    {
        Responder(new JsonObject
        {
            ["legendas"] = new JsonArray(
                new JsonObject { ["id"] = "f1", ["legenda"] = "  Primeira.  " },
                new JsonObject { ["id"] = "f1", ["legenda"] = "Repetida." },
                new JsonObject { ["id"] = "x9", ["legenda"] = "De outra foto." },
                new JsonObject { ["id"] = "f2", ["legenda"] = "   " },
                new JsonObject { ["id"] = "f3", ["legenda"] = new string('a', 400) },
                new JsonObject { ["legenda"] = "Sem id." }),
        });

        var legendas = await Analista().SugerirLegendasAsync(new PedidoDeLegendas("Obra", [Foto("f1"), Foto("f2"), Foto("f3")]), CancellationToken.None);

        legendas.Select(l => l.Id).Should().Equal("f1", "f3");
        legendas[0].Legenda.Should().Be("Primeira.");
        (legendas[1].Legenda.Length, legendas[1].Legenda[^1]).Should().Be((AnalistaGemini.TamanhoDaLegenda, '…'));
    }

    [Fact]
    public async Task Legendas_SemFotos_NaoChamaAApi()
    {
        (await Analista().SugerirLegendasAsync(new PedidoDeLegendas("Obra", []), CancellationToken.None)).Should().BeEmpty();
        _servidor.Pedidos.Should().BeEmpty();
    }

    // ---- % pelas fotos ----

    [Fact]
    public async Task Avanco_PedidoComAsTarefasEFotos_ERespostaConferida()
    {
        Responder(new JsonObject
        {
            ["tarefas"] = new JsonArray(
                new JsonObject { ["id"] = "t1", ["percentual"] = 75, ["justificativa"] = "Alvenaria até a respaldo.", ["diverge"] = true },
                new JsonObject { ["id"] = "t2", ["percentual"] = 140, ["justificativa"] = "x", ["diverge"] = false },
                new JsonObject { ["id"] = "t3", ["percentual"] = "50" },
                new JsonObject { ["id"] = "zz", ["percentual"] = 10 }),
        });
        var pedido = new PedidoDeAvanco("Obra", new DateOnly(2026, 10, 11),
        [
            new TarefaParaAvanco("t1", "2", "Alvenaria", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14), 40, 60, [Foto("a"), Foto("b")]),
            new TarefaParaAvanco("t2", "3", "Cobertura", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), 10, 30, [Foto("c")]),
            new TarefaParaAvanco("t3", "4", "Pintura", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), 0, 10, [Foto("d")]),
        ]);

        var sugestoes = await Analista().SugerirAvancoAsync(pedido, CancellationToken.None);

        sugestoes.Should().Equal(new AvancoSugerido("t1", 75, "Alvenaria até a respaldo.", true), new AvancoSugerido("t2", 100, "x", false));
        var partes = Partes(_servidor.UltimoCorpo).ToList();
        partes[1].Should().Be("Tarefa id=t1: 2 Alvenaria · previsto de 01/10/2026 a 14/10/2026 · 40% concluído informado · 60% previsto para hoje · 2 foto(s) recente(s):");
        partes.Count(p => p == "[image/jpeg]").Should().Be(4);
        Instrucoes(_servidor.UltimoCorpo).Should().Contain(AnalistaGemini.InstrucoesDoAvanco);
    }

    // ---- Texto do relatório ----

    [Fact]
    public async Task Texto_ComOsDadosDoPeriodo_ERespostaComPontos()
    {
        Responder(new JsonObject { ["andamento"] = "  A fundação terminou.\nA alvenaria começou.  ", ["pontos"] = new JsonArray("Laje atrasada", " ", 3) });
        var pedido = new PedidoDeTextoDoRelatorio("Residência", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 11),
            ["Andamento em 11/10/2026: 40% concluído (previsto: 50%)"],
            [new EtapaParaTexto("1", "Fundação", new DateOnly(2026, 9, 14), new DateOnly(2026, 10, 2), 100, 100, "Concluída", ["Sapatas prontas."]),
             new EtapaParaTexto("2", "Alvenaria", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), 20, 40, "Atrasada", [])],
            [Foto("f1")]);

        var texto = await Analista().RedigirRelatorioAsync(pedido, CancellationToken.None);

        texto.Should().BeEquivalentTo(new TextoSugerido("A fundação terminou.\nA alvenaria começou.", ["Laje atrasada"]));
        var dados = Partes(_servidor.UltimoCorpo).First();
        dados.Should().Contain("Período do relatório: 01/10/2026 a 11/10/2026.").And.Contain("- Andamento em 11/10/2026: 40% concluído (previsto: 50%)")
            .And.Contain("- 1 | Fundação | 14/09/2026 a 02/10/2026 | 100% | 100% | Concluída | Sapatas prontas.")
            .And.Contain("- 2 | Alvenaria | 05/10/2026 a 20/10/2026 | 20% | 40% | Atrasada | sem fotos");
        Partes(_servidor.UltimoCorpo).Should().Contain("[image/jpeg]");
    }

    [Fact]
    public async Task Texto_SemOAndamento_Erro()
    {
        Responder(new JsonObject { ["andamento"] = " ", ["pontos"] = new JsonArray() });

        var pedir = () => Analista().RedigirRelatorioAsync(new PedidoDeTextoDoRelatorio("Obra", default, default, [], [], []), CancellationToken.None);

        (await pedir.Should().ThrowAsync<ErroDoAssistenteException>()).WithMessage("O Gemini não escreveu o texto do andamento. Tente de novo.");
    }

    // ---- Falhas ----

    [Fact]
    public async Task RespostaQueNaoEJson_OuCortada_ErroClaro()
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("não é json")));
        var pedir = () => Analista().SugerirLegendasAsync(new PedidoDeLegendas("Obra", [Foto("f1")]), CancellationToken.None);
        (await pedir.Should().ThrowAsync<ErroDoAssistenteException>()).WithMessage("O Gemini mandou uma resposta que não deu para entender. Tente de novo.");

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("{\"legendas\": [", "MAX_TOKENS")));
        (await pedir.Should().ThrowAsync<ErroDoAssistenteException>()).WithMessage("*veio cortada. Tente com menos fotos.");
    }

    [Fact]
    public async Task ErroDaApi_TraduzidoSemAChave()
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(429, ManipuladorFalso.Erro(429, "quota")));

        var pedir = () => Analista().SugerirLegendasAsync(new PedidoDeLegendas("Obra", [Foto("f1")]), CancellationToken.None);

        (await pedir.Should().ThrowAsync<ErroDoAssistenteException>()).Which.Message.Should().StartWith("O limite de uso do Gemini foi atingido").And.NotContain(Chave);
    }

    [Fact]
    public async Task FotosDemais_RecusadoAntesDeEnviar()
    {
        var grande = new byte[8 * 1024 * 1024];
        var pedido = new PedidoDeLegendas("Obra", [Foto("a") with { Jpeg = grande }, Foto("b") with { Jpeg = grande }]);

        var pedir = () => Analista().SugerirLegendasAsync(pedido, CancellationToken.None);

        (await pedir.Should().ThrowAsync<ErroDoAssistenteException>()).WithMessage("As fotos passam de*Escolha menos fotos.");
        _servidor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public void Construtor_ValidaChaveEModelo()
    {
        ((Action)(() => _ = new AnalistaGemini(_http, "chave com espaço", "m"))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new AnalistaGemini(_http, Chave, " "))).Should().Throw<ArgumentException>();
        new AnalistaGemini(_http, Chave, "gemini-2.5-flash").EstaConfigurado.Should().BeTrue();
    }

    // ---- Conforme a configuração ----

    [Fact]
    public async Task ConformeConfiguracao_SemChaveAvisa_ComChaveUsaOProvedor()
    {
        var configuracao = new ConfiguracaoFixa();
        var provedor = new ProvedorDeTeste();
        var analista = new AnalistaConformeConfiguracao(configuracao, provedor);

        analista.EstaConfigurado.Should().BeFalse();
        var pedir = () => analista.SugerirLegendasAsync(new PedidoDeLegendas("Obra", []), CancellationToken.None);
        (await pedir.Should().ThrowAsync<ErroDoAssistenteException>()).WithMessage(AnalistaIndisponivel.Mensagem);

        configuracao.ChaveApi = "chave-nova";
        configuracao.Modelo = "gemini-2.5-pro";
        analista.EstaConfigurado.Should().BeTrue();
        await pedir.Should().ThrowAsync<ErroDoAssistenteException>("o analista do provedor de teste é o indisponível");
        provedor.AnalistasCriados.Should().Equal(("chave-nova", "gemini-2.5-pro"));
        new ProvedorGemini(_http).CriarAnalista(Chave, "models/gemini-2.5-flash").Should().BeOfType<AnalistaGemini>()
            .Which.Modelo.Should().Be("gemini-2.5-flash");
    }
}
