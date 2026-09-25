using Microsoft.Data.Sqlite;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

public class MensagensDeErroTests
{
    public static TheoryData<Exception, string> Erros => new()
    {
        { new InvalidDataException("O arquivo \"x\" não é um projeto do ProjectPlanner."), "O arquivo \"x\" não é um projeto do ProjectPlanner." },
        { new FileNotFoundException("x"), "O arquivo não foi encontrado. Ele pode ter sido movido ou apagado." },
        { new DirectoryNotFoundException("x"), "A pasta do arquivo não existe mais." },
        { new UnauthorizedAccessException("x"), "Sem permissão para gravar ou ler neste local. Escolha outra pasta ou verifique as permissões do arquivo." },
        { new IOException("x", 28), "Não há espaço suficiente no disco." },
        { new IOException("x", unchecked((int)0x80070070)), "Não há espaço suficiente no disco." },
        { new IOException("x", unchecked((int)0x80070027)), "Não há espaço suficiente no disco." },
        { new IOException("Broken pipe"), "Erro de leitura ou gravação: Broken pipe" },
        { new SqliteException("x", 13), "Não há espaço suficiente no disco." },
        { new SqliteException("x", 8), "Sem permissão para gravar ou ler neste local. Escolha outra pasta ou verifique as permissões do arquivo." },
        { new SqliteException("x", 5), "O arquivo está em uso por outro programa. Feche-o e tente de novo." },
        { new SqliteException("x", 11), "O arquivo está corrompido ou não é um projeto do ProjectPlanner." },
        { new SqliteException("constraint failed", 19), "Erro no arquivo de projeto: constraint failed" },
        { new InvalidOperationException("externo", new SqliteException("x", 13)), "Não há espaço suficiente no disco." },
        { new NullReferenceException("bug"), "Erro inesperado: bug" },
    };

    [Theory]
    [MemberData(nameof(Erros))]
    public void Descrever_TraduzCadaFalhaNumaMensagemParaOUsuario(Exception erro, string mensagem)
    {
        MensagensDeErro.Descrever(erro).Should().Be(mensagem);
    }
}
