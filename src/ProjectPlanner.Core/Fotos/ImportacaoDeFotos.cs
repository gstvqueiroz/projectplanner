using System.Globalization;

namespace ProjectPlanner.Core.Fotos;

/// <summary>A imagem como fica guardada no projeto: a versão reduzida e a miniatura, em JPEG.</summary>
/// <param name="Largura">Largura da versão reduzida.</param>
/// <param name="Altura">Altura da versão reduzida.</param>
public sealed record ConteudoDeFoto(byte[] Imagem, byte[] Miniatura, int Largura, int Altura)
{
    public byte[] Imagem { get; } = Imagem is { Length: > 0 } ? Imagem : throw new ArgumentException("A imagem está vazia.", nameof(Imagem));
    public byte[] Miniatura { get; } = Miniatura is { Length: > 0 } ? Miniatura : throw new ArgumentException("A miniatura está vazia.", nameof(Miniatura));
}

/// <summary>De onde veio a data de uma foto importada.</summary>
public enum OrigemDaData
{
    /// <summary>EXIF: quando a foto foi tirada.</summary>
    ExifOriginal,

    /// <summary>EXIF: quando a foto foi digitalizada.</summary>
    ExifDigitalizacao,

    /// <summary>EXIF: data da imagem (a mais genérica).</summary>
    ExifImagem,

    /// <summary>Sem data no EXIF: a data de modificação do arquivo.</summary>
    ArquivoModificado,
}

/// <summary>Uma foto lida de um arquivo e já preparada para guardar.</summary>
public sealed record ImagemImportada(
    ConteudoDeFoto Conteudo, string NomeArquivoOriginal, string HashOriginal, DateTime DataFoto, OrigemDaData OrigemDaData);

/// <summary>
/// Lê fotos de arquivos e as prepara para guardar no projeto (Bloco 22): valida formato e tamanho, aplica a
/// orientação EXIF, reduz para <see cref="RegrasDeFoto.LadoMaiorGuardado"/> px em JPEG, gera a miniatura e lê
/// a data. A implementação fica fora do Core (ProjectPlanner.Imagens), para o domínio não depender de
/// bibliotecas de imagem.
/// </summary>
public interface IProcessadorDeImagens
{
    /// <exception cref="ImagemRecusadaException">Formato não aceito, arquivo grande demais, corrompido ou ilegível.</exception>
    ImagemImportada Importar(string caminhoArquivo);

    /// <summary>A imagem guardada (JPEG) girada 90°, com a miniatura refeita.</summary>
    /// <exception cref="ImagemRecusadaException">Os bytes não são uma imagem válida.</exception>
    ConteudoDeFoto Girar(byte[] imagem, bool sentidoHorario);
}

/// <summary>Uma foto que não pôde entrar no projeto, com o motivo para o usuário.</summary>
public sealed class ImagemRecusadaException(string mensagem, Exception? causa = null) : Exception(mensagem, causa);

/// <summary>As regras das fotos aceitas e de como ficam guardadas (D1, D1b e D5 do Bloco 22).</summary>
public static class RegrasDeFoto
{
    public const long TamanhoMaximoDoArquivo = 50L * 1024 * 1024;
    public const long MegapixelsMaximos = 100;
    public const int LadoMaiorGuardado = 1600;
    public const int QualidadeJpeg = 80;
    public const int LadoMaiorMiniatura = 256;
    public const int QualidadeMiniatura = 75;

