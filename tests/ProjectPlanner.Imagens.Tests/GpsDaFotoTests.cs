using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.Imagens.Tests;

// Bloco 28 (FT4): o GPS do EXIF vem junto com a foto importada.
public sealed class GpsDaFotoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-gps-" + Guid.NewGuid().ToString("N"));
    private readonly ProcessadorSkia _processador = new();

    public GpsDaFotoTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private ImagemImportada Importar(byte[] bytes)
    {
        var caminho = Path.Combine(_pasta, Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(caminho, bytes);
        return _processador.Importar(caminho);
    }

    [Theory]
    [InlineData(-22.90581, -47.06263)] // Campinas: Sul e Oeste
    [InlineData(48.85837, 2.29448)]    // Paris: Norte e Leste
    public void JpegComGps_TrazAsCoordenadas(double latitude, double longitude)
    {
        var importada = Importar(ImagensDeTeste.ComGps(ImagensDeTeste.Jpeg(200, 150), latitude, longitude));

        importada.Gps.Should().NotBeNull();
        importada.Gps!.Latitude.Should().BeApproximately(latitude, 0.00001);
        importada.Gps.Longitude.Should().BeApproximately(longitude, 0.00001);
    }

    [Fact]
    public void SemExif_OuGpsZerado_SemCoordenadas()
    {
        Importar(ImagensDeTeste.Jpeg(200, 150)).Gps.Should().BeNull();
        Importar(ImagensDeTeste.ComExif(ImagensDeTeste.Jpeg(200, 150), orientacao: 1)).Gps.Should().BeNull();
        Importar(ImagensDeTeste.ComGps(ImagensDeTeste.Jpeg(200, 150), 0, 0)).Gps.Should().BeNull("câmera sem sinal grava 0,0");
    }

    [Fact]
    public void PngSemExif_SemCoordenadas()
    {
        var caminho = Path.Combine(_pasta, "planta.png");
        File.WriteAllBytes(caminho, ImagensDeTeste.Png(100, 80));

        _processador.Importar(caminho).Gps.Should().BeNull();
    }
}
