using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace ProjectPlanner.Servidor.Dados;

/// <summary>
/// Qual banco usar, pela connection string <c>ConnectionStrings:Banco</c>: com "Host=" ou no formato de URL
/// (<c>postgresql://usuario:senha@servidor/banco?sslmode=require</c>, o que o Neon e outros mostram), PostgreSQL (produção,
/// com as migrações de Dados/Migracoes); senão, SQLite (desenvolvimento e testes, criado direto do modelo).
/// </summary>
public static class Banco
{
    public const string Padrao = "Data Source=servidor.db";

    public static bool EhPostgres(string conexao) => conexao.Contains("Host=", StringComparison.OrdinalIgnoreCase) || EhUrl(conexao);

    private static bool EhUrl(string conexao) =>
        conexao.TrimStart().StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || conexao.TrimStart().StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    /// <summary>A connection string no formato do Npgsql (a de URL vira "Host=…;Database=…").</summary>
    public static string ParaNpgsql(string conexao)
    {
        if (!EhUrl(conexao))
            return conexao;
        var url = new Uri(conexao.Trim());
        var usuario = url.UserInfo.Split(':', 2);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = url.Host,
            Port = url.IsDefaultPort || url.Port <= 0 ? 5432 : url.Port,
            Database = Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(usuario[0]),
            Password = usuario.Length > 1 ? Uri.UnescapeDataString(usuario[1]) : null,
        };
        foreach (var parte in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var (chave, valor) = parte.Split('=', 2) is [var k, var v] ? (k, Uri.UnescapeDataString(v)) : (parte, "");
            switch (chave.ToLowerInvariant())
            {
                case "sslmode":
                    b.SslMode = Enum.Parse<SslMode>(valor.Replace("-", ""), ignoreCase: true);
                    break;
                case "channel_binding":
                    b.ChannelBinding = Enum.Parse<ChannelBinding>(valor, ignoreCase: true);
                    break;
            }
        }
        return b.ConnectionString;
    }

    public static void Configurar(DbContextOptionsBuilder opcoes, string conexao)
    {
        if (EhPostgres(conexao))
            opcoes.UseNpgsql(ParaNpgsql(conexao));
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
