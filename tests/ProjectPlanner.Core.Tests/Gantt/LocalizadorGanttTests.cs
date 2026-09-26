using ProjectPlanner.Core.Gantt;
using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests.Gantt;

// Projeto começando na segunda 28/09/2026, a 20 px/dia, linhas de 30 px e barras de 15 px (Y = 7,5):
//   linha 0: A, 3 dias (seg–qua)        X 60 a 120
//   linha 1: Estreita, 1 dia (seg)      X 60 a 80  (a 10 px/dia teria 10 px e não teria meio)
//   linha 2: M, marco no fim de quarta  losango de X 112,5 a 127,5 (centro 120)
//   linha 3: Fase, resumo de uma tarefa de 5 dias (linha 4)   X 60 a 160
public class LocalizadorGanttTests
{
    private readonly LayoutGantt _layout;
    private readonly LayoutGantt _layoutComZoomBaixo;

    public LocalizadorGanttTests()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        Tarefa Nova(string nome, int duracao, Tarefa? pai = null)
        {
            var tarefa = new Tarefa(nome, Segunda, duracao);
            projeto.AdicionarTarefa(tarefa, pai?.Id);
            return tarefa;
        }
        var a = Nova("A", 3);
        Nova("Estreita", 1);
        var marco = Nova("M", 0);
        var fase = Nova("Fase", 1);
        Nova("B", 5, fase);
        projeto.AdicionarPredecessora(new Predecessora(a.Id, marco.Id));
        var resultado = CalculadoraCronograma.Calcular(projeto);
        _layout = CalculadoraLayoutGantt.Calcular(projeto, resultado, new OpcoesGantt(20));
        // A 10 px/dia, a Estreita vai de X 30 a 40: com menos de 16 px, as metades são as alças.
        _layoutComZoomBaixo = CalculadoraLayoutGantt.Calcular(projeto, resultado, new OpcoesGantt(10));
    }

    private static (string? Nome, BordaDaBarra? Borda) Resumir(AlcaGantt? alca) => (alca?.Barra.Nome, alca?.Borda);

    private static (string? Nome, BordaDaBarra? Borda) Resumir(DestinoGantt? destino) => (destino?.Barra.Nome, destino?.Borda);

    // --- Alças (onde o arraste começa) ---

    [Theory]
    [InlineData(56, 15, "A", BordaDaBarra.Inicio)]   // 4 px antes do início
    [InlineData(60, 15, "A", BordaDaBarra.Inicio)]
    [InlineData(68, 15, "A", BordaDaBarra.Inicio)]   // 8 px para dentro
    [InlineData(112, 15, "A", BordaDaBarra.Termino)]
    [InlineData(124, 15, "A", BordaDaBarra.Termino)] // 4 px depois do término
    [InlineData(64, 4.5, "A", BordaDaBarra.Inicio)]  // 3 px acima da barra
    [InlineData(116, 25.5, "A", BordaDaBarra.Termino)] // 3 px abaixo
    public void AlcaEm_NasPontasDaBarra(double x, double y, string nome, BordaDaBarra borda)
    {
        Resumir(LocalizadorGantt.AlcaEm(_layout, x, y)).Should().Be((nome, borda));
    }

    [Theory]
    [InlineData(90, 15)]    // meio da barra: seleciona, não liga
    [InlineData(55, 15)]    // mais de 4 px antes
    [InlineData(125, 15)]   // mais de 4 px depois
    [InlineData(64, 4)]     // mais de 3 px acima
    [InlineData(300, 15)]   // longe de tudo
    public void AlcaEm_ForaDasPontas_Nenhuma(double x, double y)
    {
        LocalizadorGantt.AlcaEm(_layout, x, y).Should().BeNull();
    }

    [Theory]
    [InlineData(26, BordaDaBarra.Inicio)]   // 4 px antes
    [InlineData(34, BordaDaBarra.Inicio)]
    [InlineData(35, BordaDaBarra.Termino)]  // da metade em diante
    [InlineData(44, BordaDaBarra.Termino)]  // 4 px depois
    public void AlcaEm_BarraEstreita_SemMeio_PelasMetades(double x, BordaDaBarra borda)
    {
        Resumir(LocalizadorGantt.AlcaEm(_layoutComZoomBaixo, x, 45)).Should().Be(("Estreita", borda));
    }

    [Theory]
    [InlineData(66, BordaDaBarra.Inicio)]
    [InlineData(70, null)]                  // 20 px já têm meio: 68 a 72
    [InlineData(74, BordaDaBarra.Termino)]
    public void AlcaEm_BarraDeUmDiaComZoomMaior_TemMeio(double x, BordaDaBarra? borda)
    {
        (LocalizadorGantt.AlcaEm(_layout, x, 45)?.Borda).Should().Be(borda);
    }

    [Theory]
    [InlineData(115, BordaDaBarra.Inicio)]
    [InlineData(125, BordaDaBarra.Termino)]
    public void AlcaEm_Marco_PelasMetadesDoLosango(double x, BordaDaBarra borda)
    {
        var alca = LocalizadorGantt.AlcaEm(_layout, x, 75);

        Resumir(alca).Should().Be(("M", borda));
        alca!.Ponto.X.Should().Be(borda == BordaDaBarra.Inicio ? 112.5 : 127.5);
    }

    [Fact]
    public void AlcaEm_Resumo_Nenhuma()
    {
        LocalizadorGantt.AlcaEm(_layout, 62, 105).Should().BeNull();
        LocalizadorGantt.AlcaEm(_layout, 158, 105).Should().BeNull();
    }

    [Fact]
    public void AlcaEm_OPontoEAPontaDaBarraNaAlturaDoCentro()
    {
        LocalizadorGantt.AlcaEm(_layout, 118, 15)!.Ponto.Should().Be(new PontoGantt(120, 15));
    }

    // --- Barra sob o mouse (clique que seleciona) ---

    [Fact]
    public void BarraEm_DentroDaBarraOuDoResumo()
    {
        LocalizadorGantt.BarraEm(_layout, 90, 15)!.Nome.Should().Be("A");
        LocalizadorGantt.BarraEm(_layout, 100, 100)!.Nome.Should().Be("Fase");
        LocalizadorGantt.BarraEm(_layout, 90, 3).Should().BeNull();
        LocalizadorGantt.BarraEm(_layout, 130, 15).Should().BeNull();
    }

    // --- Destino (onde o arraste é solto) ---

    [Theory]
    [InlineData(90, 1, BordaDaBarra.Inicio)]      // topo da linha, fora da barra: vale a faixa da linha
    [InlineData(90, 29, BordaDaBarra.Inicio)]     // pé da linha
    [InlineData(57, 15, BordaDaBarra.Inicio)]     // 3 px antes do início
    [InlineData(111, 15, BordaDaBarra.Inicio)]    // ainda no corpo
    [InlineData(112, 15, BordaDaBarra.Termino)]   // alça do término
    [InlineData(123, 15, BordaDaBarra.Termino)]   // 3 px depois do término
    public void DestinoEm_CorpoValeInicio_AlcaDoTerminoValeTermino(double x, double y, BordaDaBarra borda)
    {
        Resumir(LocalizadorGantt.DestinoEm(_layout, x, y)).Should().Be(("A", borda));
    }

    [Theory]
    [InlineData(55, 15)]   // antes da tolerância
    [InlineData(125, 15)]  // depois
    [InlineData(90, 160)]  // abaixo da última linha
    [InlineData(90, -1)]   // acima da primeira
    public void DestinoEm_ForaDeQualquerBarra_Nenhum(double x, double y)
    {
        LocalizadorGantt.DestinoEm(_layout, x, y).Should().BeNull();
    }

    [Theory]
    [InlineData(115, "M", BordaDaBarra.Inicio)]
    [InlineData(125, "M", BordaDaBarra.Termino)]
    public void DestinoEm_Marco_PelasMetades(double x, string nome, BordaDaBarra borda)
    {
        Resumir(LocalizadorGantt.DestinoEm(_layout, x, 75)).Should().Be((nome, borda));
    }

    [Theory]
    [InlineData(33, BordaDaBarra.Inicio)]
    [InlineData(37, BordaDaBarra.Termino)]
    public void DestinoEm_BarraEstreita_PelasMetades(double x, BordaDaBarra borda)
    {
        Resumir(LocalizadorGantt.DestinoEm(_layoutComZoomBaixo, x, 45)).Should().Be(("Estreita", borda));
    }

    [Fact]
    public void DestinoEm_Resumo_AparecePraSerRecusado()
    {
        var destino = LocalizadorGantt.DestinoEm(_layout, 158, 105);

        (destino!.Barra.Nome, destino.Barra.Tipo).Should().Be(("Fase", TipoTarefa.Resumo));
    }

    [Fact]
    public void Layout_SabeAAlturaDaLinha()
    {
        _layout.AlturaLinha.Should().Be(30);
    }
}
