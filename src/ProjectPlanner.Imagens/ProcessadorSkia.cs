using System.Security.Cryptography;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using ProjectPlanner.Core.Fotos;
using SkiaSharp;

namespace ProjectPlanner.Imagens;

/// <summary>
/// Prepara as fotos para guardar no projeto (Bloco 22, D1 e D5) com SkiaSharp: aceita JPG, PNG e WebP de até
/// <see cref="RegrasDeFoto.TamanhoMaximoDoArquivo"/> e <see cref="RegrasDeFoto.MegapixelsMaximos"/> MP; aplica a
/// orientação EXIF (a imagem guardada já fica de pé); reduz o lado maior para
/// <see cref="RegrasDeFoto.LadoMaiorGuardado"/> px (nunca amplia) e grava JPEG a
/// <see cref="RegrasDeFoto.QualidadeJpeg"/>%, com transparência sobre fundo branco; gera a miniatura de
/// <see cref="RegrasDeFoto.LadoMaiorMiniatura"/> px; e lê a data EXIF (MetadataExtractor), com a data de
/// modificação do arquivo como reserva.
/// </summary>
public sealed class ProcessadorSkia : IProcessadorDeImagens
{
    private static readonly SKSamplingOptions Amostragem = new(SKCubicResampler.Mitchell);

    public ImagemImportada Importar(string caminhoArquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoArquivo);
        var arquivo = new FileInfo(caminhoArquivo);
        var nome = arquivo.Name;
        if (!arquivo.Exists)
            throw new ImagemRecusadaException($"{nome}: o arquivo não foi encontrado.");
        RegrasDeFoto.ValidarArquivo(nome, arquivo.Length);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(arquivo.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImagemRecusadaException($"{nome}: não foi possível ler o arquivo ({ex.Message}).", ex);
        }

        using var original = Decodificar(nome, bytes, RegrasDeFoto.LadoMaiorGuardado, out var orientacao);
        using var reduzida = Reduzir(original, RegrasDeFoto.LadoMaiorGuardado);
        using var final = Orientar(reduzida, orientacao);
        var conteudo = Conteudo(final);

