using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 26: as respostas estruturadas (escolha do modo, perguntas, plano, ficha) e as instruções conforme o modo.
public class ConducaoDaConversaTests
{
    private static ConducaoDaConversa Conducao(ModoDeTrabalho modo = ModoDeTrabalho.NaoEscolhido,
        PreferenciaDeModo preferencia = PreferenciaDeModo.Perguntar, int rodadas = 0, bool aprovado = false, bool planoAgora = false,
        params ItemDeFicha[] ficha) => new(modo, preferencia, ficha, rodadas, aprovado, planoAgora);

    // ---- O formato da resposta ----

    [Fact]
    public void Interpretar_EscolhaDoModo_IgnoraAlteracoes()
    {
        var resposta = FormatoDaResposta.Interpretar("""
            {"tipo": "modo", "mensagem": "Posso montar agora ou estudar antes.",
             "alteracoes": [{"acao": "limpar"}], "ficha": [{"campo": "Tipo de obra", "valor": "Residência"}]}
            """);

        (resposta.Tipo, resposta.Texto, resposta.Alteracoes.Count, resposta.Estruturada).Should()
            .Be((TipoDeResposta.EscolherModo, "Posso montar agora ou estudar antes.", 0, true));
        resposta.Ficha.Should().Equal(new ItemDeFicha("Tipo de obra", "Residência"));
    }

    [Fact]
    public void Interpretar_Perguntas_ComOpcoesLimpasELimitadas()
    {
        var longa = new string('x', 120);
        var resposta = FormatoDaResposta.Interpretar($$"""
            {"tipo": "perguntas", "mensagem": "Algumas perguntas:", "alteracoes": [{"acao": "limpar"}], "perguntas": [
              {"pergunta": " Qual a estrutura? ", "opcoes": ["Concreto armado", " concreto ARMADO ", "", "Alvenaria estrutural", 3, "{{longa}}"]},
              {"pergunta": "   ", "opcoes": ["a"]},
              {"pergunta": "Quando começa?"},
              {"pergunta": "3"}, {"pergunta": "4"}, {"pergunta": "5"}, {"pergunta": "6"}, {"pergunta": "7"}, {"pergunta": "8"}]}
            """);

        resposta.Tipo.Should().Be(TipoDeResposta.Perguntas);
        resposta.Alteracoes.Should().BeEmpty("perguntar não mexe no cronograma");
        resposta.Perguntas.Should().HaveCount(FormatoDaResposta.MaximoDePerguntas);
        resposta.Perguntas[0].Texto.Should().Be("Qual a estrutura?");
        resposta.Perguntas[0].Opcoes.Should().Equal("Concreto armado", "Alvenaria estrutural", new string('x', FormatoDaResposta.TamanhoMaximoDaOpcao));
        resposta.Perguntas[1].Should().Match<PerguntaIA>(p => p.Texto == "Quando começa?" && p.Opcoes.Count == 0);
    }

    [Theory]
    [InlineData("""{"tipo": "perguntas", "mensagem": "Hum", "alteracoes": [], "perguntas": []}""")]
    [InlineData("""{"tipo": "plano", "mensagem": "Hum", "alteracoes": [], "plano": {"etapas": [{"nome": " "}]}}""")]
    [InlineData("""{"tipo": "plano", "mensagem": "Hum", "alteracoes": []}""")]
    [InlineData("""{"tipo": "outro", "mensagem": "Hum", "alteracoes": []}""")]
    [InlineData("""{"mensagem": "Hum", "alteracoes": []}""")]
    public void Interpretar_TipoSemConteudoOuDesconhecido_ViraRespostaComum(string json)
    {
        var resposta = FormatoDaResposta.Interpretar(json);

        (resposta.Tipo, resposta.Texto, resposta.Estruturada, resposta.Plano).Should().Be((TipoDeResposta.Resposta, "Hum", false, (PlanoIA?)null));
    }

