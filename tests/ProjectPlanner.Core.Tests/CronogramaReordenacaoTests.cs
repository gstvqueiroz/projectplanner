using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Bloco 18: mudar a ordem de uma tarefa entre as irmãs (mesmo pai), com as subtarefas junto.
public class CronogramaReordenacaoTests
{
    private readonly Cronograma _cronograma = new("Obra", Segunda);

    private Tarefa Nova(string nome, Tarefa? pai = null, int duracao = 1)
    {
        var tarefa = new Tarefa(nome, Segunda, duracao);
        _cronograma.AdicionarTarefa(tarefa, pai?.Id);
        return tarefa;
    }

    private List<string> Estrutura() =>
        _cronograma.Tarefas.Select(t => new string(' ', 2 * _cronograma.NivelDe(t.Id)) + t.Nome).ToList();

    private List<string> Edt() => _cronograma.Tarefas.Select(t => $"{_cronograma.EdtDe(t.Id)} {t.Nome}").ToList();

    [Fact]
    public void NoPrimeiroNivel_TrocaDeLugarEAOrdemEAEdtAcompanham()
    {
        var (a, b, c) = (Nova("A"), Nova("B"), Nova("C"));

        _cronograma.MoverEntreIrmas(c.Id, 0).Should().BeTrue();

        Estrutura().Should().Equal("C", "A", "B");
        Edt().Should().Equal("1 C", "2 A", "3 B");

        _cronograma.MoverEntreIrmas(c.Id, 2);
        Estrutura().Should().Equal("A", "B", "C");
        _cronograma.MoverEntreIrmas(a.Id, 1);
        Edt().Should().Equal("1 B", "2 A", "3 C");
        b.TarefaPaiId.Should().BeNull();
    }

    [Fact]
    public void DentroDeUmResumo_SoAsIrmasDeleMudam()
    {
        var fundacao = Nova("Fundação");
        var escavar = Nova("Escavar", fundacao);
        var armar = Nova("Armar", fundacao);
        var concretar = Nova("Concretar", fundacao);
        var estrutura = Nova("Estrutura");
        Nova("Pilares", estrutura);
        Nova("Vigas", estrutura);

        _cronograma.MoverEntreIrmas(concretar.Id, 0);

        Estrutura().Should().Equal("Fundação", "  Concretar", "  Escavar", "  Armar", "Estrutura", "  Pilares", "  Vigas");
        Edt().Should().Equal("1 Fundação", "1.1 Concretar", "1.2 Escavar", "1.3 Armar", "2 Estrutura", "2.1 Pilares", "2.2 Vigas");
        (concretar.TarefaPaiId, escavar.TarefaPaiId, armar.TarefaPaiId).Should().Be((fundacao.Id, fundacao.Id, fundacao.Id));
    }

    [Fact]
    public void UmResumo_VaiComTodasAsSubtarefas()
    {
        var fundacao = Nova("Fundação");
        var escavar = Nova("Escavar", fundacao);
        Nova("Detalhe", escavar);
        Nova("Armar", fundacao);
        Nova("Estrutura");
        Nova("Cobertura");

        _cronograma.MoverEntreIrmas(fundacao.Id, 2);

        Estrutura().Should().Equal("Estrutura", "Cobertura", "Fundação", "  Escavar", "    Detalhe", "  Armar");
        Edt().Should().Equal("1 Estrutura", "2 Cobertura", "3 Fundação", "3.1 Escavar", "3.1.1 Detalhe", "3.2 Armar");
    }

    [Fact]
    public void ParaAMesmaPosicao_NaoMudaNada()
    {
        var (a, b) = (Nova("A"), Nova("B"));

        _cronograma.MoverEntreIrmas(b.Id, 1).Should().BeFalse();

        Estrutura().Should().Equal("A", "B");
    }

    [Fact]
    public void Reordenar_NaoMudaOCaminhoCritico()
    {
        var a = Nova("A", duracao: 3);
        var b = Nova("B", duracao: 2);
        var c = Nova("C", duracao: 1);
        var d = Nova("D", duracao: 4);
        _cronograma.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        _cronograma.AdicionarPredecessora(new Predecessora(a.Id, c.Id));
        _cronograma.AdicionarPredecessora(new Predecessora(c.Id, d.Id));
        var antes = CalculadoraCronograma.Calcular(_cronograma);
        object Valores() => _cronograma.Tarefas.OrderBy(t => t.Nome)
            .Select(t => (t.Nome, t.InicioMaisCedo, t.TerminoMaisCedo, t.InicioMaisTarde, t.TerminoMaisTarde, t.Folga)).ToList();
        var valoresAntes = Valores();

        _cronograma.MoverEntreIrmas(d.Id, 0);
        _cronograma.MoverEntreIrmas(b.Id, 1);
        var depois = CalculadoraCronograma.Calcular(_cronograma);

        Valores().Should().BeEquivalentTo(valoresAntes, o => o.WithStrictOrdering());
        depois.DataTermino.Should().Be(antes.DataTermino);
        depois.TarefasCriticas.Select(t => t.Nome).Should().BeEquivalentTo(antes.TarefasCriticas.Select(t => t.Nome));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void PosicaoQueNaoExisteEntreAsIrmas_Recusada(int posicao)
    {
        var a = Nova("A");
        Nova("B");
        Nova("Filha", a); // não conta como irmã de A

        var mover = () => _cronograma.MoverEntreIrmas(a.Id, posicao);

        mover.Should().Throw<ArgumentOutOfRangeException>();
        Estrutura().Should().Equal("A", "  Filha", "B");
    }

    [Fact]
    public void TarefaDeForaDoCronograma_Recusada()
    {
        Nova("A");

        var mover = () => _cronograma.MoverEntreIrmas(Guid.NewGuid(), 0);

        mover.Should().Throw<DomainValidationException>();
    }
}
