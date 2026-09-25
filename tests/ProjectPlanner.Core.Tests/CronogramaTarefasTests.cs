using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class CronogramaTarefasTests
{
    [Fact]
    public void NovoCronograma_ComecaVazio()
    {
        var cronograma = new Cronograma(Segunda);

        cronograma.Tarefas.Should().BeEmpty();
        cronograma.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void NovoCronograma_GuardaADataDeInicioSemHorario()
    {
        var cronograma = new Cronograma(Quarta.AddHours(9));

        cronograma.DataInicio.Should().Be(Quarta);
    }

    [Theory]
    [MemberData(nameof(FimDeSemana), MemberType = typeof(Datas))]
    public void NovoCronograma_ComInicioNoFimDeSemana_MoveParaASegundaSeguinte(DateTime inicio)
    {
        var cronograma = new Cronograma(inicio);

        cronograma.DataInicio.Should().Be(ProximaSegunda);
    }

    [Fact]
    public void AdicionarTarefa_MantemAOrdemDeInclusao()
    {
        var cronograma = new Cronograma(Segunda);
        var a = new Tarefa("A", Segunda, 1);
        var b = new Tarefa("B", Segunda, 1);

        cronograma.AdicionarTarefa(a);
        cronograma.AdicionarTarefa(b);

        cronograma.Tarefas.Should().Equal(a, b);
    }

    [Fact]
    public void AdicionarTarefa_JaExistente_LancaExcecaoENaoDuplica()
    {
        var cronograma = new Cronograma(Segunda);
        var a = new Tarefa("A", Segunda, 1);
        cronograma.AdicionarTarefa(a);

        var adicionar = () => cronograma.AdicionarTarefa(a);

        adicionar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Tarefas));
        cronograma.Tarefas.Should().ContainSingle();
    }

    [Fact]
    public void AdicionarTarefa_Nula_LancaArgumentNullException()
    {
        var adicionar = () => new Cronograma(Segunda).AdicionarTarefa(null!);

        adicionar.Should().Throw<ArgumentNullException>();
    }
}
