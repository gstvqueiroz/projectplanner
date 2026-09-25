using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class TarefaPercentualConcluidoTests
{
    [Fact]
    public void NovaTarefa_ComecaComZeroPorCento()
    {
        var tarefa = new Tarefa("Tarefa", Segunda, 1);

        tarefa.PercentualConcluido.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    public void AtualizarPercentualConcluido_DentroDoIntervalo_AtualizaOValor(int percentual)
    {
        var tarefa = new Tarefa("Tarefa", Segunda, 1);

        tarefa.AtualizarPercentualConcluido(percentual);

        tarefa.PercentualConcluido.Should().Be(percentual);
    }

    [Fact]
    public void AtualizarPercentualConcluido_PodeSerReduzido()
    {
        var tarefa = new Tarefa("Tarefa", Segunda, 1);
        tarefa.AtualizarPercentualConcluido(100);

        tarefa.AtualizarPercentualConcluido(0);

        tarefa.PercentualConcluido.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void AtualizarPercentualConcluido_ForaDoIntervalo_LancaExcecaoEMantemOValorAnterior(int percentual)
    {
        var tarefa = new Tarefa("Tarefa", Segunda, 1);
        tarefa.AtualizarPercentualConcluido(40);

        var atualizar = () => tarefa.AtualizarPercentualConcluido(percentual);

        atualizar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.PercentualConcluido));
        tarefa.PercentualConcluido.Should().Be(40);
    }
}
