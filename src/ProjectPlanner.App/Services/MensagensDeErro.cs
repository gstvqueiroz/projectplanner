using Microsoft.Data.Sqlite;

namespace ProjectPlanner.App.Services;

/// <summary>Traduz falhas ao abrir ou salvar um arquivo de projeto em mensagens para o usuário.</summary>
public static class MensagensDeErro
{
    private const string DiscoCheio = "Não há espaço suficiente no disco.";
    private const string SemPermissao =
        "Sem permissão para gravar ou ler neste local. Escolha outra pasta ou verifique as permissões do arquivo.";

    public static string Descrever(Exception erro) => erro switch
    {
        InvalidDataException => erro.Message, // o repositório já descreve o problema do arquivo
        FileNotFoundException => "O arquivo não foi encontrado. Ele pode ter sido movido ou apagado.",
        DirectoryNotFoundException => "A pasta do arquivo não existe mais.",
        UnauthorizedAccessException => SemPermissao,
        _ when ErroDoSqlite(erro) is { } sqlite => sqlite.SqliteErrorCode switch
        {
            13 => DiscoCheio,                               // SQLITE_FULL
            3 or 8 or 14 => SemPermissao,                   // SQLITE_PERM, SQLITE_READONLY, SQLITE_CANTOPEN
            5 or 6 => "O arquivo está em uso por outro programa. Feche-o e tente de novo.", // SQLITE_BUSY, SQLITE_LOCKED
            11 or 26 => "O arquivo está corrompido ou não é um projeto do ProjectPlanner.", // SQLITE_CORRUPT, SQLITE_NOTADB
            _ => $"Erro no arquivo de projeto: {sqlite.Message}",
        },
        IOException io when EhDiscoCheio(io) => DiscoCheio,
        IOException io => $"Erro de leitura ou gravação: {io.Message}",
        _ => $"Erro inesperado: {erro.Message}",
    };

    // O EF Core embrulha o erro do SQLite (por exemplo, num DbUpdateException).
    private static SqliteException? ErroDoSqlite(Exception erro)
    {
        for (var atual = erro; atual is not null; atual = atual.InnerException)
            if (atual is SqliteException sqlite)
                return sqlite;
        return null;
    }

    // ENOSPC (28) no macOS e no Linux; ERROR_DISK_FULL (112) e ERROR_HANDLE_DISK_FULL (39) no Windows.
    private static bool EhDiscoCheio(IOException erro) =>
        erro.HResult == 28 || (erro.HResult & 0xFFFF) is 112 or 39;
}
