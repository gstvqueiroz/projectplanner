using System.Text.RegularExpressions;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// O Gemini, do Google (Bloco 23), pela API REST (generativelanguage.googleapis.com), sem SDK: só o
/// <see cref="HttpClient"/>. A chave vai no cabeçalho <c>x-goog-api-key</c>, nunca na URL.
/// </summary>
public sealed partial class ProvedorGemini : IProvedorIA
{
    public const string ModeloRecomendado = "gemini-2.5-flash";

    /// <summary>Quanto esperar por uma resposta (com PDFs, o Gemini pode demorar).</summary>
    public static readonly TimeSpan TempoLimite = TimeSpan.FromMinutes(2);

    internal static readonly Uri Endereco = new("https://generativelanguage.googleapis.com/v1beta/");

    private readonly HttpClient _http;

    public ProvedorGemini(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>O cliente HTTP do programa: um só, com o tempo limite e conexões renovadas de tempos em tempos.</summary>
    public static HttpClient CriarHttpClient() =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TempoLimite };

    public string Nome => "Gemini (Google)";

    public string ModeloPadrao => ModeloRecomendado;

    public string NormalizarModelo(string modelo)
    {
        var nome = (modelo ?? "").Trim();
        if (nome.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            nome = nome["models/".Length..];
        if (nome.Length == 0)
            throw new ArgumentException("Informe o modelo do Gemini (por exemplo, gemini-2.5-flash).");
        if (nome.Length > 64 || !NomeDeModelo().IsMatch(nome))
            throw new ArgumentException($"\"{nome}\" não é um nome de modelo válido. Use um nome como gemini-2.5-flash.");
        return nome;
    }

    public IAssistenteIA CriarAssistente(string chave, string modelo) => new AssistenteGemini(_http, chave, NormalizarModelo(modelo));

    public async Task TestarAsync(string chave, string modelo, CancellationToken cancelamento)
    {
        ChamadaGemini.ValidarChave(chave);
        var nome = NormalizarModelo(modelo);
        using var pedido = ChamadaGemini.Pedido(HttpMethod.Get, $"models/{nome}", chave);
        using var _ = await ChamadaGemini.ExecutarAsync(_http, pedido, chave, nome, cancelamento);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.\\-]*$")]
    private static partial Regex NomeDeModelo();
}
