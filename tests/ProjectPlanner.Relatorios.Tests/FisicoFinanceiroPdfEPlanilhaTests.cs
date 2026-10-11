using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 35: o físico-financeiro em PDF e a leitura de planilhas (CSV e Excel) para importar valores.
public sealed class FisicoFinanceiroPdfEPlanilhaTests : IDisposable
{
    private static readonly DateTime Seg = new(2026, 9, 28);
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-planilha-" + Guid.NewGuid().ToString("N"));

    public FisicoFinanceiroPdfEPlanilhaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static (CronogramaFisicoFinanceiro Tabela, CurvaS Curva) Obra(int etapas, Periodicidade periodicidade, bool comValores = true)
    {
        var c = new Cronograma("Residência Silva", Seg);
        Tarefa? anterior = null;
        for (var i = 0; i < etapas; i++)
        {
            var tarefa = new Tarefa($"Etapa {i + 1}", Seg, 5);
            c.AdicionarTarefa(tarefa);
            if (comValores)
                tarefa.DefinirValor(1000 * (i + 1));
            if (anterior is not null)
                c.AdicionarPredecessora(new Predecessora(anterior.Id, tarefa.Id));
            anterior = tarefa;
        }
        CalculadoraCronograma.Calcular(c);
        c.SalvarLinhaDeBase(null, Seg);
        var (inicio, fim) = CalculadoraFisicoFinanceira.Intervalo(c);
        var periodos = PeriodosFinanceiros.Gerar(inicio, fim, periodicidade);
        return (CalculadoraFisicoFinanceira.Calcular(c, BaseDoFisicoFinanceiro.Atual, periodos, LinhasDoFisicoFinanceiro.Etapas),
            CurvaS.Montar(c, periodos, DateOnly.FromDateTime(Seg.AddDays(10))));
    }

    private static byte[] Pdf(CronogramaFisicoFinanceiro t, CurvaS c)
    {
        using var destino = new MemoryStream();
        new GeradorFisicoFinanceiroPdf().Gerar(t, c, "Residência Silva", "Cronograma atual · mensal", destino);
        return destino.ToArray();
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    [Fact]
    public void Pdf_PequenoNumaPaginaDeTabela_EACurvaS()
    {
        var (t, c) = Obra(3, Periodicidade.Mensal);

        var pdf = Pdf(t, c);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Paginas(pdf).Should().Be(2);
    }

    [Fact]
    public void Pdf_MuitosPeriodosEEtapas_DivideEmPaginas()
    {
        var (t, c) = Obra(GeradorFisicoFinanceiroPdf.LinhasPorPagina + 2, Periodicidade.Semanal, comValores: false);

        var pdf = Pdf(t, c);

        var colunas = (int)Math.Ceiling(t.Periodos.Count / (double)GeradorFisicoFinanceiroPdf.PeriodosPorPagina);
        colunas.Should().BeGreaterThan(1);
        Paginas(pdf).Should().Be(colunas * 2 + 1);
    }

    // ---- CSV ----

    [Fact]
    public void Csv_PontoEVirgulaComAspas_EUtf8()
    {
        var linhas = LeitorDePlanilha.LerCsv(Encoding.UTF8.GetBytes("﻿Item;Descrição;Valor\r\n1.1;\"Escavação; manual\";\"1.234,56\"\r\n1.2;\"Dito \"\"aspas\"\"\";10\n"));

        linhas.Should().HaveCount(3);
        linhas[0].Should().Equal("Item", "Descrição", "Valor");
        linhas[1].Should().Equal("1.1", "Escavação; manual", "1.234,56");
        linhas[2].Should().Equal("1.2", "Dito \"aspas\"", "10");
    }

    [Fact]
    public void Csv_VirgulaETabulacao_ELatin1()
    {
        LeitorDePlanilha.LerCsv(Encoding.UTF8.GetBytes("EDT,Valor\n1,100")).Should().HaveCount(2).And.Contain(l => l.SequenceEqual(new[] { "1", "100" }));
        LeitorDePlanilha.LerCsv(Encoding.UTF8.GetBytes("EDT\tValor\n1\t100"))[1].Should().Equal("1", "100");
        LeitorDePlanilha.LerCsv(Encoding.Latin1.GetBytes("EDT;Descrição\n1;Fundação"))[1].Should().Equal("1", "Fundação");
    }

    // ---- Excel ----

    private string Xlsx(params object?[][] linhas)
    {
        var caminho = Path.Combine(_pasta, "planilha.xlsx");
        using var documento = SpreadsheetDocument.Create(caminho, SpreadsheetDocumentType.Workbook);
        var livro = documento.AddWorkbookPart();
        livro.Workbook = new Workbook();
        var parte = livro.AddNewPart<WorksheetPart>();
        var dados = new SheetData();
        var textos = livro.AddNewPart<SharedStringTablePart>();
        textos.SharedStringTable = new SharedStringTable();
        for (var l = 0; l < linhas.Length; l++)
        {
            var linha = new Row { RowIndex = (uint)(l + 1) };
            for (var c = 0; c < linhas[l].Length; c++)
            {
                var referencia = $"{(char)('A' + c)}{l + 1}";
                switch (linhas[l][c])
                {
                    case null:
                        continue;
                    case string texto:
                        textos.SharedStringTable.Append(new SharedStringItem(new Text(texto)));
                        linha.Append(new Cell { CellReference = referencia, DataType = CellValues.SharedString,
                            CellValue = new CellValue((textos.SharedStringTable.Count() - 1).ToString()) });
                        break;
                    case double numero:
                        linha.Append(new Cell { CellReference = referencia, CellValue = new CellValue(numero) });
                        break;
                }
            }
            dados.Append(linha);
        }
        parte.Worksheet = new Worksheet(dados);
        livro.Workbook.AppendChild(new Sheets(new Sheet { Id = livro.GetIdOfPart(parte), SheetId = 1, Name = "Orçamento" }));
        return caminho;
    }

    [Fact]
    public void Excel_TextosNumerosECelulasVazias()
    {
        var caminho = Xlsx(["Item", "Descrição", "Total"], ["1.1", null, 1234.5], ["1.2", "Sapatas", 10000.0]);

        var linhas = LeitorDePlanilha.Ler(caminho);

        linhas.Should().HaveCount(3);
        linhas[1].Should().Equal("1.1", "", "1234,5");
        linhas[2].Should().Equal("1.2", "Sapatas", "10000");
        Moeda.Ler(linhas[1][2]).Should().Be(1234.5m, "o número sai no formato que o leitor de reais entende");
    }

    [Fact]
    public void Arquivo_QueNaoEPlanilha_OuFormatoNaoAceito()
    {
        var falso = Path.Combine(_pasta, "falso.xlsx");
        File.WriteAllText(falso, "não sou um xlsx");
        var outro = Path.Combine(_pasta, "x.pdf");
        File.WriteAllText(outro, "x");

        ((Action)(() => LeitorDePlanilha.Ler(falso))).Should().Throw<InvalidDataException>().WithMessage("O arquivo não é uma planilha do Excel*");
        ((Action)(() => LeitorDePlanilha.Ler(outro))).Should().Throw<InvalidDataException>().WithMessage("Formato .pdf não aceito*");
    }
}
