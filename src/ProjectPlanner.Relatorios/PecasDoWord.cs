using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace ProjectPlanner.Relatorios;

/// <summary>As peças dos documentos do Word (parágrafos, tabelas, campos e imagens), usadas pelo relatório fotográfico e pelo boletim de medição.</summary>
internal static class PecasDoWord
{
    internal const string Cinza = "6B6B6B";

    // Um campo do Word (PAGE, NUMPAGES): o número é calculado ao abrir o documento.
    internal static Run[] Campo(string nome)
    {
        static RunProperties Estilo() => new(new Color { Val = Cinza }, new FontSize { Val = "16" }); // a ordem do esquema: cor, tamanho
        return
        [
            new Run(Estilo(), new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(Estilo(), new FieldCode($" {nome} ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(Estilo(), new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(Estilo(), new Text("1")),
            new Run(Estilo(), new FieldChar { FieldCharType = FieldCharValues.End }),
        ];
    }

    internal static Paragraph QuebraDePagina() => new(new Run(new Break { Type = BreakValues.Page }));

    internal static Paragraph Paragrafo(string texto, double tamanho, bool negrito = false, string? cor = null, bool centro = false,
        int antes = 0, int depois = 40)
    {
        var propriedades = new ParagraphProperties(new SpacingBetweenLines { Before = antes.ToString(), After = depois.ToString() });
        if (centro)
            propriedades.Append(new Justification { Val = JustificationValues.Center });
        return new Paragraph(propriedades, Texto(texto, tamanho, negrito, cor));
    }

    internal static Run Texto(string texto, double tamanho, bool negrito = false, string? cor = null)
    {
        var propriedades = new RunProperties();
        if (negrito)
            propriedades.Append(new Bold());
        if (cor is not null)
            propriedades.Append(new Color { Val = cor });
        propriedades.Append(new FontSize { Val = ((int)Math.Round(tamanho * 2)).ToString() });
        var run = new Run(propriedades);
        var linhas = texto.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < linhas.Length; i++)
        {
            if (i > 0)
                run.Append(new Break());
            run.Append(new Text(linhas[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    internal static Table Tabela(bool bordas, int[] larguras)
    {
        var borda = bordas ? BorderValues.Single : BorderValues.None;
        var propriedades = new TableProperties(
            new TableWidth { Width = larguras.Sum().ToString(), Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = borda, Size = 4, Color = "CFD8DC" }, new LeftBorder { Val = borda, Size = 4, Color = "CFD8DC" },
                new BottomBorder { Val = borda, Size = 4, Color = "CFD8DC" }, new RightBorder { Val = borda, Size = 4, Color = "CFD8DC" },
                new InsideHorizontalBorder { Val = borda, Size = 4, Color = "CFD8DC" }, new InsideVerticalBorder { Val = borda, Size = 4, Color = "CFD8DC" }),
            new TableLayout { Type = TableLayoutValues.Fixed }); // a ordem é a do esquema: largura, bordas, layout
        return new Table(propriedades, new TableGrid(larguras.Select(l => new GridColumn { Width = l.ToString() })));
    }

    internal static TableCell Celula(int largura, params OpenXmlElement[] conteudo)
    {
        var celula = new TableCell(new TableCellProperties(new TableCellWidth { Width = largura.ToString(), Type = TableWidthUnitValues.Dxa }));
        celula.Append(conteudo);
        return celula;
    }

    internal static (int Largura, int Altura)? Dimensoes(byte[] bytes)
    {
        using var dados = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(dados);
        return codec is null ? null : (codec.Info.Width, codec.Info.Height);
    }

    internal static (long Largura, long Altura) Encaixar(int largura, int altura, long maximaLargura, long maximaAltura)
    {
        var escala = Math.Min((double)maximaLargura / largura, (double)maximaAltura / altura);
        return ((long)(largura * escala), (long)(altura * escala));
    }

    internal static Drawing Desenho(MainDocumentPart principal, byte[] bytes, long largura, long altura, string nome, uint numero)
    {
        var tipo = bytes.Length > 3 && bytes[0] == 0x89 && bytes[1] == 0x50 ? ImagePartType.Png : ImagePartType.Jpeg;
        var parte = principal.AddImagePart(tipo);
        using (var fluxo = new MemoryStream(bytes))
            parte.FeedData(fluxo);
        var id = principal.GetIdOfPart(parte);
        return new Drawing(new DW.Inline(
            new DW.Extent { Cx = largura, Cy = altura },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = numero, Name = nome },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = numero, Name = nome }, new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = id }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = largura, Cy = altura }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 });
    }
}