    [Fact]
    public void Interpretar_Plano_EtapasTerminoEPremissas()
    {
        var resposta = FormatoDaResposta.Interpretar("""
            {"tipo": "plano", "mensagem": "O plano:", "alteracoes": [], "plano": {
              "etapas": [{"nome": "Fundação", "duracao": 15, "observacao": "sapatas"}, {"nome": "Estrutura", "duracao": "-2"}, {"duracao": 9}],
              "termino": " 2027-02-26 ", "premissas": ["Sábado sem obra", ""]}}
            """);

        resposta.Tipo.Should().Be(TipoDeResposta.Plano);
        resposta.Plano!.Etapas.Should().Equal(new EtapaIA("Fundação", 15, "sapatas"), new EtapaIA("Estrutura", 0));
        (resposta.Plano.Termino, resposta.Plano.Premissas.Single()).Should().Be(("2027-02-26", "Sábado sem obra"));
    }

    [Fact]
    public void Interpretar_RespostaComAlteracoesEFicha_ContinuaAplicando()
    {
        var resposta = FormatoDaResposta.Interpretar("""
            {"tipo": "resposta", "mensagem": "Montei.", "ficha": [{"campo": "Início", "valor": "2026-10-05"}, {"campo": "", "valor": "x"},
             {"campo": "Equipe", "valor": ""}], "alteracoes": [{"acao": "criar", "ref": "n1", "nome": "Fundação", "duracao": 5}]}
            """);

        resposta.Alteracoes.Should().ContainSingle();
        resposta.Ficha.Should().Equal(new ItemDeFicha("Início", "2026-10-05"), new ItemDeFicha("Equipe", ""));
        resposta.Estruturada.Should().BeTrue();
    }

    [Fact]
    public void ComoJson_VoltaIgualAoInterpretar_SemAsAlteracoes()
    {
        var original = new RespostaIA("Plano para aprovar")
        {
            Tipo = TipoDeResposta.Plano,
            Plano = new PlanoIA([new EtapaIA("Fundação", 15, "sapatas"), new EtapaIA("Estrutura", 25)], "2027-02-26", ["Sábado sem obra"]),
            Ficha = [new ItemDeFicha("Área", "100 m²")],
            Alteracoes = [new AlteracaoDoCronograma("limpar")],
        };

        var json = FormatoDaResposta.ComoJson(original);
        var lida = FormatoDaResposta.Interpretar(json);

        json.Should().Contain("\"tipo\":\"plano\"").And.Contain("100 m²").And.Contain("\"alteracoes\":[]");
        (lida.Tipo, lida.Texto, lida.Alteracoes.Count).Should().Be((TipoDeResposta.Plano, "Plano para aprovar", 0));
        lida.Plano!.Etapas.Should().Equal(original.Plano.Etapas);
        lida.Ficha.Should().Equal(original.Ficha);

        var perguntas = new RespostaIA("Perguntas") { Tipo = TipoDeResposta.Perguntas, Perguntas = [new PerguntaIA("Estrutura?", ["Concreto"])] };
        FormatoDaResposta.Interpretar(FormatoDaResposta.ComoJson(perguntas)).Perguntas.Single().Should().Match<PerguntaIA>(
            p => p.Texto == "Estrutura?" && p.Opcoes.Single() == "Concreto");
    }

    // ---- As instruções ----

