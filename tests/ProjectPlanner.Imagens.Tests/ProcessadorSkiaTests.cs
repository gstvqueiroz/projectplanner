using System.Security.Cryptography;
using ProjectPlanner.Core.Fotos;
using SkiaSharp;

namespace ProjectPlanner.Imagens.Tests;

// Bloco 22: o processamento das fotos com SkiaSharp + MetadataExtractor.
public sealed class ProcessadorSkiaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-imagens-" + Guid.NewGuid().ToString("N"));
    private readonly ProcessadorSkia _processador = new();
    private static readonly DateTime DataDoArquivo = new(2026, 10, 2, 8, 30, 0);

    public ProcessadorSkiaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Arquivo(string nome, byte[] bytes)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, bytes);
        File.SetLastWriteTime(caminho, DataDoArquivo);
        return caminho;
    }

    private static SKBitmap Decodificar(byte[] jpeg) => SKBitmap.Decode(jpeg)!;

    private static bool Vermelho(SKColor c) => c.Red > 180 && c.Blue < 90;
    private static bool Azul(SKColor c) => c.Blue > 180 && c.Red < 90;

    [Fact]
    public void JpegPequenoSemExif_GuardaDoMesmoTamanho_DataDoArquivo_EHashDoOriginal()
    {
        var bytes = ImagensDeTeste.Jpeg(400, 300);

        var importada = _processador.Importar(Arquivo("obra.jpg", bytes));

        (importada.Conteudo.Largura, importada.Conteudo.Altura).Should().Be((400, 300), "não amplia");
        (importada.DataFoto, importada.OrigemDaData).Should().Be((DataDoArquivo, OrigemDaData.ArquivoModificado));
        importada.HashOriginal.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        importada.NomeArquivoOriginal.Should().Be("obra.jpg");
        using var miniatura = Decodificar(importada.Conteudo.Miniatura);
        (miniatura.Width, miniatura.Height).Should().Be((256, 192));
        SKCodec.Create(new SKMemoryStream(importada.Conteudo.Imagem))!.EncodedFormat.Should().Be(SKEncodedImageFormat.Jpeg);
    }

    [Fact]
    public void JpegGrande_ReduzidoA1600PeloLadoMaior()
    {
        var importada = _processador.Importar(Arquivo("grande.jpg", ImagensDeTeste.Jpeg(3200, 2400)));

        (importada.Conteudo.Largura, importada.Conteudo.Altura).Should().Be((1600, 1200));
        using var imagem = Decodificar(importada.Conteudo.Imagem);
        (imagem.Width, imagem.Height).Should().Be((1600, 1200));
        importada.Conteudo.Imagem.Length.Should().BeLessThan(ImagensDeTeste.Jpeg(3200, 2400).Length);
    }

    [Fact]
    public void JpegComExif_UsaADataOriginal_EAplicaAOrientacao()
    {
        var bytes = ImagensDeTeste.ComExif(ImagensDeTeste.Jpeg(400, 300), orientacao: 6, dataOriginal: new DateTime(2026, 9, 28, 7, 15, 30));

        var importada = _processador.Importar(Arquivo("celular.jpg", bytes));

        (importada.DataFoto, importada.OrigemDaData).Should().Be((new DateTime(2026, 9, 28, 7, 15, 30), OrigemDaData.ExifOriginal));
        (importada.Conteudo.Largura, importada.Conteudo.Altura).Should().Be((300, 400), "a foto de celular de pé fica de pé");
        using var imagem = Decodificar(importada.Conteudo.Imagem);
        Vermelho(imagem.GetPixel(150, 20)).Should().BeTrue("girada 90° no sentido horário, a esquerda vai para cima");
        Azul(imagem.GetPixel(150, 380)).Should().BeTrue();
    }

    [Fact]
    public void JpegComExifSoDeOrientacao_DataDoArquivo()
    {
        var importada = _processador.Importar(Arquivo("o3.jpg", ImagensDeTeste.ComExif(ImagensDeTeste.Jpeg(400, 300), orientacao: 3)));

        importada.OrigemDaData.Should().Be(OrigemDaData.ArquivoModificado);
        using var imagem = Decodificar(importada.Conteudo.Imagem);
        Azul(imagem.GetPixel(20, 150)).Should().BeTrue("orientação 3 é de ponta-cabeça: a direita vai para a esquerda");
    }

    [Fact]
    public void Png_Aceito_TransparenciaViraBranco()
    {
        var importada = _processador.Importar(Arquivo("planta.png", ImagensDeTeste.Png(200, 100, transparenteNaDireita: true)));

        using var imagem = Decodificar(importada.Conteudo.Imagem);
        Vermelho(imagem.GetPixel(20, 50)).Should().BeTrue();
        var direita = imagem.GetPixel(180, 50);
        (direita.Red > 240 && direita.Green > 240 && direita.Blue > 240).Should().BeTrue("fundo branco onde era transparente");
    }

    [Fact]
    public void Webp_Aceito()
    {
        _processador.Importar(Arquivo("foto.webp", ImagensDeTeste.Webp(320, 240))).Conteudo.Largura.Should().Be(320);
    }

    [Fact]
    public void ArquivoCorrompido_Recusado()
    {
        var bytes = new byte[5000];
        new Random(7).NextBytes(bytes);

        var importar = () => _processador.Importar(Arquivo("estragada.jpg", bytes));

        importar.Should().Throw<ImagemRecusadaException>().WithMessage("estragada.jpg: o arquivo não é uma imagem válida*");
    }

    [Fact]
    public void JpegCortadoPelaMetade_Recusado()
    {
        var bytes = ImagensDeTeste.Jpeg(800, 600);

        var importar = () => _processador.Importar(Arquivo("cortada.jpg", bytes[..(bytes.Length / 2)]));

        importar.Should().Throw<ImagemRecusadaException>().WithMessage("cortada.jpg: *corrompido*");
    }

    [Fact]
    public void Heic_RecusadoComOrientacao()
    {
        var importar = () => _processador.Importar(Arquivo("IMG_0042.HEIC", [1, 2, 3]));

        importar.Should().Throw<ImagemRecusadaException>().WithMessage("*HEIC ainda não são aceitas*");
    }

    [Fact]
    public void GifRenomeadoParaJpg_Recusado()
    {
        // GIF mínimo de 1x1 (o Skia não grava GIF): cabeçalho GIF89a, sem animação.
        byte[] gif = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x21, 0xF9, 4, 1, 0, 0, 0, 0,
            0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0, 0x3B];

        var importar = () => _processador.Importar(Arquivo("falso.jpg", gif));

        importar.Should().Throw<ImagemRecusadaException>().WithMessage("falso.jpg: formato não aceito*");
    }

    [Fact]
    public void ImagemComMaisDe100Megapixels_RecusadaAntesDeDecodificar()
    {
        var importar = () => _processador.Importar(Arquivo("gigante.png", ImagensDeTeste.PngSoCabecalho(12_000, 10_000)));

        importar.Should().Throw<ImagemRecusadaException>().WithMessage("gigante.png: a imagem tem 120 megapixels; o máximo é 100.");
    }

    [Fact]
    public void ArquivoQueNaoExiste_Recusado()
    {
        var importar = () => _processador.Importar(Path.Combine(_pasta, "sumiu.jpg"));

        importar.Should().Throw<ImagemRecusadaException>().WithMessage("sumiu.jpg: o arquivo não foi encontrado.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Girar_TrocaAsDimensoes_EMoveOsLados(bool horario)
    {
        var conteudo = _processador.Importar(Arquivo("a.jpg", ImagensDeTeste.Jpeg(400, 300))).Conteudo;

        var girada = _processador.Girar(conteudo.Imagem, horario);

        (girada.Largura, girada.Altura).Should().Be((300, 400));
        using var imagem = Decodificar(girada.Imagem);
        var (cima, baixo) = (imagem.GetPixel(150, 20), imagem.GetPixel(150, 380));
        (horario ? Vermelho(cima) && Azul(baixo) : Azul(cima) && Vermelho(baixo)).Should().BeTrue();
        using var miniatura = Decodificar(girada.Miniatura);
        (miniatura.Width, miniatura.Height).Should().Be((192, 256));
    }

    [Fact]
    public void Girar_ConteudoInvalido_Recusado()
    {
        var girar = () => _processador.Girar([1, 2, 3], true);

        girar.Should().Throw<ImagemRecusadaException>();
    }

    [Fact]
    public void Orientar_TodasAsOrigens_DimensoesCertas()
    {
        using var origem = ImagensDeTeste.Bitmap(40, 20);
        foreach (var orientacao in Enum.GetValues<SKEncodedOrigin>())
        {
            using var resultado = ProcessadorSkia.Orientar(origem, orientacao);
            var troca = orientacao is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            (resultado.Width, resultado.Height).Should().Be(troca ? (20, 40) : (40, 20), orientacao.ToString());
        }
    }
}
