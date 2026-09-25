using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProjectPlanner.Data;

/// <summary>
/// Schema de um arquivo de projeto. As exclusões em cascata (Projetos → Tarefas → Predecessoras, e
/// resumo → subtarefas) ficam no próprio banco, então valem também para quem mexer no arquivo por fora.
/// </summary>
internal sealed class ProjectPlannerDbContext(DbContextOptions<ProjectPlannerDbContext> options)
    : DbContext(options)
{
    public DbSet<ProjetoRegistro> Projetos => Set<ProjetoRegistro>();
    public DbSet<TarefaRegistro> Tarefas => Set<TarefaRegistro>();
    public DbSet<PredecessoraRegistro> Predecessoras => Set<PredecessoraRegistro>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjetoRegistro>(projeto =>
        {
            projeto.ToTable("Projetos");
            projeto.HasKey(p => p.Id);
        });

        modelBuilder.Entity<TarefaRegistro>(tarefa =>
        {
            tarefa.ToTable("Tarefas", t =>
            {
                // Tarefa comum: duração > 0; marco: duração 0; resumo: a duração calculada, 0 ou mais.
                t.HasCheckConstraint("CK_Tarefas_Duracao",
                    "(Tipo = 0 AND Duracao > 0) OR (Tipo = 1 AND Duracao = 0) OR (Tipo = 2 AND Duracao >= 0)");
                t.HasCheckConstraint("CK_Tarefas_PercentualConcluido", "PercentualConcluido BETWEEN 0 AND 100");
                t.HasCheckConstraint("CK_Tarefas_Tipo", "Tipo BETWEEN 0 AND 2");
                t.HasCheckConstraint("CK_Tarefas_PaiDiferenteDelaMesma", "TarefaPaiId IS NULL OR TarefaPaiId <> Id");
            });
            tarefa.HasKey(t => t.Id);
            tarefa.HasOne<ProjetoRegistro>().WithMany()
                .HasForeignKey(t => t.ProjetoId).OnDelete(DeleteBehavior.Cascade);
            tarefa.HasOne<TarefaRegistro>().WithMany()
                .HasForeignKey(t => t.TarefaPaiId).OnDelete(DeleteBehavior.Cascade);
            tarefa.HasIndex(t => new { t.ProjetoId, t.Ordem }).IsUnique();
            tarefa.HasIndex(t => t.TarefaPaiId);
        });

        modelBuilder.Entity<PredecessoraRegistro>(vinculo =>
        {
            vinculo.ToTable("Predecessoras", t =>
            {
                t.HasCheckConstraint("CK_Predecessoras_Tipo", "Tipo BETWEEN 0 AND 3");
                t.HasCheckConstraint("CK_Predecessoras_SemAutodependencia", "TarefaOrigemId <> TarefaDestinoId");
            });
            vinculo.HasKey(p => p.Id);
            vinculo.HasOne<TarefaRegistro>().WithMany()
                .HasForeignKey(p => p.TarefaOrigemId).OnDelete(DeleteBehavior.Cascade);
            vinculo.HasOne<TarefaRegistro>().WithMany()
                .HasForeignKey(p => p.TarefaDestinoId).OnDelete(DeleteBehavior.Cascade);
            vinculo.HasIndex(p => new { p.TarefaOrigemId, p.TarefaDestinoId }).IsUnique();
            vinculo.HasIndex(p => p.TarefaDestinoId);
        });
    }

    internal static ProjectPlannerDbContext Criar(string caminhoArquivo, bool criarSeNaoExistir) =>
        new(new DbContextOptionsBuilder<ProjectPlannerDbContext>()
            .UseSqlite(StringDeConexao(caminhoArquivo, criarSeNaoExistir))
            .Options);

    // Sem pool de conexões: o arquivo é liberado assim que a operação termina, para poder ser
    // movido, copiado ou apagado pelo usuário.
    internal static string StringDeConexao(string caminhoArquivo, bool criarSeNaoExistir) =>
        new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = caminhoArquivo,
            Mode = criarSeNaoExistir
                ? Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate
                : Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();
}

/// <summary>Usada só pelo dotnet-ef para gerar as migrations.</summary>
internal sealed class ProjectPlannerDbContextFactory : IDesignTimeDbContextFactory<ProjectPlannerDbContext>
{
    public ProjectPlannerDbContext CreateDbContext(string[] args) =>
        ProjectPlannerDbContext.Criar("design-time.sqlite", criarSeNaoExistir: true);
}
