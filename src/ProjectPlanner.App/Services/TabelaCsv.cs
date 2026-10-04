using System.Text;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Uma tabela em CSV que o Excel em português abre direto (Bloco 25, E1): ";" entre os campos, linhas com CRLF e o
/// arquivo em UTF-8 com BOM (sem ele, o Excel lê os acentos errado).
/// </summary>
public static class TabelaCsv
{
    public const char Separador = ';';

    public static readonly Encoding Codificacao = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>O texto do CSV: o cabeçalho e as linhas, cada campo protegido quando precisa.</summary>
    public static string Gerar(IReadOnlyList<string> cabecalho, IEnumerable<IReadOnlyList<string>> linhas)
    {
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(linhas);
        var texto = new StringBuilder();
        Escrever(texto, cabecalho);
        foreach (var linha in linhas)
        {
            if (linha.Count != cabecalho.Count)
                throw new ArgumentException($"Uma linha tem {linha.Count} campos; o cabeçalho tem {cabecalho.Count}.", nameof(linhas));
            Escrever(texto, linha);
        }
        return texto.ToString();
    }

    /// <summary>
    /// Um campo de texto livre (nome, notas): começando com "=", "+", "-" ou "@", ganha um apóstrofo na frente, para o
    /// Excel não tratá-lo como fórmula.
    /// </summary>
    public static string TextoLivre(string? texto) =>
        string.IsNullOrEmpty(texto) ? "" : texto[0] is '=' or '+' or '-' or '@' ? "'" + texto : texto;

    private static void Escrever(StringBuilder texto, IReadOnlyList<string> campos)
    {
        for (var i = 0; i < campos.Count; i++)
        {
            if (i > 0)
                texto.Append(Separador);
            texto.Append(Campo(campos[i] ?? ""));
        }
        texto.Append("\r\n");
    }

    // Entre aspas quando tem separador, aspas ou quebra de linha; aspas dentro viram aspas duplas.
    private static string Campo(string valor) =>
        valor.IndexOfAny([Separador, '"', '\n', '\r']) >= 0 ? "\"" + valor.Replace("\"", "\"\"") + "\"" : valor;
}
