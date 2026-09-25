using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class NumeracaoEdtTests
{
    private readonly Cronograma _cronograma = new("Obra", Segunda);

    private Tarefa Nova(string nome, Tarefa? pai = null, int duracao = 1)
    {
        var tarefa = new Tarefa(nome, Segunda, duracao);
        _cronograma.AdicionarTarefa(tarefa, pai?.Id);
        return tarefa;
    }

    private List<(string Edt, string Nome)> Numeracao()
    {
        var codigos = _cronograma.NumeracaoEdt();
        return _cronograma.Tarefas.Select(t => (codigos[t.Id], t.Nome)).ToList();
    }

    // Árvore de referência: três níveis, ramos de tamanhos diferentes e tarefas soltas entre eles.
    private void MontarReferencia()
    {
        var obra = Nova("Obra");
        var fase1 = Nova("Fase 1", obra);
        Nova("A", fase1);
        Nova("B", fase1);
        Nova("Fase 1 concluída", fase1, duracao: 0);
        var fase2 = Nova("Fase 2", obra);
        Nova("C", fase2);
        Nova("Entrega", duracao: 0);
        var posObra = Nova("Pós-obra");
        var vistoria = Nova("Vistoria", posObra);
        Nova("X", vistoria);
        Nova("Y", posObra);
    }

    [Fact]
    public void ArvoreComVariosNiveisERamos_NumeraCadaNivelNaOrdemDaLista()
    {
        MontarReferencia();

        Numeracao().Should().Equal(
            ("1", "Obra"),
            ("1.1", "Fase 1"),
            ("1.1.1", "A"),
            ("1.1.2", "B"),
            ("1.1.3", "Fase 1 concluída"),
            ("1.2", "Fase 2"),
            ("1.2.1", "C"),
            ("2", "Entrega"),
            ("3", "Pós-obra"),
            ("3.1", "Vistoria"),
            ("3.1.1", "X"),
            ("3.2", "Y"));
    }

    [Fact]
    public void SemEstrutura_EONumeroDaLinha()
    {
        Nova("A");
        Nova("B");
        Nova("C");

        Numeracao().Select(n => n.Edt).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void EdtDe_DevolveOCodigoDeUmaTarefa()
    {
        MontarReferencia();
        var x = _cronograma.Tarefas.Single(t => t.Nome == "X");

        _cronograma.EdtDe(x.Id).Should().Be("3.1.1");
    }

    [Fact]
    public void EdtDe_TarefaDeOutroCronograma_Recusada()
    {
        var edt = () => _cronograma.EdtDe(Guid.NewGuid());

        edt.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void MudancasNaEstrutura_RenumeramTudo()
    {
        MontarReferencia();
        Tarefa Tarefa(string nome) => _cronograma.Tarefas.Single(t => t.Nome == nome);

        _cronograma.Promover(Tarefa("B").Id);        // B sai da Fase 1 e fica depois dela, dentro da Obra
        _cronograma.Rebaixar(Tarefa("Entrega").Id);  // Entrega vira a última subtarefa da Obra
        _cronograma.RemoverTarefa(Tarefa("Vistoria").Id);

        Numeracao().Should().Equal(
            ("1", "Obra"),
            ("1.1", "Fase 1"),
            ("1.1.1", "A"),
            ("1.1.2", "Fase 1 concluída"),
            ("1.2", "B"),
            ("1.3", "Fase 2"),
            ("1.3.1", "C"),
            ("1.4", "Entrega"),
            ("2", "Pós-obra"),
            ("2.1", "Y"));
    }
}
