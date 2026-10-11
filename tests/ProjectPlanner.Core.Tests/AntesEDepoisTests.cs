using ProjectPlanner.Core.Fotos;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Core.Tests;

// Bloco 29 (RF3): o antes e depois, na tela e no relatório; e o texto do relatório.
public class AntesEDepoisTests
{
    private static readonly DateTime Seg = new(2026, 9, 14);

    // Fundação (5 d) e Alvenaria (10 d, depois). Fotos: Bloco A em 15/09 (Fundação, 20%), 18/09 (Fundação, 60%) e 25/09
    // (Alvenaria, 10%); "bloco a" em 25/09 de novo (mesmo local, outra grafia); Fachada só em 22/09.
    private static (Cronograma C, Tarefa Fundacao, Tarefa Alvenaria, Dictionary<string, FotoTarefa> F) Obra()
    {
        var c = new Cronograma("Residência", Seg);
        var fundacao = new Tarefa("Fundação", Seg, 5);
        var alvenaria = new Tarefa("Alvenaria", Seg, 10);
        c.AdicionarTarefa(fundacao);
        c.AdicionarTarefa(alvenaria);
        c.AdicionarPredecessora(new Predecessora(fundacao.Id, alvenaria.Id));
        CalculadoraCronograma.Calcular(c);
        var f = new Dictionary<string, FotoTarefa>();
        FotoTarefa Nova(string nome, Tarefa t, DateTime data, string? local, int? percentual)
        {
            var foto = new FotoTarefa(Guid.NewGuid(), t.Id, new DadosDaImagem(nome, 1600, 1200, 1000, FotosDeTeste.Hash(nome)),
                new MetadadosFoto(data, local: local, percentualConcluidoNaData: percentual), new DateTime(2026, 10, 1));
            t.AdicionarFoto(foto);
            return f[nome] = foto;
        }
        Nova("a1", fundacao, new DateTime(2026, 9, 15, 9, 0, 0), "Bloco A", 20);
        Nova("a2", fundacao, new DateTime(2026, 9, 18, 9, 0, 0), "Bloco A", 60);
        Nova("a3", alvenaria, new DateTime(2026, 9, 25, 9, 0, 0), "Bloco A", 10);
        Nova("a4", alvenaria, new DateTime(2026, 9, 25, 15, 0, 0), "bloco a", 15);
        Nova("fachada", alvenaria, new DateTime(2026, 9, 22, 9, 0, 0), "Fachada", 5);
        return (c, fundacao, alvenaria, f);
    }

    [Fact]
    public void Assuntos_LocaisEmDoisDiasOuMais_DepoisAsTarefas()
    {
        var (c, _, _, _) = Obra();

        AntesEDepois.Assuntos(c).Select(a => a.Nome).Should().Equal("Bloco A", "1 Fundação", "2 Alvenaria");
        AntesEDepois.Assuntos(new Cronograma("Vazio", Seg)).Should().BeEmpty();
    }

    [Fact]
    public void FotosDe_UmLocal_SemDiferenciarGrafia_EAteUmaData()
    {
        var (c, _, _, f) = Obra();
        var local = AssuntoDaComparacao.DoLocal("BLOCO A");

        AntesEDepois.FotosDe(c, local).Should().Equal(f["a1"], f["a2"], f["a3"], f["a4"]);
        AntesEDepois.FotosDe(c, local, ate: new DateOnly(2026, 9, 20)).Should().Equal(f["a1"], f["a2"]);
    }

    [Fact]
    public void Comparar_PrimeiraEUltima_EvolucaoDaMesmaTarefa()
    {
        var (c, fundacao, _, f) = Obra();
        var tarefa = AssuntoDaComparacao.DaTarefa("1 Fundação", fundacao.Id);

        var comparacao = AntesEDepois.Comparar(tarefa, AntesEDepois.FotosDe(c, tarefa))!;

        (comparacao.Antes, comparacao.Depois, comparacao.Dias).Should().Be((f["a1"], f["a2"], 3));
        comparacao.Evolucao.Should().Be("De 20% para 60% concluído em 3 dias");
        comparacao.Serie.Should().Equal(f["a1"], f["a2"]);
        ComparacaoAntesEDepois.Rotulo("Antes", f["a1"]).Should().Be("Antes · 15/09/2026 09:00");
    }

    [Fact]
    public void Comparar_TarefasDiferentes_SoOsDias_EEscolhidasInvertidasSaoTrocadas()
    {
        var (c, _, _, f) = Obra();
        var local = AssuntoDaComparacao.DoLocal("Bloco A");
        var fotos = AntesEDepois.FotosDe(c, local);

        var comparacao = AntesEDepois.Comparar(local, fotos, antes: f["a4"], depois: f["a1"])!;

        (comparacao.Antes, comparacao.Depois, comparacao.Evolucao).Should().Be((f["a1"], f["a4"], "10 dias entre as fotos"));
        comparacao.Serie.Should().Equal(f["a1"], f["a2"], f["a3"]); // uma por dia: a3 é a primeira de 25/09
    }

    [Fact]
    public void Comparar_UmDiaSo_Nula()
    {
        var (_, _, _, f) = Obra();
        var fachada = AssuntoDaComparacao.DoLocal("Fachada");

        AntesEDepois.Comparar(fachada, [f["fachada"]]).Should().BeNull();
        AntesEDepois.Comparar(fachada, [f["a3"], f["a4"]]).Should().BeNull("as duas são de 25/09");
        AntesEDepois.Comparar(fachada, []).Should().BeNull();
    }

