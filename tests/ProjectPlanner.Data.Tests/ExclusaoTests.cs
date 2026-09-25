using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

// Regra: excluir uma tarefa exclui junto os vínculos em que ela aparece (como no MS Project), sem
// religar as vizinhas. No domínio isso é Cronograma.RemoverTarefa; no banco, as chaves estrangeiras
// com ON DELETE CASCADE garantem o mesmo para quem alterar o arquivo por fora.
public class ExclusaoTests : TesteComArquivo
{
    // A → B → C, mais o atalho A → C.
    private readonly Cronograma _projeto = new("Projeto", Segunda);
    private readonly Tarefa _a = new("A", Segunda, 2);
    private readonly Tarefa _b = new("B", Segunda, 3);
    private readonly Tarefa _c = new("C", Segunda, 1);

    public ExclusaoTests()
    {
        foreach (var tarefa in new[] { _a, _b, _c })
            _projeto.AdicionarTarefa(tarefa);
        _projeto.AdicionarPredecessora(new Predecessora(_a.Id, _b.Id));
        _projeto.AdicionarPredecessora(new Predecessora(_b.Id, _c.Id));
        _projeto.AdicionarPredecessora(new Predecessora(_a.Id, _c.Id));
    }

    [Fact]
    public void ExcluirProjetoNoBanco_ExcluiTarefasEPredecessorasEmCascata()
    {
        Repositorio.Salvar(_projeto, Caminho);
        (Contar("Projetos"), Contar("Tarefas"), Contar("Predecessoras")).Should().Be((1, 3, 3));

        Executar("DELETE FROM Projetos WHERE Id = $p0", _projeto.Id);

        (Contar("Projetos"), Contar("Tarefas"), Contar("Predecessoras")).Should().Be((0, 0, 0));
    }

    [Fact]
    public void ExcluirTarefaNoBanco_ExcluiOsVinculosDelaEmCascata()
    {
        Repositorio.Salvar(_projeto, Caminho);

        Executar("DELETE FROM Tarefas WHERE Id = $p0", _b.Id);

        var aberto = Repositorio.Abrir(Caminho);
        aberto.Tarefas.Select(t => t.Nome).Should().Equal("A", "C");
        aberto.Predecessoras.Should().Equal(new Predecessora(_a.Id, _c.Id));
    }

    [Fact]
    public void RemoverTarefaQueEhPredecessora_SalvarEAbrir_NaoTrazOsVinculosDela()
    {
        Repositorio.Salvar(_projeto, Caminho);

        _projeto.RemoverTarefa(_b.Id);
        Repositorio.Salvar(_projeto, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Select(t => t.Nome).Should().Equal("A", "C");
        aberto.Predecessoras.Should().Equal(new Predecessora(_a.Id, _c.Id));
        Contar("Predecessoras").Should().Be(1);
    }
}
