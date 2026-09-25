using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class TarefaCriacaoTests
{
    [Fact]
    public void Criar_ComDadosValidos_PreencheTodasAsPropriedades()
    {
        var tarefa = new Tarefa("Levantar requisitos", Segunda, 5);

        tarefa.Id.Should().NotBeEmpty();
        tarefa.Nome.Should().Be("Levantar requisitos");
        tarefa.Duracao.Should().Be(5);
        tarefa.DataInicio.Should().Be(Segunda);
        tarefa.DataTermino.Should().Be(Sexta);
        tarefa.PercentualConcluido.Should().Be(0);
    }

    [Fact]
    public void Criar_DuasTarefas_GeraIdsDiferentes()
    {
        var a = new Tarefa("A", Segunda, 1);
        var b = new Tarefa("B", Segunda, 1);

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void Reconstruir_ComId_MantemOIdEAplicaAsMesmasRegras()
    {
        var id = Guid.NewGuid();

        var tarefa = new Tarefa(id, " Revisar ", Sabado, 2);

        tarefa.Id.Should().Be(id);
        tarefa.Nome.Should().Be("Revisar");
        tarefa.DataInicio.Should().Be(ProximaSegunda);
        tarefa.DataTermino.Should().Be(ProximaTerca);
    }

    [Fact]
    public void Reconstruir_ComIdVazio_LancaExcecao()
    {
        var criar = () => new Tarefa(Guid.Empty, "Tarefa", Segunda, 1);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.Id));
    }

    [Fact]
    public void Criar_ComEspacosNasPontasDoNome_RemoveOsEspacos()
    {
        var tarefa = new Tarefa("  Revisar contrato \t", Segunda, 1);

        tarefa.Nome.Should().Be("Revisar contrato");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Criar_ComNomeNuloVazioOuEmBranco_LancaExcecao(string? nome)
    {
        var criar = () => new Tarefa(nome!, Segunda, 1);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.Nome));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Criar_ComDuracaoZeroOuNegativa_LancaExcecao(int duracao)
    {
        var criar = () => new Tarefa("Tarefa", Segunda, duracao);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.Duracao));
    }

    [Fact]
    public void Criar_ComDuracaoDeUmDia_TerminaNoMesmoDiaDoInicio()
    {
        var tarefa = new Tarefa("Tarefa", Quarta, 1);

        tarefa.DataTermino.Should().Be(Quarta);
    }

    public static TheoryData<DateTime, int, DateTime> DuracoesEmDiasUteis => new()
    {
        { Segunda, 5, Sexta },
        { Quinta, 2, Sexta },
        { Sexta, 2, ProximaSegunda },
        { Quarta, 5, ProximaTerca },
        { Segunda, 6, ProximaSegunda },
        { Segunda, 10, ProximaSexta },
        { Sexta, 11, new DateTime(2026, 10, 16) },
        { Segunda, 260, new DateTime(2027, 9, 24) },
    };

    [Theory]
    [MemberData(nameof(DuracoesEmDiasUteis))]
    public void Criar_CalculaTerminoContandoApenasDiasUteis(DateTime inicio, int duracao, DateTime terminoEsperado)
    {
        var tarefa = new Tarefa("Tarefa", inicio, duracao);

        tarefa.DataTermino.Should().Be(terminoEsperado);
    }

    [Theory]
    [MemberData(nameof(FimDeSemana), MemberType = typeof(Datas))]
    public void Criar_ComInicioNoFimDeSemana_MoveInicioParaASegundaSeguinte(DateTime inicio)
    {
        var tarefa = new Tarefa("Tarefa", inicio, 2);

        tarefa.DataInicio.Should().Be(ProximaSegunda);
        tarefa.DataTermino.Should().Be(ProximaTerca);
    }

    [Fact]
    public void Criar_ComHorarioNaDataDeInicio_DescartaOHorario()
    {
        var tarefa = new Tarefa("Tarefa", Segunda.AddHours(14).AddMinutes(30), 1);

        tarefa.DataInicio.Should().Be(Segunda);
        tarefa.DataTermino.Should().Be(Segunda);
    }

    [Fact]
    public void Criar_TerminandoNaMaiorDataSuportada_Aceita()
    {
        var tarefa = new Tarefa("Tarefa", UltimaSegunda, 5);

        tarefa.DataTermino.Should().Be(DateTime.MaxValue.Date);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void Criar_ComTerminoAlemDaMaiorDataSuportada_LancaExcecao(int duracao)
    {
        var criar = () => new Tarefa("Tarefa", UltimaSegunda, duracao);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.DataTermino));
    }
}
