using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProjectPlanner.Servidor.Rotas;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 48: o painel financeiro (/admin e /v1/admin/painel) e o alerta de custo do mês. "Hoje" é 18/10/2026 (UTC).
public sealed class PainelTests : IDisposable
{
    // 1 milhão de tokens de entrada e 100 mil de saída: no Flash, US$ 0,30 + US$ 0,25 = 550.000 milionésimos.
    private const string RespostaGrande = """
        {"candidates":[{"content":{"parts":[{"text":"ok"}]}}],"usageMetadata":{"promptTokenCount":1000000,"candidatesTokenCount":100000,"totalTokenCount":1100000}}
        """;

    private Servidor _s = new(new() { ["Planos:ProfissionalCreditosPorMes"] = "100000", ["Planos:GratisCreditosPorMes"] = "100000" });

    public void Dispose() => _s.Dispose();

    private async Task<HttpResponseMessage> Painel(string? chave = Servidor.ChaveDeAdmin, string consulta = "")
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/painel" + consulta);
        if (chave is not null)
            pedido.Headers.Add(Admin.Cabecalho, chave);
        return await _s.CreateClient().SendAsync(pedido);
    }

    private async Task<JsonElement> Dados(string consulta = "")
    {
        var r = await Painel(consulta: consulta);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    }

    private async Task Gerar(string acesso, string modelo = "gemini-2.5-flash", string? rotina = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/v1/ia/models/{modelo}:generateContent")
        {
            Content = new StringContent("""{"contents":[{"role":"user","parts":[{"text":"Oi"}]}]}""", Encoding.UTF8, "application/json"),
        };
        if (rotina is not null)
            pedido.Headers.Add(Ia.CabecalhoDaRotina, rotina);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        (await _s.CreateClient().SendAsync(pedido)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pagina_SemDados_ComProtecoes_EOsDadosExigemAChave()
    {
        var pagina = await _s.CreateClient().GetAsync("/admin");
        pagina.StatusCode.Should().Be(HttpStatusCode.OK);
        pagina.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        pagina.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self'").And.Contain("frame-ancestors 'none'");
        (await pagina.Content.ReadAsStringAsync()).Should().Contain("Painel financeiro").And.Contain("/admin/painel.js").And.NotContain(Servidor.ChaveDeAdmin);
        var script = await _s.CreateClient().GetAsync("/admin/painel.js");
        (script.StatusCode, script.Content.Headers.ContentType!.MediaType).Should().Be((HttpStatusCode.OK, "text/javascript"));
        (await script.Content.ReadAsStringAsync()).Should().NotContain("innerHTML", "os dados entram como texto, nunca como HTML")
            .And.Contain("emailEnviadoPara");

        (await Painel(null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var errada = await Painel("errada");
        (errada.StatusCode, await Servidor.Erro(errada)).Should().Be((HttpStatusCode.Unauthorized, "Chave de admin ausente ou errada."));

        _s.Dispose();
        _s = new Servidor(new() { ["Admin:Chave"] = "" });
        (await Painel()).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "sem chave configurada, fechado");
    }

    [Fact]
    public async Task Numeros_CustoReceitaMargem_PorModeloPlanoEConta()
    {
        _s.Gemini.Resposta = RespostaGrande;
        var ana = await _s.CriarConta();
        var bia = await _s.CriarConta("bia@exemplo.com");
        await AdminTests.TrocarPlano(_s, "bia@exemplo.com", "profissional", Servidor.ChaveDeAdmin);
        await Gerar(ana.Acesso);
        await Gerar(ana.Acesso);
        await Gerar(bia.Acesso, "claude-opus-5-5"); // 1.500 (500 do cache) e 200 tokens no Claude: 8.200 milionésimos

        var d = await Dados();

        (d.GetProperty("de").GetString(), d.GetProperty("ate").GetString()).Should().Be(("2026-10-01", "2026-10-18"), "o padrão é o mês até hoje");
        var r = d.GetProperty("resumo");
        // US$ 1,1082 (2 × 0,55 + 0,0082) × R$ 5,50 × 1,035 = R$ 6,31; a receita: 1 Profissional × R$ 59 × 18/30 dias = R$ 35,40.
        (r.GetProperty("pedidos").GetInt32(), r.GetProperty("custoEmDolares").GetDecimal(), r.GetProperty("custoEmReais").GetDecimal(),
                r.GetProperty("receitaEstimadaEmReais").GetDecimal(), r.GetProperty("impostosETaxasEmReais").GetDecimal(), r.GetProperty("margemEmReais").GetDecimal())
            .Should().Be((3, 1.1082m, 6.31m, 35.40m, 3.89m, 25.20m));
        (r.GetProperty("usuarios").GetInt32(), r.GetProperty("usuariosAtivos").GetInt32(), r.GetProperty("profissionais").GetInt32(),
            r.GetProperty("novosCadastros").GetInt32(), r.GetProperty("margemPercentual").GetDecimal()).Should().Be((2, 2, 1, 2, 71.2m));

        var modelos = d.GetProperty("porModelo").EnumerateArray().ToList();
        modelos.Select(m => (m.GetProperty("nome").GetString(), m.GetProperty("nivel").GetString(), m.GetProperty("pedidos").GetInt32(), m.GetProperty("custoEmReais").GetDecimal()))
            .Should().Equal(("Gemini 2.5 Flash", "padrao", 2, 6.26m), ("Claude Opus 5.5", "maximo", 1, 0.05m));
        modelos[0].GetProperty("creditosMedios").GetDecimal().Should().Be(1100m, "US$ 0,55 por pedido / US$ 0,0005 por crédito");

        d.GetProperty("porPlano").EnumerateArray().Select(p => (p.GetProperty("plano").GetString(), p.GetProperty("usuarios").GetInt32(), p.GetProperty("pedidos").GetInt32(),
                p.GetProperty("receitaEmReais").GetDecimal(), p.GetProperty("margemEmReais").GetDecimal()))
            .Should().Equal(("gratis", 1, 2, 0m, -6.26m), ("profissional", 1, 1, 35.40m, 31.46m));
        d.GetProperty("quemMaisGasta").EnumerateArray().Select(u => (u.GetProperty("email").GetString(), u.GetProperty("pedidos").GetInt32(), u.GetProperty("creditosUsados").GetDecimal()))
            .Should().Equal(("ana@exemplo.com", 2, 2200m), ("bia@exemplo.com", 1, 16.4m));

        var dias = d.GetProperty("porDia").EnumerateArray().ToList();
        dias.Should().HaveCount(18);
        (dias[^1].GetProperty("dia").GetString(), dias[^1].GetProperty("pedidos").GetInt32(), dias[0].GetProperty("pedidos").GetInt32()).Should().Be(("2026-10-18", 3, 0));
        d.GetProperty("premissas").GetProperty("reaisPorDolar").GetDecimal().Should().Be(5.50m);
    }

    [Fact]
    public async Task PorRotina_OCustoDeCadaUma_EQuantoVeioDoCache()
    {
        // Bloco 50: 1 milhão de entrada (400 mil do cache, a 10% do preço) e 100 mil de saída no Flash dos testes:
        // 600 mil × 0,30 + 400 mil × 0,03 + 100 mil × 2,50 = US$ 0,18 + 0,012 + 0,25 = 442.000 milionésimos (884 créditos).
        _s.Gemini.Resposta = """
            {"candidates":[{"content":{"parts":[{"text":"ok"}]}}],
             "usageMetadata":{"promptTokenCount":1000000,"cachedContentTokenCount":400000,"candidatesTokenCount":100000}}
            """;
        var ana = await _s.CriarConta();
        await Gerar(ana.Acesso, rotina: "classificar-fotos");
        await Gerar(ana.Acesso, rotina: "classificar-fotos");
        await Gerar(ana.Acesso, rotina: "chat");
        await Gerar(ana.Acesso, rotina: "rotina-de-um-programa-mais-novo");
        await Gerar(ana.Acesso);

        _s.NoBanco(b => b.UsosDeIa.OrderBy(u => u.Id).Select(u => new { u.Rotina, u.TokensDoCache, u.CustoEmMicrodolares }).ToList()
                .Select(u => (u.Rotina, u.TokensDoCache, u.CustoEmMicrodolares)))
            .Should().Equal(("classificar-fotos", 400_000, 442_000L), ("classificar-fotos", 400_000, 442_000L), ("chat", 400_000, 442_000L),
                ("outra", 400_000, 442_000L), ("outra", 400_000, 442_000L));
        var rotinas = (await Dados()).GetProperty("porRotina").EnumerateArray()
            .Select(l => (l.GetProperty("rotina").GetString(), l.GetProperty("nome").GetString(), l.GetProperty("pedidos").GetInt32(),
                l.GetProperty("creditosMedios").GetDecimal(), l.GetProperty("percentualDoCache").GetDecimal()));
        rotinas.Should().BeEquivalentTo([("classificar-fotos", "Classificar fotos", 2, 884m, 40m), ("outra", "Outras", 2, 884m, 40m),
            ("chat", "Chat do programa", 1, 884m, 40m)]);
        (await (await _s.CreateClient().GetAsync("/admin/painel.js")).Content.ReadAsStringAsync()).Should().Contain("porRotina");
    }

    [Fact]
    public async Task ErroInesperado_RespondeComCodigo_FicaGuardado_EApareceNoPainel()
    {
        // 0.55.1: uma exceção que nenhuma rota trata (aqui, o "Gemini" lança uma que o servidor não espera).
        var ana = await _s.CriarConta();
        _s.Gemini.Erro = new InvalidOperationException("algo quebrou lá dentro");
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/ia/models/gemini-2.5-flash:generateContent")
        {
            Content = new StringContent("""{"contents":[{"role":"user","parts":[{"text":"Oi"}]}]}""", Encoding.UTF8, "application/json"),
        };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ana.Acesso);

        var r = await _s.CreateClient().SendAsync(pedido);

        r.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var mensagem = await Servidor.Erro(r);
        mensagem.Should().MatchRegex(@"^Erro no servidor \(código [A-Z2-9]{6}\)\. Tente de novo; se continuar, avise o suporte com o código\.$");
        var codigo = mensagem![(mensagem.IndexOf("código ", StringComparison.Ordinal) + 7)..][..6];
        var falha = _s.NoBanco(b => b.FalhasDoServidor.Single());
        (falha.Codigo, falha.Rota, falha.Tipo, falha.Mensagem, falha.UsuarioId).Should().Be((codigo, "POST /v1/ia/models/gemini-2.5-flash:generateContent",
            "System.InvalidOperationException", "algo quebrou lá dentro", _s.NoBanco(b => b.Usuarios.Single().Id)));
        falha.Pilha.Should().Contain("InvalidOperationException");

        var noPainel = (await Dados()).GetProperty("falhas").EnumerateArray().Single();
        (noPainel.GetProperty("codigo").GetString(), noPainel.GetProperty("tipo").GetString()).Should().Be((codigo, "System.InvalidOperationException"));
        (await (await _s.CreateClient().GetAsync("/admin/painel.js")).Content.ReadAsStringAsync()).Should().Contain("Nenhum erro no período.");
    }

    [Fact]
    public async Task Periodo_EscolhidoInvalidoOuLongoDemais()
    {
        var sessao = await _s.CriarConta();
        await Gerar(sessao.Acesso);
        _s.Relogio.Andar(TimeSpan.FromDays(20)); // 07/11: o pedido ficou no mês passado

        (await Dados()).GetProperty("resumo").GetProperty("pedidos").GetInt32().Should().Be(0);
        var outubro = await Dados("?de=2026-10-01&ate=2026-10-31");
        (outubro.GetProperty("resumo").GetProperty("pedidos").GetInt32(), outubro.GetProperty("porDia").GetArrayLength()).Should().Be((1, 31));
        (outubro.GetProperty("resumo").GetProperty("margemPercentual").ValueKind).Should().Be(JsonValueKind.Null, "sem receita, sem %");

        (await Servidor.Erro(await Painel(consulta: "?de=18/10/2026"))).Should().Be("Use datas no formato AAAA-MM-DD.");
        (await Servidor.Erro(await Painel(consulta: "?de=2026-10-10&ate=2026-10-01"))).Should().Be("O fim do período vem antes do começo.");
        (await Servidor.Erro(await Painel(consulta: "?de=2025-01-01&ate=2026-10-01"))).Should().Be("O período pode ter até 366 dias.");
    }

    [Fact]
    public async Task Alerta_UmaVezPorMes_PorEmailAoAdmin()
    {
        _s.Dispose();
        _s = new Servidor(new() { ["Financeiro:AlertaMensalEmReais"] = "5", ["Admin:Email"] = "dono@exemplo.com", ["Planos:GratisCreditosPorMes"] = "100000" });
        _s.Gemini.Resposta = RespostaGrande; // R$ 3,13 por pedido
        var sessao = await _s.CriarConta();
        _s.Emails.Enviados.Clear();

        await Gerar(sessao.Acesso);
        _s.Emails.Enviados.Should().BeEmpty("R$ 3,13 ainda não passou de R$ 5");
        (await Dados()).GetProperty("alerta").GetProperty("passou").GetBoolean().Should().BeFalse();

        await Gerar(sessao.Acesso);
        await Gerar(sessao.Acesso);
        var aviso = _s.Emails.Enviados.Single();
        (aviso.Para, aviso.Assunto).Should().Be(("dono@exemplo.com", "ProjectPlanner: custo de IA de 2026-10 passou de R$ 5,00"));
        aviso.Texto.Should().Contain("R$ 6,26").And.Contain("/admin");
        var alerta = (await Dados()).GetProperty("alerta");
        (alerta.GetProperty("passou").GetBoolean(), alerta.GetProperty("custoDoMesEmReais").GetDecimal(), alerta.GetProperty("alertaEm").ValueKind,
                alerta.GetProperty("emailEnviadoPara").GetString())
            .Should().Be((true, 9.39m, JsonValueKind.String, "dono@exemplo.com"));

        _s.Relogio.Andar(TimeSpan.FromDays(20)); // novembro: o alerta pode voltar
        var nova = await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" });
        var acesso = JsonDocument.Parse(await nova.Content.ReadAsStringAsync()).RootElement.GetProperty("acesso").GetString()!;
        await Gerar(acesso);
        await Gerar(acesso);
        _s.Emails.Enviados.Should().HaveCount(2);
        _s.NoBanco(b => b.AlertasDeCusto.Select(a => a.Mes).OrderBy(m => m).ToList()).Should().Equal("2026-10", "2026-11");
    }

    [Fact]
    public async Task Alerta_DesligadoOuSemEmailDoAdmin()
    {
        _s.Gemini.Resposta = RespostaGrande;
        var sessao = await _s.CriarConta();
        _s.Emails.Enviados.Clear();
        for (var i = 0; i < 40; i++)
            await Gerar(sessao.Acesso);
        _s.Emails.Enviados.Should().BeEmpty("sem Admin:Email, o alerta só aparece no painel");
        var semEmail = (await Dados()).GetProperty("alerta");
        (semEmail.GetProperty("passou").GetBoolean(), semEmail.GetProperty("emailEnviadoPara").ValueKind).Should().Be((true, JsonValueKind.Null),
            "R$ 125 passou dos R$ 100 padrão, mas não havia para quem mandar");
        _s.NoBanco(b => b.AlertasDeCusto.Count()).Should().Be(1);

        _s.Dispose();
        _s = new Servidor(new() { ["Financeiro:AlertaMensalEmReais"] = "0", ["Admin:Email"] = "dono@exemplo.com", ["Planos:GratisCreditosPorMes"] = "100000" });
        _s.Gemini.Resposta = RespostaGrande;
        var outra = await _s.CriarConta();
        _s.Emails.Enviados.Clear();
        await Gerar(outra.Acesso);
        (_s.Emails.Enviados.Count, _s.NoBanco(b => b.AlertasDeCusto.Count())).Should().Be((0, 0));
        (await Dados()).GetProperty("alerta").GetProperty("passou").GetBoolean().Should().BeFalse("limite 0 desliga");
    }

    [Fact]
    public void Reais_ComIOF()
    {
        var f = new OpcoesFinanceiras();
        f.Reais(1_000_000).Should().Be(5.6925m, "US$ 1 × R$ 5,50 × 1,035");
        new OpcoesFinanceiras { ReaisPorDolar = 6m, PercentualSobreODolar = 0 }.Reais(500_000).Should().Be(3m);
    }
}
