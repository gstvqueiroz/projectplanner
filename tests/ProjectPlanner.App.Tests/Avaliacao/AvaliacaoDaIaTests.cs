using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests.Avaliacao;

/// <summary>Bloco 68: a obra da avaliação, montada uma vez para os testes da classe (o cartão confere em cópias e não muda a obra).</summary>
public sealed class ObraMontada
{
    public MainWindowViewModel Obra { get; } = ObraDaAvaliacao.Montar();

    public ContextoDoProjeto Retrato => Obra.RetratoParaOAssistente();
}

// Bloco 68: os pedidos da avaliação sem IA: a resposta de referência de cada um passa no avaliador e no cartão de verdade (o
// esperado é coerente e o programa sabe aplicar o pedido), e o avaliador pega as respostas erradas. A avaliação com a IA de
// verdade está em AvaliacaoAoVivoTests.
public sealed class AvaliacaoDaIaTests(ObraMontada obra) : IClassFixture<ObraMontada>
{
    public static TheoryData<string> Ids() => [.. AvaliadorDaIa.Casos().Select(c => c.Id)];

    private static CasoDaAvaliacao Caso(string id) => AvaliadorDaIa.Casos().Single(c => c.Id == id);

    private ResultadoDoCaso Avaliar(string id, string resposta) =>
        AvaliadorDaIa.Avaliar(Caso(id), FormatoDaResposta.Interpretar(resposta), obra.Obra, obra.Retrato);

    [Theory]
    [MemberData(nameof(Ids))]
    public void ReferenciaDoPedido_PassaNaAvaliacao(string id)
    {
        var resultado = Avaliar(id, Caso(id).Referencia.ToJsonString());

        resultado.Falhas.Should().BeEmpty();
        resultado.Avisos.Should().BeEmpty();
    }

    [Fact]
    public void Pedidos_TemIdsUnicos_ECobremTodoTipoDeAtualizacaoEOTelegram()
    {
        var casos = AvaliadorDaIa.Casos();

        casos.Should().HaveCountGreaterThanOrEqualTo(30);
        casos.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        var tipos = casos.SelectMany(c => c.Esperado["atualizacoes"]?.AsArray() ?? []).Select(a => a!["tipo"]!.GetValue<string>()).ToHashSet();
        tipos.Should().BeEquivalentTo(TiposDeAtualizacao.Todos, "um tipo novo da IA precisa de um pedido na avaliação");
        casos.Where(c => c.Canal == CanaisDaConversa.Telegram).Should().HaveCountGreaterThanOrEqualTo(3);
        casos.Should().Contain(c => c.Esperado["semMudancas"] != null).And.Contain(c => c.Esperado["alteracoes"] != null)
            .And.Contain(c => c.Esperado["abrir"] is System.Text.Json.Nodes.JsonObject);
    }

    [Fact]
    public void Avaliador_PegaAOSErrada_EOQueOCartaoRecusa()
    {
        var errada = Avaliar("os-pagar", """{"mensagem": "Pago.", "alteracoes": [], "atualizacoes": [{"tipo": "situacaoOs", "os": 2, "acao": "pagar", "documento": "NF 123"}]}""");

        errada.Passou.Should().BeFalse();
        errada.Falhas.Should().Contain(f => f.StartsWith("faltou a atualização") && f.Contains("tipo=situacaoOs, documento=NF 123, os=2, acao=pagar"))
            .And.Contain("o cartão recusa \"OS-002: pagar as medições abertas (NF 123)\": A OS-002 não tem medição a pagar.");
    }

    [Fact]
    public void Avaliador_DaAvisoDoQueVemAMais_ENaoAceitaMudarNumaPergunta()
    {
        var aMais = Avaliar("percentual-alvenaria", """
            {"mensagem": "Ok.", "alteracoes": [], "atualizacoes": [{"tipo": "percentual", "tarefa": "#2", "percentual": "90%"},
              {"tipo": "percentual", "tarefa": "3", "percentual": 45}]}
            """);
        aMais.Passou.Should().BeTrue("o Nº com # e o % com símbolo valem");
        aMais.Avisos.Should().Equal("atualização a mais: tipo=percentual, tarefa=3, percentual=45");

        var pergunta = Avaliar("pergunta-medido", """{"mensagem": "R$ 22.800.", "alteracoes": [], "atualizacoes": [{"tipo": "linhaDeBase"}]}""");
        pergunta.Falhas.Should().ContainSingle().Which.Should().StartWith("era só para responder");
    }

    [Fact]
    public void Avaliador_NoTelegram_NaoAceitaAbrirTela_ENemTelaErrada()
    {
        Avaliar("telegram-nao-abre-tela", """{"mensagem": "Abri.", "alteracoes": [], "abrir": {"tela": "relatorioDasOrdens"}}""")
            .Falhas.Should().Equal("não era para abrir tela (abriu relatorioDasOrdens)");
        Avaliar("tela-gantt-a3", """{"mensagem": "Abri.", "alteracoes": [], "abrir": {"tela": "imprimirGantt", "aba": "A4"}}""")
            .Falhas.Should().Equal("abriu imprimirGantt (A4), e não {\"tela\":\"imprimirGantt\",\"aba\":\"A3\"}");
        Avaliar("cronograma-duracao", """{"mensagem": "Ok.", "alteracoes": [{"acao": "alterar", "tarefa": "9", "duracao": 12}]}""")
            .Falhas.Should().HaveCount(2).And.Contain(f => f.StartsWith("as alterações não se aplicam"));
    }

    [Fact]
    public void ObraDaAvaliacao_ComoODescrito()
    {
        var c = obra.Obra.Cronograma;

        c.Tarefas.Select(t => t.Nome).Should().Equal("Fundação", "Alvenaria", "Reboco", "Pintura", "Instalações elétricas");
        c.Contrato.Medicoes.Select(m => (m.Numero, m.Travada)).Should().Equal((1, true), (2, false));
        c.OrdensDeServico.Todas.Select(o => (o.Codigo, o.Situacao)).Should().Equal(("OS-001", Core.OrdensDeServico.SituacaoDaOrdem.Aceita),
            ("OS-002", Core.OrdensDeServico.SituacaoDaOrdem.Rascunho));
        c.OrdensDeServico.Todas[0].TotalMedido.Should().Be(2880);
        c.Recursos.Lista.Single().Nome.Should().Be("Pedreiro");
    }
}
