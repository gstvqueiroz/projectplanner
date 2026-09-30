using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Bloco 21: inserir uma tarefa numa posição entre as irmãs (a ordem é a da lista, como no Bloco 18).
public class CronogramaInsercaoTests
{
    private readonly Cronograma _cronograma = new("Obra", Segunda);

    private Tarefa Nova(string nome, Tarefa? pai = null)
    {
        var tarefa = new Tarefa(nome, Segunda, 1);
        _cronograma.AdicionarTarefa(tarefa, pai?.Id);
        return tarefa;
    }

    private List<string> Edt() => _cronograma.Tarefas.Select(t => $"{_cronograma.EdtDe(t.Id)} {t.Nome}").ToList();

    [Fact]
    public void NoMeioDoPrimeiroNivel_AsSeguintesAndamUmaPosicao()
    {
        foreach (var nome in new[] { "T1", "T2", "T3", "T4", "T5", "T6" })
            Nova(nome);

        _cronograma.InserirTarefaNaPosicao(new Tarefa("Nova", Segunda, 1), null, 3); // a 4ª posição

        Edt().Should().Equal("1 T1", "2 T2", "3 T3", "4 Nova", "5 T4", "6 T5", "7 T6");
    }

    [Fact]
    public void NoInicioENoFim_DoGrupo()
    {
        Nova("A");
        Nova("B");

        _cronograma.InserirTarefaNaPosicao(new Tarefa("Primeira", Segunda, 1), null, 0);
        _cronograma.InserirTarefaNaPosicao(new Tarefa("Última", Segunda, 1), null, 3);

        Edt().Should().Equal("1 Primeira", "2 A", "3 B", "4 Última");
    }

    [Fact]
    public void DentroDeUmResumo_SoAsIrmasDeleAndam()
    {
        var fundacao = Nova("Fundação");
        Nova("Escavar", fundacao);
        Nova("Armar", fundacao);
        var estrutura = Nova("Estrutura");
        Nova("Pilares", estrutura);
        var nova = new Tarefa("Gabarito", Segunda, 1);

        _cronograma.InserirTarefaNaPosicao(nova, fundacao.Id, 1);

        Edt().Should().Equal("1 Fundação", "1.1 Escavar", "1.2 Gabarito", "1.3 Armar", "2 Estrutura", "2.1 Pilares");
        nova.TarefaPaiId.Should().Be(fundacao.Id);
    }

    [Fact]
    public void NoFimDeUmGrupo_QueTemSubtarefas_EntraDepoisDeTodasElas()
    {
        var fundacao = Nova("Fundação");
        Nova("Escavar", fundacao);
        Nova("Cobertura");

        _cronograma.InserirTarefaNaPosicao(new Tarefa("Nova", Segunda, 1), null, 1);

        Edt().Should().Equal("1 Fundação", "1.1 Escavar", "2 Nova", "3 Cobertura");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void PosicaoForaDoIntervalo_Recusada_SemMudarNada(int posicao)
    {
        Nova("A");
        Nova("B");

        var inserir = () => _cronograma.InserirTarefaNaPosicao(new Tarefa("Nova", Segunda, 1), null, posicao);

        inserir.Should().Throw<DomainValidationException>().WithMessage("*vai de 0 a 2*");
        Edt().Should().Equal("1 A", "2 B");
    }

    [Fact]
    public void DentroDeUmMarco_Recusada()
    {
        var marco = new Tarefa("Marco", Segunda, 0);
        _cronograma.AdicionarTarefa(marco);

        var inserir = () => _cronograma.InserirTarefaNaPosicao(new Tarefa("Nova", Segunda, 1), marco.Id, 0);

        inserir.Should().Throw<DomainValidationException>();
        _cronograma.Tarefas.Should().ContainSingle();
    }
}
