using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 38: o consultor de orçamento contra o Gemini falso (nenhum teste chama a API de verdade). T1 é a alvenaria (com
// duas candidatas), T2 a pintura (sem candidatas), T3 um marco; R1 o pedreiro.
public sealed class ConsultorGeminiTests : IDisposable
{
    private const string Chave = "AIzaChaveDeTesteSoParaOsTestes123456789";

    private readonly ManipuladorFalso _servidor = new();
    private readonly HttpClient _http;

    public ConsultorGeminiTests() => _http = new HttpClient(_servidor) { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose() => _http.Dispose();

    private ConsultorGemini Consultor() => new(_http, Chave, "gemini-2.5-flash");

    private static PedidoDeOrcamento Pedido(GruposDeSugestao grupos = GruposDeSugestao.Todos, string? foco = null) => new(
        "Casa Silva", foco, grupos, ["Área construída: 120 m²", "Pavimentos: 2"], "SINAPI SP 09/2026 desonerado", 0, null,
        [
            new TarefaParaOrcamento("T1", "2.1", "Alvenaria do térreo", "Vedações", 8, "m²", null, false, null, null, "blocos de 9 cm",
                [new CandidataParaIA("87495", "ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS 9X19X19", "M2", 52.53m),
                 new CandidataParaIA("87503", "ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS 14X19X39", "M2", 61.2m)], []),
            new TarefaParaOrcamento("T2", "3.1", "Pintura", "Acabamentos", 5, null, 300, true, null,
                "SINAPI 88489 PINTURA LÁTEX (M2) · custo 14.20", null, [],
                [new HorasNaTarefa("PINTOR COM ENCARGOS", 120, 3)]),
            new TarefaParaOrcamento("T3", "4", "Entrega", "", 0, null, null, false, null, null, null, [], []),
        ],
        [new RecursoParaOrcamento("R1", "PEDREIRO", "Profissional", null, 3.5m)]);

    private void Responder(JsonObject json, string fim = "STOP") =>
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(json.ToJsonString(), fim)));

    private static JsonObject Sugestao(string tipo, string justificativa = "porque sim", params (string Campo, JsonNode? Valor)[] campos)
    {
        var json = new JsonObject { ["tipo"] = tipo, ["justificativa"] = justificativa };
        foreach (var (campo, valor) in campos)
            json[campo] = valor;
        return json;
    }

    [Fact]
    public async Task Pedido_LevaObraFichaBaseTarefasCandidatasEquipesERecursos_EOEsquema()
    {
        Responder(new JsonObject { ["resumo"] = "Ok.", ["sugestoes"] = new JsonArray() });

        await Consultor().SugerirAsync(Pedido(foco: "só as vedações"), CancellationToken.None);

        var (pedido, _) = _servidor.Pedidos.Single();
        pedido.RequestUri!.ToString().Should().EndWith("models/gemini-2.5-flash:generateContent");
        var corpo = _servidor.UltimoCorpo;
        corpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be(ConsultorGemini.Instrucoes);
        var texto = corpo["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();
        texto.Should().Contain("Obra: Casa Silva.")
            .And.Contain("Grupos pedidos: composições (e estimativas), quantidades, revisão (alertas e BDI), equipes (durações e disponível).")
            .And.Contain("Foco do pedido: só as vedações")
            .And.Contain("- Área construída: 120 m²")
            .And.Contain("Base de preços: SINAPI SP 09/2026 desonerado.")
            .And.Contain("BDI: 0%.")
            .And.Contain("T1 | 2.1 Alvenaria do térreo | em: Vedações | 8 dias úteis | unidade m², sem quantidade | sem composição | notas: blocos de 9 cm")
            .And.Contain("    candidata 87495: ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS 9X19X19 (M2) · custo 52.53")
            .And.Contain("T2 | 3.1 Pintura | em: Acabamentos | 5 dias úteis | quantidade 300 (estimada) | composição atual: SINAPI 88489")
            .And.Contain("    equipe: PINTOR COM ENCARGOS: 120 h no total, 3 por dia")
            .And.Contain("T3 | 4 Entrega | marco")
            .And.Contain("R1 | PEDREIRO | Profissional | disponível não informado | pico 3.5 por dia");
        var esquema = corpo["generationConfig"]!["responseSchema"]!;
        esquema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("resumo", "sugestoes");
        esquema["properties"]!["sugestoes"]!["items"]!["properties"]!["tipo"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().Equal("composicao", "estimativa", "quantidade", "duracao", "disponivel", "bdi", "alerta");
    }

    [Fact]
    public async Task Resposta_CadaTipoConferido()
    {
        Responder(new JsonObject
        {
            ["resumo"] = "  Sugeri a alvenaria e a pintura.  ",
            ["sugestoes"] = new JsonArray(
                Sugestao("composicao", "bloco de 9 cm, como nas notas", ("tarefa", "T1"), ("codigo", "87495")),
                Sugestao("quantidade", "1,2 × 120 × 2,8 − 15%", ("tarefa", "t1"), ("valor", 342.72), ("unidade", "m²")),
                Sugestao("estimativa", "faixa usual", ("tarefa", "T2"), ("descricao", "Pintura acrílica"), ("unidade", "m²"),
                    ("custoMateriais", "8,50"), ("custoMaoDeObra", 12)),
                Sugestao("duracao", "3 pintores", ("tarefa", "T2"), ("valor", 4.4)),
                Sugestao("disponivel", "uma equipe", ("recurso", "R1"), ("valor", 2)),
                Sugestao("bdi", "faixa do TCU", ("valor", 25)),
                Sugestao("alerta", "", ("descricao", "Falta chapisco e reboco."), ("tarefa", "T1")),
                Sugestao("alerta", "Sem limpeza final.")),
        });

        var r = await Consultor().SugerirAsync(Pedido(), CancellationToken.None);

        r.Resumo.Should().Be("Sugeri a alvenaria e a pintura.");
        r.Sugestoes.Should().Equal(
            new SugestaoDaIA("composicao", "bloco de 9 cm, como nas notas", "T1", Codigo: "87495"),
            new SugestaoDaIA("quantidade", "1,2 × 120 × 2,8 − 15%", "T1", Unidade: "m²", Valor: 342.72m),
            new SugestaoDaIA("estimativa", "faixa usual", "T2", Descricao: "Pintura acrílica", Unidade: "m²", CustoDeMateriais: 8.5m, CustoDeMaoDeObra: 12),
            new SugestaoDaIA("duracao", "3 pintores", "T2", Valor: 4),
            new SugestaoDaIA("disponivel", "uma equipe", Recurso: "R1", Valor: 2),
            new SugestaoDaIA("bdi", "faixa do TCU", Valor: 25),
            new SugestaoDaIA("alerta", "", "T1", Descricao: "Falta chapisco e reboco."),
            new SugestaoDaIA("alerta", "", Descricao: "Sem limpeza final."));
    }

    [Fact]
    public async Task Resposta_ForaDoPedido_InventadaOuRepetida_FicaDeFora()
    {
        Responder(new JsonObject
        {
            ["resumo"] = "x",
            ["sugestoes"] = new JsonArray(
                Sugestao("composicao", "código que não é candidata", ("tarefa", "T1"), ("codigo", "99999")),
                Sugestao("composicao", "tarefa que não existe", ("tarefa", "T9"), ("codigo", "87495")),
                Sugestao("composicao", "ok", ("tarefa", "T1"), ("codigo", "87503")),
                Sugestao("estimativa", "repete o preço da T1", ("tarefa", "T1"), ("descricao", "x"), ("unidade", "m2"), ("custoMateriais", 10)),
                Sugestao("estimativa", "custo zero", ("tarefa", "T2"), ("unidade", "m2"), ("custoMateriais", 0)),
                Sugestao("quantidade", "negativa", ("tarefa", "T2"), ("valor", -3)),
                Sugestao("duracao", "marco não tem duração", ("tarefa", "T3"), ("valor", 3)),
                Sugestao("duracao", "igual à atual", ("tarefa", "T2"), ("valor", 5)),
                Sugestao("disponivel", "recurso que não existe", ("recurso", "R7"), ("valor", 2)),
                Sugestao("bdi", "fora de 0 a 100", ("valor", 140)),
                Sugestao("bdi", "igual ao atual", ("valor", 0)),
                Sugestao("inventado", "tipo desconhecido"),
                Sugestao("alerta", "")),
        });

        var r = await Consultor().SugerirAsync(Pedido(), CancellationToken.None);

        r.Sugestoes.Should().Equal(new SugestaoDaIA("composicao", "ok", "T1", Codigo: "87503"));
    }

    [Fact]
    public async Task Resposta_SoOsGruposPedidos_EAlertasSempre()
    {
        Responder(new JsonObject
        {
            ["resumo"] = "x",
            ["sugestoes"] = new JsonArray(
                Sugestao("composicao", "a", ("tarefa", "T1"), ("codigo", "87495")),
                Sugestao("bdi", "b", ("valor", 25)),
                Sugestao("quantidade", "c", ("tarefa", "T1"), ("valor", 100)),
                Sugestao("alerta", "Informe a área construída.")),
        });

        var r = await Consultor().SugerirAsync(Pedido(GruposDeSugestao.Quantidades), CancellationToken.None);

        r.Sugestoes.Select(s => s.Tipo).Should().Equal("quantidade", "alerta");
        _servidor.UltimoCorpo["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>().Should().Contain("Grupos pedidos: quantidades.");
    }

    [Fact]
    public async Task Erros_CortadaDefeituosaEDaApi()
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("{\"resumo\": \"cort", "MAX_TOKENS")));
        await ((Func<Task>)(() => Consultor().SugerirAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage("*cortada*use o foco*");

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("não é json")));
        await ((Func<Task>)(() => Consultor().SugerirAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage("*não deu para entender*");

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(429, ManipuladorFalso.Erro(429, "quota")));
        await ((Func<Task>)(() => Consultor().SugerirAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage("*limite de uso*");
    }

    [Fact]
    public async Task SemChave_Indisponivel_ComChave_PeloProvedor()
    {
        var configuracao = new ConfiguracaoFixa();
        var provedor = new ProvedorDeTeste();
        var consultor = new ConsultorConformeConfiguracao(configuracao, provedor);

        consultor.EstaConfigurado.Should().BeFalse();
        await ((Func<Task>)(() => consultor.SugerirAsync(Pedido(), CancellationToken.None))).Should()
            .ThrowAsync<ErroDoAssistenteException>().WithMessage(AnalistaIndisponivel.Mensagem);
        provedor.ConsultoresCriados.Should().BeEmpty();

        configuracao.ChaveApi = "chave-1";
        consultor.EstaConfigurado.Should().BeTrue();
        await ((Func<Task>)(() => consultor.SugerirAsync(Pedido(), CancellationToken.None))).Should().ThrowAsync<ErroDoAssistenteException>();
        provedor.ConsultoresCriados.Should().Equal(("chave-1", "gemini-2.5-flash"));
        new ProvedorGemini(_http).CriarConsultor(Chave, " models/gemini-2.5-flash ").Should().BeOfType<ConsultorGemini>()
            .Which.Modelo.Should().Be("gemini-2.5-flash");
        ((Action)(() => _ = new ConsultorGemini(_http, "chave com espaço", "m"))).Should().Throw<ArgumentException>();
    }

    // ---- O chat (Bloco 38) ----

    [Fact]
    public void Chat_CampoOrcamento_SoNaRespostaComum_EVoltaNoHistorico()
    {
        var r = FormatoDaResposta.Interpretar("""{"mensagem": "Vou abrir as sugestões.", "alteracoes": [], "orcamento": "  composições das vedações "}""");
        r.PedidoDeOrcamento.Should().Be("composições das vedações");
        FormatoDaResposta.ComoJson(r).Should().Contain("\"orcamento\":\"composições das vedações\"");

        FormatoDaResposta.Interpretar("""{"tipo": "perguntas", "mensagem": "?", "perguntas": [{"pergunta": "Área?"}], "alteracoes": [], "orcamento": "x"}""")
            .PedidoDeOrcamento.Should().BeNull("perguntar não pede sugestões");
        FormatoDaResposta.Interpretar("""{"mensagem": "Oi", "alteracoes": [], "orcamento": "  "}""").PedidoDeOrcamento.Should().BeNull();
        FormatoDaResposta.Interpretar($$"""{"mensagem": "Oi", "alteracoes": [], "orcamento": "{{new string('a', 500)}}"}""")
            .PedidoDeOrcamento!.Length.Should().Be(FormatoDaResposta.TamanhoMaximoDoPedidoDeOrcamento);
        FormatoDaResposta.ComoJson(new RespostaIA("Oi")).Should().NotContain("orcamento");
    }

    [Fact]
    public void Chat_InstrucoesEORetratoComOOrcamento()
    {
        InstrucoesDoAssistente.Base.Should().Contain(InstrucoesDoAssistente.Orcamento);
        var hoje = new DateOnly(2026, 9, 28);
        var tarefa = new TarefaNoContexto(1, "1", "Alvenaria", 0, TipoNoContexto.Tarefa, 5, hoje, hoje.AddDays(4), 0, "", 0, true, null, 0);
        var semOrcamento = new ContextoDoProjeto("Casa", hoje, hoje, hoje, [tarefa]).ComoTexto();
        semOrcamento.Should().NotContain("Orçamento:");

        var texto = new ContextoDoProjeto("Casa", hoje, hoje, hoje, [tarefa],
            ["BDI 25% · total R$ 6.369,00", "1 Alvenaria: SINAPI 87495 · 100,00 M2 × 63,69 | 6.369,00"]).ComoTexto();

        texto.Should().EndWith("Orçamento:\n- BDI 25% · total R$ 6.369,00\n- 1 Alvenaria: SINAPI 87495 · 100,00 M2 × 63,69 / 6.369,00".ReplaceLineEndings());
        var muitas = new ContextoDoProjeto("Casa", hoje, hoje, hoje, [tarefa], [.. Enumerable.Range(1, 205).Select(i => $"linha {i}")]).ComoTexto();
        muitas.Should().EndWith("- (mais 5 serviços)");
    }

    [Fact]
    public async Task Estimativa_TrazAEquipe_DaTarefa_SemRepetirNemAbsurdos_ENumMarcoNao()
    {
        // Bloco 54
        Responder(new JsonObject
        {
            ["resumo"] = "Estimei.",
            ["sugestoes"] = new JsonArray(
                Sugestao("estimativa", "faixa usual", ("tarefa", "T2"), ("descricao", "Pintura"), ("unidade", "m²"), ("custoMaoDeObra", 12),
                    ("equipe", new JsonArray(
                        new JsonObject { ["funcao"] = "Pintor", ["quantidade"] = 2, ["custoPorDia"] = 230.456 },
                        new JsonObject { ["funcao"] = "pintor", ["quantidade"] = 1 },
                        new JsonObject { ["funcao"] = "Ajudante", ["quantidade"] = 1, ["custoPorDia"] = -5 },
                        new JsonObject { ["funcao"] = "", ["quantidade"] = 1 },
                        new JsonObject { ["funcao"] = "Exército", ["quantidade"] = 500 }))),
                Sugestao("estimativa", "marco", ("tarefa", "T3"), ("descricao", "Entrega"), ("unidade", "un"), ("custoMateriais", 1),
                    ("equipe", new JsonArray(new JsonObject { ["funcao"] = "Pintor", ["quantidade"] = 1 })))),
        });

        var resposta = await Consultor().SugerirAsync(Pedido(), default);

        resposta.Sugestoes[0].Equipe.Should().Equal(new PessoaDaEquipe("Pintor", 2, 230.46m), new PessoaDaEquipe("Ajudante", 1, null));
        resposta.Sugestoes[1].Equipe.Should().BeNull("um marco não tem equipe");
        var corpo = _servidor.UltimoCorpo;
        corpo["generationConfig"]!["responseSchema"]!["properties"]!["sugestoes"]!["items"]!["properties"]!["equipe"]!["items"]!["required"]!.ToJsonString()
            .Should().Be("""["funcao","quantidade"]""");
        corpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Contain("custoPorDia");
    }
}
