namespace ProjectPlanner.Core.Tests;

public class TarefaNotasTests
{
    private static Tarefa NovaTarefa() => new("Fundação", Datas.Segunda, 5);

    [Fact]
    public void TarefaNova_NaoTemNotas()
    {
        NovaTarefa().Notas.Should().BeNull();
    }

    [Fact]
    public void AlterarNotas_GuardaOTextoExatamenteComoVeio()
    {
        var tarefa = NovaTarefa();
        const string texto = "  Concreto fck 30 MPa.\nConferir com a obra.  ";

        tarefa.AlterarNotas(texto);

        tarefa.Notas.Should().Be(texto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void AlterarNotas_VazioOuSoEspacos_ApagaAsNotas(string? vazio)
    {
        var tarefa = NovaTarefa();
        tarefa.AlterarNotas("Algo");

        tarefa.AlterarNotas(vazio);

        tarefa.Notas.Should().BeNull();
    }

    [Fact]
    public void AlterarNotas_TextoLongo_EAceito()
    {
        var tarefa = NovaTarefa();
        var longo = new string('x', 100_000);

        tarefa.AlterarNotas(longo);

        tarefa.Notas.Should().HaveLength(100_000);
    }

    [Fact]
    public void AlterarNotas_NaoMexeNoRestoDaTarefa()
    {
        var tarefa = NovaTarefa();
        var (inicio, termino, duracao) = (tarefa.DataInicio, tarefa.DataTermino, tarefa.Duracao);

        tarefa.AlterarNotas("Nota");

        (tarefa.Nome, tarefa.DataInicio, tarefa.DataTermino, tarefa.Duracao)
            .Should().Be(("Fundação", inicio, termino, duracao));
    }
}