    private static readonly ContextoDoProjeto Obra = new("Obra", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11), []);

    [Fact]
    public void Instrucoes_SemConducao_ComoAntes()
    {
        InstrucoesDoAssistente.Montar(Obra, null).Should().Be(InstrucoesDoAssistente.Montar(Obra));
        InstrucoesDoAssistente.Montar(Obra).Should().NotContain("MODO DE TRABALHO");
    }

    [Fact]
    public void Instrucoes_ComConducao_EntreABaseEORetrato_ComAFicha()
    {
        var texto = InstrucoesDoAssistente.Montar(Obra, Conducao(ficha: [new("Área construída", "100 m²"), new("Pavimentos", "1")]));

        texto.Should().StartWith(InstrucoesDoAssistente.Base + "\n\n" + InstrucoesDoAssistente.RespostasEstruturadas);
        texto.Should().Contain("FICHA DO PROJETO").And.Contain("\n- Área construída: 100 m²\n- Pavimentos: 1");
        texto.IndexOf("MODO DE TRABALHO", StringComparison.Ordinal).Should().BeLessThan(texto.IndexOf("CRONOGRAMA ABERTO:", StringComparison.Ordinal));
        InstrucoesDoAssistente.Montar(null, Conducao()).Should().Contain("(vazia)").And.EndWith("Nenhum projeto está aberto no momento.");
    }

    [Fact]
    public void Instrucoes_SemModoEscolhido_PedemAEscolhaNoPedidoDeCronograma()
    {
        InstrucoesDoAssistente.Conducao(Conducao()).Should().Contain("ainda não escolhido").And.Contain("responda com tipo \"modo\"")
            .And.Contain("Ajustes pontuais, perguntas e análises são atendidos normalmente");
    }

    [Theory]
    [InlineData(ModoDeTrabalho.Direto, PreferenciaDeModo.Perguntar, "montar agora")]
    [InlineData(ModoDeTrabalho.NaoEscolhido, PreferenciaDeModo.Direto, "montar agora")]
    [InlineData(ModoDeTrabalho.Estudo, PreferenciaDeModo.Perguntar, "estudar e conversar antes")]
    [InlineData(ModoDeTrabalho.NaoEscolhido, PreferenciaDeModo.Estudo, "estudar e conversar antes")]
    [InlineData(ModoDeTrabalho.Direto, PreferenciaDeModo.Estudo, "montar agora")]
    public void Instrucoes_OModoEscolhidoVenceAPreferencia(ModoDeTrabalho modo, PreferenciaDeModo preferencia, string esperado)
    {
        InstrucoesDoAssistente.Conducao(Conducao(modo, preferencia)).Should().Contain($"MODO DE TRABALHO: {esperado}");
    }

    [Fact]
    public void Instrucoes_Estudo_RodadasLimiteEPlanoAgora()
    {
        InstrucoesDoAssistente.Conducao(Conducao(ModoDeTrabalho.Estudo, rodadas: 2))
            .Should().Contain("Rodadas de perguntas já feitas neste estudo: 2 de no máximo 4.").And.NotContain("NÃO faça mais perguntas");
        InstrucoesDoAssistente.Conducao(Conducao(ModoDeTrabalho.Estudo, rodadas: ConducaoDaConversa.MaximoDeRodadas))
            .Should().Contain("NÃO faça mais perguntas: apresente o plano agora");
        InstrucoesDoAssistente.Conducao(Conducao(ModoDeTrabalho.Estudo, planoAgora: true)).Should().Contain("NÃO faça mais perguntas");
    }

    [Fact]
    public void Instrucoes_PlanoAprovado_MandamMontarTudo()
    {
        InstrucoesDoAssistente.Conducao(Conducao(ModoDeTrabalho.Estudo, aprovado: true))
            .Should().Contain("APROVOU o plano").And.Contain("Monte agora o cronograma completo").And.NotContain("Rodadas de perguntas");
    }

    [Fact]
    public void Instrucoes_ConducaoNula_Recusada()
    {
        var montar = () => InstrucoesDoAssistente.Conducao(null!);

        montar.Should().Throw<ArgumentNullException>();
    }
}

// Bloco 26, no Gemini (servidor falso): o esquema com os campos novos, as instruções com a condução e o histórico estruturado.
public sealed class GeminiConducaoTests : IDisposable
{
    private readonly ManipuladorFalso _servidor = new();
    private readonly HttpClient _http;

