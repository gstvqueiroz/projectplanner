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

// Bloco 29: a página do texto do andamento e as de antes e depois, no PDF e no Word.
public class TextoEAntesEDepoisTests
{
    private static readonly DateTime Seg = new(2026, 9, 14);

    private static byte[] Jpeg(SKColor cor)
    {
        using var bitmap = new SKBitmap(160, 120);
        bitmap.Erase(cor);
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    // Uma tarefa com 4 fotos em 4 dias: 2 no Bloco A, 2 na Fachada (todas no período).
    private static (RelatorioFotografico Relatorio, Dictionary<Guid, byte[]> Imagens) Obra(TextoDoRelatorio? texto, bool antesEDepois)
    {
        var c = new Cronograma("Residência Silva", Seg);
        var t = new Tarefa("Fundação", Seg, 10);
        c.AdicionarTarefa(t);
        var imagens = new Dictionary<Guid, byte[]>();
        for (var i = 0; i < 4; i++)
        {
            var nome = $"IMG_{i}.jpg";
            var foto = new FotoTarefa(Guid.NewGuid(), t.Id,
                new DadosDaImagem(nome, 1600, 1200, 1000, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nome)))),
                new MetadadosFoto(Seg.AddDays(1 + i).AddHours(9), local: i % 2 == 0 ? "Bloco A" : "Fachada", percentualConcluidoNaData: 10 * (i + 1)),
                Seg);
            t.AdicionarFoto(foto);
            imagens[foto.Id] = Jpeg(new SKColor((byte)(60 * i), 120, 200));
        }
        CalculadoraCronograma.Calcular(c);
        var opcoes = new OpcoesDoRelatorio(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 30))
            { FotosPorPagina = 4, Texto = texto, ComAntesEDepois = antesEDepois };
        return (MontadorDoRelatorio.Montar(c, opcoes, new CabecalhoDoRelatorio(c.Nome, c.Obra, null, null, null), new DateTime(2026, 10, 1)), imagens);
    }

    private static byte[] Gerar(IGeradorDeRelatorio gerador, RelatorioFotografico r, Func<Guid, byte[]?> imagem)
    {
        using var destino = new MemoryStream();
        gerador.Gerar(r, imagem, destino);
        return destino.ToArray();
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    private static int Imagens(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Subtype\s*/Image").Count;

    private static readonly TextoDoRelatorio Texto = new("Na fundação, as sapatas foram concluídas.\nA alvenaria começou.", ["Laje atrasada", "Chuva prevista"]);

    [Fact]
    public void Pdf_TextoNumaPaginaDepoisDaCapa_EAntesEDepoisComDuasImagensCada()
    {
        var (sem, imagens) = Obra(null, false);
        var (com, _) = Obra(Texto, true);

        var pdfSem = Gerar(new GeradorPdf(), sem, id => imagens.GetValueOrDefault(id));
        var pdfCom = Gerar(new GeradorPdf(), com, id => imagens.GetValueOrDefault(id) ?? imagens.Values.First());

        com.AntesEDepois.Select(a => a.Local).Should().Equal("Bloco A", "Fachada");
        Paginas(pdfCom).Should().Be(Paginas(pdfSem) + 1 + 2).And.Be(com.TotalDePaginas);
        Imagens(pdfCom).Should().BeGreaterThanOrEqualTo(Imagens(pdfSem), "as imagens repetidas podem ser reaproveitadas no PDF");
    }

    [Fact]
    public void Pdf_TextoLongo_ContinuaNaPaginaSeguinte()
    {
        var longo = new TextoDoRelatorio(string.Join("\n", Enumerable.Repeat(string.Join(" ", Enumerable.Repeat("Concretagem das sapatas do eixo A.", 12)), 14)),
            Enumerable.Range(1, 10).Select(i => $"Ponto {i}"));
        var (r, imagens) = Obra(longo, false);
        var (sem, _) = Obra(null, false);

        var pdf = Gerar(new GeradorPdf(), r, id => imagens.GetValueOrDefault(id));

        (Paginas(pdf) - Paginas(Gerar(new GeradorPdf(), sem, id => imagens.GetValueOrDefault(id)))).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Pdf_AntesEDepois_SemImagem_QuadroSemErro()
    {
        var (r, _) = Obra(null, true);

        var pdf = Gerar(new GeradorPdf(), r, _ => null);

        Paginas(pdf).Should().Be(r.TotalDePaginas);
        Imagens(pdf).Should().Be(0);
    }

    [Fact]
    public void Word_Valido_ComOTextoEOsPontos_EAntesEDepoisEmTabela()
    {
        var (r, imagens) = Obra(Texto, true);

        using var destino = new MemoryStream(Gerar(new GeradorWord(), r, id => imagens.GetValueOrDefault(id)));
        using var documento = WordprocessingDocument.Open(destino, false);

        new OpenXmlValidator().Validate(documento).Select(e => e.Description).Should().BeEmpty();
        var corpo = documento.MainDocumentPart!.Document.Body!;
        corpo.InnerText.Should().Contain("Andamento do período").And.Contain("Na fundação, as sapatas foram concluídas.")
            .And.Contain("Pontos de atenção").And.Contain("• Laje atrasada").And.Contain("Antes e depois — Bloco A")
            .And.Contain("Antes · 15/09/2026 09:00 · 1 Fundação · 10% concluído").And.Contain("De 10% para 30% concluído em 2 dias");
        corpo.Descendants<Break>().Count(b => b.Type?.Value == BreakValues.Page).Should().Be(1 + r.Paginas.Count + 2 + 1);
        documento.MainDocumentPart.ImageParts.Should().HaveCount(4 + 4, "4 fotos nas páginas e 2 em cada antes e depois");
    }

    [Fact]
    public void Word_SemTextoNemAntesEDepois_ComoAntes()
    {
        var (r, imagens) = Obra(null, false);

        using var destino = new MemoryStream(Gerar(new GeradorWord(), r, id => imagens.GetValueOrDefault(id)));
        using var documento = WordprocessingDocument.Open(destino, false);

        documento.MainDocumentPart!.Document.Body!.InnerText.Should().NotContain("Andamento do período").And.NotContain("Antes e depois");
    }
}
