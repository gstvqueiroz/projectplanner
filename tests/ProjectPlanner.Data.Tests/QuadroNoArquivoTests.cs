using Microsoft.Data.Sqlite;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.Data.Tests;

// Bloco 75: o quadro Kanban no .pplan (o plano "Delete, arrastar e Kanban", K7): volta igual (colunas, etiquetas, raias e os
// cards com a coluna, a ordem, o % de antes, o arquivado, as etiquetas e o checklist); um projeto sem tarefas não grava linha,
// e um arquivo de antes do quadro abre com as colunas padrão, distribuindo os cards pelo %.
public sealed class QuadroNoArquivoTests : TesteComArquivo
{
    private static Cronograma ComTresTarefas(out Tarefa a, out Tarefa b, out Tarefa c)
    {
        var cronograma = new Cronograma("Obra", Segunda);
        a = new Tarefa("Escavação", Segunda, 3);
        b = new Tarefa("Sapatas", Segunda, 4);
        c = new Tarefa("Alvenaria", Segunda, 5);
        cronograma.AdicionarTarefa(a);
        cronograma.AdicionarTarefa(b);
        cronograma.AdicionarTarefa(c);
        b.AtualizarPercentualConcluido(40);
        c.AtualizarPercentualConcluido(100);
        return cronograma;
    }

    [Fact]
    public void Quadro_VoltaIgual()
    {
        var cronograma = ComTresTarefas(out var a, out var b, out var c);
        var etiqueta = new EtiquetaDoQuadro(Guid.NewGuid(), "Urgente", "#E5484D");
        var colunas = QuadroDoProjeto.ColunasPadrao().Select((k, i) => i == 1 ? k with { Nome = "Fazendo", Limite = 3, Cor = "#123456" } : k).ToList();
        cronograma.Quadro.Restaurar(colunas, [etiqueta], [
            new CardDoQuadro(a.Id, colunas[0].Id, 0) { Checklist = [new ItemDoChecklist("Locar o gabarito", true), new ItemDoChecklist("Escavar", false)] },
            new CardDoQuadro(b.Id, colunas[2].Id, 0) { PercentualAnterior = 40, Etiquetas = [etiqueta.Id] },
            new CardDoQuadro(c.Id, colunas[3].Id, 0) { Arquivado = true, PercentualAnterior = 70 },
        ], ModoDasRaias.PorResponsavel);
        Repositorio.Salvar(cronograma, Caminho);

        var aberto = Repositorio.Abrir(Caminho).Quadro;

        aberto.Colunas.Should().Equal(colunas);
        aberto.Etiquetas.Should().Equal(etiqueta);
        aberto.Raias.Should().Be(ModoDasRaias.PorResponsavel);
        aberto.CardDe(a.Id)!.Checklist.Should().Equal(new ItemDoChecklist("Locar o gabarito", true), new ItemDoChecklist("Escavar", false));
        aberto.CardDe(b.Id).Should().Match<CardDoQuadro>(k => k.ColunaId == colunas[2].Id && k.PercentualAnterior == 40 && k.Etiquetas.Single() == etiqueta.Id);
        aberto.CardDe(c.Id).Should().Match<CardDoQuadro>(k => k.Arquivado && k.PercentualAnterior == 70);
        Versao("QuadroDoProjeto").Should().Be(1);
    }

    [Fact]
    public void SemTarefas_NaoGravaLinha_EUmArquivoSemOQuadroAbreComAsColunasPadrao_DistribuindoPeloPercentual()
    {
        Repositorio.Salvar(new Cronograma("Vazio", Segunda), Caminho);
        Contar("QuadroDoProjeto").Should().Be(0);

        var cronograma = ComTresTarefas(out var a, out var b, out var c);
        cronograma.Quadro.Sincronizar();
        Repositorio.Salvar(cronograma, Caminho);
        Contar("QuadroDoProjeto").Should().Be(1);
        Executar("DELETE FROM QuadroDoProjeto"); // como um arquivo de antes do quadro

        var quadro = Repositorio.Abrir(Caminho).Quadro;

        quadro.Colunas.Select(k => k.Nome).Should().Equal("A fazer", "Em andamento", "Impedida", "Concluída");
        quadro.CardsPorColuna().Values.Select(cards => cards.Select(k => k.TarefaId).ToList())
            .Should().BeEquivalentTo(new List<List<Guid>> { new() { a.Id }, new() { b.Id }, new(), new() { c.Id } }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void FormatoDesconhecidoOuComDefeito_ERecusado()
    {
        var cronograma = ComTresTarefas(out _, out _, out _);
        cronograma.Quadro.Sincronizar(); // como o programa faz a cada alteração
        Repositorio.Salvar(cronograma, Caminho);
        Executar("UPDATE QuadroDoProjeto SET Versao = 9");

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*quadro Kanban num formato desconhecido*");
        Executar("UPDATE QuadroDoProjeto SET Versao = 1, Dados = '{\"colunas\":[]}'");
        abrir.Should().Throw<InvalidDataException>().WithMessage("O quadro Kanban tem um defeito: O quadro precisa de pelo menos uma coluna do tipo A fazer*");
    }
}
