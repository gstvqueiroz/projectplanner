using ProjectPlanner.Core.Gantt;
using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Bloco 20: pistas vazias no Gantt até o fim da altura visível, com a mesma conta das linhas vazias da grid
// (Bloco 16). A conta é uma só (PreenchimentoDaGrade.LinhasVazias), usada pelos dois painéis.
public class PistasVaziasDoGanttTests
{
    private static LayoutGantt Layout(int tarefas, double alturaVisivel, double pixelsPorDia = 30, int ocultas = 0)
    {
        var projeto = new Cronograma("Obra", Segunda);
        var lista = Enumerable.Range(1, tarefas).Select(i => new Tarefa($"T{i}", Segunda, 1)).ToList();
        foreach (var tarefa in lista)
            projeto.AdicionarTarefa(tarefa);
        var resultado = CalculadoraCronograma.Calcular(projeto);
        return CalculadoraLayoutGantt.Calcular(projeto, resultado, new OpcoesGantt(pixelsPorDia, 30)
        {
            AlturaVisivel = alturaVisivel,
            LarguraVisivel = 600,
            TarefasOcultas = lista.Take(ocultas).Select(t => t.Id).ToHashSet(),
        });
    }

    [Theory]
    [InlineData(0, 300, 10)]   // sem tarefas: a altura toda
    [InlineData(4, 300, 6)]    // poucas tarefas: o que falta
    [InlineData(4, 310, 7)]    // a última pista aparece em parte, mas conta
    [InlineData(10, 300, 0)]   // as tarefas cobrem exatamente
    [InlineData(40, 300, 0)]   // mais tarefas que a altura: o Gantt rola
    [InlineData(3, 0, 0)]      // sem altura visível (antes do primeiro desenho)
    public void PistasVazias_PreenchemAAlturaVisivel(int tarefas, double altura, int esperado)
    {
        var layout = Layout(tarefas, altura);

        layout.PistasVazias.Should().Be(esperado);
        layout.Pistas.Should().Be(tarefas + esperado);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(4, 310)]
    [InlineData(7, 450.5)]
    [InlineData(25, 300)]
    public void PistasVazias_MesmaContaDasLinhasVaziasDaGrid(int tarefas, double altura)
    {
        Layout(tarefas, altura).PistasVazias.Should().Be(PreenchimentoDaGrade.LinhasVazias(tarefas, altura, 30));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(60)]
    public void Zoom_NaoMudaAsPistas_SoALargura(double pixelsPorDia)
    {
        var padrao = Layout(4, 300);
        var comZoom = Layout(4, 300, pixelsPorDia);

        (comZoom.PistasVazias, comZoom.Altura).Should().Be((padrao.PistasVazias, padrao.Altura));
    }

    [Fact]
    public void TarefasOcultas_DeixamMaisPistasVazias_ComoNaGrid()
    {
        Layout(5, 300, ocultas: 2).PistasVazias.Should().Be(PreenchimentoDaGrade.LinhasVazias(3, 300, 30)).And.Be(7);
    }

    [Fact]
    public void PistasVazias_NaoSaoClicaveis_NemDestinoDeVinculo()
    {
        var layout = Layout(2, 300);
        var yDaPistaVazia = 5 * 30 + 15;

        LocalizadorGantt.BarraEm(layout, 100, yDaPistaVazia).Should().BeNull();
        LocalizadorGantt.AlcaEm(layout, 100, yDaPistaVazia).Should().BeNull();
        LocalizadorGantt.DestinoEm(layout, 100, yDaPistaVazia).Should().BeNull();
        LocalizadorGantt.IconeDeFotosEm(layout, 100, yDaPistaVazia).Should().BeNull();
    }

    [Fact]
    public void AlturaDoCorpo_ContinuaAMesma_DoBloco8()
    {
        (Layout(4, 300).Altura, Layout(40, 300).Altura).Should().Be((300, 1200));
    }
}
