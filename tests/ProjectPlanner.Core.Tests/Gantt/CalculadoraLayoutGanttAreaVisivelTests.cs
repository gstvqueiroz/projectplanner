using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.Core.Tests.Gantt;

// Projeto começando na seg 28/09/2026: o eixo começa 3 dias antes, na sex 25/09.
public class CalculadoraLayoutGanttAreaVisivelTests
{
    private static readonly DateTime InicioEixo = new(2026, 9, 25);

    private static LayoutGantt Layout(MontadorCronograma cenario, OpcoesGantt opcoes) =>
        CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(), opcoes);

    private static MontadorCronograma Vazio() => new();

    private static MontadorCronograma ComUmaTarefa(int duracao) => new MontadorCronograma().ComTarefa("A", duracao);

    [Fact]
    public void EscalaNaoInformada_E30PixelsPorDia()
    {
        var opcoes = new OpcoesGantt();

        opcoes.PixelsPorDia.Should().Be(30);
        OpcoesGantt.EscalaPadrao.Should().Be(30);
        Layout(ComUmaTarefa(3), opcoes).Barras[0].Should().Match<BarraGantt>(b => b.X == 90 && b.Largura == 90);
    }

    [Theory]
    [InlineData(1200, 30, 40)]
    [InlineData(1210, 30, 41)] // o último dia aparece cortado pela borda
    [InlineData(1200, 4, 300)]
    [InlineData(10, 60, 1)]
    [InlineData(0, 30, 1)]
    public void DiasParaPreencher_CobreALarguraInteira(double largura, double escala, int dias)
    {
        CalculadoraLayoutGantt.DiasParaPreencher(largura, escala).Should().Be(dias);
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(double.NaN, 30)]
    [InlineData(100, 0)]
    public void DiasParaPreencher_RecusaValoresInvalidos(double largura, double escala)
    {
        var acao = () => CalculadoraLayoutGantt.DiasParaPreencher(largura, escala);

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ViewportDe1200A30PxPorDia_GeraPeloMenos40DiasDeGrade()
    {
        var layout = Layout(Vazio(), new OpcoesGantt(30) { LarguraVisivel = 1200 });

        var dias = (layout.FimEixo - layout.InicioEixo).Days + 1;
        dias.Should().BeGreaterThanOrEqualTo(40);
        layout.Largura.Should().BeGreaterThanOrEqualTo(1200);
        layout.Escala.Inferior.Should().HaveCount(dias);
    }

    [Fact]
    public void SemTarefas_OEixoComeca3DiasAntesDoInicioEPreencheAArea()
    {
        var layout = Layout(Vazio(), new OpcoesGantt(30) { LarguraVisivel = 1200, AlturaVisivel = 500 });

        layout.Barras.Should().BeEmpty();
        layout.InicioEixo.Should().Be(InicioEixo);
        layout.FimEixo.Should().Be(new DateTime(2026, 11, 3)); // 40 dias: 25/09 + 39
        layout.Largura.Should().Be(1200);
        layout.Altura.Should().Be(500);
    }

    [Fact]
    public void SemAreaVisivel_OEixoContinuaOMesmoDeAntes()
    {
        // Sem tarefas o fim do projeto é o início (seg 28/09); + 7 dias = seg 05/10; domingo seguinte, 11/10.
        var layout = Layout(Vazio(), new OpcoesGantt(30));

        layout.FimEixo.Should().Be(new DateTime(2026, 10, 11));
        layout.Altura.Should().Be(0);
    }

    [Fact]
    public void TarefasAlemDaArea_OEixoVaiAte7DiasDepoisDoFimArredondadoParaODomingo()
    {
        // 20 dias úteis: seg 28/09 a sex 23/10; + 7 = sex 30/10; domingo seguinte, 01/11.
        var layout = Layout(ComUmaTarefa(20), new OpcoesGantt(30) { LarguraVisivel = 300 });

        layout.FimEixo.Should().Be(new DateTime(2026, 11, 1));
        layout.Largura.Should().Be(38 * 30);
    }

    [Fact]
    public void MuitasLinhas_AAlturaEADasLinhas()
    {
        var cenario = new MontadorCronograma();
        for (var i = 0; i < 30; i++)
            cenario.ComTarefa($"T{i}", 1);

        var layout = Layout(cenario, new OpcoesGantt(30) { AlturaVisivel = 500 });

        layout.Altura.Should().Be(30 * 30);
    }

    [Fact]
    public void AreaVisivelNegativa_ERecusada()
    {
        var largura = () => new OpcoesGantt { LarguraVisivel = -1 };
        var altura = () => new OpcoesGantt { AlturaVisivel = -1 };

        largura.Should().Throw<ArgumentOutOfRangeException>();
        altura.Should().Throw<ArgumentOutOfRangeException>();
    }
}
