using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 46: os níveis de modelo, os créditos pelo custo real e o Claude (o pedido no formato do Gemini, traduzido).
public sealed class ClaudeTests : IDisposable
{
    private const string RotaDoClaude = "/v1/ia/models/claude-opus-5-5:generateContent";
    private const string RotaDoPro = "/v1/ia/models/gemini-2.5-pro:generateContent";

    // Um pedido como o programa monta: instruções, conversa com foto e PDF, e a resposta estruturada.
    private const string PedidoDoPrograma = """
        {"systemInstruction":{"parts":[{"text":"Você é o assistente."},{"text":"Responda em português."}]},
         "contents":[
           {"role":"user","parts":[{"text":"Monte a obra"},{"inlineData":{"mimeType":"image/jpeg","data":"AAAA"}}]},
           {"role":"model","parts":[{"text":"Qual a área?"}]},
           {"role":"user","parts":[{"text":"100 m²"}]},
           {"role":"user","parts":[{"inlineData":{"mimeType":"application/pdf","data":"BBBB"}}]}],
         "generationConfig":{"responseMimeType":"application/json","temperature":1.4,"maxOutputTokens":64000,
           "responseSchema":{"type":"OBJECT","properties":{
             "mensagem":{"type":"STRING","description":"O texto"},
             "alteracoes":{"type":"ARRAY","items":{"type":"OBJECT","properties":{"acao":{"type":"STRING","enum":["criar","remover"]},
               "duracao":{"type":"INTEGER","nullable":true}},"required":["acao"],"propertyOrdering":["acao","duracao"]}}},
             "required":["mensagem","alteracoes"]}}}
        """;

    private readonly Servidor _s = new();

    public void Dispose() => _s.Dispose();