    [Fact]
    public void Serie_NoMaximoOito_ComAPrimeiraEAUltima()
    {
        var c = new Cronograma("Obra", Seg);
        var t = new Tarefa("Fundação", Seg, 30);
        c.AdicionarTarefa(t);
        var fotos = Enumerable.Range(0, 20).Select(i => FotosDeTeste.Nova(t.Id, Seg.AddDays(i), local: "Bloco A", arquivo: $"{i}.jpg")).ToList();

        var comparacao = AntesEDepois.Comparar(AssuntoDaComparacao.DoLocal("Bloco A"), fotos)!;

        comparacao.Serie.Should().HaveCount(AntesEDepois.FotosNaSerie);
        (comparacao.Serie[0], comparacao.Serie[^1]).Should().Be((fotos[0], fotos[^1]));
        comparacao.Serie.Should().BeInAscendingOrder(f => f.DataFoto);
        comparacao.Evolucao.Should().Be("19 dias entre as fotos", "as fotos de teste não têm %");
    }

    // ---- No relatório ----

    private static CabecalhoDoRelatorio Cabecalho(Cronograma c) => new(c.Nome, c.Obra, null, null, null);

    [Fact]
    public void Relatorio_AntesEDepoisPorLocal_AntesPodeSerDeAntesDoPeriodo()
    {
        var (c, _, _, f) = Obra();
        var opcoes = new OpcoesDoRelatorio(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 30)) { ComAntesEDepois = true };

        var r = MontadorDoRelatorio.Montar(c, opcoes, Cabecalho(c), new DateTime(2026, 10, 1));

        var pagina = r.AntesEDepois.Should().ContainSingle("a Fachada tem foto num dia só").Subject;
        (pagina.Local, pagina.Antes, pagina.Depois).Should().Be(("Bloco A", f["a1"], f["a4"]));
        (pagina.LegendaAntes, pagina.LegendaDepois, pagina.Evolucao).Should().Be(
            ("Antes · 15/09/2026 09:00 · 1 Fundação · 20% concluído", "Depois · 25/09/2026 15:00 · 2 Alvenaria · 15% concluído", "10 dias entre as fotos"));
        r.TotalDePaginas.Should().Be(1 + r.Paginas.Count + 1 + 1);
    }

    [Fact]
    public void Relatorio_SemAOpcao_LocalSemFotoNoPeriodo_OuForaDoRelatorio_NaoEntra()
    {
        var (c, fundacao, alvenaria, f) = Obra();
        var periodo = new OpcoesDoRelatorio(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 30));

        MontadorDoRelatorio.Montar(c, periodo, Cabecalho(c), new DateTime(2026, 10, 1)).AntesEDepois.Should().BeEmpty();
        MontadorDoRelatorio.Montar(c, new OpcoesDoRelatorio(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)) { ComAntesEDepois = true },
            Cabecalho(c), new DateTime(2026, 10, 9)).AntesEDepois.Should().BeEmpty("nenhuma foto no período");

        alvenaria.SubstituirFoto(f["a3"].ComCuradoria(false, false));
        alvenaria.SubstituirFoto(f["a4"].ComCuradoria(false, false));
        MontadorDoRelatorio.Montar(c, periodo with { ComAntesEDepois = true }, Cabecalho(c), new DateTime(2026, 10, 1))
            .AntesEDepois.Should().BeEmpty("as do período foram tiradas do relatório");
        _ = fundacao;
    }

    // ---- Texto ----

    [Fact]
    public void Texto_ParagrafosEPontos_EVazio()
    {
        var texto = new TextoDoRelatorio("  Primeiro parágrafo.\r\n\r\nSegundo.\n", ["  ", "Atenção à laje", ""]);

        (texto.Paragrafos, texto.PontosDeAtencao, texto.Vazio).Should().BeEquivalentTo((new[] { "Primeiro parágrafo.", "Segundo." }, new[] { "Atenção à laje" }, false));
        new TextoDoRelatorio("  ", []).Vazio.Should().BeTrue();
        TextoDoRelatorio.LerPontos("- Laje atrasada\n\n• Chuva\n* Concreto").Should().Equal("Laje atrasada", "Chuva", "Concreto");
        TextoDoRelatorio.LerPontos(null).Should().BeEmpty();
    }

    [Fact]
    public void Texto_LongoDemais_Recusado()
    {
        var criar = () => new TextoDoRelatorio(new string('x', TextoDoRelatorio.TamanhoMaximo), ["y"]);

        criar.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Relatorio_ComTexto_UmaPaginaAMais_SemTextoNenhuma()
    {
        var (c, _, _, _) = Obra();
        var opcoes = new OpcoesDoRelatorio(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 30));

        var sem = MontadorDoRelatorio.Montar(c, opcoes with { Texto = new TextoDoRelatorio(" ") }, Cabecalho(c), new DateTime(2026, 10, 1));
        var com = MontadorDoRelatorio.Montar(c, opcoes with { Texto = new TextoDoRelatorio("Andamento.") }, Cabecalho(c), new DateTime(2026, 10, 1));

        (sem.Texto, com.Texto!.Andamento).Should().Be((null, "Andamento."));
        com.TotalDePaginas.Should().Be(sem.TotalDePaginas + 1);
    }
}