    public static readonly IReadOnlySet<string> ExtensoesAceitas =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private static readonly IReadOnlySet<string> ExtensoesHeic =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif" };

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Recusa, antes de ler, formatos não aceitos (com orientação para HEIC), arquivos vazios e grandes demais.</summary>
    /// <exception cref="ImagemRecusadaException">O arquivo não pode ser importado.</exception>
    public static void ValidarArquivo(string nome, long tamanhoBytes)
    {
        var extensao = Path.GetExtension(nome);
        if (ExtensoesHeic.Contains(extensao))
            throw new ImagemRecusadaException($"{nome}: fotos HEIC ainda não são aceitas. No iPhone, use Ajustes > Câmera > " +
                                              "Formatos > Mais compatível, ou exporte a foto como JPEG.");
        if (!ExtensoesAceitas.Contains(extensao))
            throw new ImagemRecusadaException($"{nome}: formato não aceito. Use fotos JPG, PNG ou WebP.");
        if (tamanhoBytes <= 0)
            throw new ImagemRecusadaException($"{nome}: o arquivo está vazio.");
        if (tamanhoBytes > TamanhoMaximoDoArquivo)
            throw new ImagemRecusadaException($"{nome}: o arquivo tem {Megabytes(tamanhoBytes)} MB; o máximo é " +
                                              $"{TamanhoMaximoDoArquivo / (1024 * 1024)} MB.");
    }

    /// <exception cref="ImagemRecusadaException">A imagem tem mais de <see cref="MegapixelsMaximos"/> megapixels.</exception>
    public static void ValidarDimensoes(string nome, int largura, int altura)
    {
        var megapixels = (long)largura * altura / 1_000_000.0;
        if (megapixels > MegapixelsMaximos)
            throw new ImagemRecusadaException($"{nome}: a imagem tem {megapixels.ToString("0", Cultura)} megapixels; " +
                                              $"o máximo é {MegapixelsMaximos}.");
    }

    /// <summary>As dimensões depois de reduzir o lado maior a <paramref name="ladoMaior"/> (nunca amplia).</summary>
    public static (int Largura, int Altura) Reduzir(int largura, int altura, int ladoMaior)
    {
        if (largura <= 0 || altura <= 0)
            throw new ArgumentOutOfRangeException(nameof(largura), "As dimensões precisam ser positivas.");
        var maior = Math.Max(largura, altura);
        if (maior <= ladoMaior)
            return (largura, altura);
        var escala = (double)ladoMaior / maior;
        return (Math.Max(1, (int)Math.Round(largura * escala)), Math.Max(1, (int)Math.Round(altura * escala)));
    }

    private static string Megabytes(long bytes) => (bytes / (1024.0 * 1024)).ToString("0.0", Cultura);
}

/// <summary>Escolhe a data de uma foto importada (D2): EXIF quando existe, senão a data de modificação do arquivo.</summary>
public static class DataDaFoto
{
    public static (DateTime Data, OrigemDaData Origem) Escolher(
        DateTime? exifOriginal, DateTime? exifDigitalizacao, DateTime? exifImagem, DateTime modificacaoDoArquivo)
    {
        if (Valida(exifOriginal))
            return (exifOriginal!.Value, OrigemDaData.ExifOriginal);
        if (Valida(exifDigitalizacao))
            return (exifDigitalizacao!.Value, OrigemDaData.ExifDigitalizacao);
        if (Valida(exifImagem))
            return (exifImagem!.Value, OrigemDaData.ExifImagem);
        var arquivo = modificacaoDoArquivo < MetadadosFoto.DataMinima ? MetadadosFoto.DataMinima
            : modificacaoDoArquivo > MetadadosFoto.DataMaxima ? MetadadosFoto.DataMaxima
            : modificacaoDoArquivo;
        return (arquivo, OrigemDaData.ArquivoModificado);
    }

    // Câmeras sem relógio acertado gravam "0000:00:00" ou datas absurdas: valem como "sem data".
    private static bool Valida(DateTime? data) => data is { } d && d >= MetadadosFoto.DataMinima && d <= MetadadosFoto.DataMaxima;
}

/// <summary>
/// As imagens a gravar ao salvar o projeto: o arquivo de onde copiar as que não mudaram (o .pplan aberto ou
/// salvo por último; nulo num projeto novo) e as novas ou alteradas, que ainda só existem na memória.
/// </summary>
public sealed record ImagensParaSalvar(string? ArquivoDeOrigem, IReadOnlyDictionary<Guid, ConteudoDeFoto> Novas)
{
    /// <summary>Nenhuma imagem a gravar (projetos sem fotos, ou testes).</summary>
    public static readonly ImagensParaSalvar Nenhuma = new(null, new Dictionary<Guid, ConteudoDeFoto>());
}
