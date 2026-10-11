using ProjectPlanner.Core;
using ProjectPlanner.Core.Assistente;

namespace ProjectPlanner.Core.Tests;

// Bloco 26: a ficha do projeto e a conversa com o assistente, guardadas com o projeto.
public class ConversaDoProjetoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Ficha_DefinirTrocarEApagar_SemRepetirCampo()
    {
        var ficha = new FichaDoProjeto();

        ficha.Definir("Área construída", "100 m²").Should().BeTrue();
        ficha.Definir("  área   CONSTRUÍDA ", "120 m²").Should().BeTrue("o mesmo campo, escrito de outro jeito");
        ficha.Definir("Pavimentos", " 1 ").Should().BeTrue();
        ficha.Definir("Pavimentos", "1").Should().BeFalse("nada mudou");

        ficha.Itens.Should().Equal(new ItemDaFicha("área CONSTRUÍDA", "120 m²"), new ItemDaFicha("Pavimentos", "1"));
        ficha["Área construída"].Should().Be("120 m²");

        ficha.Definir("Pavimentos", "  ").Should().BeTrue("valor vazio apaga");
        ficha.Remover("Inexistente").Should().BeFalse();
        ficha.Itens.Should().ContainSingle();
    }

    [Theory]
    [InlineData("", "x", "precisa de um nome")]
    [InlineData("   ", "x", "precisa de um nome")]
    public void Ficha_CampoInvalido_Recusado(string campo, string valor, string motivo)
    {
        var definir = () => new FichaDoProjeto().Definir(campo, valor);

        definir.Should().Throw<DomainValidationException>().WithMessage($"*{motivo}*");
    }

    [Fact]
    public void Ficha_LimitesDeTamanhoEDeQuantidade()
    {
        var ficha = new FichaDoProjeto();

        ficha.Invoking(f => f.Definir(new string('c', FichaDoProjeto.TamanhoMaximoDoCampo + 1), "x"))
            .Should().Throw<DomainValidationException>();
        ficha.Invoking(f => f.Definir("Campo", new string('v', FichaDoProjeto.TamanhoMaximoDoValor + 1)))
            .Should().Throw<DomainValidationException>();
        ficha.Definir(new string('c', FichaDoProjeto.TamanhoMaximoDoCampo), new string('v', FichaDoProjeto.TamanhoMaximoDoValor))
            .Should().BeTrue("no limite ainda cabe");
        for (var i = 1; i < FichaDoProjeto.QuantidadeMaxima; i++)
            ficha.Definir($"Campo {i}", "x");
        ficha.Invoking(f => f.Definir("Mais um", "x")).Should().Throw<DomainValidationException>().WithMessage("*já tem 60 campos*");
        ficha.Definir("Campo 1", "trocar ainda vale").Should().BeTrue();
    }

    [Fact]
    public void Ficha_Mesclar_IgnoraOsItensRuins()
    {
        var ficha = new FichaDoProjeto();
        ficha.Definir("Início", "05/10/2026");

        var mudancas = ficha.Mesclar([new("Tipo de obra", "Residência"), new("", "sem nome"), new("Início", "05/10/2026"),
            new("Prazo", new string('x', 5000))]);

        mudancas.Should().Be(1);
        ficha.Itens.Select(i => i.Campo).Should().Equal("Início", "Tipo de obra");
    }

    [Fact]
    public void Conversa_ModoPlanoEMensagens()
    {
        var conversa = new ConversaDoProjeto();
        conversa.Vazia.Should().BeTrue();

        conversa.DefinirModo(ModoDaConversa.Estudo).Should().BeTrue();
        conversa.Acrescentar(new MensagemDaConversa(AutorNaConversa.Assistente, "Algumas perguntas", Agora, TipoDaMensagem.Perguntas)
        {
            Perguntas = [new PerguntaNaConversa("Estrutura?", ["Concreto armado", "Alvenaria estrutural"])],
        });
        conversa.DefinirPlanoAprovado(true).Should().BeTrue();

        (conversa.Vazia, conversa.RodadasDePerguntas, conversa.PlanoAprovado).Should().Be((false, 1, true));

        conversa.DefinirModo(ModoDaConversa.Direto);
        conversa.PlanoAprovado.Should().BeFalse("o plano aprovado só vale no estudo");
    }

    [Fact]
    public void Conversa_AprovarForaDoEstudo_Recusado_EModoDesconhecido_Recusado()
    {
        var conversa = new ConversaDoProjeto();

        conversa.Invoking(c => c.DefinirPlanoAprovado(true)).Should().Throw<InvalidOperationException>();
        conversa.Invoking(c => c.DefinirModo((ModoDaConversa)9)).Should().Throw<ArgumentOutOfRangeException>();
        conversa.DefinirPlanoAprovado(false).Should().BeFalse();
    }

    [Fact]
    public void Conversa_LimparMensagens_RecomecaOModo_EMantemAFicha()
    {
        var conversa = new ConversaDoProjeto();
        conversa.Ficha.Definir("Área", "100 m²");
        conversa.DefinirModo(ModoDaConversa.Estudo);
        conversa.Acrescentar(new MensagemDaConversa(AutorNaConversa.Usuario, "Oi", Agora));

        conversa.LimparMensagens();

        (conversa.Mensagens.Count, conversa.Modo, conversa.Ficha.Itens.Count, conversa.Vazia).Should().Be((0, ModoDaConversa.NaoEscolhido, 1, false));
    }

    [Fact]
    public void Conversa_Substituir_SoNumaPosicaoQueExiste()
    {
        var conversa = new ConversaDoProjeto();
        conversa.Acrescentar(new MensagemDaConversa(AutorNaConversa.Assistente, "Montei", Agora, ResultadoDasAlteracoes: "Cronograma alterado: 3 tarefas", AlteracoesAplicadas: true));

        conversa.Substituir(0, conversa.Mensagens[0] with { ResultadoDasAlteracoes = "Desfeito: 3 tarefas" });

        conversa.Mensagens[0].ResultadoDasAlteracoes.Should().Be("Desfeito: 3 tarefas");
        conversa.Invoking(c => c.Substituir(1, conversa.Mensagens[0])).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CopiaDoCronograma_UsaAMesmaConversa()
    {
        var cronograma = new Cronograma("Obra", new DateTime(2026, 10, 5));
        cronograma.Assistente.Ficha.Definir("Área", "100 m²");

        var copia = cronograma.Copiar();
        copia.Assistente.Acrescentar(new MensagemDaConversa(AutorNaConversa.Usuario, "Depois da cópia", Agora));

        copia.Assistente.Should().BeSameAs(cronograma.Assistente, "desfazer uma alteração do assistente não desfaz a conversa");
        cronograma.Assistente.Mensagens.Should().ContainSingle();
    }

    [Fact]
    public void AnexoNaConversa_ONomeDoArquivo()
    {
        new AnexoNaConversa("/obras/silva/planta baixa.pdf", 1200).Nome.Should().Be("planta baixa.pdf");
    }
}
