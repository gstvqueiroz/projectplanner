using ProjectPlanner.Core;
using ProjectPlanner.Core.Edicao;

namespace ProjectPlanner.Core.Tests;

// Bloco 72: as regras para excluir (o plano "Delete, arrastar e Kanban", E2 a E6), com a estrutura do plano:
// Fundação (Escavação, Sapatas), Estrutura (Pilares) e o marco Entrega.
public class RegrasDeExclusaoTests
{
    private readonly Cronograma _c = new("Obra", Datas.Segunda);
    private readonly Tarefa _fundacao = new("Fundação", Datas.Segunda, 1);
    private readonly Tarefa _escavacao = new("Escavação", Datas.Segunda, 3);
    private readonly Tarefa _sapatas = new("Sapatas", Datas.Segunda, 4);
    private readonly Tarefa _estrutura = new("Estrutura", Datas.Segunda, 1);
    private readonly Tarefa _pilares = new("Pilares", Datas.Segunda, 5);
    private readonly Tarefa _entrega = new("Entrega", Datas.Segunda, 0);

    public RegrasDeExclusaoTests()
    {
        _c.AdicionarTarefa(_fundacao);
        _c.AdicionarTarefa(_escavacao, _fundacao.Id);
        _c.AdicionarTarefa(_sapatas, _fundacao.Id);
        _c.AdicionarTarefa(_estrutura);
        _c.AdicionarTarefa(_pilares, _estrutura.Id);
        _c.AdicionarTarefa(_entrega);
        Ligar(_escavacao, _sapatas);
        Ligar(_sapatas, _pilares, lag: 2);
        Ligar(_pilares, _entrega);
        CalculadoraCronograma.Calcular(_c);
    }

    private void Ligar(Tarefa de, Tarefa para, TipoDependencia tipo = TipoDependencia.FS, int lag = 0) =>
        _c.AdicionarPredecessora(new Predecessora(de.Id, para.Id, tipo, lag));

    private IEnumerable<string> Nomes() => _c.Tarefas.Select(t => $"{new string(' ', 2 * _c.NivelDe(t.Id))}{t.Nome}");

    private IEnumerable<string> Vinculos() => _c.Predecessoras.Select(p =>
        $"{_c.Tarefas.Single(t => t.Id == p.TarefaOrigemId).Nome}→{_c.Tarefas.Single(t => t.Id == p.TarefaDestinoId).Nome}{(p.Lag == 0 ? "" : $"+{p.Lag}")}");

    [Fact]
    public void TarefaComum_NaoPergunta_ESequenciaSimplesERefeita()
    {
        var plano = RegrasDeExclusao.Planejar(_c, [_sapatas.Id]);
        plano.PedeConfirmacao.Should().BeFalse("tarefa comum sem progresso some na hora (E2)");

        var r = RegrasDeExclusao.Aplicar(_c, plano, ModoDosResumos.GrupoInteiro);

        Nomes().Should().Equal("Fundação", "  Escavação", "Estrutura", "  Pilares", "Entrega");
        Vinculos().Should().BeEquivalentTo(["Escavação→Pilares+2", "Pilares→Entrega"], "religa as pontas com a defasagem de Sapatas → Pilares (E5)");
        r.Texto.Should().Be("Tarefa \"Sapatas\" excluída. Vínculo refeito: Escavação → Pilares.");
    }

    [Fact]
    public void ForaDeSequenciaSimples_OsVinculosSaem_EAsSucessorasSaoAvisadas()
    {
        Ligar(_escavacao, _pilares, TipoDependencia.SS);
        var r = RegrasDeExclusao.Aplicar(_c, RegrasDeExclusao.Planejar(_c, [_escavacao.Id]), ModoDosResumos.GrupoInteiro);

        Vinculos().Should().BeEquivalentTo(["Sapatas→Pilares+2", "Pilares→Entrega"]);
        r.VinculosRefeitos.Should().BeEmpty("Escavação não tem predecessora");
        r.SemPredecessora.Should().Equal("Sapatas", "Pilares");
        r.Texto.Should().Be("Tarefa \"Escavação\" excluída. Podem mudar de data: Sapatas, Pilares.");
    }

