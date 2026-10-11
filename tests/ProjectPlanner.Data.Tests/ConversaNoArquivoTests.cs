using Microsoft.Data.Sqlite;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Assistente;

namespace ProjectPlanner.Data.Tests;

// Bloco 26: a conversa com o assistente e a ficha do projeto ficam no .pplan (tabela AssistenteDoProjeto, em JSON).
public sealed class ConversaNoArquivoTests : TesteComArquivo
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 11, 9, 30, 0, TimeSpan.FromHours(-3));

    private static Cronograma ObraComConversa()
    {
        var obra = new Cronograma("Residência Silva", Segunda);
        obra.AdicionarTarefa(new Tarefa("Fundação", Segunda, 5));
        var conversa = obra.Assistente;
        conversa.Ficha.Definir("Tipo de obra", "Residência térrea");
        conversa.Ficha.Definir("Área construída", "100 m²");
        conversa.DefinirModo(ModoDaConversa.Estudo);
        conversa.Acrescentar(new MensagemDaConversa(AutorNaConversa.Usuario, "Monte o cronograma desta casa", Agora)
        {
            Anexos = [new AnexoNaConversa("/obras/silva/planta.pdf", 120_000)],
        });
        conversa.Acrescentar(new MensagemDaConversa(AutorNaConversa.Assistente, "Algumas perguntas antes:", Agora.AddMinutes(1), TipoDaMensagem.Perguntas)
        {
            Perguntas = [new PerguntaNaConversa("Qual a estrutura?", ["Concreto armado", "Alvenaria estrutural"]), new PerguntaNaConversa("Data de início?", [])],
        });
        conversa.Acrescentar(new MensagemDaConversa(AutorNaConversa.Assistente, "O plano:", Agora.AddMinutes(5), TipoDaMensagem.Plano)
        {
            Plano = new PlanoNaConversa([new EtapaDoPlano("Fundação", 15, "sapatas"), new EtapaDoPlano("Estrutura", 25)], "2027-02-26", ["Sábado sem obra"]),
        });
        conversa.Acrescentar(new MensagemDaConversa(AutorNaConversa.Assistente, "Montei.", Agora.AddMinutes(9),
            ResultadoDasAlteracoes: "Cronograma alterado: 40 tarefas criadas", AlteracoesAplicadas: true));
        conversa.DefinirPlanoAprovado(true);
        return obra;
    }

    [Fact]
    public void SalvarEAbrir_AConversaEAFichaVoltamIguais()
    {
        var obra = ObraComConversa();

        Repositorio.Salvar(obra, Caminho);
        var aberta = Repositorio.Abrir(Caminho);

        var (antes, depois) = (obra.Assistente, aberta.Assistente);
        (depois.Modo, depois.PlanoAprovado, aberta.ConversaDescartadaAoAbrir).Should().Be((ModoDaConversa.Estudo, true, false));
        depois.Ficha.Itens.Should().Equal(antes.Ficha.Itens);
        depois.Mensagens.Should().HaveCount(4);
        depois.Mensagens.Should().BeEquivalentTo(antes.Mensagens, o => o.WithStrictOrdering());
        depois.Mensagens[0].Anexos.Single().Nome.Should().Be("planta.pdf");
        depois.Mensagens[0].DataHora.Should().Be(Agora);
    }

    [Fact]
    public void ConversaVazia_NaoGravaNada()
    {
        Repositorio.Salvar(new Cronograma("Obra", Segunda), Caminho);

        Contar("AssistenteDoProjeto").Should().Be(0);
        Repositorio.Abrir(Caminho).Assistente.Vazia.Should().BeTrue();
    }

    [Fact]
    public void NoArquivo_JsonVersionado_ComOsEnumsPorExtenso()
    {
        Repositorio.Salvar(ObraComConversa(), Caminho);

        using var conexao = new SqliteConnection($"Data Source={Caminho};Pooling=False");
        conexao.Open();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT Versao, Dados FROM AssistenteDoProjeto";
        using var leitor = comando.ExecuteReader();
        leitor.Read().Should().BeTrue();
        leitor.GetInt32(0).Should().Be(1);
        leitor.GetString(1).Should().Contain("\"modo\":\"estudo\"").And.Contain("\"tipo\":\"perguntas\"").And.Contain("\"autor\":\"usuario\"");
    }

    [Theory]
    [InlineData(1, "{ isto não é json")]
    [InlineData(1, """{"modo":"voando","planoAprovado":false}""")]
    [InlineData(1, """{"modo":"direto","planoAprovado":false,"ficha":[{"campo":"","valor":"x"}]}""")]
    [InlineData(7, """{"modo":"direto","planoAprovado":false}""")]
    public void ConversaQueNaoDaParaLer_FicaDeFora_EOProjetoAbre(int versao, string dados)
    {
        Repositorio.Salvar(ObraComConversa(), Caminho);
        Executar("UPDATE AssistenteDoProjeto SET Versao = $p0, Dados = $p1", versao, dados);

        var aberta = Repositorio.Abrir(Caminho);

        aberta.Tarefas.Should().ContainSingle("o cronograma abre normalmente");
        (aberta.ConversaDescartadaAoAbrir, aberta.Assistente.Vazia).Should().Be((true, true));
    }

    [Fact]
    public void ArquivoDa0240_GanhaATabela_EAConversaFicaSalva()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.24.0.pplan"), Caminho);

        var aberto = Repositorio.Abrir(Caminho);
        Tabelas().Should().Contain("AssistenteDoProjeto");
        (aberto.Assistente.Vazia, aberto.ConversaDescartadaAoAbrir, aberto.Tarefas.Count).Should().Be((true, false, 13));

        aberto.Assistente.Ficha.Definir("Prazo desejado", "6 meses");
        var salvo = Path.Combine(Pasta, "salvo.pplan");
        Repositorio.Salvar(aberto, salvo, new ProjectPlanner.Core.Fotos.ImagensParaSalvar(Caminho, new Dictionary<Guid, ProjectPlanner.Core.Fotos.ConteudoDeFoto>()));

        Repositorio.Abrir(salvo).Assistente.Ficha["Prazo desejado"].Should().Be("6 meses");
    }

    [Fact]
    public void ExcluirOProjetoDoArquivo_LevaAConversaJunto()
    {
        Repositorio.Salvar(ObraComConversa(), Caminho);

        Executar("PRAGMA foreign_keys = ON; DELETE FROM Projetos");

        Contar("AssistenteDoProjeto").Should().Be(0);
    }
}
