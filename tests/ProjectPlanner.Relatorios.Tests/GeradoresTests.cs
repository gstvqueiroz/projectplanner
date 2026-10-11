using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 27: o relatório fotográfico desenhado em PDF e em Word.
public class GeradoresTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static byte[] Jpeg(int largura, int altura, SKColor cor)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(cor);
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    private static byte[] Png(int largura, int altura)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(SKColors.SteelBlue);
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    // Uma obra com duas tarefas e <paramref name="fotos"/> fotos, metade em cada; a primeira de destaque.
    private static (RelatorioFotografico Relatorio, Dictionary<Guid, byte[]> Imagens) Obra(int fotos, int porPagina = 2, bool logo = true,
        bool comIndice = true, bool comAssinaturas = true)
    {
        var cronograma = new Cronograma("Residência Silva — Casa 1", Seg);
        var escavacao = new Tarefa("Escavação", Seg, 3);
        var sapatas = new Tarefa("Sapatas", Seg, 5);
        cronograma.AdicionarTarefa(escavacao);
        cronograma.AdicionarTarefa(sapatas);
        cronograma.AdicionarPredecessora(new Predecessora(escavacao.Id, sapatas.Id));
        var imagens = new Dictionary<Guid, byte[]>();
        for (var i = 0; i < fotos; i++)
        {
            var tarefa = i % 2 == 0 ? escavacao : sapatas;
            var nome = $"IMG_{i:000}.jpg";
            var foto = new FotoTarefa(Guid.NewGuid(), tarefa.Id,
                    new DadosDaImagem(nome, i % 3 == 0 ? 1200 : 1600, i % 3 == 0 ? 1600 : 1200, 50_000,
                        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nome)))),
                    new MetadadosFoto(Seg.AddHours(8 + i), i % 2 == 0 ? $"Frente {i}" : null,
                        i == 1 ? "Concretagem das sapatas do eixo A, conferida a armadura e o cobrimento antes da concretagem." : null,
                        "Gustavo", "Bloco A", 40), Seg)
                .ComCuradoria(true, i == 0);
            tarefa.AdicionarFoto(foto);
            imagens[foto.Id] = Jpeg(i % 3 == 0 ? 120 : 160, i % 3 == 0 ? 160 : 120, new SKColor((byte)(40 * i % 255), 120, 200));
        }
        CalculadoraCronograma.Calcular(cronograma);
        cronograma.DefinirObra(new DadosDaObra("Sr. João Silva", "C-2026-014", "Rua das Flores, 100 — Campinas/SP"));
        var cabecalho = new CabecalhoDoRelatorio(cronograma.Nome, cronograma.Obra, "Eng. Ana Souza", "CREA-SP 123456", logo ? Png(300, 100) : null);
        var opcoes = new OpcoesDoRelatorio(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31))
            { FotosPorPagina = porPagina, ComIndice = comIndice, ComAssinaturas = comAssinaturas };
        return (MontadorDoRelatorio.Montar(cronograma, opcoes, cabecalho, new DateTime(2026, 10, 11, 10, 0, 0)), imagens);
    }

    private static byte[] Gerar(IGeradorDeRelatorio gerador, RelatorioFotografico relatorio, Func<Guid, byte[]?> imagem)
    {
        using var destino = new MemoryStream();
        gerador.Gerar(relatorio, imagem, destino);
        return destino.ToArray();
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    private static int Imagens(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Subtype\s*/Image").Count;

    // ---- PDF ----

    [Fact]
    public void Pdf_CapaFotosEIndice_UmaImagemPorFoto_EOLogo()
    {
        var (relatorio, imagens) = Obra(5);

        var pdf = Gerar(new GeradorPdf(), relatorio, id => imagens[id]);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        relatorio.Paginas.Should().HaveCount(3, "Escavação: 3 fotos (2 + 1); Sapatas: 2");
        Paginas(pdf).Should().Be(1 + 3 + 1).And.Be(relatorio.TotalDePaginas);
        Imagens(pdf).Should().Be(5 + 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    public void Pdf_TodasAsGrades(int porPagina)
    {
        var (relatorio, imagens) = Obra(7, porPagina);

        var pdf = Gerar(new GeradorPdf(), relatorio, id => imagens[id]);

        Paginas(pdf).Should().Be(1 + relatorio.Paginas.Count + 1);
        Imagens(pdf).Should().Be(8);
    }

    [Fact]
    public void Pdf_ImagemQueNaoDaParaLer_SaiUmQuadro_SemErro()
    {
        var (relatorio, imagens) = Obra(3, logo: false);
        var primeira = relatorio.Fotos.First().Foto.Id;

        var pdf = Gerar(new GeradorPdf(), relatorio, id => id == primeira ? null : id == relatorio.Fotos.Last().Foto.Id ? [1, 2, 3] : imagens[id]);

        Imagens(pdf).Should().Be(1, "só a do meio era uma imagem de verdade");
    }

    [Fact]
    public void Pdf_IndiceLongo_VaiParaMaisPaginas_EAssinaturasCabem()
    {
        var (relatorio, imagens) = Obra(90, porPagina: 6);

        var pdf = Gerar(new GeradorPdf(), relatorio, id => imagens[id]);

        var paginasDoIndice = Paginas(pdf) - 1 - relatorio.Paginas.Count;
        paginasDoIndice.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Pdf_SemFotos_SoCapaEAssinaturas()
    {
        var (relatorio, _) = Obra(0);

        Paginas(Gerar(new GeradorPdf(), relatorio, _ => null)).Should().Be(2);
        var (semIndice, _) = Obra(0, comIndice: false, comAssinaturas: false);
        Paginas(Gerar(new GeradorPdf(), semIndice, _ => null)).Should().Be(1);
    }

    [Fact]
    public void Quebrar_RespeitaALargura_ECortaPalavrasEnormes()
    {
        using var fonte = new SKFont(SKTypeface.Default, 10);

        var linhas = GeradorPdf.Quebrar("Concretagem das sapatas do eixo A " + new string('x', 200) + "\nfim", fonte, 120);

        linhas.Should().OnlyContain(l => fonte.MeasureText(l) <= 120);
        linhas[^1].Should().Be("fim");
        string.Concat(linhas).Replace(" ", "").Should().Contain(new string('x', 200));
    }

    [Fact]
    public void Encaixar_SemDistorcer()
    {
        var caixa = new SKRect(0, 0, 200, 100);

        var centralizada = GeradorPdf.Encaixar(1600, 1200, caixa, centralizar: true);
        (centralizada.Left, centralizada.Top, centralizada.Width, centralizada.Height).Should().BeEquivalentTo(
            (33.333f, 0f, 133.333f, 100f), o => o.Using<float>(c => c.Subject.Should().BeApproximately(c.Expectation, 0.01f)).WhenTypeIs<float>());
        GeradorPdf.Encaixar(400, 100, caixa, centralizar: false).Should().Be(new SKRect(0, 0, 200, 50));
    }

    // ---- Word ----

    private static WordprocessingDocument AbrirWord(byte[] docx) => WordprocessingDocument.Open(new MemoryStream(docx), false);

    [Fact]
    public void Word_ValidoPeloOpenXml_ComFotosLegendasEIndice()
    {
        var (relatorio, imagens) = Obra(5);

        var docx = Gerar(new GeradorWord(), relatorio, id => imagens[id]);

        using var documento = AbrirWord(docx);
        new OpenXmlValidator().Validate(documento).Select(e => e.Description).Should().BeEmpty();
        var principal = documento.MainDocumentPart!;
        principal.ImageParts.Should().HaveCount(5 + 1);
        var texto = principal.Document.Body!.InnerText;
        texto.Should().Contain("RELATÓRIO FOTOGRÁFICO").And.Contain("Residência Silva — Casa 1").And.Contain("Sr. João Silva")
            .And.Contain("Eng. Ana Souza (CREA-SP 123456)").And.Contain(relatorio.Fotos.First().Legenda)
            .And.Contain("Concretagem das sapatas do eixo A").And.Contain("Índice das fotos").And.Contain("Responsável técnico")
            .And.Contain("1 Escavação (continuação)");
        principal.Document.Body.Descendants<Break>().Count(b => b.Type?.Value == BreakValues.Page).Should().Be(relatorio.Paginas.Count + 1);
        principal.Document.Body.Descendants<GridSpan>().Should().BeEmpty("em 2 por página há uma coluna só");
        principal.FooterParts.Single().Footer.Descendants<FieldCode>().Select(f => f.Text.Trim()).Should().Equal("PAGE", "NUMPAGES");
    }

    [Fact]
    public void Word_QuatroPorPagina_DestaqueNumaLinhaInteira_EImagemQueFalta()
    {
        var (relatorio, imagens) = Obra(5, porPagina: 4, logo: false);
        var faltando = relatorio.Fotos.Last().Foto.Id;

        var docx = Gerar(new GeradorWord(), relatorio, id => id == faltando ? null : imagens[id]);

        using var documento = AbrirWord(docx);
        new OpenXmlValidator().Validate(documento).Should().BeEmpty();
        documento.MainDocumentPart!.Document.Body!.Descendants<GridSpan>().Single().Val!.Value.Should().Be(2);
        documento.MainDocumentPart.ImageParts.Should().HaveCount(4);
        documento.MainDocumentPart.Document.Body.InnerText.Should().Contain("[Imagem indisponível]");
    }

    [Fact]
    public void Word_SemIndiceNemAssinaturas_SoCapaEFotos()
    {
        var (relatorio, imagens) = Obra(2, comIndice: false, comAssinaturas: false);

        using var documento = AbrirWord(Gerar(new GeradorWord(), relatorio, id => imagens[id]));

        documento.MainDocumentPart!.Document.Body!.InnerText.Should().NotContain("Índice das fotos").And.NotContain("______");
        documento.MainDocumentPart.Document.Body.Descendants<Break>().Count(b => b.Type?.Value == BreakValues.Page).Should().Be(relatorio.Paginas.Count);
    }

    [Fact]
    public void Extensoes()
    {
        (new GeradorPdf().Extensao, new GeradorWord().Extensao).Should().Be(("pdf", "docx"));
    }
}
