using System.Text.Json.Nodes;

namespace ProjectPlanner.IA.Tests;

// Bloco 41: a resposta do chat com as atualizações da obra (para o cartão) e a tela para abrir; o retrato com a situação da obra.
public sealed class ChatDaObraTests
{
    [Fact]
    public void Atualizacoes_TiposConhecidos_NumerosDosDoisJeitos_TextosCortados()
    {
        var r = FormatoDaResposta.Interpretar($$"""
            {"mensagem": "Proponho estas atualizações.", "alteracoes": [], "atualizacoes": [
              {"tipo": "percentual", "tarefa": "3", "percentual": 60},
              {"tipo": "MEDICAO", "tarefa": 4, "quantidade": "12,5", "medicao": 2},
              {"tipo": "custo", "data": "2026-10-18", "valor": "3200.50", "descricao": "Cimento", "categoria": "material", "documento": "NF 12"},
              {"tipo": "inicio", "tarefa": "5", "data": "2026-10-26"},
              {"tipo": "linhaDeBase", "nome": "{{new string('x', 300)}}"},
              {"tipo": "apagarTudo"},
              {"percentual": 10}
            ]}
            """);

        r.Atualizacoes.Should().HaveCount(5);
        r.Atualizacoes[0].Should().Be(new AtualizacaoDaObra("percentual", "3", Percentual: 60));
        r.Atualizacoes[1].Should().Be(new AtualizacaoDaObra("medicao", "4", Quantidade: 12.5m, Medicao: 2));
        r.Atualizacoes[2].Should().Be(new AtualizacaoDaObra("custo", Data: "2026-10-18", Categoria: "material", Descricao: "Cimento", Valor: 3200.50m, Documento: "NF 12"));
        r.Atualizacoes[3].Should().Be(new AtualizacaoDaObra("inicio", "5", Data: "2026-10-26"));
        r.Atualizacoes[4].Nome!.Length.Should().Be(FormatoDaResposta.TamanhoMaximoDoTextoDaAtualizacao);
        FormatoDaResposta.ComoJson(r).Should().Contain("\"atualizacoesPropostas\":5");
    }

    [Fact]
    public void Abrir_TelaEAbaConhecidas_SoNaRespostaComum()
    {
        var r = FormatoDaResposta.Interpretar("""{"mensagem": "Abri.", "alteracoes": [], "abrir": {"tela": "Indicadores", "aba": "CUSTOS", "medicao": 2}}""");
        r.Abrir.Should().Be(new TelaParaAbrir("indicadores", "custos", 2));
        r.Abrir!.Rotulo.Should().Be("Abrir Indicadores");
        FormatoDaResposta.ComoJson(r).Should().Contain("\"abrir\":{\"tela\":\"indicadores\",\"aba\":\"custos\"}");

        FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [], "abrir": {"tela": "relatorioFotografico", "aba": "qualquer", "de": "2026-09-01", "ate": "2026-09-30"}}""")
            .Abrir.Should().Be(new TelaParaAbrir("relatorioFotografico", null, null, "2026-09-01", "2026-09-30"), "essa tela não tem abas");
        FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [], "abrir": {"tela": "planilhaSecreta"}}""").Abrir.Should().BeNull();
        var perguntas = FormatoDaResposta.Interpretar(
            """{"tipo": "perguntas", "mensagem": "?", "perguntas": [{"pergunta": "Área?"}], "alteracoes": [], "abrir": {"tela": "resumo"}, "atualizacoes": [{"tipo": "percentual", "tarefa": "1", "percentual": 5}]}""");
        (perguntas.Abrir, perguntas.Atualizacoes.Count).Should().Be((null, 0), "perguntar não abre telas nem atualiza a obra");
        TelasDoPrograma.Todas.Select(TelasDoPrograma.Nome).Should().OnlyHaveUniqueItems().And.NotContain(TelasDoPrograma.Todas);
    }

    [Fact]
    public void Instrucoes_EORetrato_ComASituacaoDaObra()
    {
        InstrucoesDoAssistente.Base.Should().Contain(InstrucoesDoAssistente.Obra);
        InstrucoesDoAssistente.Obra.Should().Contain("ABRIR TELAS").And.Contain("ATUALIZAR A OBRA").And.Contain("Para o % concluído use atualizacoes");
        var hoje = new DateOnly(2026, 10, 18);
        var tarefa = new TarefaNoContexto(1, "1", "Alvenaria", 0, TipoNoContexto.Tarefa, 5, hoje, hoje, 0, "", 0, true, null, 0);

        var texto = new ContextoDoProjeto("Casa", hoje, hoje, hoje, [tarefa], ["1 Alvenaria: SINAPI 1"], ["Medição 01: aprovada", "Custo 18/10: Cimento"]).ComoTexto();

        texto.Should().EndWith("Situação da obra:\n- Medição 01: aprovada\n- Custo 18/10: Cimento".ReplaceLineEndings());
        texto.IndexOf("Orçamento:", StringComparison.Ordinal).Should().BeLessThan(texto.IndexOf("Situação da obra:", StringComparison.Ordinal));
        new ContextoDoProjeto("Casa", hoje, hoje, hoje, [tarefa], null, [.. Enumerable.Range(1, 260).Select(i => $"l{i}")]).ComoTexto()
            .Should().EndWith("- (mais 10 linhas)");
    }

    [Fact]
    public void Esquema_ComAtualizacoesEAbrir()
    {
        var esquema = Gemini.AssistenteGemini.EsquemaDaResposta();
        var atualizacao = esquema["properties"]!["atualizacoes"]!["items"]!;
        atualizacao["properties"]!["tipo"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(TiposDeAtualizacao.Todos);
        atualizacao["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("tipo");
        esquema["properties"]!["abrir"]!["properties"]!["tela"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(TelasDoPrograma.Todas);
    }
}
