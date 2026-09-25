using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class CronogramaTarefasTests
{
    [Fact]
    public void NovoCronograma_ComecaVazio()
    {
        var cronograma = new Cronograma("Projeto", Segunda);

        cronograma.Tarefas.Should().BeEmpty();
        cronograma.Predecessoras.Should().BeEmpty();
        cronograma.DataCriacao.Should().BeNull();
        cronograma.DataUltimaModificacao.Should().BeNull();
    }

    [Fact]
    public void NovoCronograma_GeraIdEGuardaONomeSemEspacosNasPontas()
    {
        var a = new Cronograma("  Obra da sede ", Segunda);
        var b = new Cronograma("Outro", Segunda);

        a.Id.Should().NotBeEmpty().And.NotBe(b.Id);
        a.Nome.Should().Be("Obra da sede");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NovoCronograma_SemNome_LancaExcecao(string? nome)
    {
        var criar = () => new Cronograma(nome!, Segunda);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Nome));
    }

    [Fact]
    public void ReconstruirCronograma_MantemOId()
    {
        var id = Guid.NewGuid();

        var cronograma = new Cronograma(id, "Projeto", Segunda);

        cronograma.Id.Should().Be(id);
    }

    [Fact]
    public void ReconstruirCronograma_ComIdVazio_LancaExcecao()
    {
        var criar = () => new Cronograma(Guid.Empty, "Projeto", Segunda);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Id));
    }

    [Fact]
    public void NovoCronograma_GuardaADataDeInicioSemHorario()
    {
        var cronograma = new Cronograma("Projeto", Quarta.AddHours(9));

        cronograma.DataInicio.Should().Be(Quarta);
    }

    [Theory]
    [MemberData(nameof(FimDeSemana), MemberType = typeof(Datas))]
    public void NovoCronograma_ComInicioNoFimDeSemana_MoveParaASegundaSeguinte(DateTime inicio)
    {
        var cronograma = new Cronograma("Projeto", inicio);

        cronograma.DataInicio.Should().Be(ProximaSegunda);
    }

    [Fact]
    public void AdicionarTarefa_MantemAOrdemDeInclusao()
    {
        var cronograma = new Cronograma("Projeto", Segunda);
        var a = new Tarefa("A", Segunda, 1);
        var b = new Tarefa("B", Segunda, 1);

        cronograma.AdicionarTarefa(a);
        cronograma.AdicionarTarefa(b);

        cronograma.Tarefas.Should().Equal(a, b);
    }

    [Fact]
    public void AdicionarTarefa_JaExistente_LancaExcecaoENaoDuplica()
    {
        var cronograma = new Cronograma("Projeto", Segunda);
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
        var adicionar = () => new Cronograma("Projeto", Segunda).AdicionarTarefa(null!);

        adicionar.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoverTarefa_RemoveATarefaEOsVinculosDelaSemReligarAsVizinhas()
    {
        // A → B → C, mais A → D. Remover B leva A → B e B → C, e A e C não ficam ligadas.
        var cronograma = new Cronograma("Projeto", Segunda);
        var (a, b, c, d) = (new Tarefa("A", Segunda, 1), new Tarefa("B", Segunda, 1),
            new Tarefa("C", Segunda, 1), new Tarefa("D", Segunda, 1));
        foreach (var t in new[] { a, b, c, d })
            cronograma.AdicionarTarefa(t);
        cronograma.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        cronograma.AdicionarPredecessora(new Predecessora(b.Id, c.Id));
        cronograma.AdicionarPredecessora(new Predecessora(a.Id, d.Id));

        cronograma.RemoverTarefa(b.Id);

        cronograma.Tarefas.Should().Equal(a, c, d);
        cronograma.Predecessoras.Should().Equal(new Predecessora(a.Id, d.Id));
    }

    [Fact]
    public void RemoverTarefa_ForaDoCronograma_LancaExcecaoENaoAlteraNada()
    {
        var cronograma = new Cronograma("Projeto", Segunda);
        cronograma.AdicionarTarefa(new Tarefa("A", Segunda, 1));

        var remover = () => cronograma.RemoverTarefa(Guid.NewGuid());

        remover.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Tarefas));
        cronograma.Tarefas.Should().ContainSingle();
    }
}
