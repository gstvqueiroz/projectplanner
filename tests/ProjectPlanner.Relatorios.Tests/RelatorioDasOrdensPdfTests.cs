using System.Text;
using System.Text.RegularExpressions;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 64: o relatório das OS em PDF.
public sealed class RelatorioDasOrdensPdfTests
{
    private static readonly DateTime Agora = new(2026, 10, 1, 9, 0, 0);

    internal static List<OrdemDeServico> Ordens(int quantas)
    {
        var lista = new List<OrdemDeServico>();
        for (var n = 1; n <= quantas; n++)
        {
            var dados = new DadosDaOrdem($"Serviço {n}: alvenaria do pavimento {n}", TipoDeExecutante.Terceiro, n % 3 == 0 ? "Equipe do João" : "Construtora Silva",
                null, null, null, [new ItemDaOrdem(null, "Alvenaria", "m²", 100, 30)], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1).AddDays(n),
                FormaDePagamentoDaOrdem.PorMedicao, null, null, retencao: n % 2 == 0 ? 5 : 0);
            var os = OrdemDeServico.Nova(Guid.NewGuid(), n, dados, Agora);
            if (n % 4 != 0)
                os = os.Emitir(Agora).Aceitar(Agora);
            if (n % 4 == 1)
                os = os.Concluir(Agora.AddDays(n + 2)).MedirOQueFalta(Guid.NewGuid(), new DateOnly(2026, 10, 20), Agora).Conferir(Agora);
            lista.Add(os);
        }
        return lista;
    }

    private static readonly CabecalhoDoRelatorio Cabecalho = new("Residência Silva", new DadosDaObra("Maria Silva", null, null), "Eng. Ana Souza", "CREA 12345", null);

    private static byte[] Pdf(RelatorioDasOrdens r, bool detalhar = false)
    {
        using var destino = new MemoryStream();
        new GeradorRelatorioDasOrdensPdf().Gerar(r, Cabecalho, new Dictionary<Guid, string>(), "01/11/2026 10:00", destino, detalhar);
        return destino.ToArray();
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(6, 2)]
    [InlineData(60, 4)]
    public void Pdf_UmaPaginaOuMais(int ordens, int paginas)
    {
        var pdf = Pdf(RelatorioDasOrdens.Montar(Ordens(ordens), new FiltroDoRelatorioDasOrdens(), new DateOnly(2026, 11, 1)));

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Paginas(pdf).Should().Be(paginas);
    }

    // Bloco 65: com as medições e os pagamentos (os gráficos por mês) e o detalhe de cada OS no fim.
    [Fact]
    public void Pdf_ComGraficosEDetalhe_TemMaisPaginas()
    {
        var ordens = Ordens(8);
        ordens[0] = ordens[0].RegistrarPagamento(ordens[0].Medicoes[0].Id, new PagamentoDaMedicao(new DateOnly(2026, 10, 25), "NF 1", []), Agora);
        var r = RelatorioDasOrdens.Montar(ordens, new FiltroDoRelatorioDasOrdens(), new DateOnly(2026, 11, 1));

        r.PorMes.Should().Equal(new MesNoRelatorio(new DateOnly(2026, 10, 1), 6000, 3000));
        var simples = Paginas(Pdf(r));
        Paginas(Pdf(r, detalhar: true)).Should().BeGreaterThan(simples);
        File.WriteAllBytes("/private/tmp/claude-501/-Users-gustavodossantosqueiroz-Documents-Project/c210a034-3e6a-4dd5-9d65-1c296228fb41/scratchpad/relatorio-os-65.pdf", Pdf(r, detalhar: true));
    }

    [Fact]
    public void Boletim_PdfEExcel()
    {
        var os = Ordens(1)[0];
        var boletim = BoletimDaOrdem.Montar(os, os.Medicoes[0].Id);
        using var pdf = new MemoryStream();
        new GeradorBoletimDaOrdemPdf().Gerar(boletim, Cabecalho, new Dictionary<Guid, string>(), "01/11/2026 10:00", pdf);
        using var excel = new MemoryStream();
        new GeradorOrdensExcel().Boletim(boletim, Cabecalho, new Dictionary<Guid, string>(), Agora, excel);
        using var relatorio = new MemoryStream();
        new GeradorOrdensExcel().Relatorio(RelatorioDasOrdens.Montar(Ordens(6), new FiltroDoRelatorioDasOrdens(), new DateOnly(2026, 11, 1)), Cabecalho,
            new Dictionary<Guid, string>(), Agora, relatorio);

        Paginas(pdf.ToArray()).Should().Be(1);
        File.WriteAllBytes("/private/tmp/claude-501/-Users-gustavodossantosqueiroz-Documents-Project/c210a034-3e6a-4dd5-9d65-1c296228fb41/scratchpad/boletim-os.pdf", pdf.ToArray());
        relatorio.Position = 0;
        using var livro = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(relatorio, false);
        livro.WorkbookPart!.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(a => a.Name!.Value)
            .Should().Equal("Resumo", "OS", "Itens", "Medições", "Por mês");
        excel.Length.Should().BeGreaterThan(0);
    }
}
