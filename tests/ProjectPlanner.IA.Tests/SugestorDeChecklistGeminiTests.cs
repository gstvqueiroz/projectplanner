using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 78: o checklist de um card sugerido pelo Gemini falso (o gancho de IA do Kanban).
public sealed class SugestorDeChecklistGeminiTests : IDisposable
{
    private const string Chave = "AIzaChaveDeTesteSoParaOsTestes123456789";

    private readonly ManipuladorFalso _servidor = new();
    private readonly HttpClient _http;

    public SugestorDeChecklistGeminiTests() => _http = new HttpClient(_servidor) { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose() => _http.Dispose();

    private SugestorDeChecklistGemini Sugestor() => new(_http, Chave, "gemini-2.5-flash");

    private static PedidoDeChecklist Pedido() =>
        new("Armação e concretagem das sapatas", "Fundação", "Concreto 25 MPa.", 3, ["Conferir a armação"], ["Área construída: 120 m²"]);

    private void Responder(JsonObject json, string fim = "STOP") =>
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(json.ToJsonString(), fim)));

    [Fact]
    public async Task Pedido_ComATarefaOResumoEOsItensQueJaExistem()
    {
        Responder(new JsonObject { ["itens"] = new JsonArray("Limpar as cavas") });

        await Sugestor().SugerirAsync(Pedido(), CancellationToken.None);

        var corpo = _servidor.UltimoCorpo;
        corpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be(SugestorDeChecklistGemini.Instrucoes);
        corpo["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be(
            "Tarefa: Armação e concretagem das sapatas.\nEtapa (resumo): Fundação.\nDuração: 3 dia(s) útil(eis).\nDescrição: Concreto 25 MPa.\n" +
            "Já no checklist (não repita): Conferir a armação\nFicha da obra: Área construída: 120 m²\n");
        corpo["generationConfig"]!["responseSchema"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("itens");
    }

    [Fact]
    public async Task Resposta_AparadaSemMarcadoresSemRepetirELimitada()
    {
        Responder(new JsonObject
        {
            ["itens"] = new JsonArray("- Limpar as cavas", "conferir a armação", "Limpar as cavas", " ", 4, "• Lançar o concreto",
                "a", "b", "c", "d", "e", "f", "g"),
        });

        var itens = await Sugestor().SugerirAsync(Pedido(), CancellationToken.None);

        itens.Should().Equal("Limpar as cavas", "Lançar o concreto", "a", "b", "c", "d", "e", "f");
    }

    [Fact]
    public async Task SemItens_Erro_ESemChave_Indisponivel()
    {
        Responder(new JsonObject { ["itens"] = new JsonArray("Conferir a armação") });
        await ((Func<Task>)(() => Sugestor().SugerirAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage("*não sugeriu itens*");

        var sugestor = new SugestorDeChecklistConformeConfiguracao(new ConfiguracaoFixa(), new ProvedorDeTeste());
        sugestor.EstaConfigurado.Should().BeFalse();
        await ((Func<Task>)(() => sugestor.SugerirAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage(AnalistaIndisponivel.Mensagem);
        new ProvedorGemini(_http).CriarSugestorDeChecklist(Chave, "gemini-2.5-flash").Should().BeOfType<SugestorDeChecklistGemini>();
        RotinasDeIa.Normalizar("checklist").Should().Be(RotinasDeIa.Checklist);
        RotinasDeIa.Nome(RotinasDeIa.Checklist).Should().Be("Checklist dos cards");
    }
}