    [Fact]
    public void Resumo_PedeAJanela_EOGrupoInteiroSai_ComAsPontasReligadas()
    {
        var plano = RegrasDeExclusao.Planejar(_c, [_fundacao.Id, _sapatas.Id]);

        (plano.TemResumo, plano.Selecionadas.Count, plano.Subtarefas, plano.Total).Should().Be((true, 1, 2, 3), "Sapatas dentro de Fundação conta uma vez (E6)");
        plano.PerguntaDoResumo.Should().StartWith("Excluir \"Fundação\" e suas 2 subtarefas?");
        var r = RegrasDeExclusao.Aplicar(_c, plano, ModoDosResumos.GrupoInteiro);

        Nomes().Should().Equal("Estrutura", "  Pilares", "Entrega");
        Vinculos().Should().Equal("Pilares→Entrega");
        r.Texto.Should().Be("\"Fundação\" e 2 subtarefas excluídos. Podem mudar de data: Pilares.");
    }

    [Fact]
    public void SoOResumo_AsSubtarefasSobemUmNivel_NoMesmoLugar()
    {
        var plano = RegrasDeExclusao.Planejar(_c, [_fundacao.Id]);

        var r = RegrasDeExclusao.Aplicar(_c, plano, ModoDosResumos.SoOResumo);

        Nomes().Should().Equal("Escavação", "Sapatas", "Estrutura", "  Pilares", "Entrega");
        Vinculos().Should().HaveCount(3, "os vínculos das subtarefas ficam");
        (r.Excluidas, r.Subiram).Should().Be((1, 2));
        r.Texto.Should().Be("Resumo \"Fundação\" excluído; 2 subtarefas subiram um nível.");
    }

    [Fact]
    public void ResumoQueFicaVazio_ViraTarefa_ComOInicioEOTerminoQueTinha()
    {
        var (inicio, termino) = (_estrutura.InicioMaisCedo, _estrutura.TerminoMaisCedo);
        inicio.Should().BeAfter(Datas.Segunda);

        var r = RegrasDeExclusao.Aplicar(_c, RegrasDeExclusao.Planejar(_c, [_pilares.Id]), ModoDosResumos.GrupoInteiro);
        CalculadoraCronograma.Calcular(_c);

        _estrutura.EhResumo.Should().BeFalse();
        (_estrutura.InicioMaisCedo, _estrutura.TerminoMaisCedo).Should().Be((inicio, termino), "E4");
        r.ViraramTarefa.Should().Equal("Estrutura");
        Vinculos().Should().Equal("Escavação→Sapatas", "Sapatas→Entrega+0".Replace("+0", ""));
    }

    [Fact]
    public void ProgressoFotosEQuantidade_PedemConfirmacao()
    {
        _escavacao.AtualizarPercentualConcluido(40);
        RegrasDeExclusao.Planejar(_c, [_escavacao.Id]).Should().Match<PlanoDeExclusao>(p => p.PedeConfirmacao && p.ComProgresso == 1);
        RegrasDeExclusao.Planejar(_c, [_escavacao.Id]).PerguntaSemResumo.Should().Be("Excluir \"Escavação\"?\n1 tarefa tem progresso registrado.");

        var muitas = Enumerable.Range(1, 11).Select(i => new Tarefa($"T{i}", Datas.Segunda, 1)).ToList();
        foreach (var t in muitas)
            _c.AdicionarTarefa(t);
        var plano = RegrasDeExclusao.Planejar(_c, muitas.Select(t => t.Id));
        (plano.PedeConfirmacao, plano.PerguntaSemResumo).Should().Be((true, "Excluir 11 tarefas de uma vez?"));
        RegrasDeExclusao.Planejar(_c, muitas.Take(10).Select(t => t.Id)).PedeConfirmacao.Should().BeFalse("até 10, sem pergunta");
    }

    [Fact]
    public void VariasDaMesmaSequencia_ViramUmVinculoSo()
    {
        var r = RegrasDeExclusao.Aplicar(_c, RegrasDeExclusao.Planejar(_c, [_sapatas.Id, _pilares.Id]), ModoDosResumos.GrupoInteiro);

        Vinculos().Should().Equal("Escavação→Entrega");
        r.VinculosRefeitos.Should().Equal("Escavação → Entrega");
        r.ViraramTarefa.Should().Equal("Estrutura");
        r.Texto.Should().StartWith("2 tarefas excluídas.");
    }
}
