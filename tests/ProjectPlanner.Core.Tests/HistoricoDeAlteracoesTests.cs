using ProjectPlanner.Core.Edicao;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Core.Tests;

// Bloco 30: o histórico do Desfazer e do Refazer.
public class HistoricoDeAlteracoesTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static (Cronograma Cronograma, HistoricoDeAlteracoes Historico) Novo(int niveis = HistoricoDeAlteracoes.NiveisPadrao)
    {
        var c = new Cronograma("Obra", Seg);
        return (c, new HistoricoDeAlteracoes(c, niveis));
    }

    private static string[] Nomes(Cronograma c) => c.Tarefas.Select(t => $"{t.Nome}:{t.Duracao}").ToArray();

    [Fact]
    public void Novo_NadaParaDesfazerNemRefazer_ENoEstadoSalvo()
    {
        var (c, h) = Novo();

        (h.PodeDesfazer, h.PodeRefazer, h.ProximaADesfazer, h.ProximaARefazer, h.NoEstadoSalvo).Should().Be((false, false, null, null, true));
        ((Action)(() => h.Desfazer(c))).Should().Throw<InvalidOperationException>();
        ((Action)(() => h.Refazer(c))).Should().Throw<InvalidOperationException>();
        h.FotosNoHistorico().Should().BeEmpty();
    }

    [Fact]
    public void DesfazerERefazer_VariosNiveis_NaOrdem()
    {
        var (c, h) = Novo();
        var a = new Tarefa("A", Seg, 3);
        c.AdicionarTarefa(a);
        h.Registrar("Adicionar A", c);
        a.AlterarDuracao(5);
        h.Registrar("Duração de A", c);
        c.AdicionarTarefa(new Tarefa("B", Seg, 2));
        h.Registrar("Adicionar B", c);

        var (um, d1) = h.Desfazer(c);
        Nomes(um).Should().Equal("A:5");
        (d1, h.ProximaADesfazer, h.ProximaARefazer).Should().Be(("Adicionar B", "Duração de A", "Adicionar B"));
        var (dois, d2) = h.Desfazer(um);
        Nomes(dois).Should().Equal("A:3");
        d2.Should().Be("Duração de A");
        var (tres, _) = h.Desfazer(dois);
        tres.Tarefas.Should().BeEmpty();
        h.PodeDesfazer.Should().BeFalse();

        var (refeito, d3) = h.Refazer(tres);
        Nomes(refeito).Should().Equal("A:3");
        d3.Should().Be("Adicionar A");
        Nomes(h.Refazer(refeito).Cronograma).Should().Equal("A:5");
        (h.ProximaARefazer, h.ProximaADesfazer).Should().Be(("Adicionar B", "Duração de A"));
    }

    [Fact]
    public void Restaurado_ECopiaNova_MexerNeleNaoMudaOHistorico()
    {
        var (c, h) = Novo();
        var a = new Tarefa("A", Seg, 3);
        c.AdicionarTarefa(a);
        h.Registrar("Adicionar A", c);
        a.AlterarDuracao(9); // mexer no aberto depois de registrar não muda o retrato
        h.Registrar("Duração de A", c);

        var (antes, _) = h.Desfazer(c);
        antes.Tarefas[0].AlterarDuracao(1);
        h.Refazer(antes);
        var (denovo, _) = h.Desfazer(antes);

        Nomes(denovo).Should().Equal("A:3");
        denovo.Tarefas[0].Should().NotBeSameAs(antes.Tarefas[0]);
        denovo.Tarefas[0].Id.Should().Be(a.Id, "os Ids continuam os mesmos");
    }

    [Fact]
    public void AlteracaoNova_DepoisDeDesfazer_ApagaORefazer()
    {
        var (c, h) = Novo();
        c.AdicionarTarefa(new Tarefa("A", Seg, 3));
        var adicionarA = h.Registrar("Adicionar A", c);
        var (vazio, _) = h.Desfazer(c);
        h.Situacao(adicionarA).Should().Be(SituacaoNoHistorico.Desfeita);

        vazio.AdicionarTarefa(new Tarefa("B", Seg, 2));
        var adicionarB = h.Registrar("Adicionar B", vazio);

        (h.PodeRefazer, h.Situacao(adicionarA), h.Situacao(adicionarB)).Should().Be((false, SituacaoNoHistorico.Esquecida, SituacaoNoHistorico.Feita));
    }

    [Fact]
    public void Niveis_AsMaisAntigasSaem()
    {
        var (c, h) = Novo(niveis: 2);
        var passos = Enumerable.Range(1, 3).Select(i =>
        {
            c.AdicionarTarefa(new Tarefa($"T{i}", Seg, i));
            return h.Registrar($"Adicionar T{i}", c);
        }).ToList();

        h.Situacao(passos[0]).Should().Be(SituacaoNoHistorico.Esquecida);
        var (um, _) = h.Desfazer(c);
        var (dois, _) = h.Desfazer(um);
        Nomes(dois).Should().Equal("T1:1");
        h.PodeDesfazer.Should().BeFalse();
        ((Action)(() => _ = new HistoricoDeAlteracoes(c, 0))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AlteracoesDepoisDe_ContaAsFeitasDepois()
    {
        var (c, h) = Novo();
        var passos = Enumerable.Range(1, 3).Select(i =>
        {
            c.AdicionarTarefa(new Tarefa($"T{i}", Seg, i));
            return h.Registrar($"Adicionar T{i}", c);
        }).ToList();

        passos.Select(h.AlteracoesDepoisDe).Should().Equal(2, 1, 0);
        h.Desfazer(c);
        h.AlteracoesDepoisDe(passos[2]).Should().Be(-1, "está desfeita");
    }

    [Fact]
    public void EstadoSalvo_VoltaAoDesfazerAteEle()
    {
        var (c, h) = Novo();
        c.AdicionarTarefa(new Tarefa("A", Seg, 3));
        h.Registrar("Adicionar A", c);
        h.MarcarSalvo();
        c.AdicionarTarefa(new Tarefa("B", Seg, 3));
        h.Registrar("Adicionar B", c);
        h.NoEstadoSalvo.Should().BeFalse();

        var (salvo, _) = h.Desfazer(c);
        h.NoEstadoSalvo.Should().BeTrue();
        h.Desfazer(salvo);
        h.NoEstadoSalvo.Should().BeFalse("antes do salvo também é diferente do arquivo");
    }

    [Fact]
    public void DadosDoProjeto_NaoVoltam_FicamOsDoAberto()
    {
        var (c, h) = Novo();
        c.AdicionarTarefa(new Tarefa("A", Seg, 3));
        h.Registrar("Adicionar A", c);
        c.DefinirObra(new DadosDaObra("Sr. Silva", "C-1", null));
        c.DefinirConfiguracaoDasFotos(new ConfiguracaoDasFotos(["Bloco A"], 3));
        c.RegistrarRelatorio(new RelatorioGerado(1, Seg, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 28), 4));
        c.Renomear("Obra nova");

        var (antes, _) = h.Desfazer(c);

        antes.Tarefas.Should().BeEmpty();
        (antes.Nome, antes.Obra.Contratante, antes.ConfiguracaoDasFotos.Locais.Single(), antes.Relatorios.Single().Numero)
            .Should().Be(("Obra", "Sr. Silva", "Bloco A", 1), "o nome é do cronograma (volta); obra, locais e relatórios ficam");
        antes.Assistente.Should().BeSameAs(c.Assistente);
        antes.Id.Should().Be(c.Id);
    }

    [Fact]
    public void FotosNoHistorico_DeTodosOsEstados()
    {
        var (c, h) = Novo();
        var a = new Tarefa("A", Seg, 3);
        c.AdicionarTarefa(a);
        var f1 = FotosDeTeste.Nova(a.Id, Seg, arquivo: "1.jpg");
        var f2 = FotosDeTeste.Nova(a.Id, Seg, arquivo: "2.jpg");
        a.AdicionarFoto(f1);
        h.Registrar("Foto 1", c);
        a.RemoverFoto(f1.Id);
        a.AdicionarFoto(f2);
        h.Registrar("Troca", c);
        var (desfeito, _) = h.Desfazer(c);

        h.FotosNoHistorico().Should().BeEquivalentTo([f1.Id, f2.Id]);
        desfeito.Tarefas[0].Fotos.Should().Equal(f1);
        h.Recomecar(desfeito);
        (h.PodeDesfazer, h.PodeRefazer, h.FotosNoHistorico().Single()).Should().Be((false, false, f1.Id));
    }

    [Fact]
    public void Argumentos()
    {
        var (c, h) = Novo();

        ((Action)(() => h.Registrar(" ", c))).Should().Throw<ArgumentException>();
        ((Action)(() => h.Registrar("x", null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new HistoricoDeAlteracoes(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Desempenho_DuasMilTarefas_RegistrarERestaurarRapido()
    {
        var c = new Cronograma("Grande", Seg);
        for (var i = 0; i < 2000; i++)
            c.AdicionarTarefa(new Tarefa($"T{i}", Seg, 1 + i % 5));
        var h = new HistoricoDeAlteracoes(c);
        var relogio = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 20; i++)
        {
            c.Tarefas[i].AlterarDuracao(9);
            h.Registrar("Duração", c);
        }
        for (var i = 0; i < 20; i++)
            c = h.Desfazer(c).Cronograma;

        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "40 retratos de 2.000 tarefas");
        c.Tarefas.Take(20).Should().OnlyContain(t => t.Duracao != 9);
    }
}
