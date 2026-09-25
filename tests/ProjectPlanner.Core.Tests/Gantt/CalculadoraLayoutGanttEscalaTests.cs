using System.Globalization;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.Core.Tests.Gantt;

// Mesmo eixo do cenário de referência: de sex 25/09/2026 a dom 25/10/2026 (31 dias).
public class CalculadoraLayoutGanttEscalaTests
{
    private static EscalaGantt EscalaCom(double pixelsPorDia)
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 3).ComTarefa("B", 4, "A").ComTarefa("C", 2, "A")
            .ComTarefa("D", 5, "B").ComTarefa("E", 3, "C").ComTarefa("F", 2, "D", "E");
        var opcoes = new OpcoesGantt(pixelsPorDia) { Cultura = CultureInfo.GetCultureInfo("pt-BR") };
        return CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(), opcoes).Escala;
    }

    [Fact]
    public void ZoomAlto_MostraSemanasEDias()
    {
        var escala = EscalaCom(20);

        escala.UnidadeSuperior.Should().Be(UnidadeEscala.Semana);
        escala.UnidadeInferior.Should().Be(UnidadeEscala.Dia);
        escala.Inferior.Should().HaveCount(31);
        escala.Inferior[0].Should().Be(new MarcaEscala(new DateTime(2026, 9, 25), 0, 20, "25"));
        escala.Inferior[^1].Should().Be(new MarcaEscala(new DateTime(2026, 10, 25), 600, 20, "25"));
        // A primeira semana (seg 21/09) aparece só a partir do início do eixo: sex a dom, 3 dias.
        escala.Superior.Select(m => (m.X, m.Largura, m.Rotulo)).Should().Equal(
            (0.0, 60.0, "21/09/2026"),
            (60.0, 140.0, "28/09/2026"),
            (200.0, 140.0, "05/10/2026"),
            (340.0, 140.0, "12/10/2026"),
            (480.0, 140.0, "19/10/2026"));
    }

    [Fact]
    public void ZoomMedio_MostraMesesESemanas()
    {
        var escala = EscalaCom(10);

        escala.UnidadeSuperior.Should().Be(UnidadeEscala.Mes);
        escala.UnidadeInferior.Should().Be(UnidadeEscala.Semana);
        escala.Superior.Select(m => (m.X, m.Largura, m.Rotulo)).Should().Equal(
            (0.0, 60.0, "setembro 2026"),
            (60.0, 250.0, "outubro 2026"));
        escala.Inferior.Select(m => (m.X, m.Largura, m.Rotulo)).Should().Equal(
            (0.0, 30.0, "21/09"),
            (30.0, 70.0, "28/09"),
            (100.0, 70.0, "05/10"),
            (170.0, 70.0, "12/10"),
            (240.0, 70.0, "19/10"));
    }

    [Fact]
    public void ZoomBaixo_MostraAnosEMeses()
    {
        var escala = EscalaCom(4);

        escala.UnidadeSuperior.Should().Be(UnidadeEscala.Ano);
        escala.UnidadeInferior.Should().Be(UnidadeEscala.Mes);
        escala.Superior.Select(m => (m.X, m.Largura, m.Rotulo)).Should().Equal((0.0, 124.0, "2026"));
        escala.Inferior.Select(m => (m.X, m.Largura, m.Rotulo)).Should().Equal(
            (0.0, 24.0, "set."),
            (24.0, 100.0, "out."));
    }

    [Theory]
    [InlineData(60, UnidadeEscala.Dia)]
    [InlineData(18, UnidadeEscala.Dia)]
    [InlineData(17.9, UnidadeEscala.Semana)]
    [InlineData(6, UnidadeEscala.Semana)]
    [InlineData(5.9, UnidadeEscala.Mes)]
    [InlineData(4, UnidadeEscala.Mes)]
    public void Limiares_DecidemAsUnidadesDoCabecalho(double pixelsPorDia, UnidadeEscala inferior)
    {
        EscalaCom(pixelsPorDia).UnidadeInferior.Should().Be(inferior);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(10)]
    [InlineData(4)]
    public void Marcas_CobremOEixoInteiroSemBuracos(double pixelsPorDia)
    {
        var escala = EscalaCom(pixelsPorDia);

        foreach (var faixa in new[] { escala.Superior, escala.Inferior })
        {
            faixa[0].X.Should().Be(0);
            faixa.Zip(faixa.Skip(1)).Where(par => par.First.X + par.First.Largura != par.Second.X).Should().BeEmpty();
            (faixa[^1].X + faixa[^1].Largura).Should().Be(31 * pixelsPorDia);
        }
    }
}
