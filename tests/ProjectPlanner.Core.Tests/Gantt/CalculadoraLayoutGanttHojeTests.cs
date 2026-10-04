using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.Core.Tests.Gantt;

// Bloco 25 (C1): a linha "Hoje". Projeto começando na seg 28/09/2026: o eixo começa 3 dias antes, na sex 25/09.
public class CalculadoraLayoutGanttHojeTests
{
    private static LayoutGantt LayoutDe(MontadorCronograma cenario, DateTime? hoje) =>
        CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(), new OpcoesGantt(30) { Hoje = hoje });

    [Theory]
    [InlineData("2026-09-25", 15)] // primeiro dia do eixo: meio da coluna
    [InlineData("2026-09-30", 165)] // qua: 5 dias depois
    [InlineData("2026-10-03", 255)] // sábado também tem linha
    [InlineData("2026-09-30 17:45", 165)] // o horário não conta
    public void Hoje_NoMeioDaColunaDoDia(string hoje, double x)
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 10);

        LayoutDe(cenario, DateTime.Parse(hoje)).XDeHoje.Should().Be(x);
    }

    [Theory]
    [InlineData("2026-09-24")] // antes do eixo
    [InlineData("2027-06-01")] // depois do fim do eixo
    public void HojeForaDoEixo_SemLinha(string hoje)
    {
        LayoutDe(new MontadorCronograma().ComTarefa("A", 10), DateTime.Parse(hoje)).XDeHoje.Should().BeNull();
    }

    [Fact]
    public void SemHoje_SemLinha_EOResto_NaoMuda()
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 10);
        var sem = LayoutDe(cenario, null);
        var com = LayoutDe(cenario, new DateTime(2026, 9, 30));

        sem.XDeHoje.Should().BeNull();
        (com.InicioEixo, com.FimEixo, com.Largura, com.Barras[0].X).Should().Be((sem.InicioEixo, sem.FimEixo, sem.Largura, sem.Barras[0].X),
            "a linha não estica nem desloca o eixo");
    }

    [Fact]
    public void Zoom_AcompanhaALinha()
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 10);

        CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(), new OpcoesGantt(10) { Hoje = new DateTime(2026, 9, 30) })
            .XDeHoje.Should().Be(55);
    }
}
