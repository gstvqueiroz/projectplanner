using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 78: as raias do quadro (o plano "Delete, arrastar e Kanban", K6). Estrutura: 1 Fundação (1.1 Escavação, 1.2 Sapatas),
// 2 Estrutura (2.1 Pilares) e 3 Entrega (marco, no primeiro nível). Profissionais: Pedreiro, Carpinteiro; material: Cimento.
public class RegrasDasRaiasTests
{
    private readonly Cronograma _c = new("Obra", Datas.Segunda);
    private readonly Tarefa _fundacao = new("Fundação", Datas.Segunda, 1);
    private readonly Tarefa _escavacao = new("Escavação", Datas.Segunda, 3);
    private readonly Tarefa _sapatas = new("Sapatas", Datas.Segunda, 4);
    private readonly Tarefa _estrutura = new("Estrutura", Datas.Segunda, 1);
    private readonly Tarefa _pilares = new("Pilares", Datas.Segunda, 5);
    private readonly Tarefa _entrega = new("Entrega", Datas.Segunda, 0);
    private readonly Recurso _pedreiro = new(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, "dia", 200);
    private readonly Recurso _carpinteiro = new(Guid.NewGuid(), "Carpinteiro", TipoDeRecurso.Profissional, "dia", 220);
    private readonly Recurso _cimento = new(Guid.NewGuid(), "Cimento", TipoDeRecurso.Material, "sc", 40);

    public RegrasDasRaiasTests()
    {
        _c.AdicionarTarefa(_fundacao);
        _c.AdicionarTarefa(_escavacao, _fundacao.Id);
        _c.AdicionarTarefa(_sapatas, _fundacao.Id);
        _c.AdicionarTarefa(_estrutura);
        _c.AdicionarTarefa(_pilares, _estrutura.Id);
        _c.AdicionarTarefa(_entrega);
        foreach (var r in new[] { _pedreiro, _carpinteiro, _cimento })
            _c.Recursos.Adicionar(r);
        _c.Recursos.Atribuir(_sapatas, [new AtribuicaoDeRecurso(_carpinteiro.Id, 2), new AtribuicaoDeRecurso(_pedreiro.Id, 1), new AtribuicaoDeRecurso(_cimento.Id, 30)]);
        CalculadoraCronograma.Calcular(_c);
        _c.Quadro.Sincronizar();
    }

    private string Estrutura() => string.Join(" · ", _c.Tarefas.Select(t => $"{new string('-', _c.NivelDe(t.Id))}{t.Nome}"));

    [Fact]
    public void PorResumo_UmaRaiaPorResumoComTarefas_ESemResumoNoFim()
    {
        RegrasDasRaias.Raias(_c, ModoDasRaias.PorResumo).Select(r => r.Nome).Should().Equal("1 Fundação", "2 Estrutura", "Sem resumo");
        RegrasDasRaias.RaiaDe(_c, ModoDasRaias.PorResumo, _sapatas).Should().Be($"resumo:{_fundacao.Id}");
        RegrasDasRaias.RaiaDe(_c, ModoDasRaias.PorResumo, _entrega).Should().Be(RegrasDasRaias.SemResumo);
        RegrasDasRaias.Raias(_c, ModoDasRaias.Nenhuma).Should().BeEmpty();
    }

    [Fact]
    public void LevarParaOutroResumo_VaiParaOFimDoGrupo_EResumoVazioViraTarefaComum()
    {
        var r = RegrasDasRaias.MoverParaRaia(_c, ModoDasRaias.PorResumo, [_sapatas.Id], $"resumo:{_estrutura.Id}");

        r.Should().Be(new ResultadoDaRaia("\"Sapatas\" agora está em \"Estrutura\" (no fim do grupo).", true));
        Estrutura().Should().Be("Fundação · -Escavação · Estrutura · -Pilares · -Sapatas · Entrega");

        r = RegrasDasRaias.MoverParaRaia(_c, ModoDasRaias.PorResumo, [_escavacao.Id], RegrasDasRaias.SemResumo);
        r.Texto.Should().Be("\"Escavação\" saiu dos resumos: está no fim do cronograma, no primeiro nível. \"Fundação\" virou tarefa comum.");
        Estrutura().Should().Be("Fundação · Estrutura · -Pilares · -Sapatas · Entrega · Escavação");
        _fundacao.EhResumo.Should().BeFalse();
    }

    [Fact]
    public void PorResponsavel_OPrimeiroProfissionalDaARaia_ELevarTrocaOResponsavel()
    {
        RegrasDasRaias.Raias(_c, ModoDasRaias.PorResponsavel).Select(r => r.Nome).Should().Equal("Pedreiro", "Carpinteiro", "Sem responsável");
        RegrasDasRaias.RaiaDe(_c, ModoDasRaias.PorResponsavel, _sapatas).Should().Be($"recurso:{_carpinteiro.Id}");
        RegrasDasRaias.RaiaDe(_c, ModoDasRaias.PorResponsavel, _pilares).Should().Be(RegrasDasRaias.SemResponsavel);

        var r = RegrasDasRaias.MoverParaRaia(_c, ModoDasRaias.PorResponsavel, [_sapatas.Id], $"recurso:{_pedreiro.Id}");

        r.Should().Be(new ResultadoDaRaia("\"Sapatas\" agora é de Pedreiro (era Carpinteiro).", false));
        _sapatas.Atribuicoes.Should().Equal(new AtribuicaoDeRecurso(_pedreiro.Id, 1), new AtribuicaoDeRecurso(_cimento.Id, 30));

        RegrasDasRaias.MoverParaRaia(_c, ModoDasRaias.PorResponsavel, [_pilares.Id], $"recurso:{_carpinteiro.Id}");
        _pilares.Atribuicoes.Should().Equal(new AtribuicaoDeRecurso(_carpinteiro.Id, 1));

        RegrasDasRaias.MoverParaRaia(_c, ModoDasRaias.PorResponsavel, [_sapatas.Id], RegrasDasRaias.SemResponsavel).Texto
            .Should().Be("\"Sapatas\" ficou sem responsável (era Pedreiro): os profissionais saíram da tarefa.");
        _sapatas.Atribuicoes.Should().Equal([new AtribuicaoDeRecurso(_cimento.Id, 30)], "o material fica");
    }

    [Fact]
    public void JaNaRaia_NaoMudaNada_ERaiaQueNaoExiste_ERecusada()
    {
        RegrasDasRaias.MoverParaRaia(_c, ModoDasRaias.PorResumo, [_sapatas.Id], $"resumo:{_fundacao.Id}").Texto.Should().BeEmpty();
        FluentActions.Invoking(() => RegrasDasRaias.MoverParaRaia(_c, ModoDasRaias.PorResumo, [_sapatas.Id], $"resumo:{Guid.NewGuid()}"))
            .Should().Throw<DomainValidationException>().WithMessage("A raia não existe mais no quadro.");
        RegrasDasRaias.DefinirModo(_c, ModoDasRaias.PorResponsavel);
        _c.Quadro.Raias.Should().Be(ModoDasRaias.PorResponsavel);
        _c.Copiar().Quadro.Raias.Should().Be(ModoDasRaias.PorResponsavel, "volta no Desfazer, como o resto do quadro");
    }
}
