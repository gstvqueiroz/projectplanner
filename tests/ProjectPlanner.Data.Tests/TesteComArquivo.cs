using Microsoft.Data.Sqlite;

namespace ProjectPlanner.Data.Tests;

/// <summary>
/// Base dos testes de integração: cada teste ganha uma pasta temporária própria (apagada no
/// Dispose) com um caminho de arquivo de projeto, um relógio controlável e acesso SQL direto ao arquivo.
/// </summary>
public abstract class TesteComArquivo : IDisposable
{
    protected static readonly DateTime Segunda = new(2026, 9, 28);

    protected TesteComArquivo()
    {
        Directory.CreateDirectory(Pasta);
        Repositorio = new SqliteProjetoRepository(Relogio);
    }

    protected string Pasta { get; } = Path.Combine(Path.GetTempPath(), "projectplanner-testes", Guid.NewGuid().ToString("N"));

    protected string Caminho => Path.Combine(Pasta, "projeto.sqlite");

    protected RelogioFixo Relogio { get; } = new(new DateTimeOffset(2026, 9, 24, 21, 0, 0, TimeSpan.Zero));

    protected SqliteProjetoRepository Repositorio { get; }

    protected long Contar(string tabela, string? caminho = null)
    {
        using var conexao = Conectar(caminho);
        using var comando = conexao.CreateCommand();
        comando.CommandText = $"SELECT COUNT(*) FROM {tabela}";
        return (long)comando.ExecuteScalar()!;
    }

    protected IReadOnlyList<string> Tabelas()
    {
        using var conexao = Conectar();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        using var leitor = comando.ExecuteReader();
        var tabelas = new List<string>();
        while (leitor.Read())
            tabelas.Add(leitor.GetString(0));
        return tabelas;
    }

    /// <summary>Executa SQL direto no arquivo; os parâmetros viram $p0, $p1...</summary>
    protected void Executar(string sql, params object[] parametros)
    {
        using var conexao = Conectar();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        for (var i = 0; i < parametros.Length; i++)
            comando.Parameters.AddWithValue($"$p{i}", parametros[i]);
        comando.ExecuteNonQuery();
    }

    // Chaves estrangeiras ligadas, como o EF faz em toda conexão.
    private SqliteConnection Conectar(string? caminho = null)
    {
        var conexao = new SqliteConnection($"Data Source={caminho ?? Caminho};Foreign Keys=True;Pooling=False");
        conexao.Open();
        return conexao;
    }

    public void Dispose()
    {
        if (Directory.Exists(Pasta))
            Directory.Delete(Pasta, recursive: true);
        GC.SuppressFinalize(this);
    }
}

public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public DateTimeOffset Agora { get; private set; } = agora;

    public void Avancar(TimeSpan intervalo) => Agora += intervalo;

    public override DateTimeOffset GetUtcNow() => Agora;
}
