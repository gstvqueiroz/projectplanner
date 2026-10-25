using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProjectPlanner.Data;

/// <summary>
/// Schema de um arquivo de projeto. As exclusões em cascata (Projetos → Tarefas → Predecessoras e
/// FotosTarefa, e resumo → subtarefas) ficam no próprio banco, então valem também para quem mexer no arquivo por fora.
/// </summary>
internal sealed class ProjectPlannerDbContext(DbContextOptions<ProjectPlannerDbContext> options)
    : DbContext(options)
{
    public DbSet<ProjetoRegistro> Projetos => Set<ProjetoRegistro>();
    public DbSet<TarefaRegistro> Tarefas => Set<TarefaRegistro>();
    public DbSet<PredecessoraRegistro> Predecessoras => Set<PredecessoraRegistro>();
    public DbSet<FotoRegistro> Fotos => Set<FotoRegistro>();
    public DbSet<FotoImagemRegistro> Imagens => Set<FotoImagemRegistro>();
    public DbSet<FotoMiniaturaRegistro> Miniaturas => Set<FotoMiniaturaRegistro>();
    public DbSet<AssistenteRegistro> Assistente => Set<AssistenteRegistro>();
    public DbSet<RelatorioRegistro> Relatorio => Set<RelatorioRegistro>();
    public DbSet<LinhasDeBaseRegistro> LinhasDeBase => Set<LinhasDeBaseRegistro>();
    public DbSet<ContratoRegistro> Contrato => Set<ContratoRegistro>();
    public DbSet<CustosRegistro> Custos => Set<CustosRegistro>();
    public DbSet<OrdensRegistro> Ordens => Set<OrdensRegistro>();
    public DbSet<RecursoRegistro> Recursos => Set<RecursoRegistro>();
    public DbSet<OrcamentoRegistro> Orcamento => Set<OrcamentoRegistro>();
    public DbSet<AtribuicaoRegistro> Atribuicoes => Set<AtribuicaoRegistro>();

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

        modelBuilder.Entity<FotoRegistro>(foto =>
        {
            foto.ToTable("FotosTarefa", t =>
                t.HasCheckConstraint("CK_FotosTarefa_Percentual",
                    "PercentualConcluidoNaData IS NULL OR PercentualConcluidoNaData BETWEEN 0 AND 100"));
            foto.HasKey(f => f.Id);
            foto.Property(f => f.NoRelatorio).HasDefaultValue(true).HasSentinel(true); // as fotos de antes do Bloco 27 entram no relatório
            foto.HasOne<TarefaRegistro>().WithMany()
                .HasForeignKey(f => f.TarefaId).OnDelete(DeleteBehavior.Cascade);
            foto.HasIndex(f => f.TarefaId);
        });

        // Imagens e miniaturas em tabelas próprias (Bloco 22): abrir o projeto e listar fotos não leem os BLOBs.
        modelBuilder.Entity<FotoImagemRegistro>(imagem =>
        {
            imagem.ToTable("FotosImagens");
            imagem.HasKey(i => i.FotoId);
            imagem.HasOne<FotoRegistro>().WithOne()
                .HasForeignKey<FotoImagemRegistro>(i => i.FotoId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FotoMiniaturaRegistro>(miniatura =>
        {
            miniatura.ToTable("FotosMiniaturas");
            miniatura.HasKey(m => m.FotoId);
            miniatura.HasOne<FotoRegistro>().WithOne()
                .HasForeignKey<FotoMiniaturaRegistro>(m => m.FotoId).OnDelete(DeleteBehavior.Cascade);
        });

        // Os dados da obra e o histórico dos relatórios fotográficos (Bloco 27).
        // As linhas de base (Bloco 31), em JSON.
        modelBuilder.Entity<LinhasDeBaseRegistro>(linhas =>
        {
            linhas.ToTable("LinhasDeBaseDoProjeto");
            linhas.HasKey(l => l.ProjetoId);
            linhas.HasOne<ProjetoRegistro>().WithOne()
                .HasForeignKey<LinhasDeBaseRegistro>(l => l.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        // O contrato, os aditivos e as medições (Bloco 36), em JSON.
        modelBuilder.Entity<ContratoRegistro>(contrato =>
        {
            contrato.ToTable("ContratoDoProjeto");
            contrato.HasKey(c => c.ProjetoId);
            contrato.HasOne<ProjetoRegistro>().WithOne()
                .HasForeignKey<ContratoRegistro>(c => c.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        // Os recursos do projeto e as atribuições às tarefas (Bloco 32). Um recurso em uso não sai do arquivo sem sair das tarefas.
        modelBuilder.Entity<RecursoRegistro>(recurso =>
        {
            recurso.ToTable("Recursos", t =>
            {
                t.HasCheckConstraint("CK_Recursos_Tipo", "Tipo BETWEEN 0 AND 2");
                t.HasCheckConstraint("CK_Recursos_Custo", "CustoUnitario >= 0");
            });
            recurso.HasKey(r => r.Id);
            recurso.HasOne<ProjetoRegistro>().WithMany().HasForeignKey(r => r.ProjetoId).OnDelete(DeleteBehavior.Cascade);
            recurso.HasIndex(r => new { r.ProjetoId, r.Ordem }).IsUnique();
        });
        modelBuilder.Entity<AtribuicaoRegistro>(atribuicao =>
        {
            atribuicao.ToTable("AtribuicoesDeRecurso", t => t.HasCheckConstraint("CK_Atribuicoes_Quantidade", "Quantidade > 0"));
            atribuicao.HasKey(a => a.Id);
            atribuicao.HasOne<TarefaRegistro>().WithMany().HasForeignKey(a => a.TarefaId).OnDelete(DeleteBehavior.Cascade);
            atribuicao.HasOne<RecursoRegistro>().WithMany().HasForeignKey(a => a.RecursoId).OnDelete(DeleteBehavior.Restrict);
            atribuicao.HasIndex(a => new { a.TarefaId, a.RecursoId }).IsUnique();
            atribuicao.HasIndex(a => a.RecursoId);
        });

        // O orçamento (Bloco 33), em JSON.
        modelBuilder.Entity<OrcamentoRegistro>(orcamento =>
        {
            orcamento.ToTable("OrcamentoDoProjeto");
            orcamento.HasKey(o => o.ProjetoId);
            orcamento.HasOne<ProjetoRegistro>().WithOne()
                .HasForeignKey<OrcamentoRegistro>(o => o.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        // Os custos realizados (Bloco 37), em JSON.
        modelBuilder.Entity<CustosRegistro>(custos =>
        {
            custos.ToTable("CustosDoProjeto");
            custos.HasKey(c => c.ProjetoId);
            custos.HasOne<ProjetoRegistro>().WithOne()
                .HasForeignKey<CustosRegistro>(c => c.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        // As ordens de serviço (Bloco 58), em JSON.
        modelBuilder.Entity<OrdensRegistro>(ordens =>
        {
            ordens.ToTable("OrdensDeServicoDoProjeto");
            ordens.HasKey(o => o.ProjetoId);
            ordens.HasOne<ProjetoRegistro>().WithOne()
                .HasForeignKey<OrdensRegistro>(o => o.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RelatorioRegistro>(relatorio =>
        {
            relatorio.ToTable("RelatorioDoProjeto");
            relatorio.HasKey(r => r.ProjetoId);
            relatorio.HasOne<ProjetoRegistro>().WithOne()
                .HasForeignKey<RelatorioRegistro>(r => r.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        // A conversa com o assistente e a ficha do projeto (Bloco 26).
        modelBuilder.Entity<AssistenteRegistro>(assistente =>
        {
            assistente.ToTable("AssistenteDoProjeto");
            assistente.HasKey(a => a.ProjetoId);
            assistente.HasOne<ProjetoRegistro>().WithOne()
                .HasForeignKey<AssistenteRegistro>(a => a.ProjetoId).OnDelete(DeleteBehavior.Cascade);
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
