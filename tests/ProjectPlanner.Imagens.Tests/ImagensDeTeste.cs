using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace ProjectPlanner.Imagens.Tests;

/// <summary>
/// Imagens pequenas feitas na hora para os testes: metade esquerda vermelha e direita azul (para ver a
/// orientação), com ou sem EXIF (data e orientação montados à mão, como uma câmera grava).
/// </summary>
public static class ImagensDeTeste
{
    public static SKBitmap Bitmap(int largura, int altura, bool transparenteNaDireita = false)
    {
        var bitmap = new SKBitmap(new SKImageInfo(largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var tela = new SKCanvas(bitmap);
        tela.Clear(SKColors.Transparent);
        using var vermelho = new SKPaint { Color = SKColors.Red };
        using var azul = new SKPaint { Color = transparenteNaDireita ? SKColors.Transparent : SKColors.Blue };
        tela.DrawRect(0, 0, largura / 2f, altura, vermelho);
        tela.DrawRect(largura / 2f, 0, largura / 2f, altura, azul);
        return bitmap;
    }

    public static byte[] Jpeg(int largura, int altura, int qualidade = 90)
    {
        using var bitmap = Bitmap(largura, altura);
        using var dados = bitmap.Encode(SKEncodedImageFormat.Jpeg, qualidade);
        return dados.ToArray();
    }

    public static byte[] Png(int largura, int altura, bool transparenteNaDireita = false)
    {
        using var bitmap = Bitmap(largura, altura, transparenteNaDireita);
        using var dados = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return dados.ToArray();
    }

    public static byte[] Webp(int largura, int altura)
    {
        using var bitmap = Bitmap(largura, altura);
        using var dados = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
        return dados.ToArray();
    }

    /// <summary>O JPEG com um segmento EXIF (APP1) logo depois do início, com orientação e/ou data original.</summary>
    public static byte[] ComExif(byte[] jpeg, ushort? orientacao = null, DateTime? dataOriginal = null)
    {
        var tiff = Tiff(orientacao, dataOriginal);
        var app1 = new List<byte> { 0xFF, 0xE1 };
        var tamanho = 2 + 6 + tiff.Length;
        app1.Add((byte)(tamanho >> 8));
        app1.Add((byte)tamanho);
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);
        return [.. jpeg.Take(2), .. app1, .. jpeg.Skip(2)];
    }

    // TIFF big-endian: IFD0 com Orientation (0x0112) e o ponteiro do Exif SubIFD (0x8769), onde fica
    // DateTimeOriginal (0x9003, ASCII "aaaa:mm:dd hh:mm:ss\0").
    private static byte[] Tiff(ushort? orientacao, DateTime? dataOriginal)
    {
        var entradas0 = (orientacao is null ? 0 : 1) + (dataOriginal is null ? 0 : 1);
        var inicioIfd0 = 8;
        var tamanhoIfd0 = 2 + entradas0 * 12 + 4;
        var inicioSub = inicioIfd0 + tamanhoIfd0;
        var tamanhoSub = dataOriginal is null ? 0 : 2 + 12 + 4;
        var inicioTexto = inicioSub + tamanhoSub;
        var texto = dataOriginal is { } d ? Encoding.ASCII.GetBytes(d.ToString("yyyy:MM:dd HH:mm:ss") + "\0") : [];

        var dados = new byte[inicioTexto + texto.Length];
        "MM"u8.CopyTo(dados);
        BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(dados.AsSpan(4), (uint)inicioIfd0);
        var p = inicioIfd0;
        BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(p), (ushort)entradas0);
        p += 2;
        if (orientacao is { } o)
        {
            Entrada(dados, p, 0x0112, 3, 1, (uint)o << 16); // SHORT, alinhado à esquerda
            p += 12;
        }
        if (dataOriginal is not null)
        {
            Entrada(dados, p, 0x8769, 4, 1, (uint)inicioSub);
            p += 12;
            BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(inicioSub), 1);
            Entrada(dados, inicioSub + 2, 0x9003, 2, (uint)texto.Length, (uint)inicioTexto);
            texto.CopyTo(dados, inicioTexto);
        }
        return dados;
    }

    /// <summary>Bloco 28: o JPEG com um EXIF só de GPS (graus decimais; negativos são Sul e Oeste).</summary>
    public static byte[] ComGps(byte[] jpeg, double latitude, double longitude)
    {
        // IFD0 com o ponteiro do GPS IFD (0x8825); o GPS IFD com LatitudeRef, Latitude, LongitudeRef e Longitude,
        // cada coordenada em três RATIONAL (graus, minutos, segundos × 1000).
        const int inicioIfd0 = 8, tamanhoIfd0 = 2 + 12 + 4, inicioGps = inicioIfd0 + tamanhoIfd0, tamanhoGps = 2 + 4 * 12 + 4;
        const int inicioLat = inicioGps + tamanhoGps, inicioLon = inicioLat + 24;
        var dados = new byte[inicioLon + 24];
        "MM"u8.CopyTo(dados);
        BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(dados.AsSpan(4), inicioIfd0);
        BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(inicioIfd0), 1);
        Entrada(dados, inicioIfd0 + 2, 0x8825, 4, 1, inicioGps);
        BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(inicioGps), 4);
        Entrada(dados, inicioGps + 2, 0x0001, 2, 2, (uint)(latitude < 0 ? 'S' : 'N') << 24);
        Entrada(dados, inicioGps + 14, 0x0002, 5, 3, inicioLat);
        Entrada(dados, inicioGps + 26, 0x0003, 2, 2, (uint)(longitude < 0 ? 'W' : 'E') << 24);
        Entrada(dados, inicioGps + 38, 0x0004, 5, 3, inicioLon);
        Graus(dados, inicioLat, Math.Abs(latitude));
        Graus(dados, inicioLon, Math.Abs(longitude));

        var app1 = new List<byte> { 0xFF, 0xE1, (byte)((8 + dados.Length) >> 8), (byte)(8 + dados.Length) };
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(dados);
        return [.. jpeg.Take(2), .. app1, .. jpeg.Skip(2)];
    }

    private static void Graus(byte[] dados, int p, double valor)
    {
        var graus = (uint)Math.Floor(valor);
        var minutos = (uint)Math.Floor((valor - graus) * 60);
        var segundos = (uint)Math.Round(((valor - graus) * 60 - minutos) * 60 * 1000);
        foreach (var (numerador, denominador) in new[] { (graus, 1u), (minutos, 1u), (segundos, 1000u) })
        {
            BinaryPrimitives.WriteUInt32BigEndian(dados.AsSpan(p), numerador);
            BinaryPrimitives.WriteUInt32BigEndian(dados.AsSpan(p + 4), denominador);
            p += 8;
        }
    }

    private static void Entrada(byte[] dados, int p, ushort tag, ushort tipo, uint quantidade, uint valor)
    {
        BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(p), tag);
        BinaryPrimitives.WriteUInt16BigEndian(dados.AsSpan(p + 2), tipo);
        BinaryPrimitives.WriteUInt32BigEndian(dados.AsSpan(p + 4), quantidade);
        BinaryPrimitives.WriteUInt32BigEndian(dados.AsSpan(p + 8), valor);
    }

    /// <summary>Um PNG que só declara as dimensões (cabeçalho IHDR válido, sem pixels): para testar o limite de megapixels.</summary>
    public static byte[] PngSoCabecalho(int largura, int altura)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, largura);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), altura);
        ihdr[8] = 8;   // profundidade
        ihdr[9] = 2;   // RGB
        // Um IDAT mínimo (zlib vazio): sem ele o leitor nem chega a aceitar o arquivo como PNG.
        return [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Pedaco("IHDR", ihdr),
            .. Pedaco("IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]), .. Pedaco("IEND", [])];
    }

    private static byte[] Pedaco(string tipo, byte[] conteudo)
    {
        var tipoBytes = Encoding.ASCII.GetBytes(tipo);
        var resultado = new byte[12 + conteudo.Length];
        BinaryPrimitives.WriteInt32BigEndian(resultado, conteudo.Length);
        tipoBytes.CopyTo(resultado, 4);
        conteudo.CopyTo(resultado, 8);
        BinaryPrimitives.WriteUInt32BigEndian(resultado.AsSpan(8 + conteudo.Length), Crc32([.. tipoBytes, .. conteudo]));
        return resultado;
    }

    private static uint Crc32(byte[] dados)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in dados)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
