using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class TarefaMarcoTests
{
    [Fact]
    public void TarefaNova_EhComumENoPrimeiroNivel()
    {
        var tarefa = new Tarefa("Fundação", Segunda, 5);

        tarefa.Tipo.Should().Be(TipoTarefa.Normal);
        tarefa.EhMarco.Should().BeFalse();
        tarefa.EhResumo.Should().BeFalse();
        tarefa.TarefaPaiId.Should().BeNull();
    }

    [Fact]
    public void Criar_ComDuracaoZero_EhMarcoComInicioIgualAoTermino()
    {
        var marco = new Tarefa("Início da obra", Quarta, 0);

        marco.Tipo.Should().Be(TipoTarefa.Marco);
        marco.EhMarco.Should().BeTrue();
        marco.Duracao.Should().Be(0);
        marco.DataInicio.Should().Be(Quarta);
        marco.DataTermino.Should().Be(Quarta);
    }

    [Theory]
    [MemberData(nameof(FimDeSemana), MemberType = typeof(Datas))]
    public void Criar_MarcoNoFimDeSemana_VaiParaASegundaSeguinte(DateTime fimDeSemana)
    {
        var marco = new Tarefa("Marco", fimDeSemana, 0);

        marco.DataInicio.Should().Be(ProximaSegunda);
        marco.DataTermino.Should().Be(ProximaSegunda);
    }

    [Fact]
    public void AlterarDuracaoParaZero_ViraMarcoNoDiaDoInicio()
    {
        var tarefa = new Tarefa("Estrutura concluída", Segunda, 5);

        tarefa.AlterarDuracao(0);

        tarefa.Tipo.Should().Be(TipoTarefa.Marco);
        tarefa.DataTermino.Should().Be(Segunda);
    }

    [Fact]
    public void AlterarDuracaoDeUmMarco_VoltaASerTarefaComum()
    {
        var marco = new Tarefa("Marco", Sexta, 0);

        marco.AlterarDuracao(3);

        marco.Tipo.Should().Be(TipoTarefa.Normal);
        marco.DataInicio.Should().Be(Sexta);
        marco.DataTermino.Should().Be(ProximaTerca);
    }

    [Fact]
    public void Marco_AlterarInicio_MoveOMarcoInteiro()
    {
        var marco = new Tarefa("Marco", Segunda, 0);

        marco.AlterarDataInicio(Sabado);

        marco.DataInicio.Should().Be(ProximaSegunda);
        marco.DataTermino.Should().Be(ProximaSegunda);
        marco.Duracao.Should().Be(0);
    }

    [Fact]
    public void Marco_AlterarTermino_MoveOMarcoInteiroMesmoParaAntesDoInicio()
    {
        var marco = new Tarefa("Marco", ProximaSegunda, 0);

        marco.AlterarDataTermino(Domingo);

        // O término num fim de semana volta para a sexta, como em qualquer tarefa.
        marco.DataInicio.Should().Be(Sexta);
        marco.DataTermino.Should().Be(Sexta);
        marco.Duracao.Should().Be(0);
        marco.Tipo.Should().Be(TipoTarefa.Marco);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Marco_TemPercentualConcluido(int percentual)
    {
        var marco = new Tarefa("Marco", Segunda, 0);

        marco.AtualizarPercentualConcluido(percentual);

        marco.PercentualConcluido.Should().Be(percentual);
    }

    [Fact]
    public void DuracaoNegativa_ContinuaRecusada()
    {
        var tarefa = new Tarefa("Tarefa", Segunda, 2);

        var alterar = () => tarefa.AlterarDuracao(-1);

        alterar.Should().Throw<DomainValidationException>()
            .WithMessage("A duração não pode ser negativa (informado: -1).");
        tarefa.Duracao.Should().Be(2);
    }
}
