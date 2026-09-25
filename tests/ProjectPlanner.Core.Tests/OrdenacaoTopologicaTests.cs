using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Testa direto o método interno, que recebe listas soltas: é o único jeito de montar um grafo com
// ciclo, já que o Cronograma recusa esses vínculos.
public class OrdenacaoTopologicaTests
{
    private static Tarefa NovaTarefa(string nome) => new(nome, Segunda, 1);

    private static Predecessora Vinculo(Tarefa origem, Tarefa destino) => new(origem.Id, destino.Id);

    [Fact]
    public void Ordenar_ColocaCadaTarefaDepoisDasPredecessorasEDesempataPelaOrdemDada()
    {
        // Dadas na ordem D, C, B, A, com A → B, A → C, B → D e C → D. Só A começa liberada; depois
        // dela, B e C ficam livres juntas, e C vem antes por aparecer antes na lista.
        var (a, b, c, d) = (NovaTarefa("A"), NovaTarefa("B"), NovaTarefa("C"), NovaTarefa("D"));

        var ordem = CalculadoraCronograma.OrdenarTopologicamente(
            [d, c, b, a], [Vinculo(a, b), Vinculo(a, c), Vinculo(b, d), Vinculo(c, d)]);

        ordem.Should().Equal(a, c, b, d);
    }

    [Fact]
    public void Ordenar_SemVinculos_MantemAOrdemDada()
    {
        var (a, b, c) = (NovaTarefa("A"), NovaTarefa("B"), NovaTarefa("C"));

        var ordem = CalculadoraCronograma.OrdenarTopologicamente([b, c, a], []);

        ordem.Should().Equal(b, c, a);
    }

    [Fact]
    public void Ordenar_ComCiclo_LancaCircularDependencyExceptionComOCiclo()
    {
        // E depende de C, que está no ciclo A → B → C → A. E vem primeiro na lista, mas não faz
        // parte do ciclo e não deve aparecer nele.
        var (e, a, b, c) = (NovaTarefa("E"), NovaTarefa("A"), NovaTarefa("B"), NovaTarefa("C"));

        var ordenar = () => CalculadoraCronograma.OrdenarTopologicamente(
            [e, a, b, c], [Vinculo(a, b), Vinculo(b, c), Vinculo(c, a), Vinculo(c, e)]);

        ordenar.Should().Throw<CircularDependencyException>()
            .WithMessage("*C → A → B → C*")
            .Which.Ciclo.Should().Equal(c.Id, a.Id, b.Id, c.Id);
    }
}
