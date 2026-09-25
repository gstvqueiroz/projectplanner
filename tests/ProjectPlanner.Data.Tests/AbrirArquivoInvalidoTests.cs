using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

// Regra: Abrir nunca devolve null. Arquivo inexistente lança FileNotFoundException; qualquer
// arquivo que não seja um projeto válido desta versão lança InvalidDataException.
public class AbrirArquivoInvalidoTests : TesteComArquivo
{
    [Fact]
    public void Abrir_ArquivoInexistente_LancaFileNotFoundException()
    {
        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<FileNotFoundException>()
            .Which.FileName.Should().Be(Caminho);
    }

    [Fact]
    public void Abrir_ArquivoQueNaoEhSqlite_LancaInvalidDataException()
    {
        File.WriteAllText(Caminho, "isto não é um banco de dados");

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*não é um projeto*");
    }

    [Fact]
    public void Abrir_ArquivoVazio_LancaInvalidDataException()
    {
        File.WriteAllBytes(Caminho, []);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*não é um projeto*");
    }

    [Fact]
    public void Abrir_BancoSqliteDeOutroSistema_LancaInvalidDataExceptionSemAlterarOArquivo()
    {
        Executar("CREATE TABLE Clientes (Id INTEGER PRIMARY KEY, Nome TEXT)");
        var antes = File.ReadAllBytes(Caminho);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*não é um projeto*");
        File.ReadAllBytes(Caminho).Should().Equal(antes);
        Tabelas().Should().Equal("Clientes");
    }

    [Fact]
    public void Abrir_ArquivoDeVersaoMaisNova_LancaInvalidDataException()
    {
        Repositorio.Salvar(new Cronograma("Projeto", Segunda), Caminho);
        Executar("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29991231000000_Futura', '99.0.0')");

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*versão mais nova*");
    }

    [Fact]
    public void Abrir_ArquivoSemProjeto_LancaInvalidDataException()
    {
        Repositorio.Salvar(new Cronograma("Projeto", Segunda), Caminho);
        Executar("DELETE FROM Projetos");

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*deveria ter um projeto, mas tem 0*");
    }

    [Fact]
    public void Abrir_ArquivoComCicloGravadoPorFora_LancaInvalidDataExceptionComACausa()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        var (a, b) = (new Tarefa("A", Segunda, 1), new Tarefa("B", Segunda, 1));
        projeto.AdicionarTarefa(a);
        projeto.AdicionarTarefa(b);
        projeto.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        Repositorio.Salvar(projeto, Caminho);
        Executar("INSERT INTO Predecessoras (Ordem, TarefaOrigemId, TarefaDestinoId, Tipo, Lag) VALUES (1, $p0, $p1, 0, 0)",
            b.Id, a.Id);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*dados inválidos*")
            .WithInnerException<CircularDependencyException>();
    }
}
