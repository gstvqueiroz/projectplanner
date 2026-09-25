using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.Core.Tests.Gantt;

public class CalculadoraLayoutGanttFinsDeSemanaTests
{
    [Fact]
    public void IntervaloQueViraOMes_MarcaOsSabadosEDomingos()
    {
        // Qua 23/09/2026 a qua 07/10/2026.
        var dias = CalculadoraLayoutGantt.DiasDeFimDeSemana(new DateTime(2026, 9, 23), new DateTime(2026, 10, 7));

        dias.Should().Equal(
            new DateTime(2026, 9, 26), new DateTime(2026, 9, 27),
            new DateTime(2026, 10, 3), new DateTime(2026, 10, 4));
    }

    [Fact]
    public void IntervaloQueViraOAno_MarcaOsSabadosEDomingos()
    {
        // Seg 28/12/2026 a dom 03/01/2027.
        var dias = CalculadoraLayoutGantt.DiasDeFimDeSemana(new DateTime(2026, 12, 28), new DateTime(2027, 1, 3));

        dias.Should().Equal(new DateTime(2027, 1, 2), new DateTime(2027, 1, 3));
    }

    [Fact]
    public void BordasInclusivas_EHorarioIgnorado()
    {
        var dias = CalculadoraLayoutGantt.DiasDeFimDeSemana(new DateTime(2026, 10, 3, 15, 0, 0), new DateTime(2026, 10, 4, 8, 0, 0));

        dias.Should().Equal(new DateTime(2026, 10, 3), new DateTime(2026, 10, 4));
    }

    [Theory]
    [InlineData(2026, 9, 28, 2026, 10, 2)] // seg a sex
    [InlineData(2026, 10, 5, 2026, 10, 1)] // fim antes do início
    public void SemFimDeSemana_ListaVazia(int a1, int m1, int d1, int a2, int m2, int d2)
    {
        CalculadoraLayoutGantt.DiasDeFimDeSemana(new DateTime(a1, m1, d1), new DateTime(a2, m2, d2)).Should().BeEmpty();
    }

    [Fact]
    public void FimDoCalendario_NaoEstoura()
    {
        // 31/12/9999 é sexta: o último fim de semana é 25 e 26/12.
        var dias = CalculadoraLayoutGantt.DiasDeFimDeSemana(new DateTime(9999, 12, 24), DateTime.MaxValue);

        dias.Should().Equal(new DateTime(9999, 12, 25), new DateTime(9999, 12, 26));
    }

    [Fact]
    public void NoLayout_OsFinsDeSemanaCobremTodoOEixoInclusiveAlemDasTarefas()
    {
        // Eixo de sex 25/09 a 40 dias (até ter 03/11), a 30 px/dia; uma tarefa só na primeira semana.
        var cenario = new MontadorCronograma().ComTarefa("A", 2);
        var layout = CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(),
            new OpcoesGantt(30) { LarguraVisivel = 1200 });

        layout.FinsDeSemana.Select(c => c.Dia.ToString("dd/MM")).Should().Equal(
            "26/09", "27/09", "03/10", "04/10", "10/10", "11/10", "17/10", "18/10", "24/10", "25/10", "31/10", "01/11");
        layout.FinsDeSemana[0].Should().Be(new ColunaDia(new DateTime(2026, 9, 26), 30, 30));
        layout.FinsDeSemana[^1].Should().Be(new ColunaDia(new DateTime(2026, 11, 1), 37 * 30, 30));
    }
}
