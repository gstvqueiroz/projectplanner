using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 36: o boletim de medição em PDF, Word e Excel.
public sealed class BoletimDeMedicaoTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);
    private static readonly DateTime Agora = new(2026, 10, 20, 9, 0, 0);

    private static readonly CabecalhoDoRelatorio Cabecalho = new("Residência Silva",
        new DadosDaObra("Maria Silva", "Contrato 12/2026", "Rua A, 10"), "Eng. João", "CREA 1234", null);

    // Uma etapa com "itens" itens (R$ 1.000 e 10 m² cada), todos medidos pela metade, com retenção, reajuste, glosa e observações.
    private static BoletimDeMedicao Boletim(int itens = 3)
    {
        var c = new Cronograma("Residência Silva", Seg);
        var etapa = new Tarefa("Alvenaria", Seg, 1);
        c.AdicionarTarefa(etapa);
        for (var i = 0; i < itens; i++)
        {
            var t = new Tarefa($"Parede {i + 1}", Seg, 2);
            c.AdicionarTarefa(t, etapa.Id);
            t.DefinirValor(1000);
            t.DefinirUnidade("m²");
            t.DefinirQuantidade(10);
        }
        CalculadoraCronograma.Calcular(c);
        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { PercentualDeRetencao = 5, NomeDoIndice = "INCC-DI", IndiceBase = 100 });
        c.Contrato.NovaMedicao(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 16));
        foreach (var t in c.Tarefas.Where(t => !t.EhResumo))
            c.Contrato.Medir(1, t.Id, 5);
        c.Contrato.DefinirIndice(1, 102);
        c.Contrato.DefinirGlosas(1, [new GlosaDaMedicao("Retrabalho na parede 1", 10)]);
        c.Contrato.DefinirObservacoes(1, "Medição conferida em campo com a fiscalização.");
        return c.Contrato.Aprovar(1, Agora);
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    [Fact]
    public void Pdf_UmaPagina_ComOFimJunto()
    {
        using var destino = new MemoryStream();

        new GeradorBoletimPdf().Gerar(Boletim(), Cabecalho, Agora, destino);

        var pdf = destino.ToArray();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Paginas(pdf).Should().Be(1);
    }

    [Fact]
    public void Pdf_MuitosItens_ContinuaNasPaginasSeguintes_ComPaginasExtras()
    {
        // Com o grupo e o total, 3 páginas de tabela, a última com 20 linhas: o resumo e as assinaturas não cabem nela.
        var itens = GeradorBoletimPdf.LinhasNaPrimeira + GeradorBoletimPdf.LinhasNasOutras + 18;
        var extras = 0;
        using var destino = new MemoryStream();

        new GeradorBoletimPdf().Gerar(Boletim(itens), Cabecalho, Agora, destino, [(_, area) => extras += area.Width > 0 ? 1 : 0]);

        Paginas(destino.ToArray()).Should().Be(3 + 1 + 1, "3 de tabela, uma do resumo (não coube) e a extra");
        extras.Should().Be(1);
    }

    [Fact]
    public void Word_Paisagem_ComTabelaResumoEAssinaturas()
    {
        using var destino = new MemoryStream();
        new GeradorBoletimWord().Gerar(Boletim(), Cabecalho, Agora, destino);

        destino.Position = 0;
        using var documento = WordprocessingDocument.Open(destino, false);
        var corpo = documento.MainDocumentPart!.Document!.Body!;
        var texto = corpo.InnerText;
        texto.Should().Contain("BOLETIM DE MEDIÇÃO Nº 01").And.Contain("Residência Silva").And.Contain("Contrato 12/2026")
            .And.Contain("Parede 2").And.Contain("Valor líquido a faturar").And.Contain("Retrabalho na parede 1")
            .And.Contain("Medição conferida em campo").And.Contain("Contratante (fiscalização)");
        corpo.Descendants<W.PageSize>().Single().Orient!.Value.Should().Be(W.PageOrientationValues.Landscape);
        var itens = corpo.Elements<W.Table>().Single(t => t.InnerText.Contains("Qtd. contratada"));
        itens.Elements<W.TableRow>().Should().HaveCount(1 + 4 + 1, "cabeçalho, o grupo e 3 itens, e o total");
        itens.Elements<W.TableRow>().First().TableRowProperties!.GetFirstChild<W.TableHeader>().Should().NotBeNull("o cabeçalho se repete nas páginas");
    }

    [Fact]
    public void Excel_NumerosDeVerdade_EOResumo()
    {
        var boletim = Boletim();
        using var destino = new MemoryStream();
        new GeradorBoletimExcel().Gerar(boletim, Cabecalho, Agora, destino);

        destino.Position = 0;
        using var documento = SpreadsheetDocument.Open(destino, false);
        var livro = documento.WorkbookPart!;
        livro.Workbook!.Descendants<Sheet>().Single().Name!.Value.Should().Be("BM 01");
        var celulas = livro.WorksheetParts.Single().Worksheet!.Descendants<Cell>().ToList();
        string Texto(Cell c) => c.InlineString?.InnerText ?? c.CellValue?.Text ?? "";
        var parede = celulas.Single(c => Texto(c) == "  Parede 1").CellReference!.Value![1..];
        var linha = celulas.Where(c => c.CellReference!.Value![1..] == parede).ToDictionary(c => c.CellReference!.Value![..1], Texto);
        (linha["C"], linha["D"], linha["E"], linha["H"], linha["K"], linha["N"]).Should().Be(("m²", "10", "100", "5", "500", "0.5"));
        celulas.Select(Texto).Should().Contain("Valor líquido a faturar");
        celulas.Where(c => c.DataType?.Value == CellValues.Number).Select(Texto).Should().Contain(boletim.Resumo.Liquido.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
