using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProjectPlanner.Servidor.Dados;

/// <summary>
/// Qual banco usar, pela connection string <c>ConnectionStrings:Banco</c>: com "Host=", PostgreSQL (produção, com as migrações
/// de Dados/Migracoes); senão, SQLite (desenvolvimento e testes, criado direto do modelo).
/// </summary>
public static class Banco
{
    public const string Padrao = "Data Source=servidor.db";

    public static bool EhPostgres(string conexao) => conexao.Contains("Host=", StringComparison.OrdinalIgnoreCase);

    public static void Configurar(DbContextOptionsBuilder opcoes, string conexao)
    {
        if (EhPostgres(conexao))
            opcoes.UseNpgsql(conexao);
        else
            opcoes.UseSqlite(conexao);
    }

    /// <summary>Deixa o banco pronto: aplica as migrações (PostgreSQL) ou cria as tabelas (SQLite).</summary>
    public static void Preparar(BancoDoServidor banco)
    {
        if (banco.Database.IsNpgsql())
            banco.Database.Migrate();
        else
            banco.Database.EnsureCreated();
    }
}

/// <summary>Para o <c>dotnet ef migrations add</c>: as migrações são as do PostgreSQL (não conecta em nada para gerá-las).</summary>
public sealed class FabricaDoBancoParaMigracoes : IDesignTimeDbContextFactory<BancoDoServidor>
{
    public BancoDoServidor CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BancoDoServidor>().UseNpgsql("Host=localhost;Database=projectplanner").Options);
}
