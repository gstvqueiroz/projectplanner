using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;

namespace ProjectPlanner.Data.Tests;

// Bloco 84: o executado acumulado das tarefas e a produção do dia no RDO, no .pplan.
public sealed class ProducaoNoArquivoTests : TesteComArquivo
{
    private static (Cronograma C, Tarefa Reboco) Obra()
    {
        var c = new Cronograma("Obra", Segunda);
        var reboco = new Tarefa("Reboco", Segunda, 10);
        c.AdicionarTarefa(reboco);
        reboco.DefinirUnidade("m²");
        reboco.DefinirQuantidade(200);
        reboco.ApontarProducao(81.25m);
        return (c, reboco);
    }

    [Fact]
    public void Executado_VoltaIgual()
    {
        var (c, _) = Obra();
        Repositorio.Salvar(c, Caminho);

        var lida = Repositorio.Abrir(Caminho).Tarefas.Single();

        (lida.QuantidadeExecutada, lida.PercentualConcluido, lida.Quantidade).Should().Be(((decimal?)81.25m, 41, (decimal?)200m));
    }

    [Fact]
    public void UmPercentualGravadoQueNaoBateComOExecutado_Vale_EOExecutadoSai()
    {
        var (c, _) = Obra();
        Repositorio.Salvar(c, Caminho);
        Executar("UPDATE Tarefas SET PercentualConcluido = 50");

        var lida = Repositorio.Abrir(Caminho).Tarefas.Single();

        (lida.QuantidadeExecutada, lida.PercentualConcluido, lida.ExecutadoAtual).Should().Be(((decimal?)null, 50, (decimal?)100m));
    }

    [Fact]
    public void ProducaoDoDia_NoRdo_VoltaIgual()
    {
        var (c, reboco) = Obra();
        c.Diario.Abrir(new DiaDaObra(DateOnly.FromDateTime(Segunda))
        {
            Atividades = [new AtividadeNoDia(reboco.Id, "1 Reboco", 41, 20.5m, "m²"), new AtividadeNoDia(null, "Limpeza do canteiro")],
        });
        Repositorio.Salvar(c, Caminho);

        var atividades = Repositorio.Abrir(Caminho).Diario.Dias.Single().Atividades;

        atividades.Should().Equal(new AtividadeNoDia(reboco.Id, "1 Reboco", 41, 20.5m, "m²"), new AtividadeNoDia(null, "Limpeza do canteiro"));
        Versao("DiarioDaObra").Should().Be(1, "os campos novos são opcionais: o formato continua o 1");
    }
}
