using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 47: a cobrança pelo Mercado Pago (assinatura, pacotes de créditos, avisos) e os créditos avulsos. "Hoje" é 18/10/2026.
public sealed class PagamentosTests : IDisposable
{
    private const string Segredo = "segredo-do-webhook-do-mercado-pago";

    private Servidor _s = new(new() { ["MercadoPago:SegredoDoWebhook"] = Segredo });

    public void Dispose() => _s.Dispose();

    private Task<HttpResponseMessage> Pedir(HttpMethod metodo, string rota, string acesso)
    {
        var pedido = new HttpRequestMessage(metodo, rota);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        return _s.CreateClient().SendAsync(pedido);
    }

    private async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    }

    // Um aviso do Mercado Pago, assinado como ele assina (ou com outra assinatura).
    private Task<HttpResponseMessage> Aviso(string tipo, string id, string? assinatura = "certa")
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/v1/pagamentos/webhook?data.id={id}&type={tipo}")
        {
            Content = new StringContent(new JsonObject { ["type"] = tipo, ["action"] = tipo + ".updated", ["data"] = new JsonObject { ["id"] = id } }.ToJsonString(),
                Encoding.UTF8, "application/json"),
        };
        if (assinatura is not null)
        {
            const string ts = "1760788800", pedidoId = "req-1";
            var v1 = assinatura == "certa"
                ? Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Segredo), Encoding.UTF8.GetBytes($"id:{id.ToLowerInvariant()};request-id:{pedidoId};ts:{ts};")))
                : assinatura;
            pedido.Headers.Add("x-signature", $"ts={ts},v1={v1}");
            pedido.Headers.Add("x-request-id", pedidoId);
        }
        return _s.CreateClient().SendAsync(pedido);
    }

    private void AssinaturaNoMercadoPago(string id, string status, Guid dono, string? proxima = "2026-11-18T12:00:00.000-03:00") =>
        _s.MercadoPago.Respostas[$"GET preapproval/{id}"] = (HttpStatusCode.OK, new JsonObject
        {
            ["id"] = id, ["status"] = status, ["external_reference"] = dono.ToString(), ["next_payment_date"] = proxima,
            ["auto_recurring"] = new JsonObject { ["transaction_amount"] = 59.0 },
        }.ToJsonString());

    private void PagamentoNoMercadoPago(long id, string status, string referencia, decimal valor) =>
        _s.MercadoPago.Respostas[$"GET v1/payments/{id}"] = (HttpStatusCode.OK, new JsonObject
        {
            ["id"] = id, ["status"] = status, ["external_reference"] = referencia, ["transaction_amount"] = valor,
        }.ToJsonString());

    private Guid IdDe(string email) => _s.NoBanco(b => b.Usuarios.Single(u => u.Email == email).Id);

    [Fact]
    public async Task Assinar_CriaOLinkNoMercadoPago_ComOCompradorDeTesteEOValor_EUmaAtivaNaoAssinaDeNovo()
    {
        var ana = await _s.CriarConta();

        var link = await Json(await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", ana.Acesso));

        link.GetProperty("url").GetString().Should().Contain("preapproval_id=assin-1");
        var enviado = _s.MercadoPago.Ultimo;
        (enviado.Metodo, enviado.Caminho, enviado.Token).Should().Be((HttpMethod.Post, "preapproval", Servidor.TokenDoMercadoPago));
        var corpo = JsonNode.Parse(enviado.Corpo)!;
        (corpo["payer_email"]!.GetValue<string>(), corpo["external_reference"]!.GetValue<string>(), corpo["status"]!.GetValue<string>(),
                corpo["back_url"]!.GetValue<string>(), corpo["auto_recurring"]!["transaction_amount"]!.GetValue<decimal>(),
                corpo["auto_recurring"]!["frequency_type"]!.GetValue<string>())
            .Should().Be((Servidor.CompradorDeTeste, IdDe("ana@exemplo.com").ToString(), "pending", "https://servidor.teste/pagamento", 59m, "months"),
                "no modo de teste o comprador é o de teste; o preço é o do Profissional");
        _s.NoBanco(b => b.Assinaturas.Single()).Should().Match<Assinatura>(a => a.Id == "assin-1" && a.Status == "pending" && a.ValorEmReais == 59m);
        (await Json(await Pedir(HttpMethod.Get, "/v1/pagamentos", ana.Acesso))).GetProperty("assinatura").GetString().Should().Be("pending");

        AssinaturaNoMercadoPago("assin-1", "authorized", IdDe("ana@exemplo.com"));
        (await Aviso("subscription_preapproval", "assin-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        var r = await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", ana.Acesso);
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.Conflict, "Você já assina o Profissional."));
    }

    [Fact]
    public async Task AvisoDaAssinatura_AutorizadaViraProfissional_CanceladaValeAteAProximaCobranca_EDepoisVoltaAoGratis()
    {
        var ana = await _s.CriarConta();
        await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", ana.Acesso);
        var id = IdDe("ana@exemplo.com");

        AssinaturaNoMercadoPago("assin-1", "authorized", id);
        (await Aviso("subscription_preapproval", "assin-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        var eu = await Json(await _s.Get("/v1/eu", ana.Acesso));
        (eu.GetProperty("perfil").GetProperty("plano").GetString(), eu.GetProperty("creditosDoPlano").GetInt64()).Should().Be(("profissional", 6000));
        _s.MercadoPago.Pedidos.Should().Contain(p => p.Metodo == HttpMethod.Get && p.Caminho == "preapproval/assin-1", "o aviso só vale depois de conferido na API");

        // Cancelada no Mercado Pago: o mês já pago continua valendo.
        AssinaturaNoMercadoPago("assin-1", "cancelled", id);
        await Aviso("subscription_preapproval", "assin-1");
        _s.NoBanco(b => b.Usuarios.Single()).Should().Match<Usuario>(u => u.Plano == "profissional" && u.ProfissionalAte == new DateTime(2026, 11, 18, 15, 0, 0));

        _s.NoBanco(b => ExpiracaoDosPlanos.ExpirarAsync(b, new DateTime(2026, 11, 18, 14, 59, 0), default).Result).Should().Be(0);
        _s.NoBanco(b => ExpiracaoDosPlanos.ExpirarAsync(b, new DateTime(2026, 11, 18, 15, 0, 0), default).Result).Should().Be(1);
        _s.NoBanco(b => b.Usuarios.Single()).Should().Match<Usuario>(u => u.Plano == "gratis" && u.ProfissionalAte == null);
    }

    [Fact]
    public async Task Cancelar_PeloPrograma_AvisaOMercadoPago_EOPlanoValeAteOFimDoMes()
    {
        var ana = await _s.CriarConta();
        (await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura/cancelar", ana.Acesso)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", ana.Acesso);
        AssinaturaNoMercadoPago("assin-1", "authorized", IdDe("ana@exemplo.com"));
        await Aviso("subscription_preapproval", "assin-1");
        _s.MercadoPago.Respostas["PUT preapproval/assin-1"] = (HttpStatusCode.OK, """{"id":"assin-1","status":"cancelled"}""");

        var r = await Json(await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura/cancelar", ana.Acesso));

        r.GetProperty("profissionalAte").GetDateTime().Should().Be(new DateTime(2026, 11, 18, 15, 0, 0));
        (_s.MercadoPago.Ultimo.Caminho, JsonNode.Parse(_s.MercadoPago.Ultimo.Corpo)!["status"]!.GetValue<string>()).Should().Be(("preapproval/assin-1", "cancelled"));
        _s.NoBanco(b => (b.Usuarios.Single().Plano, b.Assinaturas.Single().Status)).Should().Be(("profissional", "cancelled"));

        // Sem a próxima cobrança (nunca pagou), acaba na hora.
        _s.Dispose();
        _s = new Servidor(new() { ["MercadoPago:SegredoDoWebhook"] = Segredo });
        var bia = await _s.CriarConta("bia@exemplo.com");
        await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", bia.Acesso);
        AssinaturaNoMercadoPago("assin-1", "authorized", IdDe("bia@exemplo.com"), proxima: null);
        await Aviso("subscription_preapproval", "assin-1");
        AssinaturaNoMercadoPago("assin-1", "cancelled", IdDe("bia@exemplo.com"), proxima: null);
        await Aviso("subscription_preapproval", "assin-1");
        _s.NoBanco(b => b.Usuarios.Single().Plano).Should().Be("gratis");
    }

    [Fact]
    public async Task PacoteDeCreditos_LinkDeTeste_PagamentoAprovadoSomaUmaVezSo()
    {
        var ana = await _s.CriarConta();
        var id = IdDe("ana@exemplo.com");

        var link = await Json(await Pedir(HttpMethod.Post, "/v1/pagamentos/creditos", ana.Acesso));

        link.GetProperty("url").GetString().Should().StartWith("https://sandbox.mercadopago.com.br/", "com token de teste, o link do ambiente de teste");
        var corpo = JsonNode.Parse(_s.MercadoPago.Ultimo.Corpo)!;
        var referencia = corpo["external_reference"]!.GetValue<string>();
        referencia.Should().StartWith($"creditos:{id}:3000:");
        (corpo["items"]![0]!["unit_price"]!.GetValue<decimal>(), corpo["notification_url"]!.GetValue<string>(), corpo["items"]![0]!["title"]!.GetValue<string>())
            .Should().Be((19m, "https://servidor.teste/v1/pagamentos/webhook", "ProjectPlanner: 3.000 créditos de IA"));

        PagamentoNoMercadoPago(111, "pending", referencia, 19m);
        await Aviso("payment", "111");
        _s.NoBanco(b => b.Usuarios.Single().MicrodolaresAvulsos).Should().Be(0, "pendente ainda não conta");

        PagamentoNoMercadoPago(111, "approved", referencia, 19m);
        (await Aviso("payment", "111")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Aviso("payment", "111")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(await _s.Get("/v1/eu", ana.Acesso))).GetProperty("creditosAvulsos").GetDecimal().Should().Be(3000m, "o aviso repetido não soma de novo");
        _s.NoBanco(b => b.Pagamentos.Single()).Should().Match<Pagamento>(p => p.Id == "111" && p.Tipo == "creditos" && p.Creditos == 3000 && p.ValorEmReais == 19m);
        (await Json(await Pedir(HttpMethod.Get, "/v1/pagamentos", ana.Acesso))).GetProperty("creditosAvulsos").GetDecimal().Should().Be(3000m);

        // A mensalidade (referência = a conta) fica registrada como receita; um pagamento de fora do ProjectPlanner é ignorado.
        PagamentoNoMercadoPago(222, "approved", id.ToString(), 59m);
        PagamentoNoMercadoPago(333, "approved", "pedido-de-outra-loja", 10m);
        await Aviso("payment", "222");
        await Aviso("payment", "333");
        _s.NoBanco(b => b.Pagamentos.OrderBy(p => p.Id).Select(p => p.Tipo).ToList()).Should().Equal("creditos", "assinatura");
    }

    [Fact]
    public async Task Aviso_AssinaturaErradaRecusada_SemAssinaturaConferidoNaAPI_EFalhaDaAPIPedeRepeticao()
    {
        var ana = await _s.CriarConta();
        var id = IdDe("ana@exemplo.com");
        PagamentoNoMercadoPago(444, "approved", $"creditos:{id}:3000:abc", 19m);

        (await Aviso("payment", "444", assinatura: "0000")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _s.NoBanco(b => b.Pagamentos.Count()).Should().Be(0);

        // Sem o cabeçalho (o formato antigo do Mercado Pago), só a consulta à API decide: um aviso falso não tem efeito.
        (await Aviso("payment", "555", assinatura: null)).StatusCode.Should().Be(HttpStatusCode.InternalServerError, "a API não conhece o 555: tenta depois");
        (await Aviso("payment", "444", assinatura: null)).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.NoBanco(b => b.Usuarios.Single().MicrodolaresAvulsos).Should().Be(1_500_000);

        // Tipos que não interessam, sem id ou id gigante: 200 sem fazer nada.
        (await Aviso("merchant_order", "1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _s.CreateClient().PostAsync("/v1/pagamentos/webhook", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        AssinaturaDoWebhook.Valida(Segredo, "ts=1,v1=abc", "r", "x").Should().BeFalse();
        AssinaturaDoWebhook.Valida(Segredo, "sem-formato", "r", "x").Should().BeFalse();
        AssinaturaDoWebhook.Valida("", "ts=1,v1=abc", "r", "x").Should().BeFalse();
    }

    [Fact]
    public async Task CreditosAvulsos_SoDepoisDosDoPlano_NaoVencem_EQuandoTudoAcaba402()
    {
        // 1 milhão de entrada e 100 mil de saída no Flash dos testes = 550.000 milionésimos = 1.100 créditos por pedido.
        _s.Gemini.Resposta = """
            {"candidates":[{"content":{"parts":[{"text":"ok"}]}}],"usageMetadata":{"promptTokenCount":1000000,"candidatesTokenCount":100000}}
            """;
        var ana = await _s.CriarConta();
        var dar = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/usuarios/ana@exemplo.com/creditos") { Content = JsonContent.Create(new { creditos = 2000 }) };
        dar.Headers.Add(Admin.Cabecalho, Servidor.ChaveDeAdmin);
        (await Json(await _s.CreateClient().SendAsync(dar))).GetProperty("creditosAvulsos").GetDecimal().Should().Be(2000m);

        async Task<HttpResponseMessage> Gerar()
        {
            var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/ia/models/gemini-2.5-flash:generateContent")
            {
                Content = new StringContent("""{"contents":[{"role":"user","parts":[{"text":"Oi"}]}]}""", Encoding.UTF8, "application/json"),
            };
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ana.Acesso);
            return await _s.CreateClient().SendAsync(pedido);
        }

        // Grátis: 300 do plano + 2.000 avulsos. O 1º pedido gasta os 300 do plano e 800 dos avulsos.
        (await Gerar()).Headers.GetValues(Ia.CabecalhoDosCreditosRestantes).Single().Should().Be("1200");
        _s.NoBanco(b => b.Usuarios.Single().MicrodolaresAvulsos).Should().Be(600_000);
        (await Gerar()).Headers.GetValues(Ia.CabecalhoDosCreditosRestantes).Single().Should().Be("100");
        (await Gerar()).StatusCode.Should().Be(HttpStatusCode.OK, "ainda havia 100 créditos; o pedido passa e zera");
        var r = await Gerar();
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.PaymentRequired, Ia.CotaEsgotada));

        // No mês seguinte, os do plano voltam; os avulsos acabaram e continuam zerados.
        _s.Relogio.Andar(TimeSpan.FromDays(15));
        var sessao = (await (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" })).Content.ReadFromJsonAsync<Sessao>())!;
        var eu = await Json(await _s.Get("/v1/eu", sessao.Acesso));
        (eu.GetProperty("creditosUsadosNoMes").GetDecimal(), eu.GetProperty("creditosAvulsos").GetDecimal()).Should().Be((0m, 0m));
    }

    [Fact]
    public async Task Admin_DarCreditos_ExigeChaveValorEConta_ETirarNaoFicaNegativo()
    {
        await _s.CriarConta();
        async Task<HttpResponseMessage> Dar(object corpo, string? chave = Servidor.ChaveDeAdmin, string email = "ana@exemplo.com")
        {
            var pedido = new HttpRequestMessage(HttpMethod.Post, $"/v1/admin/usuarios/{email}/creditos") { Content = JsonContent.Create(corpo) };
            if (chave is not null)
                pedido.Headers.Add(Admin.Cabecalho, chave);
            return await _s.CreateClient().SendAsync(pedido);
        }

        (await Dar(new { creditos = 10 }, chave: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Dar(new { creditos = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Dar(new { creditos = 2_000_000 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Dar(new { creditos = 10 }, email: "ninguem@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await Dar(new { creditos = 500.5 }))).GetProperty("creditosAvulsos").GetDecimal().Should().Be(500.5m);
        (await Json(await Dar(new { creditos = -1000 }))).GetProperty("creditosAvulsos").GetDecimal().Should().Be(0m);

        // O Profissional dado pelo admin não vence.
        _s.NoBanco(b =>
        {
            var u = b.Usuarios.Single();
            u.ProfissionalAte = new DateTime(2026, 1, 1);
            b.SaveChanges();
            return 0;
        });
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        _s.NoBanco(b => b.Usuarios.Single().ProfissionalAte).Should().BeNull();
    }

    [Fact]
    public async Task Desligados_SemToken_EAPaginaDeRetorno()
    {
        _s.Dispose();
        _s = new Servidor(new() { ["MercadoPago:AccessToken"] = "" });
        var ana = await _s.CriarConta();

        var situacao = await Json(await Pedir(HttpMethod.Get, "/v1/pagamentos", ana.Acesso));
        (situacao.GetProperty("disponivel").GetBoolean(), situacao.GetProperty("precoMensal").GetDecimal(), situacao.GetProperty("creditosDoPacote").GetInt64(),
            situacao.GetProperty("precoDoPacote").GetDecimal(), situacao.GetProperty("assinatura").ValueKind).Should().Be((false, 59m, 3000L, 19m, JsonValueKind.Null));
        var r = await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", ana.Acesso);
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.ServiceUnavailable, Pagamentos.Desligados));
        (await Pedir(HttpMethod.Post, "/v1/pagamentos/creditos", ana.Acesso)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Aviso("payment", "1")).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.MercadoPago.Pedidos.Should().BeEmpty();
        (await _s.CreateClient().GetAsync("/v1/pagamentos")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var pagina = await _s.CreateClient().GetAsync("/pagamento");
        (pagina.StatusCode, pagina.Content.Headers.ContentType!.MediaType).Should().Be((HttpStatusCode.OK, "text/html"));
        (await pagina.Content.ReadAsStringAsync()).Should().Contain("Volte ao ProjectPlanner");
    }

    [Fact]
    public async Task VendedorDeTeste_TokenAppUsrComTeste_UsaOCompradorDeTesteEOLinkNormal()
    {
        _s.Dispose();
        _s = new Servidor(new() { ["MercadoPago:AccessToken"] = "APP_USR-do-vendedor-de-teste", ["MercadoPago:Teste"] = "true" });
        var ana = await _s.CriarConta();

        (await Json(await Pedir(HttpMethod.Get, "/v1/pagamentos", ana.Acesso))).GetProperty("modoDeTeste").GetBoolean().Should().BeTrue();
        await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", ana.Acesso);
        JsonNode.Parse(_s.MercadoPago.Ultimo.Corpo)!["payer_email"]!.GetValue<string>().Should().Be(Servidor.CompradorDeTeste);
        (await Json(await Pedir(HttpMethod.Post, "/v1/pagamentos/creditos", ana.Acesso))).GetProperty("url").GetString()
            .Should().StartWith("https://www.mercadopago.com.br/", "com credenciais de produção de um vendedor de teste, o link normal");

        // Sem o Teste, um APP_USR- é de verdade: o comprador é o e-mail da conta.
        _s.Dispose();
        _s = new Servidor(new() { ["MercadoPago:AccessToken"] = "APP_USR-de-verdade" });
        var bia = await _s.CriarConta("bia@exemplo.com");
        await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", bia.Acesso);
        JsonNode.Parse(_s.MercadoPago.Ultimo.Corpo)!["payer_email"]!.GetValue<string>().Should().Be("bia@exemplo.com");
    }

    [Fact]
    public async Task MercadoPagoRecusa_502ComMensagem_SemGuardarNada()
    {
        var ana = await _s.CriarConta();
        _s.MercadoPago.Respostas["POST preapproval"] = (HttpStatusCode.BadRequest, """{"message":"Both payer and collector must be real or test users"}""");

        var r = await Pedir(HttpMethod.Post, "/v1/pagamentos/assinatura", ana.Acesso);

        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.BadGateway, "O Mercado Pago recusou o pedido. Tente de novo; se continuar, fale com o suporte."));
        _s.NoBanco(b => b.Assinaturas.Count()).Should().Be(0);
    }
}
