using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.IA;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 49: o assistente da obra pelo Telegram: ligar a conta, escolher a obra, perguntar e atualizar (caixa de entrada) e
// mandar fotos; e as rotas do programa (marcar a obra com o retrato, o código de ligação e a caixa).
public sealed class TelegramTests : IDisposable
{
    private const long Chat = 777_001;
    private static readonly Guid Projeto = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Alvenaria = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    // A resposta do assistente (o formato do programa): uma mensagem e uma atualização do % da tarefa Nº 2.
    private static readonly string RespostaComAtualizacao = Resposta(
        """{"mensagem":"Anotei: alvenaria em 60%.","alteracoes":[],"atualizacoes":[{"tipo":"percentual","tarefa":"2","percentual":60}]}""");

    private readonly Servidor _s = new();

    public void Dispose() => _s.Dispose();

    private static string Resposta(string json) => new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["content"] = new JsonObject { ["role"] = "model", ["parts"] = new JsonArray(new JsonObject { ["text"] = json }) },
            ["finishReason"] = "STOP",
        }),
        ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 10_000, ["candidatesTokenCount"] = 1_500, ["totalTokenCount"] = 11_500 },
    }.ToJsonString();

    private static ContextoDoProjeto Retrato() => new("Casa da Ana", new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 18), new DateOnly(2026, 10, 18),
    [
        new TarefaNoContexto(1, "1", "Fundação", 0, TipoNoContexto.Tarefa, 10, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14), 100, "", 0, true, null, 0,
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")),
        new TarefaNoContexto(2, "2", "Alvenaria", 0, TipoNoContexto.Tarefa, 15, new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 4), 40, "1", 0, true, null, 0, Alvenaria),
    ]);

    private async Task<string> Pedir(HttpMethod metodo, string rota, string acesso, object? corpo = null, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var pedido = new HttpRequestMessage(metodo, rota) { Content = corpo is null ? null : JsonContent.Create(corpo, options: AtendenteDoTelegram.Json) };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        var r = await _s.CreateClient().SendAsync(pedido);
        var texto = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(esperado, texto);
        return texto;
    }

    /// <summary>Uma conta Profissional, com a obra marcada e o Telegram ligado (pelo código, como o programa faz).</summary>
    private async Task<string> Preparar(bool ligar = true)
    {
        var sessao = await _s.CriarConta();
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", sessao.Acesso, new { nome = "Casa da Ana", retrato = Retrato() });
        if (ligar)
        {
            var codigo = JsonDocument.Parse(await Pedir(HttpMethod.Post, "/v1/telegram/codigo", sessao.Acesso)).RootElement.GetProperty("codigo").GetString();
            await Mandar($"/start {codigo}");
        }
        return sessao.Acesso;
    }

    private async Task Mandar(string? texto, string? foto = null, long chat = Chat)
    {
        var mensagem = new JsonObject { ["chat"] = new JsonObject { ["id"] = chat }, ["from"] = new JsonObject { ["first_name"] = "Ana" } };
        if (foto is not null)
        {
            mensagem["photo"] = new JsonArray(new JsonObject { ["file_id"] = foto + "-pequena", ["file_size"] = 1000 }, new JsonObject { ["file_id"] = foto, ["file_size"] = 90_000 });
            if (texto is not null)
                mensagem["caption"] = texto;
        }
        else if (texto is not null)
            mensagem["text"] = texto;
        using var escopo = _s.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>()
            .AtenderAsync(JsonDocument.Parse(new JsonObject { ["update_id"] = 1, ["message"] = mensagem }.ToJsonString()).RootElement, default);
    }

    [Fact]
    public async Task Ligar_PeloCodigo_DepoisAjudaEObra()
    {
        await Mandar("oi");
        _s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.SemVinculo);

        var acesso = await Preparar();
        _s.Telegram.Ultima.Should().StartWith("Pronto, Ana! Este chat está ligado à sua conta ProjectPlanner.").And.Contain("Obra: Casa da Ana");
        _s.NoBanco(b => b.VinculosDoTelegram.Single()).Should().Match<VinculoDoTelegram>(v => v.ChatId == Chat && v.Nome == "Ana");
        _s.NoBanco(b => b.CodigosDoTelegram.Count()).Should().Be(0, "o código é de uso único");

        await Mandar("/ajuda");
        _s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.Ajuda);
        var situacao = JsonDocument.Parse(await Pedir(HttpMethod.Get, "/v1/telegram", acesso)).RootElement;
        (situacao.GetProperty("ligado").GetBoolean(), situacao.GetProperty("nome").GetString(), situacao.GetProperty("disponivel").GetBoolean())
            .Should().Be((true, "Ana", true));

        await Mandar("/sair");
        _s.Telegram.Ultima.Should().StartWith("Pronto: este chat não está mais ligado");
        await Mandar("oi");
        _s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.SemVinculo);
    }

    [Fact]
    public async Task Codigo_VencidoOuErrado_EOLinkDoBot()
    {
        var sessao = await _s.CriarConta();
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        var resposta = JsonDocument.Parse(await Pedir(HttpMethod.Post, "/v1/telegram/codigo", sessao.Acesso)).RootElement;
        var codigo = resposta.GetProperty("codigo").GetString()!;
        (codigo.Length, resposta.GetProperty("link").GetString(), resposta.GetProperty("minutos").GetInt32())
            .Should().Be((8, $"https://t.me/ProjectPlannerTesteBot?start={codigo}", 15));

        await Mandar("/start ERRADO99");
        _s.Telegram.Ultima.Should().StartWith("Código inválido ou vencido.");
        _s.Relogio.Andar(TimeSpan.FromMinutes(16));
        await Mandar($"/start {codigo}");
        _s.Telegram.Ultima.Should().StartWith("Código inválido ou vencido.");
        await Mandar($"/start {codigo.ToLowerInvariant()}"); // ainda vencido, e minúsculas valem igual
        _s.NoBanco(b => b.VinculosDoTelegram.Count()).Should().Be(0);
    }

    [Fact]
    public async Task SoProfissional_ParaMarcarGerarCodigoEUsar()
    {
        var sessao = await _s.CriarConta();
        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", sessao.Acesso, new { nome = "Casa", retrato = Retrato() }, HttpStatusCode.Forbidden);
        await Pedir(HttpMethod.Post, "/v1/telegram/codigo", sessao.Acesso, esperado: HttpStatusCode.Forbidden);

        // Ligado quando era Profissional e voltou ao Grátis: o bot avisa.
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", sessao.Acesso, new { nome = "Casa", retrato = Retrato() });
        var codigo = JsonDocument.Parse(await Pedir(HttpMethod.Post, "/v1/telegram/codigo", sessao.Acesso)).RootElement.GetProperty("codigo").GetString();
        await Mandar($"/start {codigo}");
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Gratis, Servidor.ChaveDeAdmin);
        await Mandar("quanto já medi?");
        _s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.SoProfissional);
        _s.Gemini.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Pergunta_UsaOAssistenteComORetrato_ERespondeNoChat_EAtualizacaoVaiParaACaixa()
    {
        var acesso = await Preparar();
        _s.Gemini.Resposta = RespostaComAtualizacao;

        await Mandar("a alvenaria está em 60%");

        _s.Telegram.Ultima.Should().Be("Anotei: alvenaria em 60%.\n\n📥 1 atualização ficou na caixa da obra Casa da Ana: confira e aplique no programa (Conta > Buscar do Telegram).");
        _s.Telegram.Digitando.Should().Be(1);
        var enviado = JsonNode.Parse(_s.Gemini.Pedidos.Single().Corpo)!;
        var instrucoes = enviado["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();
        instrucoes.Should().Contain(InstrucoesDoAssistente.Telegram).And.Contain("Alvenaria").And.Contain("Casa da Ana");
        _s.Gemini.Pedidos.Single().Chave.Should().Be(Servidor.ChaveDoGemini);
        // 10.000 de entrada e 1.500 de saída no Flash = 6.750 milionésimos.
        _s.NoBanco(b => b.UsosDeIa.Single()).Should().Match<UsoDeIa>(u => u.Modelo == "gemini-2.5-flash" && u.CustoEmMicrodolares == 6_750);

        var caixa = JsonDocument.Parse(await Pedir(HttpMethod.Get, $"/v1/obras/{Projeto}/caixa", acesso)).RootElement.EnumerateArray().Single();
        (caixa.GetProperty("tipo").GetString(), caixa.GetProperty("texto").GetString()).Should().Be(("atualizacoes", "a alvenaria está em 60%"));
        var atualizacao = caixa.GetProperty("atualizacoes")[0];
        (atualizacao.GetProperty("tipo").GetString(), atualizacao.GetProperty("tarefa").GetString(), atualizacao.GetProperty("percentual").GetDecimal())
            .Should().Be(("percentual", "2", 60m));
        caixa.GetProperty("tarefas").EnumerateArray().Select(t => (t.GetProperty("numero").GetInt32(), t.GetProperty("id").GetGuid()))
            .Should().Contain((2, Alvenaria), "o programa acha a tarefa pelo id, mesmo se a numeração mudar");
        var obras = JsonDocument.Parse(await Pedir(HttpMethod.Get, "/v1/obras", acesso)).RootElement.EnumerateArray().Single();
        (obras.GetProperty("nome").GetString(), obras.GetProperty("pendentes").GetInt32()).Should().Be(("Casa da Ana", 1));

        await Pedir(HttpMethod.Delete, $"/v1/caixa/{caixa.GetProperty("id").GetGuid()}", acesso, esperado: HttpStatusCode.NoContent);
        _s.NoBanco(b => b.CaixaDeEntrada.Count()).Should().Be(0);
    }

    [Fact]
    public async Task Pergunta_SegueAConversa_ESemAtualizacaoNaoMexeNaCaixa()
    {
        await Preparar();
        _s.Gemini.Resposta = Resposta("""{"mensagem":"A fundação está concluída.","alteracoes":[]}""");
        await Mandar("como está a fundação?");
        await Mandar("e a alvenaria?");

        _s.Telegram.Ultima.Should().Be("A fundação está concluída.");
        var segundo = JsonNode.Parse(_s.Gemini.Pedidos.Last().Corpo)!["contents"]!.AsArray();
        segundo.Select(c => c!["role"]!.GetValue<string>()).Should().Equal("user", "model", "user");
        _s.NoBanco(b => b.CaixaDeEntrada.Count()).Should().Be(0);
    }

    [Fact]
    public async Task Foto_FicaNaCaixa_EOProgramaBaixaELeva()
    {
        var acesso = await Preparar();
        _s.Telegram.Arquivos["foto-1"] = Encoding.UTF8.GetBytes("bytes-da-foto");

        await Mandar("laje concretada", foto: "foto-1");

        _s.Telegram.Ultima.Should().StartWith("📷 Foto guardada na caixa da obra Casa da Ana (\"laje concretada\")");
        _s.Gemini.Pedidos.Should().BeEmpty("a foto não vai à IA no servidor: o programa classifica ao importar");
        var item = JsonDocument.Parse(await Pedir(HttpMethod.Get, $"/v1/obras/{Projeto}/caixa", acesso)).RootElement.EnumerateArray().Single();
        (item.GetProperty("tipo").GetString(), item.GetProperty("texto").GetString(), item.GetProperty("nomeDaFoto").GetString())
            .Should().Match<(string?, string?, string?)>(x => x.Item1 == "foto" && x.Item2 == "laje concretada" && x.Item3!.StartsWith("telegram-20261018-"));
        (await Pedir(HttpMethod.Get, $"/v1/caixa/{item.GetProperty("id").GetGuid()}/foto", acesso)).Should().Be("bytes-da-foto");

        await Mandar(null, foto: "sumiu");
        _s.Telegram.Ultima.Should().Be("Não deu para receber a foto. Mande de novo.");
    }

    [Fact]
    public async Task Obras_TrocarPeloTelegram_EDesmarcarLimpaACaixa()
    {
        var acesso = await Preparar();
        var outra = Guid.NewGuid();
        await Pedir(HttpMethod.Put, $"/v1/obras/{outra}", acesso, new { nome = "Galpão", retrato = Retrato() with { Nome = "Galpão" } });

        await Mandar("/obra");
        _s.Telegram.Ultima.Should().Be("Suas obras no Telegram:\n1. Casa da Ana ← agora\n2. Galpão\n\nPara trocar, mande /obra e o número (por exemplo, /obra 2).");
        await Mandar("/obra 2");
        _s.Telegram.Ultima.Should().Be("Obra: Galpão.");
        _s.Telegram.Arquivos["f"] = [1, 2, 3];
        await Mandar(null, foto: "f");
        _s.Telegram.Ultima.Should().Contain("caixa da obra Galpão");

        await Pedir(HttpMethod.Delete, $"/v1/obras/{outra}", acesso, esperado: HttpStatusCode.NoContent);
        (_s.NoBanco(b => b.Obras.Count()), _s.NoBanco(b => b.CaixaDeEntrada.Count()), _s.NoBanco(b => b.VinculosDoTelegram.Single().ObraAtiva))
            .Should().Be((1, 0, (Guid?)null));

        // Marcar de novo atualiza o retrato (não duplica).
        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", acesso, new { nome = "Casa da Ana (revisão)", retrato = Retrato() });
        _s.NoBanco(b => b.Obras.Single().Nome).Should().Be("Casa da Ana (revisão)");
        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", acesso, new { nome = "", retrato = Retrato() }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SemObraMarcada_CreditosAcabados_ELimitePorMinuto()
    {
        var sessao = await _s.CriarConta();
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        var codigo = JsonDocument.Parse(await Pedir(HttpMethod.Post, "/v1/telegram/codigo", sessao.Acesso)).RootElement.GetProperty("codigo").GetString();
        await Mandar($"/start {codigo}");
        _s.Telegram.Ultima.Should().Contain("Agora marque uma obra no programa");
        await Mandar("oi");
        _s.Telegram.Ultima.Should().StartWith("Nenhuma obra está no Telegram ainda.");

        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", sessao.Acesso, new { nome = "Casa da Ana", retrato = Retrato() });
        _s.NoBanco(b =>
        {
            b.UsosDeIa.Add(new UsoDeIa { UsuarioId = b.Usuarios.Single().Id, Quando = _s.Relogio.Agora.UtcDateTime, Modelo = "x", CustoEmMicrodolares = 3_000_000 });
            return b.SaveChanges();
        });
        await Mandar("oi");
        _s.Telegram.Ultima.Should().Be(Rotas.Ia.CotaEsgotada);

        for (var i = 0; i < 25; i++)
            await Mandar("/ajuda", chat: 999);
        _s.Telegram.Ultima.Should().Be("Muitas mensagens seguidas. Espere um minuto e mande de novo.");
    }

    [Fact]
    public async Task Webhook_ConfereOSegredo_EAtendePelaFila()
    {
        async Task<HttpStatusCode> Aviso(string? segredo, string corpo)
        {
            var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/telegram/webhook") { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
            if (segredo is not null)
                pedido.Headers.Add(Rotas.Telegram.CabecalhoDoSegredo, segredo);
            return (await _s.CreateClient().SendAsync(pedido)).StatusCode;
        }
        const string Oi = """{"update_id":5,"message":{"chat":{"id":4242},"text":"oi"}}""";

        (await Aviso(null, Oi)).Should().Be(HttpStatusCode.Unauthorized);
        (await Aviso("errado", Oi)).Should().Be(HttpStatusCode.Unauthorized);
        var segredo = new OpcoesDoTelegram { Segredo = Servidor.SegredoDoTelegram }.SegredoParaOTelegram!;
        (await Aviso(Servidor.SegredoDoTelegram, Oi)).Should().Be(HttpStatusCode.Unauthorized, "o Telegram manda o segredo derivado, não o configurado");
        (await Aviso(segredo, "não é json")).Should().Be(HttpStatusCode.BadRequest);
        (await Aviso(segredo, Oi)).Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < 50 && !_s.Telegram.Mensagens.Any(m => m.Chat == 4242); i++)
            await Task.Delay(100);
        _s.Telegram.Mensagens.Single(m => m.Chat == 4242).Texto.Should().Be(AtendenteDoTelegram.SemVinculo, "o atendente da fila respondeu");
    }

    [Fact]
    public async Task SemTelegramConfigurado_Desligado()
    {
        _s.Dispose();
        using var s = new Servidor(new() { ["Telegram:Token"] = "" });
        var sessao = await s.CriarConta();
        await AdminTests.TrocarPlano(s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/telegram/codigo");
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        var r = await s.CreateClient().SendAsync(pedido);
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.ServiceUnavailable, "O Telegram ainda não está configurado no servidor."));
        var aviso = new HttpRequestMessage(HttpMethod.Post, "/v1/telegram/webhook") { Content = new StringContent("{}") };
        aviso.Headers.Add(Rotas.Telegram.CabecalhoDoSegredo, Servidor.SegredoDoTelegram);
        (await s.CreateClient().SendAsync(aviso)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Segredo_ComoOTelegramAceita_MesmoOGeradoPeloRender()
    {
        var gerado = new OpcoesDoTelegram { Segredo = "nyYlRYj0cmIKgXhaJ41kQfwfy9pVh8/xRuIqMslvEmQ=" }.SegredoParaOTelegram!;
        gerado.Should().MatchRegex("^[A-Za-z0-9_-]{1,256}$", "o Telegram só aceita esses caracteres no secret_token");
        gerado.Should().HaveLength(64);
        new OpcoesDoTelegram { Segredo = " " }.SegredoParaOTelegram.Should().BeNull();
    }

    [Fact]
    public async Task AoSubir_RegistraOWebhookComOSegredoDerivado()
    {
        _s.Dispose();
        using var s = new Servidor(new() { ["Telegram:UrlPublica"] = "https://servidor.teste/" });
        _ = s.CreateClient(); // sobe o servidor (e os serviços de início)
        for (var i = 0; i < 50 && s.Telegram.Webhooks.IsEmpty; i++)
            await Task.Delay(50);
        s.Telegram.Webhooks.Single().Should().Be(("https://servidor.teste/v1/telegram/webhook",
            new OpcoesDoTelegram { Segredo = Servidor.SegredoDoTelegram }.SegredoParaOTelegram!));
    }

    [Fact]
    public async Task PadraoNoClaude_OTelegramPassaPelaPonte_EMedeOsTokens()
    {
        _s.Dispose();
        using var s = new Servidor(catalogoDoCodigo: true);
        var sessao = await s.CriarConta();
        await AdminTests.TrocarPlano(s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        var marcar = new HttpRequestMessage(HttpMethod.Put, $"/v1/obras/{Projeto}") { Content = JsonContent.Create(new { nome = "Casa da Ana", retrato = Retrato() }, options: AtendenteDoTelegram.Json) };
        marcar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        (await s.CreateClient().SendAsync(marcar)).StatusCode.Should().Be(HttpStatusCode.OK);
        var gerar = new HttpRequestMessage(HttpMethod.Post, "/v1/telegram/codigo");
        gerar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        var codigo = JsonDocument.Parse(await (await s.CreateClient().SendAsync(gerar)).Content.ReadAsStringAsync()).RootElement.GetProperty("codigo").GetString();
        async Task Mandar(string texto)
        {
            using var escopo = s.Services.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>().AtenderAsync(JsonDocument.Parse(new JsonObject
            {
                ["message"] = new JsonObject { ["chat"] = new JsonObject { ["id"] = Chat }, ["text"] = texto },
            }.ToJsonString()).RootElement, default);
        }
        await Mandar($"/start {codigo}");
        s.Claude.Resposta = """
            {"content":[{"type":"tool_use","name":"responder","input":{"mensagem":"Anotei a alvenaria.","alteracoes":[],"atualizacoes":[{"tipo":"percentual","tarefa":"2","percentual":60}]}}],
             "stop_reason":"tool_use","usage":{"input_tokens":2000,"cache_read_input_tokens":10000,"output_tokens":800}}
            """;

        await Mandar("a alvenaria está em 60%");

        s.Telegram.Ultima.Should().StartWith("Anotei a alvenaria.").And.Contain("1 atualização ficou na caixa");
        var enviado = JsonNode.Parse(s.Claude.Pedidos.Single().Corpo)!;
        (enviado["model"]!.GetValue<string>(), enviado["system"]![0]!["text"]!.GetValue<string>().Contains(InstrucoesDoAssistente.Telegram)).Should().Be(("claude-haiku-5-5", true));
        s.Claude.Pedidos.Single().Chave.Should().Be(Servidor.ChaveDaAnthropic, "a chave de verdade vai pela ponte");
        s.Gemini.Pedidos.Should().BeEmpty();
        // 12.000 de entrada e 800 de saída no Haiku = 1.200 + 400 = 1.600 milionésimos.
        s.NoBanco(b => b.UsosDeIa.Single()).Should().Match<UsoDeIa>(u => u.Modelo == "claude-haiku-5-5" && u.TokensDeEntrada == 12_000 && u.TokensDoCache == 10_000
            && u.CustoEmMicrodolares == 700 && u.Rotina == "telegram", "Bloco 50: 2.000 × 0,10 + 10.000 do cache × 0,01 + 800 × 0,50 = 700 (seria 1.600 sem o cache)");
        s.NoBanco(b => b.CaixaDeEntrada.Count()).Should().Be(1);

        s.Claude.Status = HttpStatusCode.Unauthorized;
        await Mandar("e a laje?");
        s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.NaoRespondeu, "sem falar em Gemini quando quem responde é o Claude");
    }
}
