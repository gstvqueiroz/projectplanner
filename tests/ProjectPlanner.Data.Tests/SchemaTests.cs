using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

public class SchemaTests : TesteComArquivo
{
    [Fact]
    public void Modelo_NaoTemMudancasForaDeMigration()
    {
        using var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: true);

        contexto.Database.HasPendingModelChanges().Should().BeFalse(
            "toda mudança no modelo precisa de uma migration (dotnet ef migrations add)");
    }

    [Fact]
    public void Salvar_CriaAsTabelasDoProjetoEOHistoricoDeMigrations()
    {
        Repositorio.Salvar(new Cronograma("Projeto", Segunda), Caminho);

        Tabelas().Should().Contain(["Projetos", "Tarefas", "Predecessoras", "__EFMigrationsHistory"]);
    }

    [Fact]
    public void Banco_RecusaVinculoDuplicadoEAutodependencia()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        var (a, b) = (new Tarefa("A", Segunda, 1), new Tarefa("B", Segunda, 1));
        projeto.AdicionarTarefa(a);
        projeto.AdicionarTarefa(b);
        projeto.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        Repositorio.Salvar(projeto, Caminho);
        const string inserir =
            "INSERT INTO Predecessoras (Ordem, TarefaOrigemId, TarefaDestinoId, Tipo, Lag) VALUES (9, $p0, $p1, 0, 0)";

        var duplicado = () => Executar(inserir, a.Id, b.Id);
        var autodependencia = () => Executar(inserir, a.Id, a.Id);

        duplicado.Should().Throw<SqliteException>().WithMessage("*UNIQUE*");
        autodependencia.Should().Throw<SqliteException>().WithMessage("*CK_Predecessoras_SemAutodependencia*");
    }

    [Fact]
    public void Banco_RecusaDuracaoEPercentualForaDasRegras()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        var tarefa = new Tarefa("A", Segunda, 1);
        projeto.AdicionarTarefa(tarefa);
        Repositorio.Salvar(projeto, Caminho);

        var duracaoZero = () => Executar("UPDATE Tarefas SET Duracao = 0 WHERE Id = $p0", tarefa.Id);
        var percentual101 = () => Executar("UPDATE Tarefas SET PercentualConcluido = 101 WHERE Id = $p0", tarefa.Id);

        duracaoZero.Should().Throw<SqliteException>().WithMessage("*CK_Tarefas_Duracao*");
        percentual101.Should().Throw<SqliteException>().WithMessage("*CK_Tarefas_PercentualConcluido*");
    }
}
