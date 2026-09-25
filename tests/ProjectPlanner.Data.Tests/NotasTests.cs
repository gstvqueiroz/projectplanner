using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

public sealed class NotasTests : TesteComArquivo
{
    private Tarefa SalvarEReabrir(string? notas)
    {
        var projeto = new Cronograma("Obra", Segunda);
        var tarefa = new Tarefa("Fundação", Segunda, 5);
        tarefa.AlterarNotas(notas);
        projeto.AdicionarTarefa(tarefa);

        Repositorio.Salvar(projeto, Caminho);
        return Repositorio.Abrir(Caminho).Tarefas.Single();
    }

    [Theory]
    [InlineData("Concreto fck 30 MPa")]
    [InlineData("  Linha 1\nLinha 2 — ção, 日本語, 🙂  ")]
    public void SalvarEAbrir_RecuperaAsNotasExatamente(string notas)
    {
        SalvarEReabrir(notas).Notas.Should().Be(notas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SalvarEAbrir_SemNotas_VoltaNulo(string? notas)
    {
        SalvarEReabrir(notas).Notas.Should().BeNull();
    }

    [Fact]
    public void SemNotas_GravaNuloNoBanco()
    {
        SalvarEReabrir("");

        using var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false);
        contexto.Tarefas.Single().Notas.Should().BeNull();
    }

    [Fact]
    public void ApagarAsNotasESalvarDeNovo_OArquivoFicaSemNotas()
    {
        var projeto = new Cronograma("Obra", Segunda);
        var tarefa = new Tarefa("Fundação", Segunda, 5);
        tarefa.AlterarNotas("Provisório");
        projeto.AdicionarTarefa(tarefa);
        Repositorio.Salvar(projeto, Caminho);

        tarefa.AlterarNotas(null);
        Repositorio.Salvar(projeto, Caminho);

        Repositorio.Abrir(Caminho).Tarefas.Single().Notas.Should().BeNull();
    }

    [Fact]
    public void ArquivoDaVersaoAnterior_AbreSemNotasEGanhaAColuna()
    {
        // Um arquivo do Bloco 7/9: só a migration inicial, sem a coluna Notas.
        using (var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: true))
            contexto.GetService<IMigrator>().Migrate("20260925014258_Inicial");
        var projetoId = Guid.NewGuid();
        Executar("INSERT INTO Projetos (Id, Nome, DataInicio, DataCriacao, DataUltimaModificacao) VALUES ($p0, 'Antigo', $p1, $p1, $p1)",
            projetoId.ToString().ToUpperInvariant(), "2026-09-28 00:00:00");
        Executar("INSERT INTO Tarefas (Id, ProjetoId, Ordem, Nome, Duracao, DataInicio, DataTermino, PercentualConcluido) " +
                 "VALUES ($p0, $p1, 0, 'Fundação', 5, $p2, $p3, 0)",
            Guid.NewGuid().ToString().ToUpperInvariant(), projetoId.ToString().ToUpperInvariant(),
            "2026-09-28 00:00:00", "2026-10-02 00:00:00");

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Single().Notas.Should().BeNull();
        using var depois = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false);
        depois.Database.GetAppliedMigrations().Should().Contain(m => m.EndsWith("_NotasDaTarefa"));
    }
}
