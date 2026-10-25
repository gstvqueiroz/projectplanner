using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 51: o nível Automático (a triagem: a regra grátis, a pergunta curta à IA, o nível que o plano libera e a estimativa).
public sealed class TriagemTests : IDisposable
{
    private readonly Servidor _s = new();

    public void Dispose() => _s.Dispose();

    // A resposta do Gemini falso com a classificação dentro (como o modelo do Padrão responde à triagem).
    private static string Classificacao(string complexidade, string motivo = "montar o cronograma inteiro") => new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["content"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = new JsonObject { ["complexidade"] = complexidade, ["motivo"] = motivo }.ToJsonString() }),
            },
        }),
        ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 600, ["candidatesTokenCount"] = 20 },
    }.ToJsonString();

    private async Task<JsonElement> Avaliar(string acesso, object pedido)
    {
        var r = await _s.Post("/v1/ia/avaliar", pedido, acesso);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    }

    [Theory]
    [InlineData("qual a data de término da alvenaria?", 0, Triagem.Simples)]
    [InlineData("a laje está em 60%", 0, Triagem.Simples)]
    [InlineData("monte o cronograma de uma casa de 100 m²", 0, null)]
    [InlineData("analise o atraso da obra", 0, null)]
    [InlineData("o que diz este arquivo?", 1, null)]
    public void Regra_SoOsCurtosSemAnexoESemMontarSaoSimplesDeGraca(string mensagem, int anexos, string? esperado) =>
        Triagem.PelaRegra(new PedidoDeTriagem(mensagem, anexos)).Should().Be(esperado);

    [Fact]
    public void Regra_TextoLongo_NiveisEstimativasELeitura()
    {
        Triagem.PelaRegra(new PedidoDeTriagem(new string('a', Triagem.MensagemCurta + 1))).Should().BeNull();
        (Triagem.NivelDa(Triagem.Simples), Triagem.NivelDa(Triagem.Media), Triagem.NivelDa(Triagem.Complexa)).Should().Be(("padrao", "intermediario", "maximo"));
        // 40 mil caracteres = 10 mil tokens; 1 MB de anexos = mais 10.485 tokens; nunca menos de 2 mil.
        (Triagem.EntradaEstimada(new PedidoDeTriagem("x", Caracteres: 40_000)), Triagem.EntradaEstimada(new PedidoDeTriagem("x", 1, 1_048_576, 40_000)),
            Triagem.EntradaEstimada(new PedidoDeTriagem("x"))).Should().Be((10_000L, 20_485L, 2_000L));
        Triagem.Ler(Encoding.UTF8.GetBytes(Classificacao("media", "várias tarefas"))).Should().Be(("media", "várias tarefas"));
        Triagem.Ler(Encoding.UTF8.GetBytes(Classificacao("enorme"))).Should().BeNull("só os três tamanhos valem");
        Triagem.Ler(Encoding.UTF8.GetBytes(GeminiDeTeste.RespostaPadrao)).Should().BeNull("texto que não é JSON");
        Triagem.Ler("nada"u8.ToArray()).Should().BeNull();
        var corpo = Triagem.Corpo(new PedidoDeTriagem(new string('b', 3_000), 2, 2048, 9_000, 45));
        var dados = corpo["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();
        dados.Should().Contain("Anexos: 2 (2 KB no total)").And.Contain("Tarefas na obra: 45").And.Contain(new string('b', 2_000) + "…");
        corpo["generationConfig"]!["responseSchema"]!["properties"]!["complexidade"]!["enum"]!.AsArray().Count.Should().Be(3);
        corpo["generationConfig"]!.AsObject().ContainsKey("temperature").Should().BeFalse("0.53.1: o Haiku 5.5 recusa temperatura diferente do padrão");
    }

    [Fact]
    public async Task Curto_PelaRegra_SemChamarAIA_NoPadraoSemConfirmar()
    {
        var ana = await _s.CriarConta();

        var a = await Avaliar(ana.Acesso, new { mensagem = "a laje está em 60%", caracteres = 8_000, tarefas = 12 });

        (a.GetProperty("complexidade").GetString(), a.GetProperty("nivel").GetString(), a.GetProperty("modelo").GetString(),
                a.GetProperty("pedirConfirmacao").GetBoolean(), a.GetProperty("limitadoPeloPlano").GetBoolean())
            .Should().Be(("simples", "padrao", "gemini-2.5-flash", false, false));
        // 2 mil de entrada e 800 de saída no Flash dos testes = 600 + 2.000 = 2.600 milionésimos = 5,2 créditos → 6.
        a.GetProperty("creditosEstimados").GetDecimal().Should().Be(6m);
        _s.Gemini.Pedidos.Should().BeEmpty("a regra não gasta nada");
        _s.NoBanco(b => b.UsosDeIa.Count()).Should().Be(0);
    }

    [Fact]
    public async Task Complexo_TriagemPelaIA_ProfissionalVaiAoMaximo_ComConfirmacaoEAEstimativaDoPadrao()
    {
        var ana = await _s.CriarConta();
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        _s.Gemini.Resposta = Classificacao("complexa");

        var a = await Avaliar(ana.Acesso, new { mensagem = "monte o cronograma completo de um prédio de 4 pavimentos", caracteres = 40_000, tarefas = 0 });

        (a.GetProperty("complexidade").GetString(), a.GetProperty("motivo").GetString(), a.GetProperty("nivel").GetString(), a.GetProperty("modelo").GetString(),
                a.GetProperty("nome").GetString(), a.GetProperty("pedirConfirmacao").GetBoolean())
            .Should().Be(("complexa", "montar o cronograma inteiro", "maximo", "claude-opus-5-5", "Claude Opus 5.5", true));
        // Opus dos testes: 10 mil × 4 + 8 mil × 20 = 200.000 milionésimos = 400 créditos; no Flash, 3.000 + 20.000 = 46 créditos.
        (a.GetProperty("creditosEstimados").GetDecimal(), a.GetProperty("modeloPadrao").GetString(), a.GetProperty("creditosNoPadrao").GetDecimal())
            .Should().Be((400m, "gemini-2.5-flash", 46m));
        var triagem = JsonNode.Parse(_s.Gemini.Pedidos.Single().Corpo)!;
        triagem["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().StartWith("Você faz a triagem");
        triagem.ToJsonString().Should().NotContain("retrato", "só a mensagem e alguns números vão à triagem");
        // A triagem custa o real (600 × 0,30 + 20 × 2,50 = 230 milionésimos) e fica na rotina dela.
        _s.NoBanco(b => b.UsosDeIa.Single()).Should().Match<ProjectPlanner.Servidor.Dados.UsoDeIa>(u => u.Rotina == "triagem" && u.CustoEmMicrodolares == 230);
    }

    [Fact]
    public async Task Gratis_ComplexoFicaNoPadrao_AvisandoQueOPlanoLimitou_ETriagemQueFalhaViraSimples()
    {
        var ana = await _s.CriarConta();
        _s.Gemini.Resposta = Classificacao("media");

        var media = await Avaliar(ana.Acesso, new { mensagem = "acrescente ao cronograma a etapa de cobertura depois da estrutura", caracteres = 8_000 });
        (media.GetProperty("complexidade").GetString(), media.GetProperty("nivel").GetString(), media.GetProperty("limitadoPeloPlano").GetBoolean())
            .Should().Be(("media", "padrao", true));

        _s.Gemini.Status = HttpStatusCode.InternalServerError;
        var falhou = await Avaliar(ana.Acesso, new { mensagem = "analise o documento anexo", anexos = 1, bytesDosAnexos = 500_000 });
        (falhou.GetProperty("complexidade").GetString(), falhou.GetProperty("nivel").GetString()).Should().Be(("simples", "padrao"));
        _s.NoBanco(b => b.UsosDeIa.Count()).Should().Be(1, "a triagem que falhou não é cobrada");
        (await _s.Post("/v1/ia/avaliar", new { mensagem = "oi" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Montagem_PeloBotao_EComplexaSemPerguntarAIA()
    {
        // 0.55.1: "Pode montar agora, com o que já tem." não diz o tamanho do trabalho; o programa avisa que é a montagem.
        var ana = await _s.CriarConta();
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);

        var a = await Avaliar(ana.Acesso, new { mensagem = "Pode montar agora, com o que já tem.", caracteres = 20_000, montagem = true });

        (a.GetProperty("complexidade").GetString(), a.GetProperty("motivo").GetString(), a.GetProperty("modelo").GetString(), a.GetProperty("pedirConfirmacao").GetBoolean())
            .Should().Be(("complexa", "montar o cronograma completo", "claude-opus-5-5", true));
        _s.Gemini.Pedidos.Should().BeEmpty("sem triagem pela IA");
    }

    [Fact]
    public async Task Automatico_NoGenerateContent_VaiAoPadrao_EDizQuemRespondeu()
    {
        var ana = await _s.CriarConta();
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/ia/models/automatico:generateContent")
        {
            Content = new StringContent("""{"contents":[{"role":"user","parts":[{"text":"Oi"}]}]}""", Encoding.UTF8, "application/json"),
        };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ana.Acesso);

        var r = await _s.CreateClient().SendAsync(pedido);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.GetValues(Ia.CabecalhoDoModeloUsado).Single().Should().Be("gemini-2.5-flash");
        _s.Gemini.Pedidos.Single().Endereco.AbsolutePath.Should().EndWith("/models/gemini-2.5-flash:generateContent");
        _s.NoBanco(b => b.UsosDeIa.Single().Modelo).Should().Be("gemini-2.5-flash");
        (await _s.Get("/v1/ia/models/automatico", ana.Acesso)).StatusCode.Should().Be(HttpStatusCode.OK, "o teste da conexão também aceita");
    }

    [Fact]
    public async Task SemCreditos_402_ELimiteDeConfirmacaoConfiguravel()
    {
        _s.Dispose();
        using var s = new Servidor(new() { ["Planos:CreditosParaConfirmar"] = "5", ["Planos:GratisCreditosPorMes"] = "0" });
        var ana = await s.CriarConta();
        var r = await s.Post("/v1/ia/avaliar", new { mensagem = "oi" }, ana.Acesso);
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.PaymentRequired, Ia.CotaEsgotada));

        var dar = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/usuarios/ana@exemplo.com/creditos") { Content = JsonContent.Create(new { creditos = 100 }) };
        dar.Headers.Add(Admin.Cabecalho, Servidor.ChaveDeAdmin);
        await s.CreateClient().SendAsync(dar);
        var curto = JsonDocument.Parse(await (await s.Post("/v1/ia/avaliar", new { mensagem = "oi" }, ana.Acesso)).Content.ReadAsStringAsync()).RootElement;
        curto.GetProperty("pedirConfirmacao").GetBoolean().Should().BeTrue("6 créditos estimados passam do limite de 5");
    }
}