    public GeminiConducaoTests() => _http = new HttpClient(_servidor) { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose() => _http.Dispose();

    private AssistenteGemini Assistente() => new(_http, "AIzaChaveDeTesteSoParaOsTestes123456789", "gemini-2.5-flash");

    [Fact]
    public async Task Esquema_GanhaTipoFichaPerguntasEPlano_OpcionaisEOsObrigatoriosSaoOsDeAntes()
    {
        await Assistente().EnviarAsync(new PedidoIA("Oi", [], []), CancellationToken.None);

        var esquema = _servidor.UltimoCorpo["generationConfig"]!["responseSchema"]!;
        esquema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("mensagem", "alteracoes");
        var propriedades = esquema["properties"]!.AsObject().Select(p => p.Key);
        propriedades.Should().Equal("tipo", "mensagem", "ficha", "perguntas", "plano", "alteracoes", "orcamento", "atualizacoes", "abrir", "importarFotos"); // 38, 41 e 42: opcionais
        esquema["properties"]!["tipo"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("resposta", "modo", "perguntas", "plano");
        esquema["properties"]!["plano"]!["properties"]!["etapas"]!["items"]!["properties"]!["duracao"]!["type"]!.GetValue<string>().Should().Be("INTEGER");
    }

    [Fact]
    public async Task Instrucoes_LevamAConducao()
    {
        var conducao = new ConducaoDaConversa(ModoDeTrabalho.Estudo, PreferenciaDeModo.Perguntar, [new ItemDeFicha("Área", "100 m²")], 1);

        await Assistente().EnviarAsync(new PedidoIA("Monte a casa", [], [], null, conducao), CancellationToken.None);

        _servidor.UltimoCorpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>()
            .Should().Contain("MODO DE TRABALHO: estudar e conversar antes").And.Contain("- Área: 100 m²");
    }

    [Fact]
    public async Task Historico_RespostaEstruturadaVaiComoJson_ETextoSimplesComoAntes()
    {
        var perguntas = new RespostaIA("Antes, duas perguntas.")
        {
            Tipo = TipoDeResposta.Perguntas,
            Perguntas = [new PerguntaIA("Qual a estrutura?", ["Concreto armado", "Alvenaria estrutural"])],
        };
        var historico = new MensagemChat[]
        {
            new(AutorMensagem.Usuario, "Monte a casa", DateTimeOffset.UnixEpoch, []),
            new(AutorMensagem.Assistente, perguntas.Texto, DateTimeOffset.UnixEpoch, [], Resposta: perguntas),
            new(AutorMensagem.Usuario, "Concreto armado", DateTimeOffset.UnixEpoch, []),
            new(AutorMensagem.Assistente, "Anotado.", DateTimeOffset.UnixEpoch, [], Resposta: new RespostaIA("Anotado.")),
        };

        await Assistente().EnviarAsync(new PedidoIA("Pode seguir", [], historico), CancellationToken.None);

        var modelo = _servidor.UltimoCorpo["contents"]!.AsArray().Where(c => c!["role"]!.GetValue<string>() == "model")
            .Select(c => c!["parts"]![0]!["text"]!.GetValue<string>()).ToList();
        modelo[0].Should().StartWith("{\"tipo\":\"perguntas\"").And.Contain("Qual a estrutura?").And.Contain("Alvenaria estrutural");
        modelo[1].Should().Be("Anotado.", "uma resposta só de texto vai como texto");
    }

    [Fact]
    public async Task Resposta_DoGemini_ComPerguntas()
    {
        const string json = """
            {"tipo": "perguntas", "mensagem": "Li a planta.", "ficha": [{"campo": "Área construída", "valor": "98 m²"}],
             "perguntas": [{"pergunta": "Qual a data de início?", "opcoes": ["Este mês", "Próximo mês"]}], "alteracoes": []}
            """;
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(json)));

        var resposta = await Assistente().EnviarAsync(new PedidoIA("Estude a planta", [], []), CancellationToken.None);

        (resposta.Tipo, resposta.Perguntas.Single().Opcoes.Count, resposta.Ficha.Single().Valor).Should()
            .Be((TipoDeResposta.Perguntas, 2, "98 m²"));
    }
}
