using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.Core.Tests;

// Bloco 16: linhas vazias na grid, como o Gantt já preenche a área visível (Bloco 8).
public class PreenchimentoDaGradeTests
{
    [Theory]
    [InlineData(0, 300, 30, 10)]   // sem tarefas: a área toda
    [InlineData(4, 300, 30, 6)]    // poucas tarefas: o que falta
    [InlineData(4, 310, 30, 7)]    // a última aparece só em parte, mas conta
    [InlineData(10, 300, 30, 0)]   // as reais cobrem exatamente
    [InlineData(40, 300, 30, 0)]   // mais tarefas que a área: a grid rola, nada a preencher
    public void LinhasVazias_ParaCobrirAAlturaVisivel(int reais, double altura, double alturaLinha, int esperado)
    {
        PreenchimentoDaGrade.LinhasVazias(reais, altura, alturaLinha).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void LinhasVazias_SemAlturaUtil_Nenhuma(double altura)
    {
        PreenchimentoDaGrade.LinhasVazias(3, altura, 30).Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(double.NaN)]
    public void LinhasVazias_AlturaDeLinhaInvalida_Recusada(double alturaLinha)
    {
        var calcular = () => PreenchimentoDaGrade.LinhasVazias(3, 300, alturaLinha);

        calcular.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("alturaLinha");
    }

    [Fact]
    public void LinhasVazias_QuantidadeNegativaDeTarefas_Recusada()
    {
        var calcular = () => PreenchimentoDaGrade.LinhasVazias(-1, 300, 30);

        calcular.Should().Throw<ArgumentOutOfRangeException>();
    }
}
