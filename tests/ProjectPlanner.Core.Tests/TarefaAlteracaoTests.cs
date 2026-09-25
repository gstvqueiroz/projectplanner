using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class TarefaAlteracaoTests
{
    // Tarefa de referência: de segunda a sexta da mesma semana (5 dias úteis).
    private static Tarefa NovaTarefa() => new("Tarefa", Segunda, 5);

    private sealed record Estado(string Nome, int Duracao, DateTime DataInicio, DateTime DataTermino, int PercentualConcluido);

    private static Estado EstadoDe(Tarefa t) =>
        new(t.Nome, t.Duracao, t.DataInicio, t.DataTermino, t.PercentualConcluido);

    // Renomear

    [Fact]
    public void Renomear_ComNomeValido_AtualizaONome()
    {
        var tarefa = NovaTarefa();

        tarefa.Renomear("  Novo nome ");

        tarefa.Nome.Should().Be("Novo nome");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Renomear_ComNomeInvalido_LancaExcecaoEMantemATarefaInalterada(string? nome)
    {
        var tarefa = NovaTarefa();
        var antes = EstadoDe(tarefa);

        var renomear = () => tarefa.Renomear(nome!);

        renomear.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.Nome));
        EstadoDe(tarefa).Should().Be(antes);
    }

    // AlterarDuracao

    [Fact]
    public void AlterarDuracao_RecalculaTerminoEMantemInicio()
    {
        var tarefa = NovaTarefa();

        tarefa.AlterarDuracao(6);

        tarefa.Duracao.Should().Be(6);
        tarefa.DataInicio.Should().Be(Segunda);
        tarefa.DataTermino.Should().Be(ProximaSegunda);
    }

    // Duração 0 não é mais erro: transforma a tarefa em marco (Bloco 11, ver TarefaMarcoTests).
    [Theory]
    [InlineData(-1)]
    [InlineData(-3)]
    public void AlterarDuracao_Negativa_LancaExcecaoEMantemATarefaInalterada(int duracao)
    {
        var tarefa = NovaTarefa();
        var antes = EstadoDe(tarefa);

        var alterar = () => tarefa.AlterarDuracao(duracao);

        alterar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.Duracao));
        EstadoDe(tarefa).Should().Be(antes);
    }

    [Fact]
    public void AlterarDuracao_ComTerminoAlemDaMaiorDataSuportada_LancaExcecaoEMantemATarefaInalterada()
    {
        var tarefa = NovaTarefa();
        var antes = EstadoDe(tarefa);

        var alterar = () => tarefa.AlterarDuracao(int.MaxValue);

        alterar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.DataTermino));
        EstadoDe(tarefa).Should().Be(antes);
    }

    // AlterarDataInicio

    [Fact]
    public void AlterarDataInicio_MantemDuracaoEDeslocaTermino()
    {
        var tarefa = NovaTarefa();

        tarefa.AlterarDataInicio(Quarta);

        tarefa.DataInicio.Should().Be(Quarta);
        tarefa.Duracao.Should().Be(5);
        tarefa.DataTermino.Should().Be(ProximaTerca);
    }

    [Theory]
    [MemberData(nameof(FimDeSemana), MemberType = typeof(Datas))]
    public void AlterarDataInicio_ParaFimDeSemana_MoveParaASegundaSeguinte(DateTime inicio)
    {
        var tarefa = NovaTarefa();

        tarefa.AlterarDataInicio(inicio);

        tarefa.DataInicio.Should().Be(ProximaSegunda);
        tarefa.DataTermino.Should().Be(ProximaSexta);
    }

    [Fact]
    public void AlterarDataInicio_ComTerminoAlemDaMaiorDataSuportada_LancaExcecaoEMantemATarefaInalterada()
    {
        var tarefa = NovaTarefa();
        var antes = EstadoDe(tarefa);

        // Começando numa terça, 5 dias úteis terminariam na segunda seguinte, que não existe.
        var alterar = () => tarefa.AlterarDataInicio(UltimaSegunda.AddDays(1));

        alterar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.DataTermino));
        EstadoDe(tarefa).Should().Be(antes);
    }

    // AlterarDataTermino

    [Fact]
    public void AlterarDataTermino_RecalculaDuracaoEmDiasUteisEMantemInicio()
    {
        var tarefa = NovaTarefa();

        tarefa.AlterarDataTermino(ProximaSexta);

        tarefa.DataTermino.Should().Be(ProximaSexta);
        tarefa.Duracao.Should().Be(10);
        tarefa.DataInicio.Should().Be(Segunda);
    }

    [Fact]
    public void AlterarDataTermino_IgualAoInicio_ResultaEmDuracaoDeUmDia()
    {
        var tarefa = NovaTarefa();

        tarefa.AlterarDataTermino(Segunda);

        tarefa.DataTermino.Should().Be(Segunda);
        tarefa.Duracao.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(FimDeSemana), MemberType = typeof(Datas))]
    public void AlterarDataTermino_NoFimDeSemana_VoltaParaASextaAnterior(DateTime termino)
    {
        var tarefa = new Tarefa("Tarefa", Segunda, 2);

        tarefa.AlterarDataTermino(termino);

        tarefa.DataTermino.Should().Be(Sexta);
        tarefa.Duracao.Should().Be(5);
    }

    [Fact]
    public void AlterarDataTermino_NoSabadoDeTarefaQueComecaNaSexta_ResultaEmDuracaoDeUmDia()
    {
        var tarefa = new Tarefa("Tarefa", Sexta, 3);

        tarefa.AlterarDataTermino(Sabado);

        tarefa.DataTermino.Should().Be(Sexta);
        tarefa.Duracao.Should().Be(1);
    }

    [Fact]
    public void AlterarDataTermino_ComHorario_DescartaOHorario()
    {
        var tarefa = NovaTarefa();

        tarefa.AlterarDataTermino(Quarta.AddHours(18));

        tarefa.DataTermino.Should().Be(Quarta);
        tarefa.Duracao.Should().Be(3);
    }

    [Fact]
    public void AlterarDataTermino_AnteriorAoInicio_LancaExcecaoEMantemATarefaInalterada()
    {
        var tarefa = new Tarefa("Tarefa", Quarta, 2);
        var antes = EstadoDe(tarefa);

        var alterar = () => tarefa.AlterarDataTermino(Terca);

        alterar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.DataTermino));
        EstadoDe(tarefa).Should().Be(antes);
    }

    [Fact]
    public void AlterarDataTermino_NoFimDeSemanaAnteriorAoInicio_LancaExcecao()
    {
        var tarefa = new Tarefa("Tarefa", ProximaSegunda, 2);

        var alterar = () => tarefa.AlterarDataTermino(Domingo);

        alterar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.DataTermino));
    }
}
