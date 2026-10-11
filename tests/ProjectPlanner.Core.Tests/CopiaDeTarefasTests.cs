using ProjectPlanner.Core.Edicao;

namespace ProjectPlanner.Core.Tests;

// Bloco 30: copiar, colar e duplicar tarefas. Fundação (resumo) > Escavação (3 d, 40%), Sapatas (5 d, com notas e foto);
// Estrutura (10 d). Vínculos: Escavação → Sapatas (TI+1) e Sapatas → Estrutura.
public class CopiaDeTarefasTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static (Cronograma C, Dictionary<string, Tarefa> T) Obra()
    {
        var c = new Cronograma("Obra", Seg);
        var t = new Dictionary<string, Tarefa>
        {
            ["Fundação"] = new("Fundação", Seg, 1),
            ["Escavação"] = new("Escavação", Seg, 3),
            ["Sapatas"] = new("Sapatas", Seg, 5),
            ["Estrutura"] = new("Estrutura", Seg, 10),
        };
        c.AdicionarTarefa(t["Fundação"]);
        c.AdicionarTarefa(t["Escavação"], t["Fundação"].Id);
        c.AdicionarTarefa(t["Sapatas"], t["Fundação"].Id);
        c.AdicionarTarefa(t["Estrutura"]);
        t["Escavação"].AtualizarPercentualConcluido(40);
        t["Sapatas"].AlterarNotas("Conferir armadura");
        t["Sapatas"].AdicionarFoto(FotosDeTeste.Nova(t["Sapatas"].Id, Seg));
        c.AdicionarPredecessora(new Predecessora(t["Escavação"].Id, t["Sapatas"].Id, TipoDependencia.FS, 1));
        c.AdicionarPredecessora(new Predecessora(t["Sapatas"].Id, t["Estrutura"].Id));
        return (c, t);
    }

    private static string[] Linhas(Cronograma c)
    {
        var edt = c.NumeracaoEdt();
        return c.Tarefas.Select(t => $"{edt[t.Id]} {t.Nome}").ToArray();
    }

    [Fact]
    public void Copiar_ResumoComSubtarefas_EOsVinculosDeDentro()
    {
        var (c, t) = Obra();

        var trecho = CopiaDeTarefas.Copiar(c, [t["Fundação"].Id]);

        trecho.Tarefas.Select(x => x.Nome).Should().Equal("Fundação", "Escavação", "Sapatas");
        trecho.Tarefas.Select(x => x.PaiOriginal).Should().Equal(null, t["Fundação"].Id, t["Fundação"].Id);
        trecho.Vinculos.Should().ContainSingle().Which.TarefaDestinoId.Should().Be(t["Sapatas"].Id, "Sapatas → Estrutura sai do trecho");
        (trecho.Raizes, trecho.Descricao).Should().Be((1, "\"Fundação\" (com 2 subtarefas)"));
    }

    [Fact]
    public void Copiar_Descricoes_EPedidaDentroDeOutra()
    {
        var (c, t) = Obra();

        CopiaDeTarefas.Copiar(c, [t["Estrutura"].Id]).Descricao.Should().Be("\"Estrutura\"");
        var dois = CopiaDeTarefas.Copiar(c, [t["Escavação"].Id, t["Estrutura"].Id]);
        (dois.Raizes, dois.Descricao).Should().Be((2, "2 tarefas"));
        CopiaDeTarefas.Copiar(c, [t["Sapatas"].Id, t["Fundação"].Id]).Tarefas.Should().HaveCount(3, "Sapatas já vem com a Fundação");
    }

    [Fact]
    public void Copiar_Recusas()
    {
        var (c, _) = Obra();

        ((Action)(() => CopiaDeTarefas.Copiar(c, []))).Should().Throw<DomainValidationException>().WithMessage("Selecione uma tarefa para copiar.");
        ((Action)(() => CopiaDeTarefas.Copiar(c, [Guid.NewGuid()]))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Colar_ResumoNoFim_TarefasNovasComTudoMenosFotos()
    {
        var (c, t) = Obra();
        var trecho = CopiaDeTarefas.Copiar(c, [t["Fundação"].Id]);

        var novas = CopiaDeTarefas.Colar(c, trecho, null, 2);

        Linhas(c).Should().Equal("1 Fundação", "1.1 Escavação", "1.2 Sapatas", "2 Estrutura", "3 Fundação", "3.1 Escavação", "3.2 Sapatas");
        novas.Select(n => n.Nome).Should().Equal("Fundação", "Escavação", "Sapatas");
        novas.Select(n => n.Id).Should().NotIntersectWith(t.Values.Select(x => x.Id));
        (novas[0].EhResumo, novas[1].Duracao, novas[1].PercentualConcluido, novas[2].Notas, novas[2].Fotos.Count)
            .Should().Be((true, 3, 40, "Conferir armadura", 0));
        var vinculo = c.PredecessorasDe(novas[2].Id).Single();
        (vinculo.TarefaOrigemId, vinculo.Lag).Should().Be((novas[1].Id, 1));
        c.PredecessorasDe(t["Sapatas"].Id).Should().ContainSingle("o original não ganhou vínculos");
    }

    [Fact]
    public void Colar_DentroDeUmResumo_NaPosicao_EDuasVezes()
    {
        var (c, t) = Obra();
        var trecho = CopiaDeTarefas.Copiar(c, [t["Estrutura"].Id]);

        CopiaDeTarefas.Colar(c, trecho, t["Fundação"].Id, 1);
        CopiaDeTarefas.Colar(c, trecho, t["Fundação"].Id, 0);

        Linhas(c).Should().Equal("1 Fundação", "1.1 Estrutura", "1.2 Escavação", "1.3 Estrutura", "1.4 Sapatas", "2 Estrutura");
    }

    [Fact]
    public void Colar_EmOutroProjeto()
    {
        var (c, t) = Obra();
        var trecho = CopiaDeTarefas.Copiar(c, [t["Fundação"].Id, t["Estrutura"].Id]);
        var outro = new Cronograma("Outra obra", Seg.AddDays(7));

        var novas = CopiaDeTarefas.Colar(outro, trecho, null, 0);

        Linhas(outro).Should().Equal("1 Fundação", "1.1 Escavação", "1.2 Sapatas", "2 Estrutura");
        outro.Predecessoras.Should().HaveCount(2);
        novas.Should().HaveCount(4);
        CalculadoraCronograma.Calcular(outro).DataTermino.Should().BeAfter(Seg);
    }

    [Fact]
    public void Colar_PosicaoInvalida_OuPaiQueNaoPodeTerSubtarefas_NaoMudaNada()
    {
        var (c, t) = Obra();
        var trecho = CopiaDeTarefas.Copiar(c, [t["Fundação"].Id]);
        var antes = Linhas(c);

        ((Action)(() => CopiaDeTarefas.Colar(c, trecho, null, 9))).Should().Throw<DomainValidationException>();
        // Sapatas tem vínculos: não pode virar resumo.
        ((Action)(() => CopiaDeTarefas.Colar(c, trecho, t["Sapatas"].Id, 0))).Should().Throw<DomainValidationException>();

        Linhas(c).Should().Equal(antes);
        c.Predecessoras.Should().HaveCount(2);
        CopiaDeTarefas.Colar(c, new TrechoCopiado([], []), null, 0).Should().BeEmpty();
    }

    [Fact]
    public void Copia_NaoMudaQuandoOOriginalMuda()
    {
        var (c, t) = Obra();
        var trecho = CopiaDeTarefas.Copiar(c, [t["Estrutura"].Id]);
        t["Estrutura"].Renomear("Outra coisa");

        CopiaDeTarefas.Colar(c, trecho, null, 2)[0].Nome.Should().Be("Estrutura");
    }

    [Fact]
    public void Marco_ECopiadoComoMarco()
    {
        var c = new Cronograma("Obra", Seg);
        var marco = new Tarefa("Entrega", Seg, 0);
        c.AdicionarTarefa(marco);

        var novo = CopiaDeTarefas.Colar(c, CopiaDeTarefas.Copiar(c, [marco.Id]), null, 1).Single();

        (novo.EhMarco, novo.Nome).Should().Be((true, "Entrega"));
    }
}
