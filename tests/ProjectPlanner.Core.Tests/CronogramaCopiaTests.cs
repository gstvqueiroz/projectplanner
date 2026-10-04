using ProjectPlanner.Core;

namespace ProjectPlanner.Core.Tests;

// Bloco 24: a cópia independente do cronograma (as alterações do assistente são feitas numa cópia).
public class CronogramaCopiaTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    // Obra > Fundação (resumo) > Escavar (2 d, 100%, nota, foto), Armar (3 d, pred Escavar TI+1); Entrega (marco, pred Armar TT).
    private static (Cronograma Cronograma, Tarefa Fundacao, Tarefa Escavar, Tarefa Armar, Tarefa Entrega) Obra()
    {
        var cronograma = new Cronograma("Obra", Seg);
        var fundacao = new Tarefa("Fundação", Seg, 1);
        var escavar = new Tarefa("Escavar", Seg, 2);
        var armar = new Tarefa("Armar", Seg, 3);
        var entrega = new Tarefa("Entrega", Seg, 0);
        cronograma.AdicionarTarefa(fundacao);
        cronograma.AdicionarTarefa(escavar, fundacao.Id);
        cronograma.AdicionarTarefa(armar, fundacao.Id);
        cronograma.AdicionarTarefa(entrega);
        escavar.AtualizarPercentualConcluido(100);
        escavar.AlterarNotas("Solo argiloso");
        escavar.AdicionarFoto(FotosDeTeste.Nova(escavar.Id, Seg.AddHours(9), "Vala"));
        cronograma.AdicionarPredecessora(new Predecessora(escavar.Id, armar.Id, TipoDependencia.FS, 1));
        cronograma.AdicionarPredecessora(new Predecessora(armar.Id, entrega.Id, TipoDependencia.FF));
        CalculadoraCronograma.Calcular(cronograma);
        return (cronograma, fundacao, escavar, armar, entrega);
    }

    [Fact]
    public void Copiar_MesmosDados_EmObjetosNovos()
    {
        var (original, _, escavar, _, _) = Obra();

        var copia = original.Copiar();

        (copia.Id, copia.Nome, copia.DataInicio).Should().Be((original.Id, original.Nome, original.DataInicio));
        copia.Tarefas.Select(t => (t.Id, t.Nome, t.Duracao, t.TarefaPaiId, t.EhResumo, t.PercentualConcluido, t.Notas, t.InicioMaisCedo, t.Folga))
            .Should().Equal(original.Tarefas.Select(t => (t.Id, t.Nome, t.Duracao, t.TarefaPaiId, t.EhResumo, t.PercentualConcluido, t.Notas, t.InicioMaisCedo, t.Folga)));
        copia.Tarefas.Should().OnlyContain(t => !original.Tarefas.Contains(t), "as tarefas são outros objetos");
        copia.Predecessoras.Should().Equal(original.Predecessoras);
        copia.Tarefas.Single(t => t.Id == escavar.Id).Fotos.Should().Equal(escavar.Fotos);
        copia.NumeracaoEdt().Should().Equal(original.NumeracaoEdt());
    }

    [Fact]
    public void Copiar_MudarACopia_NaoMudaOOriginal()
    {
        var (original, fundacao, escavar, armar, entrega) = Obra();
        var copia = original.Copiar();

        copia.Tarefas.Single(t => t.Id == armar.Id).AlterarDuracao(10);
        copia.Tarefas.Single(t => t.Id == escavar.Id).Renomear("Escavação");
        copia.RemoverTarefa(entrega.Id);
        copia.RemoverPredecessora(escavar.Id, armar.Id);
        copia.AdicionarTarefa(new Tarefa("Nova", Seg, 1), fundacao.Id);
        copia.Renomear("Outra obra");
        copia.Tarefas.Single(t => t.Id == escavar.Id).AdicionarFoto(FotosDeTeste.Nova(escavar.Id, Seg, "Outra"));

        (original.Nome, original.Tarefas.Count, original.Predecessoras.Count).Should().Be(("Obra", 4, 2));
        (armar.Duracao, escavar.Nome, escavar.Fotos.Count).Should().Be((3, "Escavar", 1));
        original.FilhasDe(fundacao.Id).Select(t => t.Nome).Should().Equal("Escavar", "Armar");
    }

    [Fact]
    public void Copiar_ACopiaSegueAsRegrasDoDominio()
    {
        var (original, fundacao, escavar, armar, _) = Obra();
        var copia = original.Copiar();

        var ciclo = () => copia.AdicionarPredecessora(new Predecessora(armar.Id, escavar.Id));
        var resumoComVinculo = () => copia.AdicionarPredecessora(new Predecessora(fundacao.Id, armar.Id));
        var duracaoDoResumo = () => copia.Tarefas.Single(t => t.Id == fundacao.Id).AlterarDuracao(4);

        ciclo.Should().Throw<CircularDependencyException>();
        resumoComVinculo.Should().Throw<DomainValidationException>();
        duracaoDoResumo.Should().Throw<DomainValidationException>();
        CalculadoraCronograma.Calcular(copia).DataTermino.Should().Be(CalculadoraCronograma.Calcular(original).DataTermino);
    }

    [Fact]
    public void Copiar_ProjetoVazio()
    {
        var vazio = new Cronograma("Novo", Seg);

        var copia = vazio.Copiar();

        (copia.Id, copia.Tarefas.Count, copia.Predecessoras.Count, copia.FotosDescartadasAoAbrir).Should().Be((vazio.Id, 0, 0, 0));
    }
}