        var diretorios = Metadados(bytes);
        var (exifOriginal, exifDigitalizacao, exifImagem) = DatasExif(diretorios);
        var (data, origem) = DataDaFoto.Escolher(exifOriginal, exifDigitalizacao, exifImagem, arquivo.LastWriteTime);
        return new ImagemImportada(conteudo, nome, Convert.ToHexStringLower(SHA256.HashData(bytes)), data, origem, Gps(diretorios));
    }

    public ConteudoDeFoto Girar(byte[] imagem, bool sentidoHorario)
    {
        ArgumentNullException.ThrowIfNull(imagem);
        using var codec = SKCodec.Create(new SKMemoryStream(imagem))
                          ?? throw new ImagemRecusadaException("A imagem guardada da foto está corrompida.");
        using var decodificada = Decodificar(codec, codec.Info.Size)
                                 ?? throw new ImagemRecusadaException("A imagem guardada da foto está corrompida.");
        using var girada = Orientar(decodificada, sentidoHorario ? SKEncodedOrigin.RightTop : SKEncodedOrigin.LeftBottom);
        return Conteudo(girada);
    }

    // Decodifica já reduzindo o que o formato permite (JPEG decodifica a 1/2, 1/4, 1/8), para uma foto de 48 MP
    // não ocupar centenas de MB de memória.
    private static SKBitmap Decodificar(string nome, byte[] bytes, int ladoMaior, out SKEncodedOrigin orientacao)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(bytes))
                          ?? throw new ImagemRecusadaException($"{nome}: o arquivo não é uma imagem válida (está corrompido ou não é uma foto).");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new ImagemRecusadaException($"{nome}: formato não aceito ({codec.EncodedFormat}). Use fotos JPG, PNG ou WebP.");
        RegrasDeFoto.ValidarDimensoes(nome, codec.Info.Width, codec.Info.Height);
        orientacao = codec.EncodedOrigin;

        var escala = Math.Min(1f, (float)ladoMaior / Math.Max(codec.Info.Width, codec.Info.Height));
        var tamanho = codec.GetScaledDimensions(escala);
        if (Math.Max(tamanho.Width, tamanho.Height) < Math.Min(ladoMaior, Math.Max(codec.Info.Width, codec.Info.Height)))
            tamanho = codec.Info.Size; // o formato não reduz até o tamanho pedido sem passar do ponto

        var bitmap = Decodificar(codec, tamanho);
        if (bitmap is null && tamanho != codec.Info.Size)
            bitmap = Decodificar(codec, codec.Info.Size);
        return bitmap ?? throw new ImagemRecusadaException($"{nome}: o arquivo está corrompido ou incompleto.");
    }

    private static SKBitmap? Decodificar(SKCodec codec, SKSizeI tamanho)
    {
        var info = new SKImageInfo(tamanho.Width, tamanho.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) == SKCodecResult.Success)
            return bitmap;
        bitmap.Dispose();
        return null;
    }

    private static SKBitmap Reduzir(SKBitmap bitmap, int ladoMaior)
    {
        var (largura, altura) = RegrasDeFoto.Reduzir(bitmap.Width, bitmap.Height, ladoMaior);
        return largura == bitmap.Width && altura == bitmap.Height
            ? bitmap.Copy()
            : bitmap.Resize(new SKImageInfo(largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul), Amostragem);
    }

    // Desenha a imagem na orientação certa sobre fundo branco (o JPEG não tem transparência). As origens 5 a 8
    // trocam largura e altura. Cada matriz leva o ponto (x, y) da imagem para o lugar dele no resultado.
    internal static SKBitmap Orientar(SKBitmap origem, SKEncodedOrigin orientacao)
    {
        float l = origem.Width, a = origem.Height;
        var (matriz, troca) = orientacao switch
        {
            SKEncodedOrigin.TopRight => (Matriz(-1, 0, l, 0, 1, 0), false),
            SKEncodedOrigin.BottomRight => (Matriz(-1, 0, l, 0, -1, a), false),
            SKEncodedOrigin.BottomLeft => (Matriz(1, 0, 0, 0, -1, a), false),
            SKEncodedOrigin.LeftTop => (Matriz(0, 1, 0, 1, 0, 0), true),
            SKEncodedOrigin.RightTop => (Matriz(0, -1, a, 1, 0, 0), true),
            SKEncodedOrigin.RightBottom => (Matriz(0, -1, a, -1, 0, l), true),
            SKEncodedOrigin.LeftBottom => (Matriz(0, 1, 0, -1, 0, l), true),
            _ => (SKMatrix.Identity, false),
        };
        var resultado = new SKBitmap(new SKImageInfo(troca ? origem.Height : origem.Width, troca ? origem.Width : origem.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul));
        using var tela = new SKCanvas(resultado);
        tela.Clear(SKColors.White);
        tela.SetMatrix(matriz);
        tela.DrawBitmap(origem, 0, 0);
        return resultado;
    }

    private static SKMatrix Matriz(float escalaX, float inclinaX, float moveX, float inclinaY, float escalaY, float moveY) =>
        new() { ScaleX = escalaX, SkewX = inclinaX, TransX = moveX, SkewY = inclinaY, ScaleY = escalaY, TransY = moveY, Persp2 = 1 };

    private static ConteudoDeFoto Conteudo(SKBitmap imagem)
    {
        using var miniatura = Reduzir(imagem, RegrasDeFoto.LadoMaiorMiniatura);
        return new ConteudoDeFoto(Jpeg(imagem, RegrasDeFoto.QualidadeJpeg), Jpeg(miniatura, RegrasDeFoto.QualidadeMiniatura),
            imagem.Width, imagem.Height);
    }

    private static byte[] Jpeg(SKBitmap bitmap, int qualidade)
    {
        using var dados = bitmap.Encode(SKEncodedImageFormat.Jpeg, qualidade)
                          ?? throw new ImagemRecusadaException("Não foi possível gravar a imagem em JPEG.");
        return dados.ToArray();
    }

    private static IReadOnlyList<MetadataExtractor.Directory> Metadados(byte[] bytes)
    {
        try
        {
            return ImageMetadataReader.ReadMetadata(new MemoryStream(bytes));
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or FormatException or ArgumentException)
        {
            return []; // sem EXIF legível: fica a data do arquivo, e sem GPS
        }
    }

    private static (DateTime? Original, DateTime? Digitalizacao, DateTime? Imagem) DatasExif(IReadOnlyList<MetadataExtractor.Directory> diretorios)
    {
        var sub = diretorios.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var ifd0 = diretorios.OfType<ExifIfd0Directory>().FirstOrDefault();
        return (Data(sub, ExifDirectoryBase.TagDateTimeOriginal), Data(sub, ExifDirectoryBase.TagDateTimeDigitized),
            Data(ifd0, ExifDirectoryBase.TagDateTime));
    }

    // Bloco 28: o GPS do EXIF; câmeras sem sinal gravam 0,0 ou valores impossíveis, que valem como "sem GPS".
    internal static Coordenadas? Gps(IReadOnlyList<MetadataExtractor.Directory> diretorios)
    {
        try
        {
            if (diretorios.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation() is not { } local
                || (local.Latitude == 0 && local.Longitude == 0))
                return null;
            return new Coordenadas(local.Latitude, local.Longitude);
        }
        catch (Exception ex) when (ex is MetadataException or FormatException or ArgumentException or ProjectPlanner.Core.DomainValidationException)
        {
            return null;
        }
    }

    private static DateTime? Data(MetadataExtractor.Directory? diretorio, int tag) =>
        diretorio is not null && diretorio.TryGetDateTime(tag, out var data) ? data : null;
}
