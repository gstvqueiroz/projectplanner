using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 42: a IA dizendo em que tarefa cada foto se encaixa (Gemini falso), e os campos novos do chat.
public sealed class ClassificacaoDeFotosTests : IDisposable
{
    private const string Chave = "AIzaChaveDeTesteSoParaOsTestes123456789";

    private readonly ManipuladorFalso _servidor = new();
    private readonly HttpClient _http;

    public ClassificacaoDeFotosTests() => _http = new HttpClient(_servidor) { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose() => _http.Dispose();

    private AnalistaGemini Analista() => new(_http, Chave, "gemini-2.5-flash");

    private static PedidoDeClassificacao Pedido() => new("Residência", new DateOnly(2026, 10, 18),
        [
            new TarefaParaClassificar("T1", "1.1", "Fundação", "Estrutura", new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30), 100, false),
            new TarefaParaClassificar("T2", "2.1", "Alvenaria", "Vedações", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20), 40, true),
        ],
        [new FotoParaIA("F1", [0xFF, 0xD8, 1], "sugerida pela data: 2.1 Alvenaria", new DateOnly(2026, 10, 15)),
         new FotoParaIA("F2", [0xFF, 0xD8, 2], "sem tarefa pela data", new DateOnly(2026, 10, 16))],
        ["Bloco A", "Bloco B"]);

    private void Responder(JsonObject json) =>
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(json.ToJsonString())));

    [Fact]
    public async Task Pedido_ComAsTarefasOsLocaisEAsFotos_EOFormato()
    {
        Responder(new JsonObject { ["fotos"] = new JsonArray() });

        await Analista().ClassificarFotosAsync(Pedido(), CancellationToken.None);

        var corpo = _servidor.UltimoCorpo;
        corpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().EndWith(AnalistaGemini.InstrucoesDaClassificacao);
        var partes = corpo["contents"]![0]!["parts"]!.AsArray();
        partes[0]!["text"]!.GetValue<string>().Should()
            .Contain("Locais da obra: Bloco A; Bloco B")
            .And.Contain("T1 | 1.1 Fundação | Estrutura | 28/09/2026 a 30/09/2026 | 100% | não")
            .And.Contain("T2 | 2.1 Alvenaria | Vedações | 01/10/2026 a 20/10/2026 | 40% | sim");
        partes.Count.Should().Be(5, "os dados e, para cada foto, o texto e a imagem");
        var itens = corpo["generationConfig"]!["responseSchema"]!["properties"]!["fotos"]!["items"]!;
        itens["properties"]!["confianca"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("alta", "media", "baixa");
    }

    [Fact]
    public async Task Resposta_Conferida_TarefaELocalDoPedido_PercentualNoIntervalo()
    {
        Responder(new JsonObject
        {
            ["fotos"] = new JsonArray(
                new JsonObject { ["id"] = "F1", ["tarefa"] = "t2", ["confianca"] = "média", ["legenda"] = " Blocos assentados. ", ["local"] = "bloco a", ["percentual"] = 140, ["justificativa"] = "Paredes." },
                new JsonObject { ["id"] = "F1", ["tarefa"] = "T1", ["confianca"] = "alta", ["legenda"] = "Repetida", ["justificativa"] = "x" },
                new JsonObject { ["id"] = "F2", ["tarefa"] = "T9", ["confianca"] = "alta", ["legenda"] = "Céu", ["local"] = "Garagem", ["justificativa"] = "?" },
                new JsonObject { ["id"] = "F7", ["tarefa"] = "T1", ["confianca"] = "alta", ["legenda"] = "x", ["justificativa"] = "x" }),
        });

        var r = await Analista().ClassificarFotosAsync(Pedido(), CancellationToken.None);

        r.Should().Equal(
            new ClassificacaoDaFoto("F1", "T2", ConfiancaDaIA.Media, "Blocos assentados.", "Bloco A", 100, "Paredes."),
            new ClassificacaoDaFoto("F2", null, ConfiancaDaIA.Baixa, "Céu", null, null, "?"));
        (await Analista().ClassificarFotosAsync(Pedido() with { Fotos = [] }, CancellationToken.None)).Should().BeEmpty();
        _servidor.Pedidos.Should().HaveCount(1, "sem fotos não chama a API");
    }

    [Fact]
    public async Task SemChave_Indisponivel()
    {
        await ((Func<Task>)(() => new AnalistaIndisponivel().ClassificarFotosAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage(AnalistaIndisponivel.Mensagem);
    }

    [Fact]
    public void Chat_ImportarFotosEORelatorioComTexto()
    {
        FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [], "importarFotos": "ANEXOS"}""").ImportarFotos.Should().Be("anexos");
        FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [], "importarFotos": "pasta"}""").ImportarFotos.Should().Be("pasta");
        FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [], "importarFotos": "email"}""").ImportarFotos.Should().BeNull();
        FormatoDaResposta.ComoJson(new RespostaIA("x") { ImportarFotos = "pasta" }).Should().Contain("\"importarFotos\":\"pasta\"");

        FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [], "abrir": {"tela": "relatorioFotografico", "de": "2026-10-12", "texto": true}}""")
            .Abrir.Should().Be(new TelaParaAbrir("relatorioFotografico", De: "2026-10-12", Texto: true));
        FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [], "abrir": {"tela": "indicadores", "texto": true}}""")
            .Abrir!.Texto.Should().BeFalse("só o relatório fotográfico tem texto");
        InstrucoesDoAssistente.Obra.Should().Contain("importarFotos").And.Contain("\"texto\": true");
        AssistenteGemini.EsquemaDaResposta()["properties"]!["importarFotos"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().Equal(OrigensDasFotos.Todas);
    }
}
