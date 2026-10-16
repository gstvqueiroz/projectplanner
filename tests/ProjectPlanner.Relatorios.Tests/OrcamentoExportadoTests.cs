using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 33: o orçamento em PDF, Word e Excel (sintética e analítica). Uma etapa com "itens" alvenarias de 10 m² na composição
// 1 (pedreiro 0,8 h a 28 e bloco 13,5 un a 1,20: custo 38,60/m²) e uma pintura de valor informado; BDI 25%.
public sealed class OrcamentoExportadoTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static (PlanilhaOrcamentaria Planilha, IReadOnlyDictionary<Guid, ComposicaoAberta> Composicoes) Orcamento(int itens = 2)
    {
        var tabela = new TabelaDeReferencia(new BaseDeReferencia("SINAPI", "SP", "09/2026", true),
            [new Insumo("88309", "PEDREIRO", "H", TipoDeInsumo.MaoDeObra, 28), new Insumo("7271", "BLOCO CERAMICO", "UN", TipoDeInsumo.Material, 1.2m)],
            [new Composicao("1", "ALVENARIA DE BLOCOS", "M2", [new(false, "88309", 0.8m), new(false, "7271", 13.5m)])]);
        var c = new Cronograma("Casa", Seg);
        var etapa = new Tarefa("Vedações", Seg, 1);
        c.AdicionarTarefa(etapa);
        for (var i = 0; i < itens; i++)
        {
            var t = new Tarefa($"Alvenaria {i + 1}", Seg, 2);
            c.AdicionarTarefa(t, etapa.Id);
            t.DefinirQuantidade(10);
            c.Orcamento.Associar(t, tabela.Abrir("1"));
        }
        var pintura = new Tarefa("Pintura", Seg, 2);
        c.AdicionarTarefa(pintura);
        pintura.DefinirValor(1500);
        c.Orcamento.DefinirConfiguracao(new ConfiguracaoDoOrcamento { Bdi = 25 });
        CalculadoraCronograma.Calcular(c);
        return (c.Orcamento.Planilha(), c.Orcamento.Servicos);
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    [Fact]
    public void Textos_DaPlanilhaEDaAnalitica()
    {
        var (p, composicoes) = Orcamento();

        TextosDoOrcamento.Celulas(p.Linhas[1]).Should().Equal("1.1", "1", "SINAPI", "  Alvenaria 1", "M2", "10,00", "38,60", "48,25", "482,50");
        TextosDoOrcamento.Celulas(p.Linhas[3]).Should().Equal("2", "", "informado", "Pintura", "", "", "", "", "1.500,00");
        TextosDoOrcamento.Totais(p).Select(t => (t.Rotulo, t.Valor)).Should().Equal(
            ("Custo direto (composições)", "R$ 772,00"), ("BDI 25%", "R$ 193,00"), ("Tarefas sem composição (valor informado)", "R$ 1.500,00"),
            ("Total do orçamento", "R$ 2.465,00"));
        TextosDoOrcamento.Rodape(p).Should().Be("Preços: SINAPI SP 09/2026 desonerado · BDI 25%");
        var analitica = TextosDoOrcamento.Analitica(p, composicoes).ToList();
        analitica.Should().HaveCount(2);
        TextosDoOrcamento.TituloDaComposicao(analitica[0].Linha, analitica[0].Composicao).Should()
            .Be("1.1 Alvenaria 1 — SINAPI 1: ALVENARIA DE BLOCOS (M2) · custo 38,60 · preço com BDI 48,25");
        TextosDoOrcamento.CelulasDoInsumo(analitica[0].Composicao.Insumos.Single(i => i.Codigo == "88309")).Should().Equal("88309", "PEDREIRO", "Mão de obra", "H", "0,8000", "28,00", "22,40");
    }

    [Fact]
    public void Pdf_SinteticaEAnalitica_EmPaginasProprias()
    {
        var (p, composicoes) = Orcamento();
        using var destino = new MemoryStream();

        new GeradorOrcamentoPdf().Gerar(p, composicoes, "Casa · gerado em 15/10/2026 10:00", destino);

        Encoding.ASCII.GetString(destino.ToArray(), 0, 5).Should().Be("%PDF-");
        Paginas(destino.ToArray()).Should().Be(2, "a sintética e a analítica");
    }

    [Fact]
    public void Pdf_MuitosItens_QuebraAsPaginas_ESemComposicoes_SoASintetica()
    {
        var (muitos, composicoes) = Orcamento(60);
        using var destino = new MemoryStream();
        new GeradorOrcamentoPdf().Gerar(muitos, composicoes, "Casa", destino);
        Paginas(destino.ToArray()).Should().BeGreaterThan(4);

        var (p, _) = Orcamento(0);
        using var soSintetica = new MemoryStream();
        new GeradorOrcamentoPdf().Gerar(p, new Dictionary<Guid, ComposicaoAberta>(), "Casa", soSintetica);
        Paginas(soSintetica.ToArray()).Should().Be(1);
    }

    [Fact]
    public void Word_ComAsDuasPartes()
    {
        var (p, composicoes) = Orcamento();
        using var destino = new MemoryStream();

        new GeradorOrcamentoWord().Gerar(p, composicoes, "Casa", destino);

        destino.Position = 0;
        using var documento = WordprocessingDocument.Open(destino, false);
        var texto = documento.MainDocumentPart!.Document!.Body!.InnerText;
        texto.Should().Contain("PLANILHA SINTÉTICA").And.Contain("482,50").And.Contain("R$ 2.465,00").And.Contain("COMPOSIÇÕES (ANALÍTICA)")
            .And.Contain("BLOCO CERAMICO");
    }

    [Fact]
    public void Excel_DuasPlanilhas_ComNumeros()
    {
        var (p, composicoes) = Orcamento();
        using var destino = new MemoryStream();

        new GeradorOrcamentoExcel().Gerar(p, composicoes, "Casa", destino);

        destino.Position = 0;
        using var documento = SpreadsheetDocument.Open(destino, false);
        var livro = documento.WorkbookPart!;
        livro.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value).Should().Equal("Sintética", "Analítica");
        var sintetica = (WorksheetPart)livro.GetPartById(livro.Workbook.Sheets!.Elements<Sheet>().First().Id!);
        var linha = sintetica.Worksheet!.Descendants<Row>().Single(r => r.Elements<Cell>().FirstOrDefault()?.InnerText == "1.1");
        linha.Elements<Cell>().Select(c => c.CellReference!.Value).First().Should().StartWith("A", "a linha começa na coluna A");
        linha.Elements<Cell>().Last().CellValue!.Text.Should().Be("482.50");
        var analitica = (WorksheetPart)livro.GetPartById(livro.Workbook.Sheets!.Elements<Sheet>().Last().Id!);
        analitica.Worksheet!.Descendants<Cell>().Select(c => c.CellValue?.Text).Should().Contain("0.8").And.Contain("38.60");
    }
}
