namespace ProjectPlanner.IA.Tests;

// Bloco 24: a resposta do assistente em JSON ({mensagem, alteracoes}), igual para qualquer provedor.
public class FormatoDaRespostaTests
{
    [Fact]
    public void Interpretar_MensagemEAlteracoes_ComTodosOsCampos()
    {
        var resposta = FormatoDaResposta.Interpretar("""
            {"mensagem": " Pronto. ", "alteracoes": [
              {"acao": "CRIAR", "ref": "n1", "nome": "Laje", "duracao": 5, "pai": "2", "percentual": 10, "notas": "fck 30"},
              {"acao": "alterar", "tarefa": 4, "duracao": "3"},
              {"acao": "vincular", "de": "n1", "para": "1.2", "tipo": "TT", "defasagem": -2},
              {"acao": "projeto", "nome": "Casa 100 m²", "inicio": "2026-10-05"},
              {"acao": "limpar"}]}
            """);

        resposta.Texto.Should().Be("Pronto.");
        resposta.Alteracoes.Should().Equal(
            new AlteracaoDoCronograma("criar", Ref: "n1", Nome: "Laje", Duracao: 5, Pai: "2", Percentual: 10, Notas: "fck 30"),
            new AlteracaoDoCronograma("alterar", Tarefa: "4", Duracao: 3),
            new AlteracaoDoCronograma("vincular", De: "n1", Para: "1.2", Tipo: "TT", Defasagem: -2),
            new AlteracaoDoCronograma("projeto", Nome: "Casa 100 m²", Inicio: "2026-10-05"),
            new AlteracaoDoCronograma("limpar"));
    }

    [Fact]
    public void Interpretar_SemAlteracoes_SoConversa()
    {
        var resposta = FormatoDaResposta.Interpretar("""{"mensagem": "O caminho crítico é 1.1 → 1.2.", "alteracoes": []}""");

        (resposta.Texto, resposta.Alteracoes.Count).Should().Be(("O caminho crítico é 1.1 → 1.2.", 0));
    }

    [Fact]
    public void Interpretar_JsonEmCercaDeCodigo()
    {
        var resposta = FormatoDaResposta.Interpretar("```json\n{\"mensagem\": \"Ok\", \"alteracoes\": [{\"acao\": \"remover\", \"tarefa\": \"3\"}]}\n```");

        resposta.Alteracoes.Should().Equal(new AlteracaoDoCronograma("remover", Tarefa: "3"));
    }

    [Theory]
    [InlineData("Texto simples, sem JSON.", "Texto simples, sem JSON.")]
    [InlineData("[1, 2, 3]", "[1, 2, 3]")]
    [InlineData("\"só um texto\"", "\"só um texto\"")]
    public void Interpretar_QueNaoEObjeto_ViraSoMensagem(string texto, string mensagem)
    {
        var resposta = FormatoDaResposta.Interpretar(texto);

        (resposta.Texto, resposta.Alteracoes.Count).Should().Be((mensagem, 0));
    }

    [Fact]
    public void Interpretar_ObjetoComDefeito_Recusado_ECortado_ExplicaOCorte()
    {
        var defeito = () => FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [}""");
        var cortado = () => FormatoDaResposta.Interpretar("""{"mensagem": "x", "alteracoes": [{"acao": "cri""", cortada: true);

        defeito.Should().Throw<ErroDoAssistenteException>().WithMessage("O assistente mandou uma resposta com defeito*");
        cortado.Should().Throw<ErroDoAssistenteException>().WithMessage("*longa demais e veio cortada*uma etapa da obra por vez*");
    }

    [Fact]
    public void Interpretar_TextoCortado_AvisaNoFim()
    {
        FormatoDaResposta.Interpretar("Começo", cortada: true).Texto.Should().Be("Começo\n\n(Resposta cortada: ficou longa demais.)");
        FormatoDaResposta.Interpretar("""{"mensagem": "Parte", "alteracoes": []}""", cortada: true).Texto
            .Should().Be("Parte\n\n(Resposta cortada: ficou longa demais.)");
    }

    [Fact]
    public void Interpretar_ItensEstranhos_SaoIgnoradosOuViramAcaoVazia()
    {
        var resposta = FormatoDaResposta.Interpretar("""
            {"mensagem": "x", "alteracoes": [1, "texto", {"nome": "sem ação"}, {"acao": "teletransportar", "tarefa": true}]}
            """);

        resposta.Alteracoes.Should().Equal(
            new AlteracaoDoCronograma("", Nome: "sem ação"),
            new AlteracaoDoCronograma("teletransportar"));
    }

    [Fact]
    public void Alteracao_Descricao()
    {
        new AlteracaoDoCronograma("criar", Ref: "n1", Nome: "Laje").ToString().Should().Be("criar \"Laje\" (n1)");
        new AlteracaoDoCronograma("vincular", De: "2", Para: "n1", Tipo: "II", Defasagem: 2).ToString().Should().Be("vincular 2 → n1 (II+2)");
        new AlteracaoDoCronograma("vincular", De: "2", Para: "n1").ToString().Should().Be("vincular 2 → n1");
        new AlteracaoDoCronograma("desvincular", De: "2", Para: "3").ToString().Should().Be("desvincular 2 → 3");
        new AlteracaoDoCronograma("remover", Tarefa: "4").ToString().Should().Be("remover 4");
        new AlteracaoDoCronograma("limpar").ToString().Should().Be("limpar");
    }
}
