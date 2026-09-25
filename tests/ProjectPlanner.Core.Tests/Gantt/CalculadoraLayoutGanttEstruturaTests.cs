using ProjectPlanner.Core.Gantt;
using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests.Gantt;

// Projeto começando na segunda 28/09/2026, a 20 px/dia: o eixo começa na sexta 25/09, então o começo
// de 28/09 fica em X = 60 e o fim de sexta 02/10 em X = 160. A barra tem 15 px de altura, e o losango
// do marco, 15 × 15 centrado no instante dele.
public class CalculadoraLayoutGanttEstruturaTests
{
    private readonly Cronograma _projeto = new("Projeto", Segunda);

    private Tarefa Nova(string nome, int duracao = 1, Tarefa? pai = null)
    {
        var tarefa = new Tarefa(nome, Segunda, duracao);
        _projeto.AdicionarTarefa(tarefa, pai?.Id);
        return tarefa;
    }

    private void Ligar(Tarefa de, Tarefa para) => _projeto.AdicionarPredecessora(new Predecessora(de.Id, para.Id));

    private LayoutGantt Layout(IReadOnlySet<Guid>? ocultas = null) =>
        CalculadoraLayoutGantt.Calcular(_projeto, CalculadoraCronograma.Calcular(_projeto),
            new OpcoesGantt(20) { TarefasOcultas = ocultas ?? new HashSet<Guid>() });

    [Fact]
    public void MarcoNoFimDoDia_LosangoCentradoNaBordaDireitaDoDia()
    {
        var a = Nova("A", 5);
        var marco = Nova("M", 0);
        Ligar(a, marco);

        var layout = Layout();

        var barra = layout.Barras[1];
        (barra.Tipo, barra.X, barra.Largura, barra.Linha, barra.LarguraConcluida).Should().Be((TipoTarefa.Marco, 152.5, 15.0, 1, 0.0));
        layout.Setas.Should().ContainSingle().Which.Chegada.X.Should().Be(152.5);
    }

    [Fact]
    public void MarcoNoComecoDoDia_LosangoCentradoNaBordaEsquerdaDoDia()
    {
        Nova("Início", 0);

        Layout().Barras[0].X.Should().Be(52.5);
    }

    [Fact]
    public void MarcoComFolga_AFolgaVaiAteOInstanteTardeDele()
    {
        Nova("A", 5);
        Nova("M", 0);

        var barra = Layout().Barras[1];

        barra.FimDaFolga.Should().Be(160);
    }

    [Fact]
    public void Resumo_VaiDePontaAPontaDasSubtarefasSemFolga()
    {
        var resumo = Nova("Fase");
        var a = Nova("A", 3, resumo);
        var b = Nova("B", 2, resumo);
        Ligar(a, b);

        var layout = Layout();

        layout.Barras.Select(x => (x.Nome, x.Tipo, x.Linha)).Should().Equal(
            ("Fase", TipoTarefa.Resumo, 0), ("A", TipoTarefa.Normal, 1), ("B", TipoTarefa.Normal, 2));
        var barra = layout.Barras[0];
        (barra.X, barra.Largura, barra.FimDaFolga, barra.LarguraConcluida).Should().Be((60.0, 100.0, (double?)null, 0.0));
    }

    [Fact]
    public void TarefasOcultas_NaoGanhamLinha_NemSetas_EOResumoContinuaCobrindoTodas()
    {
        var resumo = Nova("Fase");
        var a = Nova("A", 3, resumo);
        var b = Nova("B", 2, resumo);
        var x = Nova("X", 1);
        Ligar(a, b);
        Ligar(a, x);

        var layout = Layout(new HashSet<Guid> { a.Id, b.Id });

        layout.Barras.Select(barra => (barra.Nome, barra.Linha)).Should().Equal(("Fase", 0), ("X", 1));
        (layout.Barras[0].X, layout.Barras[0].Largura).Should().Be((60.0, 100.0));
        layout.Setas.Should().BeEmpty();
        layout.Altura.Should().Be(2 * 30);
    }
}
