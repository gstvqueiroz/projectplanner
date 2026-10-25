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
    private const string RotaDoPro = "/v1/ia/models/gemini-3.1-pro-preview:generateContent";

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

        (pedido["model"]!.GetValue<string>(), pedido["system"]![0]!["text"]!.GetValue<string>(), pedido["max_tokens"]!.GetValue<int>(), pedido.ContainsKey("temperature"))
            .Should().Be(("claude-opus-5-5", "Você é o assistente.\n\nResponda em português.\n\n" + TradutorClaude.InstrucaoDaFerramenta, 16_000, false),
                "o máximo fica no limite do servidor, a temperatura não vai (0.53.1: o Haiku e o Sonnet 5.5 recusam) e as instruções pedem a ferramenta");
        // Bloco 50: as instruções (com o retrato da obra) vão num bloco só, marcado para o cache.
        (pedido["system"]!.AsArray().Count, pedido["system"]![0]!["cache_control"]!.ToJsonString()).Should().Be((1, """{"type":"ephemeral"}"""));
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
        var (resposta, tokens) = TradutorClaude.Resposta(JsonNode.Parse(Servidor.ClaudeDeTeste)!);
        tokens.Should().Be(new TokensDoPedido(1500, 200, 500, 0), "a entrada inclui a lida do cache, que o Bloco 50 separa");
        (resposta["usageMetadata"]!["cachedContentTokenCount"]!.GetValue<long>(), resposta["usageMetadata"]![TokensDoPedido.CampoGravadosNoCache]!.GetValue<long>())
            .Should().Be((500L, 0L), "o Telegram mede pelo usageMetadata");
        resposta["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("""{"mensagem":"Olá do Claude","alteracoes":[]}""");
        (resposta["candidates"]![0]!["finishReason"]!.GetValue<string>(), resposta["usageMetadata"]!["totalTokenCount"]!.GetValue<long>()).Should().Be(("STOP", 1700L));

        var (cortada, _) = TradutorClaude.Resposta(JsonNode.Parse(
            """{"content":[{"type":"text","text":"Parte 1 "},{"type":"text","text":"parte 2"}],"stop_reason":"max_tokens","usage":{"input_tokens":5,"output_tokens":7}}""")!);
        (cortada["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>(), cortada["candidates"]![0]!["finishReason"]!.GetValue<string>())
            .Should().Be(("Parte 1 parte 2", "MAX_TOKENS"));
        // Com o pensamento e um texto antes da ferramenta, vale só a ferramenta.
        var (comPensamento, _) = TradutorClaude.Resposta(JsonNode.Parse(
            """{"content":[{"type":"thinking","thinking":"vou montar"},{"type":"text","text":"Aqui está:"},{"type":"tool_use","name":"responder","input":{"mensagem":"ok"}}],"stop_reason":"tool_use","usage":{}}""")!);
        comPensamento["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("""{"mensagem":"ok"}""");
        // Sem a ferramenta, o JSON é tirado do texto (inclusive de uma cerca ```json).
        var (emTexto, _) = TradutorClaude.Resposta(JsonNode.Parse(
            """{"content":[{"type":"text","text":"Segue:\n```json\n{\"mensagem\": \"oi\", \"alteracoes\": []}\n```"}],"stop_reason":"end_turn","usage":{}}""")!);
        emTexto["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("""{"mensagem": "oi", "alteracoes": []}""");
        var (soTexto, _) = TradutorClaude.Resposta(JsonNode.Parse(
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
        // Bloco 50: 1.000 de entrada a US$ 4, 500 lidos do cache a 10% disso e 200 de saída a US$ 20 por milhão
        // = 4.000 + 200 + 4.000 = 8.200 milionésimos = 16,4 créditos (seriam 20 sem o desconto do cache).
        var uso = _s.NoBanco(b => b.UsosDeIa.Single());
        (uso.Modelo, uso.TokensDeEntrada, uso.TokensDeSaida, uso.TokensDoCache, uso.CustoEmMicrodolares).Should().Be(("claude-opus-5-5", 1500, 200, 500, 8_200L));
        (r.Headers.GetValues(Ia.CabecalhoDosCreditosUsados).Single(), r.Headers.GetValues(Ia.CabecalhoDosCreditosRestantes).Single()).Should().Be(("16.4", "5983.6"));
    }

    [Fact]
    public async Task Gratis_SoNoPadrao_AvancadoEMaximoSaoDoProfissional()
    {
        var sessao = await _s.CriarConta();

        var pro = await Gerar(sessao.Acesso, RotaDoPro, PedidoDoPrograma);
        (pro.StatusCode, await Servidor.Erro(pro)).Should().Be((HttpStatusCode.Forbidden,
            "O nível Avançado (Gemini 3.1 Pro) é do plano Profissional. Assine em Conta > Minha conta, ou volte ao nível Padrão."));
        (await Gerar(sessao.Acesso, RotaDoClaude, PedidoDoPrograma)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _s.Get("/v1/ia/models/claude-opus-5-5", sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (_s.Gemini.Pedidos.Count, _s.Claude.Pedidos.Count).Should().Be((0, 0));

        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        (await Gerar(sessao.Acesso, RotaDoPro, PedidoDoPrograma)).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.Gemini.Pedidos.Single().Endereco.AbsolutePath.Should().EndWith("/models/gemini-3.1-pro-preview:generateContent");
        // 100 de entrada a US$ 2 e 50 (40 + 10 de raciocínio) de saída a US$ 12 = 800 milionésimos.
        _s.NoBanco(b => b.UsosDeIa.Single().CustoEmMicrodolares).Should().Be(800);
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
            .Should().Equal(("automatico", "Automático", true, true, 14), // Bloco 51: o gasto típico do Padrão
                ("gemini-2.5-flash", "Gemini 2.5 Flash", true, true, 14), ("gemini-3.1-pro-preview", "Gemini 3.1 Pro", false, true, 76),
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
            ("automatico", true), ("gemini-3.5-flash", true), ("claude-opus-5-5", false));
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
    public async Task SemOFormato_RepeteUmaVezComOLembrete_ECobraAsDuas_CortadaNaoRepete()
    {
        // 0.53.1: o Claude respondeu em texto, sem chamar a ferramenta: o programa não mudaria nada. O servidor repete uma vez.
        var acesso = await ContaProfissional();
        _s.Claude.Proximas.Enqueue("""
            {"content":[{"type":"text","text":"Aqui está o cronograma: 1. Fundação, 2. Alvenaria"}],"stop_reason":"end_turn","usage":{"input_tokens":100,"output_tokens":50}}
            """);
        _s.Claude.Proximas.Enqueue(Servidor.ClaudeDeTeste);

        var r = await Gerar(acesso, RotaDoClaude, PedidoDoPrograma);

        JsonNode.Parse(await r.Content.ReadAsStringAsync())!["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>()
            .Should().Be("""{"mensagem":"Olá do Claude","alteracoes":[]}""");
        var pedidos = _s.Claude.Pedidos.ToList();
        pedidos.Should().HaveCount(2);
        JsonNode.Parse(pedidos[1].Corpo)!["messages"]!.AsArray()[^1]!["content"]!.AsArray()[^1]!["text"]!.GetValue<string>()
            .Should().StartWith("(Lembrete: Formato da resposta:");
        _s.NoBanco(b => b.UsosDeIa.Single()).TokensDeEntrada.Should().Be(1600, "as duas tentativas são cobradas: 100 + 1.500");

        // Cortada pelo tamanho: repetir não adianta.
        _s.Claude.Proximas.Enqueue("""
            {"content":[{"type":"text","text":"{ cortada no meio, sem fechar"}],"stop_reason":"max_tokens","usage":{"input_tokens":100,"output_tokens":50}}
            """);
        (await Gerar(acesso, RotaDoClaude, PedidoDoPrograma)).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.Claude.Pedidos.Should().HaveCount(3);
        // Sem esquema (uma conversa sem formato), texto é a resposta certa.
        _s.Claude.Proximas.Enqueue("""{"content":[{"type":"text","text":"Oi!"}],"stop_reason":"end_turn","usage":{}}""");
        (await Gerar(acesso, RotaDoClaude, """{"contents":[{"role":"user","parts":[{"text":"Oi"}]}]}""")).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.Claude.Pedidos.Should().HaveCount(4);
        TradutorClaude.SemOFormato("nada"u8.ToArray()).Should().BeTrue();
    }

    [Fact]
    public void Custo_EmMilionesimosDeDolar_ArredondaParaCima_EComFaixa()
    {
        var flash = CatalogoDeModelos.Reserva;
        (flash.Microdolares(10_000, 1_500), flash.Microdolares(1, 0), flash.Microdolares(0, 0)).Should().Be((6_750L, 1L, 0L));
        // 0.47.2: o catálogo do código (Haiku no Padrão, Sonnet no Máximo).
        CatalogoDeModelos.Padrao.Select(m => (m.Id, m.Nivel, m.Provedor)).Should().Equal(
            ("claude-haiku-5-5", Niveis.Padrao, Provedores.Anthropic), ("gemini-3-flash-preview", Niveis.Intermediario, Provedores.Gemini),
            ("gemini-3.1-pro-preview", Niveis.Avancado, Provedores.Gemini),
            ("claude-sonnet-5-5", Niveis.Maximo, Provedores.Anthropic));
        var haiku = CatalogoDeModelos.Padrao[0];
        // Até 100 mil tokens de entrada: US$ 0,10 e 0,50 por milhão; acima, o pedido inteiro a US$ 0,50 e 2,50.
        (haiku.Microdolares(10_000, 1_500), haiku.Microdolares(100_000, 1_000), haiku.Microdolares(100_001, 1_000))
            .Should().Be((1_750L, 10_500L, 52_501L));
        CatalogoDeModelos.Padrao[3].Microdolares(10_000, 1_500).Should().Be(35_000L, "Sonnet: US$ 2 e 10 por milhão");
        CatalogoDeModelos.Padrao[1].Microdolares(10_000, 1_500).Should().Be(9_500L, "Bloco 50, Gemini 3 Flash: US$ 0,50 e 3 por milhão (19 créditos)");
        (Niveis.Nome(Niveis.Padrao), Niveis.Nome(Niveis.Intermediario), Niveis.Nome(Niveis.Avancado), Niveis.Nome(Niveis.Maximo))
            .Should().Be(("Padrão", "Intermediário", "Avançado", "Máximo"));
    }

    [Fact]
    public void Custo_ComCache_LeituraA10PorCento_GravacaoA125_EAFaixaPeloTotal()
    {
        var sonnet = CatalogoDeModelos.Padrao[3];
        // 10 mil de entrada, 8 mil deles lidos do cache: 2.000 × 2 + 8.000 × 0,2 + 1.500 × 10 = 4.000 + 1.600 + 15.000.
        sonnet.Microdolares(10_000, 1_500, lidosDoCache: 8_000).Should().Be(20_600L);
        // A primeira vez grava no cache: 25% a mais nos gravados.
        sonnet.Microdolares(10_000, 1_500, gravadosNoCache: 8_000).Should().Be(39_000L);
        // Lidos maiores que a entrada (um número estranho do provedor) não dão custo negativo.
        sonnet.Microdolares(1_000, 0, lidosDoCache: 5_000, gravadosNoCache: 5_000).Should().Be(200L);
        // No Haiku, a faixa vale pelo total da entrada, com ou sem cache: 120 mil, 100 mil do cache, a US$ 0,50 (acima da faixa).
        CatalogoDeModelos.Padrao[0].Microdolares(120_000, 0, lidosDoCache: 100_000).Should().Be(15_000L);
        // A reserva (Gemini 2.5 Flash) lê o cache a 25%.
        CatalogoDeModelos.Reserva.Microdolares(10_000, 0, lidosDoCache: 10_000).Should().Be(750L);
        // O Claude traduzido conta os gravados; o Gemini só os lidos (que já estão no promptTokenCount).
        TokensDoPedido.DoUso(JsonDocument.Parse("""{"promptTokenCount":900,"cachedContentTokenCount":600,"candidatesTokenCount":50,"thoughtsTokenCount":5}""").RootElement)
            .Should().Be(new TokensDoPedido(900, 55, 600, 0));
        TokensDoPedido.DoUso(JsonDocument.Parse("""{"totalTokenCount":70}""").RootElement).Should().Be(new TokensDoPedido(70, 0));
        TokensDoPedido.DoUso(JsonDocument.Parse("[]").RootElement).Should().Be(default(TokensDoPedido));
    }

    [Fact]
    public async Task CatalogoDoCodigo_GratisNoHaiku_PeloClaude_ComOCustoDaFaixa()
    {
        _s.Dispose();
        using var s = new Servidor(catalogoDoCodigo: true);
        var sessao = await s.CriarConta();
        var lista = System.Text.Json.JsonDocument.Parse(await (await s.Get("/v1/modelos", sessao.Acesso)).Content.ReadAsStringAsync()).RootElement;
        lista.EnumerateArray().Select(m => (m.GetProperty("id").GetString(), m.GetProperty("liberado").GetBoolean(), m.GetProperty("creditosPorPedido").GetInt32()))
            .Should().Equal(("automatico", true, 4), ("claude-haiku-5-5", true, 4), ("gemini-3-flash-preview", false, 19), ("gemini-3.1-pro-preview", false, 76),
                ("claude-sonnet-5-5", false, 70));

        var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/ia/models/claude-haiku-5-5:generateContent")
        {
            Content = new StringContent(PedidoDoPrograma, Encoding.UTF8, "application/json"),
        };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        var r = await s.CreateClient().SendAsync(pedido);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(s.Claude.Pedidos.Single().Corpo)!["model"]!.GetValue<string>().Should().Be("claude-haiku-5-5");
        // 1.000 de entrada, 500 do cache (a 10%) e 200 de saída no Haiku = 100 + 5 + 100 = 205 milionésimos (0,4 crédito).
        s.NoBanco(b => b.UsosDeIa.Single().CustoEmMicrodolares).Should().Be(205);
        s.Gemini.Pedidos.Should().BeEmpty();
    }

    // 0.63.1: a conta da Anthropic sem crédito: o pedido vai ao Gemini do mesmo porte, o dono fica sabendo (painel e e-mail, uma
    // vez), e os pedidos seguintes nem tentam o Claude por 15 minutos.
    [Fact]
    public async Task AnthropicSemCredito_VaiAoGemini_AvisaUmaVez_ENaoTentaDeNovo()
    {
        _s.Dispose();
        using var s = new Servidor(new() { ["Admin:Email"] = "dono@exemplo.com" }, catalogoDoCodigo: true);
        var sessao = await s.CriarConta();
        s.Claude.Status = HttpStatusCode.BadRequest;
        s.Claude.Resposta = """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."}}""";
        async Task<HttpResponseMessage> Pedir()
        {
            var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/ia/models/claude-haiku-5-5:generateContent")
            {
                Content = new StringContent(PedidoDoPrograma, Encoding.UTF8, "application/json"),
            };
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
            return await s.CreateClient().SendAsync(pedido);
        }

        var r = await Pedir();

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.GetValues(Rotas.Ia.CabecalhoDoModeloUsado).Single().Should().Be("gemini-3-flash-preview");
        (s.Claude.Pedidos.Count, s.Gemini.Pedidos.Count).Should().Be((1, 1));
        s.NoBanco(b => b.FalhasDoServidor.Single().Tipo).Should().Be("Anthropic sem crédito");
        s.NoBanco(b => b.UsosDeIa.Single().Modelo).Should().Be("gemini-3-flash-preview", "cobra o que respondeu");
        s.Emails.Ultimo.Should().Match<EmailEnviado>(e => e.Para == "dono@exemplo.com" && e.Assunto.Contains("sem crédito"));

        (await Pedir()).StatusCode.Should().Be(HttpStatusCode.OK);
        (s.Claude.Pedidos.Count, s.Gemini.Pedidos.Count, s.NoBanco(b => b.FalhasDoServidor.Count())).Should().Be((1, 2, 1),
            "nos 15 minutos seguintes, vai direto ao Gemini, sem avisar de novo");
    }
}