    private Task<HttpResponseMessage> Gerar(string acesso, string rota, string corpo)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, rota) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        return _s.CreateClient().SendAsync(pedido);
    }

    private async Task<string> ContaProfissional()
    {
        var sessao = await _s.CriarConta();
        (await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin)).StatusCode.Should().Be(HttpStatusCode.OK);
        return sessao.Acesso;
    }

    [Fact]
    public void Traducao_DoPedido_InstrucoesPapeisAnexosEFerramentaObrigatoria()
    {
        var pedido = TradutorClaude.Pedido(JsonNode.Parse(PedidoDoPrograma)!, "claude-opus-5-5", 16_000);

        (pedido["model"]!.GetValue<string>(), pedido["system"]!.GetValue<string>(), pedido["max_tokens"]!.GetValue<int>(), pedido["temperature"]!.GetValue<double>())
            .Should().Be(("claude-opus-5-5", "Você é o assistente.\n\nResponda em português.\n\n" + TradutorClaude.InstrucaoDaFerramenta, 16_000, 1.0),
                "o máximo e a temperatura ficam nos limites do Claude, e as instruções pedem a ferramenta");
        var mensagens = pedido["messages"]!.AsArray();
        mensagens.Select(m => m!["role"]!.GetValue<string>()).Should().Equal("user", "assistant", "user");
        mensagens[0]!["content"]![1]!.ToJsonString().Should().Be("""{"type":"image","source":{"type":"base64","media_type":"image/jpeg","data":"AAAA"}}""");
        mensagens[2]!["content"]!.AsArray().Select(p => p!["type"]!.GetValue<string>()).Should().Equal(["text", "document"],
            "duas mensagens seguidas do usuário viram uma só");
        pedido["tool_choice"]!.ToJsonString().Should().Be("""{"type":"auto"}""",
            "o Opus 5.5 (que pensa antes de responder) recusa forçar a ferramenta (0.45.1)");
        pedido["tools"]![0]!["input_schema"]!.ToJsonString().Should().Be(
            """{"type":"object","properties":{"mensagem":{"type":"string","description":"O texto"},"alteracoes":{"type":"array","items":{"type":"object","properties":{"acao":{"type":"string","enum":["criar","remover"]},"duracao":{"type":["integer","null"]}},"required":["acao"]}}},"required":["mensagem","alteracoes"]}""");
    }

    [Fact]
    public void Traducao_SemEsquema_SemInstrucoes_ComecandoPeloModelo()
    {
        var pedido = TradutorClaude.Pedido(JsonNode.Parse("""{"contents":[{"role":"model","parts":[{"text":"Oi"}]},{"role":"user","parts":[{"text":""},{"text":"Tudo?"}]}]}""")!,
            "claude-opus-5-5", 8_000);

        (pedido.ContainsKey("system"), pedido.ContainsKey("tools"), pedido["max_tokens"]!.GetValue<int>()).Should().Be((false, false, 8_000));
        pedido["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()).Should().Equal("user", "assistant", "user");
        FluentActions.Invoking(() => TradutorClaude.Pedido(JsonNode.Parse("""{"contents":[]}""")!, "m", 1)).Should().Throw<PedidoIncompativelException>();
        FluentActions.Invoking(() => TradutorClaude.Pedido(JsonNode.Parse("""{"contents":[{"role":"user","parts":[{"inlineData":{"mimeType":"image/heic","data":"x"}}]}]}""")!, "m", 1))
            .Should().Throw<PedidoIncompativelException>().WithMessage("O Claude não lê fotos HEIC*");
        FluentActions.Invoking(() => TradutorClaude.Pedido(JsonNode.Parse("""{"contents":[{"role":"user","parts":[{"inlineData":{"mimeType":"text/csv","data":"x"}}]}]}""")!, "m", 1))
            .Should().Throw<PedidoIncompativelException>().WithMessage("O Claude não lê anexos do tipo text/csv*");
    }

    [Fact]
    public void Traducao_DaResposta_FerramentaTextoECorte()
    {
        var (resposta, entrada, saida) = TradutorClaude.Resposta(JsonNode.Parse(Servidor.ClaudeDeTeste)!);
        (entrada, saida).Should().Be((1500L, 200L), "a entrada inclui a lida do cache");
        resposta["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("""{"mensagem":"Olá do Claude","alteracoes":[]}""");
        (resposta["candidates"]![0]!["finishReason"]!.GetValue<string>(), resposta["usageMetadata"]!["totalTokenCount"]!.GetValue<long>()).Should().Be(("STOP", 1700L));

        var (cortada, _, _) = TradutorClaude.Resposta(JsonNode.Parse(
            """{"content":[{"type":"text","text":"Parte 1 "},{"type":"text","text":"parte 2"}],"stop_reason":"max_tokens","usage":{"input_tokens":5,"output_tokens":7}}""")!);
        (cortada["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>(), cortada["candidates"]![0]!["finishReason"]!.GetValue<string>())
            .Should().Be(("Parte 1 parte 2", "MAX_TOKENS"));
        // Com o pensamento e um texto antes da ferramenta, vale só a ferramenta.
        var (comPensamento, _, _) = TradutorClaude.Resposta(JsonNode.Parse(
            """{"content":[{"type":"thinking","thinking":"vou montar"},{"type":"text","text":"Aqui está:"},{"type":"tool_use","name":"responder","input":{"mensagem":"ok"}}],"stop_reason":"tool_use","usage":{}}""")!);
        comPensamento["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("""{"mensagem":"ok"}""");
        // Sem a ferramenta, o JSON é tirado do texto (inclusive de uma cerca ```json).
        var (emTexto, _, _) = TradutorClaude.Resposta(JsonNode.Parse(
            """{"content":[{"type":"text","text":"Segue:\n```json\n{\"mensagem\": \"oi\", \"alteracoes\": []}\n```"}],"stop_reason":"end_turn","usage":{}}""")!);
        emTexto["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("""{"mensagem": "oi", "alteracoes": []}""");
        var (soTexto, _, _) = TradutorClaude.Resposta(JsonNode.Parse(
            """{"content":[{"type":"text","text":"Sem chaves {aqui"}],"usage":{}}""")!);
        soTexto["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("Sem chaves {aqui");
        (TradutorClaude.MensagemDeErro("""{"type":"error","error":{"type":"invalid_request_error","message":"prompt is too long"}}"""),
            TradutorClaude.MensagemDeErro("nada")).Should().Be(("prompt is too long", null));
    }

    [Fact]
    public async Task Maximo_PeloClaude_ComAChaveDoServidor_RespostaNoFormatoDoGemini_ECustoReal()
    {
        var acesso = await ContaProfissional();

        var r = await Gerar(acesso, RotaDoClaude, PedidoDoPrograma);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var resposta = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        resposta["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("""{"mensagem":"Olá do Claude","alteracoes":[]}""");
        var enviado = _s.Claude.Pedidos.Single();
        (enviado.Endereco, enviado.Chave, enviado.TemAuthorization, _s.Claude.Versoes.Single())
            .Should().Be((new Uri("https://api.anthropic.com/v1/messages"), Servidor.ChaveDaAnthropic, false, "2023-06-01"));
        JsonNode.Parse(enviado.Corpo)!["model"]!.GetValue<string>().Should().Be("claude-opus-5-5");
        _s.Gemini.Pedidos.Should().BeEmpty();
        // 1.500 de entrada a US$ 4 e 200 de saída a US$ 20 por milhão = 10.000 milionésimos = 20 créditos.
        var uso = _s.NoBanco(b => b.UsosDeIa.Single());
        (uso.Modelo, uso.TokensDeEntrada, uso.TokensDeSaida, uso.CustoEmMicrodolares).Should().Be(("claude-opus-5-5", 1500, 200, 10_000L));
        (r.Headers.GetValues(Ia.CabecalhoDosCreditosUsados).Single(), r.Headers.GetValues(Ia.CabecalhoDosCreditosRestantes).Single()).Should().Be(("20", "5980"));
    }

    [Fact]
    public async Task Gratis_SoNoPadrao_AvancadoEMaximoSaoDoProfissional()
    {
        var sessao = await _s.CriarConta();

        var pro = await Gerar(sessao.Acesso, RotaDoPro, PedidoDoPrograma);
        (pro.StatusCode, await Servidor.Erro(pro)).Should().Be((HttpStatusCode.Forbidden,
            "O nível Avançado (Gemini 2.5 Pro) é do plano Profissional. Assine em Conta > Minha conta, ou volte ao nível Padrão."));
        (await Gerar(sessao.Acesso, RotaDoClaude, PedidoDoPrograma)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _s.Get("/v1/ia/models/claude-opus-5-5", sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (_s.Gemini.Pedidos.Count, _s.Claude.Pedidos.Count).Should().Be((0, 0));

        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        (await Gerar(sessao.Acesso, RotaDoPro, PedidoDoPrograma)).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.Gemini.Pedidos.Single().Endereco.AbsolutePath.Should().EndWith("/models/gemini-2.5-pro:generateContent");
        // 100 de entrada a US$ 1,25 e 50 (40 + 10 de raciocínio) de saída a US$ 10 = 625 milionésimos.
        _s.NoBanco(b => b.UsosDeIa.Single().CustoEmMicrodolares).Should().Be(625);
        var teste = await _s.Get("/v1/ia/models/claude-opus-5-5", sessao.Acesso);
        JsonDocument.Parse(await teste.Content.ReadAsStringAsync()).RootElement.GetProperty("displayName").GetString().Should().Be("Claude Opus 5.5");
    }

    [Fact]
    public async Task Modelos_DizemONivelOPlanoEOGastoDeUmPedidoTipico()
    {
        var sessao = await _s.CriarConta();
        var lista = JsonDocument.Parse(await (await _s.Get("/v1/modelos", sessao.Acesso)).Content.ReadAsStringAsync()).RootElement;

        lista.EnumerateArray().Select(m => (m.GetProperty("id").GetString(), m.GetProperty("nome").GetString(), m.GetProperty("liberado").GetBoolean(),
                m.GetProperty("disponivel").GetBoolean(), m.GetProperty("creditosPorPedido").GetInt32()))
            .Should().Equal(("gemini-2.5-flash", "Gemini 2.5 Flash", true, true, 14), ("gemini-2.5-pro", "Gemini 2.5 Pro", false, true, 55),
                ("claude-opus-5-5", "Claude Opus 5.5", false, true, 140));
        (await _s.Get("/v1/modelos")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ModelosPelaConfiguracao_TrocamONivelSemVersaoNova_ESemChaveDaAnthropicMaximoIndisponivel()
    {
        _s.Dispose();
        using var s = new Servidor(new()
        {
            ["Anthropic:Chave"] = "",
            ["Modelos:0:Id"] = "gemini-3.5-flash", ["Modelos:0:Nivel"] = "padrao", ["Modelos:0:Provedor"] = "gemini", ["Modelos:0:Nome"] = "Gemini 3.5 Flash",
            ["Modelos:0:DolaresPorMilhaoDeEntrada"] = "1.5", ["Modelos:0:DolaresPorMilhaoDeSaida"] = "9",
            ["Modelos:1:Id"] = "claude-opus-5-5", ["Modelos:1:Nivel"] = "maximo", ["Modelos:1:Provedor"] = "anthropic", ["Modelos:1:Nome"] = "Claude Opus 5.5",
            ["Modelos:1:DolaresPorMilhaoDeEntrada"] = "4", ["Modelos:1:DolaresPorMilhaoDeSaida"] = "20",
        });
        var sessao = await s.CriarConta();
        var lista = JsonDocument.Parse(await (await s.Get("/v1/modelos", sessao.Acesso)).Content.ReadAsStringAsync()).RootElement;
        lista.EnumerateArray().Select(m => (m.GetProperty("id").GetString(), m.GetProperty("disponivel").GetBoolean())).Should().Equal(
            ("gemini-3.5-flash", true), ("claude-opus-5-5", false));
        (await s.Get("/v1/ia/models/gemini-2.5-flash", sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "o 2.5 saiu da lista");

        await AdminTests.TrocarPlano(s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        var semChave = await s.Get("/v1/ia/models/claude-opus-5-5", sessao.Acesso);
        (semChave.StatusCode, await Servidor.Erro(semChave)).Should().Be((HttpStatusCode.ServiceUnavailable, Ia.SemChave));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", 502, "configuração")]
    [InlineData(HttpStatusCode.NotFound, """{"type":"error","error":{"message":"model not found"}}""", 502, "configuração")]
    [InlineData((HttpStatusCode)529, """{"type":"error","error":{"type":"overloaded_error"}}""", 503, "muito procurada")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", 503, "muito procurada")]
    [InlineData(HttpStatusCode.InternalServerError, "", 502, "com problemas")]
    [InlineData(HttpStatusCode.BadRequest, """{"type":"error","error":{"message":"prompt is too long"}}""", 400, "O Claude recusou o pedido: prompt is too long")]
    [InlineData(HttpStatusCode.OK, "não é json", 502, "não deu para entender")]
    public async Task Maximo_FalhasDoClaude_ViramMensagens_SemGastarCreditos(HttpStatusCode status, string corpo, int esperado, string trecho)
    {
        var acesso = await ContaProfissional();
        (_s.Claude.Status, _s.Claude.Resposta) = (status, corpo.Replace("invalid", "invalid " + Servidor.ChaveDaAnthropic));

        var r = await Gerar(acesso, RotaDoClaude, PedidoDoPrograma);

        ((int)r.StatusCode).Should().Be(esperado);
        var erro = await Servidor.Erro(r);
        erro.Should().Contain(trecho).And.NotContain(Servidor.ChaveDaAnthropic).And.NotContain("x-api-key");
        _s.NoBanco(b => b.UsosDeIa.Count()).Should().Be(0);
    }

    [Fact]
    public async Task Maximo_AnexoQueOClaudeNaoLe_400SemChamar_EPedidoQueNaoEJson()
    {
        var acesso = await ContaProfissional();

        var heic = await Gerar(acesso, RotaDoClaude, """{"contents":[{"role":"user","parts":[{"inlineData":{"mimeType":"image/heic","data":"x"}}]}]}""");
        (heic.StatusCode, await Servidor.Erro(heic)).Should().Be((HttpStatusCode.BadRequest,
            "O Claude não lê fotos HEIC. Use o nível Padrão ou Avançado, ou converta a foto para JPG."));
        var lixo = await Gerar(acesso, RotaDoClaude, "não é json");
        (lixo.StatusCode, await Servidor.Erro(lixo)).Should().Be((HttpStatusCode.BadRequest,
            "O pedido de IA veio num formato que o servidor não entendeu. Atualize o programa."));
        _s.Claude.Pedidos.Should().BeEmpty();

        _s.Claude.Erro = new HttpRequestException("sem rede");
        (await Gerar(acesso, RotaDoClaude, PedidoDoPrograma)).StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public void Custo_EmMilionesimosDeDolar_ArredondaParaCima()
    {
        var flash = CatalogoDeModelos.Padrao[0];
        (flash.Microdolares(10_000, 1_500), flash.Microdolares(1, 0), flash.Microdolares(0, 0)).Should().Be((6_750L, 1L, 0L));
        CatalogoDeModelos.Padrao.Select(m => (m.Nivel, m.Provedor)).Should().Equal(
            (Niveis.Padrao, Provedores.Gemini), (Niveis.Avancado, Provedores.Gemini), (Niveis.Maximo, Provedores.Anthropic));
        (Niveis.Nome(Niveis.Padrao), Niveis.Nome(Niveis.Avancado), Niveis.Nome(Niveis.Maximo)).Should().Be(("Padrão", "Avançado", "Máximo"));
    }
}
