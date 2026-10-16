using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectPlanner.Core.Orcamento;
using static ProjectPlanner.Relatorios.PecasDoWord;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 33: o orçamento em Word (.docx, A4 paisagem), com o mesmo conteúdo do PDF para ajustar antes de enviar: a planilha
/// sintética (o cabeçalho das colunas se repete em cada página), os totais com o BDI e a analítica das composições.
/// </summary>
public sealed class GeradorOrcamentoWord
{
    private const int Margem = 720;
    private const int LarguraUtil = 16838 - 2 * Margem;

    private static readonly int[] Larguras = Proporcionais([42, 52, 52, 300, 36, 62, 70, 70, 86]);
    private static readonly int[] LargurasDaAnalitica = Proporcionais([60, 330, 90, 40, 70, 70, 80]);

    public void Gerar(PlanilhaOrcamentaria planilha, IReadOnlyDictionary<Guid, ComposicaoAberta> composicoes, string subtitulo, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(planilha);
        ArgumentNullException.ThrowIfNull(composicoes);
        ArgumentNullException.ThrowIfNull(destino);
        using var documento = WordprocessingDocument.Create(destino, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var principal = documento.AddMainDocumentPart();
        var corpo = new Body();
        principal.Document = new Document(corpo);
        documento.PackageProperties.Title = "Orçamento";
        documento.PackageProperties.Creator = "ProjectPlanner";

        corpo.Append(Paragrafo("ORÇAMENTO · PLANILHA SINTÉTICA", 10, negrito: true, cor: Cinza));
        corpo.Append(Paragrafo(subtitulo, 14, negrito: true, depois: 120));
        var tabela = Tabela(bordas: true, larguras: Larguras);
        tabela.Append(Linha(TextosDoOrcamento.Colunas, Larguras, [5, 6, 7, 8], negrito: true, fundo: "F4F6F8", cabecalho: true));
        foreach (var l in planilha.Linhas)
            tabela.Append(Linha(TextosDoOrcamento.Celulas(l), Larguras, [5, 6, 7, 8], l.EhGrupo, l.EhGrupo ? "F4F6F8" : null));
        tabela.Append(Linha(["", "", "", "Total", "", "", "", "", Core.Planejamento.Moeda.Formatar(planilha.Total)], Larguras, [8], true, "E3F2FD"));
        corpo.Append(tabela);

        corpo.Append(Paragrafo("", 6));
        var totais = Tabela(bordas: false, larguras: [5200, 2600]);
        foreach (var (rotulo, valor, forte) in TextosDoOrcamento.Totais(planilha))
            totais.Append(new TableRow(
                Celula(5200, Paragrafo(rotulo, 9, negrito: forte, depois: 0)),
                Celula(2600, new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }, new Justification { Val = JustificationValues.Right }),
                    Texto(valor, 9, forte)))));
        corpo.Append(totais);
        corpo.Append(Paragrafo(TextosDoOrcamento.Rodape(planilha), 8, cor: Cinza, antes: 80));

        var analitica = TextosDoOrcamento.Analitica(planilha, composicoes).ToList();
        if (analitica.Count > 0)
        {
            corpo.Append(QuebraDePagina());
            corpo.Append(Paragrafo("ORÇAMENTO · COMPOSIÇÕES (ANALÍTICA)", 10, negrito: true, cor: Cinza));
            corpo.Append(Paragrafo(subtitulo, 14, negrito: true, depois: 120));
            foreach (var (linha, composicao) in analitica)
            {
                corpo.Append(Paragrafo(TextosDoOrcamento.TituloDaComposicao(linha, composicao), 9, negrito: true, antes: 160, depois: 60));
                var insumos = Tabela(bordas: true, larguras: LargurasDaAnalitica);
                insumos.Append(Linha(TextosDoOrcamento.ColunasDaAnalitica, LargurasDaAnalitica, [4, 5, 6], negrito: true, fundo: "F4F6F8", cabecalho: true));
                foreach (var i in composicao.Insumos)
                    insumos.Append(Linha(TextosDoOrcamento.CelulasDoInsumo(i), LargurasDaAnalitica, [4, 5, 6], false, null));
                corpo.Append(insumos);
            }
        }
        corpo.Append(new SectionProperties(
            new PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape },
            new PageMargin { Top = Margem, Bottom = Margem, Left = (uint)Margem, Right = (uint)Margem, Footer = 360, Header = 360, Gutter = 0 }));
        principal.Document.Save();
    }

    private static TableRow Linha(string[] celulas, int[] larguras, int[] aDireita, bool negrito, string? fundo, bool cabecalho = false)
    {
        var linha = new TableRow(cabecalho ? new TableRowProperties(new TableHeader()) : new TableRowProperties(new CantSplit()));
        for (var i = 0; i < celulas.Length; i++)
        {
            var texto = celulas[i].TrimStart();
            var propriedades = new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" });
            var recuo = celulas[i].Length - texto.Length;
            if (recuo > 0)
                propriedades.Append(new Indentation { Left = (recuo * 60).ToString() });
            if (aDireita.Contains(i))
                propriedades.Append(new Justification { Val = JustificationValues.Right });
            var celula = new TableCell(new TableCellProperties(new TableCellWidth { Width = larguras[i].ToString(), Type = TableWidthUnitValues.Dxa }));
            if (fundo is not null)
                celula.TableCellProperties!.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fundo });
            celula.Append(new Paragraph(propriedades, Texto(texto, 7.5, negrito)));
            linha.Append(celula);
        }
        return linha;
    }

    private static int[] Proporcionais(int[] pontos)
    {
        var total = pontos.Sum();
        return [.. pontos.Select(p => (int)Math.Floor((double)p / total * LarguraUtil))];
    }
}
