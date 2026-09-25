using System.Globalization;
using ProjectPlanner.Core.Gantt;
using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests.Gantt;

public class CalculadoraLayoutGanttBarrasTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static OpcoesGantt Escala(double pixelsPorDia) => new(pixelsPorDia, alturaLinha: 30) { Cultura = PtBr };

    // O cenário de referência do Bloco 3 (CalculadoraCronogramaTests), que começa na seg 28/09/2026.
    // O eixo começa 3 dias antes, na sex 25/09, então X = (dias corridos desde 25/09) × escala.
    // Com 10 px/dia, barras do começo do Início cedo ao fim do Término cedo:
    //
    //   Tarefa  Início cedo       Término cedo      X                 Largura
    //     A     seg 28/09 (+3)    qua 30/09 (+5)    30                (5+1)×10 − 30 = 30
    //     B     qui 01/10 (+6)    ter 06/10 (+11)   60                120 − 60 = 60  (4 dias úteis = 6 corridos)
    //     C     qui 01/10 (+6)    sex 02/10 (+7)    60                80 − 60 = 20
    //     D     qua 07/10 (+12)   ter 13/10 (+18)   120               190 − 120 = 70
    //     E     seg 05/10 (+10)   qua 07/10 (+12)   100               130 − 100 = 30
    //     F     qua 14/10 (+19)   qui 15/10 (+20)   190               210 − 190 = 20
    //
    // Folga (só não críticas), até o fim do Término tarde: C até qui 08/10 (+13) → 140; E até ter 13/10 (+18) → 190.
    // Linhas de 30 com barras de 15 centralizadas: Y = linha × 30 + 7,5.
    // Fim do eixo: domingo pelo menos 7 dias depois do fim (qui 15/10) → dom 25/10 (+30) → largura 310.
    private static MontadorCronograma CenarioDeReferencia() => new MontadorCronograma()
        .ComTarefa("A", 3)
        .ComTarefa("B", 4, "A")
        .ComTarefa("C", 2, "A")
        .ComTarefa("D", 5, "B")
        .ComTarefa("E", 3, "C")
        .ComTarefa("F", 2, "D", "E");

    private static LayoutGantt Calcular(MontadorCronograma cenario, double pixelsPorDia = 10) =>
        CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(), Escala(pixelsPorDia));

    [Fact]
    public void Barras_TemPosicaoELarguraDasDatasCalculadas()
    {
        var layout = Calcular(CenarioDeReferencia());

        layout.Barras.Select(b => (b.Nome, b.Linha, b.X, b.Largura, b.Y, b.Altura)).Should().Equal(
            ("A", 0, 30.0, 30.0, 7.5, 15.0),
            ("B", 1, 60.0, 60.0, 37.5, 15.0),
            ("C", 2, 60.0, 20.0, 67.5, 15.0),
            ("D", 3, 120.0, 70.0, 97.5, 15.0),
            ("E", 4, 100.0, 30.0, 127.5, 15.0),
            ("F", 5, 190.0, 20.0, 157.5, 15.0));
    }

    [Fact]
    public void Barras_MarcamAsCriticasEMostramAFolgaSoDasOutras()
    {
        var layout = Calcular(CenarioDeReferencia());

        layout.Barras.Select(b => (b.Nome, b.EhCritica, b.FimDaFolga)).Should().Equal(
            ("A", true, (double?)null),
            ("B", true, null),
            ("C", false, 140.0),
            ("D", true, null),
            ("E", false, 190.0),
            ("F", true, null));
    }

    [Fact]
    public void Eixo_VaiDeTresDiasAntesDoInicioAoDomingoUmaSemanaDepoisDoFim()
    {
        var layout = Calcular(CenarioDeReferencia());

        layout.InicioEixo.Should().Be(new DateTime(2026, 9, 25));
        layout.FimEixo.Should().Be(new DateTime(2026, 10, 25));
        layout.Largura.Should().Be(310);
        layout.Altura.Should().Be(180);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(50, 15.0)]
    [InlineData(100, 30.0)]
    public void PercentualConcluido_PreencheAParteProporcionalDaBarra(int percentual, double larguraConcluida)
    {
        var cenario = CenarioDeReferencia();
        cenario["A"].AtualizarPercentualConcluido(percentual);

        var barraA = Calcular(cenario).Barras[0];

        barraA.LarguraConcluida.Should().Be(larguraConcluida);
    }

    [Fact]
    public void Linhas_SeguemAOrdemDoCronogramaENaoADataDeInicio()
    {
        // E (linha 4) começa antes de D (linha 3), mas continua abaixo dela, como na grid.
        var layout = Calcular(CenarioDeReferencia());

        layout.Barras.Select(b => b.Nome).Should().Equal("A", "B", "C", "D", "E", "F");
        layout.Barras[4].X.Should().BeLessThan(layout.Barras[3].X);
    }

    [Fact]
    public void Zoom_EscalaPosicoesELargurasNaMesmaProporcaoEMantemAsAlturas()
    {
        var com10 = Calcular(CenarioDeReferencia(), pixelsPorDia: 10);
        var com25 = Calcular(CenarioDeReferencia(), pixelsPorDia: 25);

        com25.Barras.Select(b => (b.X, b.Largura, b.FimDaFolga, b.Y, b.Altura)).Should().Equal(
            com10.Barras.Select(b => (b.X * 2.5, b.Largura * 2.5, b.FimDaFolga * 2.5, b.Y, b.Altura)));
        com25.Setas.Select(s => (s.Saida, s.Chegada)).Should().Equal(
            com10.Setas.Select(s => (new PontoGantt(s.Saida.X * 2.5, s.Saida.Y), new PontoGantt(s.Chegada.X * 2.5, s.Chegada.Y))));
        com25.Largura.Should().Be(com10.Largura * 2.5);
        com25.Altura.Should().Be(com10.Altura);
    }

    [Fact]
    public void CronogramaVazio_TemEixoMasNenhumaBarra()
    {
        // Sem tarefas, o fim do projeto é o próprio início (seg 28/09): o eixo vai de sex 25/09 a dom 11/10.
        var cronograma = new Cronograma("Vazio", Segunda);

        var layout = CalculadoraLayoutGantt.Calcular(cronograma, CalculadoraCronograma.Calcular(cronograma), Escala(10));

        layout.Barras.Should().BeEmpty();
        layout.Setas.Should().BeEmpty();
        (layout.InicioEixo, layout.FimEixo, layout.Largura).Should().Be((new DateTime(2026, 9, 25), new DateTime(2026, 10, 11), 170.0));
    }

    [Fact]
    public void CronogramaNaoCalculado_LancaExcecao()
    {
        var cenario = CenarioDeReferencia();
        var resultado = cenario.Calcular();
        cenario.ComTarefa("Nova", 1);

        var calcular = () => CalculadoraLayoutGantt.Calcular(cenario.Cronograma, resultado, Escala(10));

        calcular.Should().Throw<InvalidOperationException>().WithMessage("Calcule o cronograma*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void Escala_ZeroNegativaOuInvalida_LancaExcecao(double pixelsPorDia)
    {
        var criar = () => new OpcoesGantt(pixelsPorDia);

        criar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ProjetoNoPrimeiroDiaDoCalendario_NaoEstouraOEixo()
    {
        // 01/01/0001 é uma segunda; não há 3 dias antes dela, então o eixo começa nela mesma.
        var cronograma = new Cronograma("Antigo", DateTime.MinValue);
        cronograma.AdicionarTarefa(new Tarefa("A", DateTime.MinValue, 2));

        var layout = CalculadoraLayoutGantt.Calcular(cronograma, CalculadoraCronograma.Calcular(cronograma), Escala(10));

        layout.InicioEixo.Should().Be(DateTime.MinValue);
        layout.Barras[0].X.Should().Be(0);
        layout.Barras[0].Largura.Should().Be(20);
    }
}
