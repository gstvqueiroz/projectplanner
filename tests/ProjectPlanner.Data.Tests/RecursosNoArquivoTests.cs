using ProjectPlanner.Core;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Data.Tests;

// Bloco 32: os recursos e as atribuições no .pplan.
public sealed class RecursosNoArquivoTests : TesteComArquivo
{
    private static (Cronograma C, Tarefa A, Recurso Pedreiro, Recurso Cimento) Obra()
    {
        var c = new Cronograma("Obra", Segunda);
        var a = new Tarefa("Alvenaria", Segunda, 5);
        c.AdicionarTarefa(a);
        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, null, 280.5m, 2, "Equipe");
        var cimento = new Recurso(Guid.NewGuid(), "Cimento CP-II", TipoDeRecurso.Material, "sc", 38.9m);
        c.Recursos.Adicionar(pedreiro);
        c.Recursos.Adicionar(cimento);
        c.Recursos.Atribuir(a, [new(cimento.Id, 120), new(pedreiro.Id, 1.5m)]);
        return (c, a, pedreiro, cimento);
    }

    [Fact]
    public void RecursosEAtribuicoes_VoltamIguais_NaOrdem()
    {
        var (c, a, _, _) = Obra();

        Repositorio.Salvar(c, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Recursos.Lista.Should().Equal(c.Recursos.Lista);
        aberto.Tarefas.Single().Atribuicoes.Should().Equal(a.Atribuicoes);
        aberto.Recursos.CustoOrcado(aberto.Tarefas.Single()).Should().Be(c.Recursos.CustoOrcado(a));
    }

    [Fact]
    public void SemRecursos_NadaGravado_EArquivoAntigoAbre()
    {
        var c = new Cronograma("Vazio", Segunda);
        c.AdicionarTarefa(new Tarefa("A", Segunda, 1));
        Repositorio.Salvar(c, Caminho);
        (Contar("Recursos"), Contar("AtribuicoesDeRecurso")).Should().Be((0, 0));

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.24.0.pplan"), Path.Combine(Pasta, "antigo.pplan"));
        Repositorio.Abrir(Path.Combine(Pasta, "antigo.pplan")).Recursos.Vazio.Should().BeTrue();
    }

    [Fact]
    public void AtribuicaoComQuantidadeZero_RecusadaPeloBanco_OuArquivoComDefeito()
    {
        var (c, _, _, _) = Obra();
        Repositorio.Salvar(c, Caminho);

        var mexer = () => Executar("UPDATE AtribuicoesDeRecurso SET Quantidade = 0");
        mexer.Should().Throw<Microsoft.Data.Sqlite.SqliteException>("o banco confere a quantidade");
        Executar("UPDATE Recursos SET Tipo = 1 WHERE Nome = 'Pedreiro'; UPDATE Recursos SET Nome = 'Cimento CP-II' WHERE Nome = 'Pedreiro'");
        ((Action)(() => Repositorio.Abrir(Caminho))).Should().Throw<InvalidDataException>("dois recursos com o mesmo nome e tipo");
    }
}
