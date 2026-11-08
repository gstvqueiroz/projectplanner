using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.Core.Tests;

// Bloco 77: o conteúdo dos cards (o plano "Delete, arrastar e Kanban", K4 e K5): checklist (não muda o %), etiquetas do
// projeto e arquivar. Tarefas: A 0%, B 40%, C 0%.
public class RegrasDosCardsTests
{
    private readonly Cronograma _c = new("Obra", Datas.Segunda);
    private readonly Tarefa _a = new("A", Datas.Segunda, 2);
    private readonly Tarefa _b = new("B", Datas.Segunda, 2);
    private readonly Tarefa _cc = new("C", Datas.Segunda, 2);

    public RegrasDosCardsTests()
    {
        _c.AdicionarTarefa(_a);
        _c.AdicionarTarefa(_b);
        _c.AdicionarTarefa(_cc);
        _b.AtualizarPercentualConcluido(40);
        _c.Quadro.Sincronizar();
    }

    private IEnumerable<string> NaColuna(TipoDeColuna tipo) =>
        _c.Quadro.CardsDa(_c.Quadro.Primeira(tipo).Id).Select(k => _c.Tarefas.Single(t => t.Id == k.TarefaId).Nome);

    [Fact]
    public void Checklist_NaoMudaOPercentual_EOsTextosSaoConferidos()
    {
        RegrasDosCards.DefinirChecklist(_c, _b.Id, [new ItemDoChecklist(" Formas ", true), new ItemDoChecklist("Armação", false)]);

        _c.Quadro.CardDe(_b.Id)!.Checklist.Should().Equal(new ItemDoChecklist("Formas", true), new ItemDoChecklist("Armação", false));
        _b.PercentualConcluido.Should().Be(40, "o checklist não altera o % (decisão do plano)");
        FluentActions.Invoking(() => RegrasDosCards.DefinirChecklist(_c, _b.Id, [new ItemDoChecklist(" ", false)]))
            .Should().Throw<DomainValidationException>().WithMessage("Um item do checklist precisa de um texto.");
        FluentActions.Invoking(() => RegrasDosCards.DefinirChecklist(_c, _b.Id, Enumerable.Range(1, 51).Select(i => new ItemDoChecklist($"{i}", false))))
            .Should().Throw<DomainValidationException>().WithMessage("O checklist pode ter até 50 itens.");
        _c.Quadro.CardDe(_b.Id)!.Checklist.Should().HaveCount(2, "recusado, nada muda");
    }

    [Fact]
    public void Etiquetas_DoProjeto_MarcadasNosCards_ExcluirTiraDeTodos()
    {
        var urgente = RegrasDosCards.CriarEtiqueta(_c, " Urgente ");
        var eletrica = RegrasDosCards.CriarEtiqueta(_c, "Elétrica");
        (urgente.Nome, urgente.Cor, eletrica.Cor).Should().Be(("Urgente", "#E5484D", "#F76B15"), "cada uma com uma cor ainda sem uso");
        FluentActions.Invoking(() => RegrasDosCards.CriarEtiqueta(_c, "urgente")).Should().Throw<DomainValidationException>()
            .WithMessage("Já existe a etiqueta \"Urgente\" no projeto.");

        RegrasDosCards.MarcarEtiqueta(_c, _a.Id, urgente.Id, true);
        RegrasDosCards.MarcarEtiqueta(_c, _a.Id, eletrica.Id, true);
        RegrasDosCards.MarcarEtiqueta(_c, _b.Id, urgente.Id, true);
        RegrasDosCards.MarcarEtiqueta(_c, _a.Id, urgente.Id, false);
        _c.Quadro.CardDe(_a.Id)!.Etiquetas.Should().Equal(eletrica.Id);

        RegrasDosCards.RenomearEtiqueta(_c, urgente.Id, "Prioridade");
        RegrasDosCards.ColorirEtiqueta(_c, urgente.Id, "#8E4EC6");
        _c.Quadro.Etiquetas.Should().Equal(new EtiquetaDoQuadro(urgente.Id, "Prioridade", "#8E4EC6"), eletrica);

        RegrasDosCards.ExcluirEtiqueta(_c, urgente.Id).Should().Be(1);
        _c.Quadro.CardDe(_b.Id)!.Etiquetas.Should().BeEmpty();
        FluentActions.Invoking(() => RegrasDosCards.MarcarEtiqueta(_c, _b.Id, urgente.Id, true)).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Arquivar_EscondeDoQuadro_ATarefaFica_EDesarquivarVoltaNoFimDaColuna()
    {
        RegrasDosCards.Arquivar(_c, [_a.Id, _a.Id]).Should().Be(1);

        NaColuna(TipoDeColuna.AFazer).Should().Equal("C");
        _c.Tarefas.Should().HaveCount(3, "a tarefa continua no cronograma");

        RegrasDosCards.Desarquivar(_c, [_a.Id]).Should().Be(1);
        NaColuna(TipoDeColuna.AFazer).Should().Equal("C", "A");
    }

    [Fact]
    public void Desarquivar_UmCardCujoPercentualMudou_VaiParaAColunaDoPercentual()
    {
        RegrasDosCards.Arquivar(_c, [_a.Id]);
        _a.AtualizarPercentualConcluido(100);
        _c.Quadro.Sincronizar();

        RegrasDosCards.Desarquivar(_c, [_a.Id]);

        NaColuna(TipoDeColuna.Concluida).Should().Equal("A");
    }
}
