using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Rotas;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 44: o ponto de IA, que repassa ao Gemini com a chave do servidor, confere a cota do plano e registra os tokens.
public sealed class IaTests : IDisposable
{
    private const string Rota = "/v1/ia/models/gemini-2.5-flash:generateContent";
    private const string Pedido = """{"contents":[{"role":"user","parts":[{"text":"Oi"}]}]}""";
    private Servidor _s = new();

    public void Dispose() => _s.Dispose();

    private Task<HttpResponseMessage> Gerar(string acesso, string rota = Rota, string corpo = Pedido)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, rota) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        return _s.CreateClient().SendAsync(pedido);
    }

    private long UsadosNoBanco() => _s.NoBanco(b => b.UsosDeIa.Sum(u => (long)u.TokensTotais));

    [Fact]
    public async Task Gerar_RepassaComAChaveDoServidor_DevolveComoVeio_ERegistraOsTokens()
    {
        var sessao = await _s.CriarConta();

        var r = await Gerar(sessao.Acesso);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Be(GeminiDeTeste.RespostaPadrao);
        var enviado = _s.Gemini.Pedidos.Single();
        enviado.Should().Be(new PedidoAoGemini(HttpMethod.Post,
            new Uri("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent"), Servidor.ChaveDoGemini, false, Pedido),
            "o token do usuário não vai ao Google");
        var uso = _s.NoBanco(b => b.UsosDeIa.Single());
        uso.Should().Match<UsoDeIa>(u => u.Modelo == "gemini-2.5-flash" && u.TokensDeEntrada == 100 && u.TokensDeSaida == 50 && u.TokensTotais == 150);

        var eu = JsonDocument.Parse(await (await _s.Get("/v1/eu", sessao.Acesso)).Content.ReadAsStringAsync()).RootElement;
        eu.GetProperty("tokensUsadosNoMes").GetInt64().Should().Be(150);
    }

    [Fact]
    public async Task Gerar_SemUsageMetadata_RegistraZero_ESemLoginOuModeloForaDaLista_Recusa()
    {
        var sessao = await _s.CriarConta();
        _s.Gemini.Resposta = """{"candidates":[]}""";
        (await Gerar(sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.NoBanco(b => b.UsosDeIa.Single().TokensTotais).Should().Be(0);
        _s.Gemini.Pedidos.Clear();

        var semLogin = await _s.CreateClient().PostAsync(Rota, new StringContent(Pedido, Encoding.UTF8, "application/json"));
        semLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var modelo = await Gerar(sessao.Acesso, "/v1/ia/models/gemini-ultra-caro:generateContent");
        (modelo.StatusCode, await Servidor.Erro(modelo)).Should().Be((HttpStatusCode.BadRequest,
            "O modelo \"gemini-ultra-caro\" não está disponível. Use um destes: gemini-2.5-flash, gemini-2.5-pro, claude-opus-5-5."));
        _s.Gemini.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Cota_EsgotadaNoMes_402_VoltaNoMesSeguinte_EOPlanoProfissionalLibera()
    {
        // Bloco 46: a cota é em créditos. Cada pedido de teste custa 155 milionésimos de dólar no Flash (100 de entrada a
        // US$ 0,30 e 50 de saída a US$ 2,50 por milhão); com 1 crédito = US$ 0,0001, são 1,55 crédito.
        _s.Dispose();
        _s = new Servidor(new() { ["Planos:GratisCreditosPorMes"] = "3", ["Planos:ProfissionalCreditosPorMes"] = "10", ["Planos:DolaresPorCredito"] = "0.0001" });
        _s.Relogio.Agora = new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero);
        var sessao = await _s.CriarConta();

        var primeiro = await Gerar(sessao.Acesso);
        primeiro.StatusCode.Should().Be(HttpStatusCode.OK);
        (primeiro.Headers.GetValues(Ia.CabecalhoDosCreditosUsados).Single(), primeiro.Headers.GetValues(Ia.CabecalhoDosCreditosRestantes).Single())
            .Should().Be(("1.6", "1.5"));
        (await Gerar(sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.OK, "1,55 de 3: ainda tinha créditos (o pedido que passa do limite é atendido)");
        var esgotada = await Gerar(sessao.Acesso);
        (esgotada.StatusCode, await Servidor.Erro(esgotada)).Should().Be((HttpStatusCode.PaymentRequired, Ia.CotaEsgotada));
        _s.Gemini.Pedidos.Should().HaveCount(2);

        // Virou o mês (UTC): a cota volta.
        _s.Relogio.Andar(TimeSpan.FromHours(1.5));
        var renovada = await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" });
        var acesso = (await renovada.Content.ReadFromJsonAsync<Servicos.Sessao>())!.Acesso;
        (await Gerar(acesso)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Gerar(acesso)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Gerar(acesso)).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        var plano = await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        plano.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Gerar(acesso)).StatusCode.Should().Be(HttpStatusCode.OK);
        var eu = JsonDocument.Parse(await (await _s.Get("/v1/eu", acesso)).Content.ReadAsStringAsync()).RootElement;
        (eu.GetProperty("perfil").GetProperty("plano").GetString(), eu.GetProperty("tokensUsadosNoMes").GetInt64(),
                eu.GetProperty("creditosUsadosNoMes").GetDecimal(), eu.GetProperty("creditosDoPlano").GetInt64())
            .Should().Be((Planos.Profissional, 450L, 4.7m, 10L));
    }

    [Fact]
    public async Task Gerar_PedidoGrandeDemais_413()
    {
        _s.Dispose();
        _s = new Servidor(new() { ["Limites:MegabytesPorPedido"] = "1" });
        var sessao = await _s.CriarConta();

        var r = await Gerar(sessao.Acesso, corpo: "{\"x\":\"" + new string('a', 1024 * 1024) + "\"}");

        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.RequestEntityTooLarge,
            "O pedido passa de 1 MB. Mande menos fotos ou anexos menores."));
        _s.Gemini.Pedidos.Should().BeEmpty();
        (await Gerar(sessao.Acesso, corpo: "{\"x\":\"" + new string('a', 1000 * 1000) + "\"}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Permission denied"}}""", 502)]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":401}}""", 502)]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"models/x is not found"}}""", 502)]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"API key not valid","status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""", 502)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":429}}""", 503)]
    [InlineData(HttpStatusCode.InternalServerError, "erro", 502)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", 502)]
    public async Task Gerar_FalhasDoGemini_ViramMensagensSemAChave_ENaoGastamCota(HttpStatusCode status, string corpo, int esperado)
    {
        var sessao = await _s.CriarConta();
        (_s.Gemini.Status, _s.Gemini.Resposta) = (status, corpo);

        var r = await Gerar(sessao.Acesso);

        ((int)r.StatusCode).Should().Be(esperado);
        var texto = await r.Content.ReadAsStringAsync();
        texto.Should().NotContain(Servidor.ChaveDoGemini).And.NotContain("Permission denied").And.NotContain("API key");
        (await Servidor.Erro(r)).Should().NotBeNullOrEmpty();
        UsadosNoBanco().Should().Be(0);
    }

    [Fact]
    public async Task Gerar_400DoPedido_PassaOMotivo_SemAChave()
    {
        var sessao = await _s.CriarConta();
        (_s.Gemini.Status, _s.Gemini.Resposta) = (HttpStatusCode.BadRequest,
            $$$"""{"error":{"code":400,"message":"Invalid JSON payload (key {{{Servidor.ChaveDoGemini}}})","status":"INVALID_ARGUMENT"}}""");

        var r = await Gerar(sessao.Acesso);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Be("""{"error":{"code":400,"message":"Invalid JSON payload (key (chave))","status":"INVALID_ARGUMENT"}}""");
    }

    [Fact]
    public async Task Gerar_SemConexaoComOGemini_504()
    {
        var sessao = await _s.CriarConta();
        _s.Gemini.Erro = new HttpRequestException("sem rede");

        var r = await Gerar(sessao.Acesso);

        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.GatewayTimeout, "O Gemini não respondeu. Tente de novo."));
        _s.Gemini.Erro = new TaskCanceledException("tempo limite");
        (await Gerar(sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public async Task SemChaveDoGeminiNoServidor_503_ETestarConexao()
    {
        var sessao = await _s.CriarConta();
        var teste = await _s.Get("/v1/ia/models/gemini-2.5-flash", sessao.Acesso);
        teste.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await teste.Content.ReadAsStringAsync()).RootElement.GetProperty("name").GetString().Should().Be("models/gemini-2.5-flash");
        (await _s.Get("/v1/ia/models/outro", sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _s.Get("/v1/ia/models/gemini-2.5-flash")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _s.Gemini.Pedidos.Should().BeEmpty("testar a conexão não vai ao Gemini");

        _s.Dispose();
        _s = new Servidor(new() { ["Gemini:Chave"] = "" });
        sessao = await _s.CriarConta();
        var r = await Gerar(sessao.Acesso);
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.ServiceUnavailable, Ia.SemChave));
        (await _s.Get("/v1/ia/models/gemini-2.5-flash", sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task LimitePorMinuto_PorUsuario_429()
    {
        _s.Dispose();
        _s = new Servidor(new() { ["Limites:PedidosDeIaPorMinuto"] = "2" });
        var ana = await _s.CriarConta();
        var bia = await _s.CriarConta("bia@exemplo.com");

        (await Gerar(ana.Acesso)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Gerar(ana.Acesso)).StatusCode.Should().Be(HttpStatusCode.OK);
        var r = await Gerar(ana.Acesso);
        (r.StatusCode, await Servidor.Erro(r)).Should().Be(((HttpStatusCode)429, "Muitos pedidos seguidos. Espere um minuto e tente de novo."));
        r.Headers.RetryAfter.Should().NotBeNull();
        (await Gerar(bia.Acesso)).StatusCode.Should().Be(HttpStatusCode.OK, "o limite é de cada usuário");
    }
}
