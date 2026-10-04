using System.Globalization;

namespace ProjectPlanner.IA;

/// <summary>
/// Que anexos o assistente consegue ler (Bloco 23) e quanto cabe num envio. PDF e imagens vão como arquivo; texto vai
/// lido, como parte da mensagem. Documentos do Word e do Excel ainda não: o usuário exporta como PDF.
/// </summary>
public static class AnexosAceitos
{
    /// <summary>
    /// Soma máxima dos anexos de uma conversa (todos vão a cada envio, para o assistente continuar enxergando-os).
    /// O Gemini aceita até 20 MB por pedido, e os arquivos vão em base64 (um terço maior).
    /// </summary>
    public const long TamanhoMaximoPorEnvio = 14L * 1024 * 1024;

    /// <summary>Para as mensagens: "PDF, imagens (PNG, JPG, WebP, HEIC) e texto (TXT, MD, CSV, JSON)".</summary>
    public const string Descricao = "PDF, imagens (PNG, JPG, WebP, HEIC) e texto (TXT, MD, CSV, JSON)";

    private static readonly Dictionary<string, string> Arquivos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["webp"] = "image/webp",
        ["heic"] = "image/heic",
        ["heif"] = "image/heif",
    };

    private static readonly HashSet<string> Textos = new(StringComparer.OrdinalIgnoreCase) { "txt", "md", "csv", "json" };

    /// <summary>As extensões aceitas, para o filtro do diálogo ("*.pdf", ...).</summary>
    public static IReadOnlyList<string> Padroes { get; } = [.. Arquivos.Keys.Concat(Textos).Select(e => "*." + e)];

    public static bool Aceita(AnexoIA anexo) => Arquivos.ContainsKey(anexo.Extensao) || Textos.Contains(anexo.Extensao);

    /// <summary>Texto puro: vai lido, dentro da mensagem.</summary>
    public static bool EhTexto(AnexoIA anexo) => Textos.Contains(anexo.Extensao);

    /// <summary>O tipo MIME de um anexo que vai como arquivo; nulo para texto ou formato não aceito.</summary>
    public static string? TipoMime(AnexoIA anexo) => Arquivos.GetValueOrDefault(anexo.Extensao);

    /// <summary>Por que o anexo não pode ir ("contrato.docx: o assistente ainda não lê arquivos .docx. …"); nulo se pode.</summary>
    public static string? Problema(AnexoIA anexo)
    {
        if (!Aceita(anexo))
        {
            var tipo = anexo.Extensao.Length == 0 ? "sem extensão" : "." + anexo.Extensao;
            return $"{anexo.Nome}: o assistente ainda não lê arquivos {tipo}. Use {Descricao}; " +
                   "um documento do Word ou do Excel pode ser exportado como PDF.";
        }
        if (anexo.TamanhoEmBytes > TamanhoMaximoPorEnvio)
            return $"{anexo.Nome}: o arquivo tem {Megabytes(anexo.TamanhoEmBytes)}; o limite é {Megabytes(TamanhoMaximoPorEnvio)} por envio.";
        return null;
    }

    /// <summary>"14 MB", "15,2 MB".</summary>
    public static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " MB";
}
