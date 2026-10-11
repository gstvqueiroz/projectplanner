using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// O relatório em Word (.docx, A4 retrato), com o mesmo conteúdo do PDF, para ajustar o texto antes de enviar: capa com
/// os dados e o andamento, as fotos em tabelas (uma página por grupo), o índice e as assinaturas, e o número da página
/// no rodapé.
/// </summary>
public sealed class GeradorWord : IGeradorDeRelatorio
{
    private const long EmuPorCm = 360_000;
    private const int Twips15Cm = 850; // margem de 1,5 cm
    private const long LarguraUtil = 18 * EmuPorCm; // 21 cm − 2 × 1,5 cm
    private const long AlturaUtil = 23 * EmuPorCm; // 29,7 cm − margens, títulos e rodapé
    private const string Cinza = "6B6B6B";

    private uint _proximoDesenho;

    public string Extensao => "docx";

    public void Gerar(RelatorioFotografico relatorio, Func<Guid, byte[]?> imagem, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(relatorio);
        ArgumentNullException.ThrowIfNull(imagem);
        ArgumentNullException.ThrowIfNull(destino);
        _proximoDesenho = 1;
        using var documento = WordprocessingDocument.Create(destino, WordprocessingDocumentType.Document);
        var principal = documento.AddMainDocumentPart();
        var corpo = new Body();
        principal.Document = new Document(corpo);
        documento.PackageProperties.Title = relatorio.Titulo;
        documento.PackageProperties.Creator = relatorio.Cabecalho.Responsavel ?? "ProjectPlanner";
        documento.PackageProperties.Subject = relatorio.Cabecalho.Obra;

        Capa(corpo, principal, relatorio);
        if (relatorio.Texto is { } texto)
        {
            corpo.Append(QuebraDePagina());
            TextoDoAndamento(corpo, relatorio, texto);
        }
        foreach (var pagina in relatorio.Paginas)
        {
            corpo.Append(QuebraDePagina());
            Fotos(corpo, principal, relatorio, pagina, imagem);
        }
        foreach (var comparacao in relatorio.AntesEDepois)
        {
            corpo.Append(QuebraDePagina());
            AntesEDepois(corpo, principal, relatorio, comparacao, imagem);
        }
        if (relatorio.Opcoes.ComIndice || relatorio.Opcoes.ComAssinaturas)
        {
            corpo.Append(QuebraDePagina());
            Indice(corpo, relatorio);
        }
        corpo.Append(Secao(principal, relatorio));
        principal.Document.Save();
    }

    // ---- Capa ----

    private void Capa(Body corpo, MainDocumentPart principal, RelatorioFotografico r)
    {
        if (r.Cabecalho.Logotipo is { Length: > 0 } logo && Dimensoes(logo) is { } tamanho)
        {
            var (l, a) = Encaixar(tamanho.Largura, tamanho.Altura, 6 * EmuPorCm, (long)(2.6 * EmuPorCm));
            corpo.Append(new Paragraph(new Run(Desenho(principal, logo, l, a, "Logotipo"))));
        }
        corpo.Append(Paragrafo("", 12, depois: 1800));
        corpo.Append(Paragrafo(TextosDoRelatorio.Titulo, 13, negrito: true, cor: Cinza, centro: true));
        corpo.Append(Paragrafo(r.Cabecalho.Obra, 24, negrito: true, centro: true));
        corpo.Append(Paragrafo($"{TextosDoRelatorio.Numero(r)} · {TextosDoRelatorio.Periodo(r)}", 12, cor: Cinza, centro: true, depois: 480));

        var tabela = Tabela(bordas: false, larguras: [2800, 7400]);
        foreach (var (rotulo, valor) in TextosDoRelatorio.DadosDaCapa(r))
            tabela.Append(new TableRow(
                Celula(2800, Paragrafo(rotulo, 10, negrito: true, cor: Cinza)),
                Celula(7400, Paragrafo(valor, 11))));
        corpo.Append(tabela);

        var andamento = TextosDoRelatorio.Andamento(r);
        if (andamento.Count == 0)
            return;
        corpo.Append(Paragrafo("Andamento da obra", 13, negrito: true, antes: 360));
        foreach (var linha in andamento)
            corpo.Append(Paragrafo(linha, 10));
        Lista(corpo, "Atrasadas", TextosDoRelatorio.Atrasadas(r));
        Lista(corpo, "Próximos marcos (30 dias)", TextosDoRelatorio.ProximosMarcos(r));
    }

    private static void Lista(Body corpo, string titulo, IReadOnlyList<string> itens)
    {
        if (itens.Count == 0)
            return;
        corpo.Append(Paragrafo(titulo, 10, negrito: true, antes: 160));
        foreach (var item in itens)
            corpo.Append(Paragrafo("• " + item, 9.5));
    }

    // ---- Fotos ----

    private void Fotos(Body corpo, MainDocumentPart principal, RelatorioFotografico r, PaginaDeFotos pagina, Func<Guid, byte[]?> imagem)
    {
        corpo.Append(Paragrafo(TextosDoRelatorio.Cabecalho(r), 8, cor: Cinza));
        corpo.Append(Paragrafo(TextosDoRelatorio.TituloDaPagina(pagina), 14, negrito: true, antes: 120));
        if (pagina.Grupo.Detalhe is { } detalhe && !pagina.Continuacao)
            corpo.Append(Paragrafo(detalhe, 9, cor: Cinza));

        var (colunas, linhas) = r.Opcoes.Grade;
        var larguraDaColuna = 10200 / colunas; // twips: 18 cm
        var alturaMaxima = AlturaUtil / linhas - (long)(1.6 * EmuPorCm); // o resto da linha é a legenda
        var tabela = Tabela(bordas: false, larguras: [.. Enumerable.Repeat(larguraDaColuna, colunas)]);
        TableRow? linha = null;
        var ocupadas = 0;
        foreach (var (foto, ocupa) in pagina.Fotos)
        {
            if (linha is null || ocupadas + ocupa > colunas)
            {
                if (linha is not null)
                    Completar(linha, colunas - ocupadas, larguraDaColuna);
                tabela.Append(linha = new TableRow());
                ocupadas = 0;
            }
            var larguraDaFoto = LarguraUtil / colunas * ocupa - (long)(0.4 * EmuPorCm);
            var celula = Celula(larguraDaColuna * ocupa, [.. ParagrafosDaFoto(principal, foto, larguraDaFoto, alturaMaxima, imagem)]);
            if (ocupa > 1)
                celula.TableCellProperties!.Append(new GridSpan { Val = ocupa });
            linha.Append(celula);
            ocupadas += ocupa;
        }
        if (linha is not null)
            Completar(linha, colunas - ocupadas, larguraDaColuna);
        corpo.Append(tabela);
    }

    private static void Completar(TableRow linha, int faltam, int largura)
    {
        for (var i = 0; i < faltam; i++)
            linha.Append(Celula(largura, new Paragraph()));
    }

    private IEnumerable<OpenXmlElement> ParagrafosDaFoto(MainDocumentPart principal, FotoNoRelatorio foto, long largura, long alturaMaxima,
        Func<Guid, byte[]?> imagem)
    {
        if (imagem(foto.Foto.Id) is { Length: > 0 } bytes && Dimensoes(bytes) is { } tamanho)
        {
            var (l, a) = Encaixar(tamanho.Largura, tamanho.Altura, largura, alturaMaxima);
            yield return new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(Desenho(principal, bytes, l, a, $"Foto {foto.Numero}")));
        }
        else
            yield return Paragrafo("[Imagem indisponível]", 9, cor: Cinza, centro: true);
        if (foto.Titulo is { } titulo)
            yield return Paragrafo(titulo, 9, negrito: true);
        yield return Paragrafo(foto.Legenda, 8, cor: Cinza);
        if (foto.Texto is { } texto)
            yield return Paragrafo(texto, 8.5, depois: 120);
    }

    // ---- Bloco 29: texto do andamento e antes e depois ----

    private static void TextoDoAndamento(Body corpo, RelatorioFotografico r, TextoDoRelatorio texto)
    {
        corpo.Append(Paragrafo(TextosDoRelatorio.Cabecalho(r), 8, cor: Cinza));
        corpo.Append(Paragrafo(TextosDoRelatorio.TituloDoTexto, 14, negrito: true, antes: 120, depois: 120));
        foreach (var paragrafo in texto.Paragrafos)
            corpo.Append(Paragrafo(paragrafo, 10.5, depois: 120));
        if (texto.PontosDeAtencao.Count == 0)
            return;
        corpo.Append(Paragrafo(TextosDoRelatorio.TituloDosPontos, 11.5, negrito: true, antes: 240));
        foreach (var ponto in texto.PontosDeAtencao)
            corpo.Append(Paragrafo("• " + ponto, 10, depois: 60));
    }

    private void AntesEDepois(Body corpo, MainDocumentPart principal, RelatorioFotografico r, AntesEDepoisNoRelatorio comparacao,
        Func<Guid, byte[]?> imagem)
    {
        corpo.Append(Paragrafo(TextosDoRelatorio.Cabecalho(r), 8, cor: Cinza));
        corpo.Append(Paragrafo(TextosDoRelatorio.TituloAntesEDepois(comparacao), 14, negrito: true, antes: 120));
        corpo.Append(Paragrafo(comparacao.Evolucao, 10, cor: Cinza, depois: 120));
        var tabela = Tabela(bordas: false, larguras: [5100, 5100]);
        var linha = new TableRow();
        foreach (var (foto, legenda, nome) in new[] { (comparacao.Antes, comparacao.LegendaAntes, "Antes"), (comparacao.Depois, comparacao.LegendaDepois, "Depois") })
        {
            var paragrafos = new List<OpenXmlElement>();
            if (imagem(foto.Id) is { Length: > 0 } bytes && Dimensoes(bytes) is { } tamanho)
            {
                var (l, a) = Encaixar(tamanho.Largura, tamanho.Altura, LarguraUtil / 2 - (long)(0.4 * EmuPorCm), (long)(14.5 * EmuPorCm));
                paragrafos.Add(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                    new Run(Desenho(principal, bytes, l, a, nome))));
            }
            else
                paragrafos.Add(Paragrafo("[Imagem indisponível]", 9, cor: Cinza, centro: true));
            paragrafos.Add(Paragrafo(legenda, 8.5));
            if (foto.Titulo is { } titulo)
                paragrafos.Add(Paragrafo(titulo, 9, negrito: true));
            linha.Append(Celula(5100, [.. paragrafos]));
        }
        tabela.Append(linha);
        corpo.Append(tabela);
    }

    // ---- Índice e assinaturas ----

    private static void Indice(Body corpo, RelatorioFotografico r)
    {
        corpo.Append(Paragrafo(TextosDoRelatorio.Cabecalho(r), 8, cor: Cinza));
        if (r.Opcoes.ComIndice && r.TotalDeFotos > 0)
        {
            corpo.Append(Paragrafo("Índice das fotos", 14, negrito: true, antes: 120));
            int[] larguras = [700, 1700, 3900, 3900];
            var tabela = Tabela(bordas: true, larguras: larguras);
            tabela.Append(new TableRow(TextosDoRelatorio.ColunasDoIndice.Select((t, i) => Celula(larguras[i], Paragrafo(t, 8.5, negrito: true)))));
            foreach (var foto in r.Fotos)
                tabela.Append(new TableRow(TextosDoRelatorio.LinhaDoIndice(foto).Select((t, i) => Celula(larguras[i], Paragrafo(t, 8.5)))));
            corpo.Append(tabela);
        }
        if (!r.Opcoes.ComAssinaturas)
            return;
        corpo.Append(Paragrafo("", 10, antes: 1400));
        var assinaturas = TextosDoRelatorio.Assinaturas(r);
        var largura = 10200 / assinaturas.Count;
        var linha = new TableRow();
        foreach (var (cargo, nome) in assinaturas)
            linha.Append(Celula(largura,
                Paragrafo("______________________________", 10, centro: true),
                Paragrafo(nome ?? "", 9, centro: true),
                Paragrafo(cargo, 8.5, cor: Cinza, centro: true)));
        var daAssinatura = Tabela(bordas: false, larguras: [.. Enumerable.Repeat(largura, assinaturas.Count)]);
        daAssinatura.Append(linha);
        corpo.Append(daAssinatura);
    }

    // ---- Página, rodapé e peças ----

    private static SectionProperties Secao(MainDocumentPart principal, RelatorioFotografico r)
    {
        var rodape = principal.AddNewPart<FooterPart>();
        rodape.Footer = new Footer(new Paragraph(
            new ParagraphProperties(new Tabs(new TabStop { Val = TabStopValues.Right, Position = 10200 })),
            Texto($"Relatório fotográfico nº {r.Numero:00}", 8, cor: Cinza),
            new Run(new TabChar()),
            Texto("Página ", 8, cor: Cinza)));
        var paragrafo = rodape.Footer.GetFirstChild<Paragraph>()!;
        paragrafo.Append(Campo("PAGE"));
        paragrafo.Append(Texto(" de ", 8, cor: Cinza));
        paragrafo.Append(Campo("NUMPAGES"));
        rodape.Footer.Save();
        return new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = principal.GetIdOfPart(rodape) },
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = Twips15Cm, Bottom = Twips15Cm, Left = (uint)Twips15Cm, Right = (uint)Twips15Cm, Footer = 400, Header = 400, Gutter = 0 });
    }

    // Um campo do Word (PAGE, NUMPAGES): o número é calculado ao abrir o documento.
    private static Run[] Campo(string nome)
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

    private static Paragraph QuebraDePagina() => new(new Run(new Break { Type = BreakValues.Page }));

    private static Paragraph Paragrafo(string texto, double tamanho, bool negrito = false, string? cor = null, bool centro = false,
        int antes = 0, int depois = 40)
    {
        var propriedades = new ParagraphProperties(new SpacingBetweenLines { Before = antes.ToString(), After = depois.ToString() });
        if (centro)
            propriedades.Append(new Justification { Val = JustificationValues.Center });
        return new Paragraph(propriedades, Texto(texto, tamanho, negrito, cor));
    }

    private static Run Texto(string texto, double tamanho, bool negrito = false, string? cor = null)
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

    private static Table Tabela(bool bordas, int[] larguras)
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

    private static TableCell Celula(int largura, params OpenXmlElement[] conteudo)
    {
        var celula = new TableCell(new TableCellProperties(new TableCellWidth { Width = largura.ToString(), Type = TableWidthUnitValues.Dxa }));
        celula.Append(conteudo);
        return celula;
    }

    private static (int Largura, int Altura)? Dimensoes(byte[] bytes)
    {
        using var dados = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(dados);
        return codec is null ? null : (codec.Info.Width, codec.Info.Height);
    }

    private static (long Largura, long Altura) Encaixar(int largura, int altura, long maximaLargura, long maximaAltura)
    {
        var escala = Math.Min((double)maximaLargura / largura, (double)maximaAltura / altura);
        return ((long)(largura * escala), (long)(altura * escala));
    }

    private Drawing Desenho(MainDocumentPart principal, byte[] bytes, long largura, long altura, string nome)
    {
        var tipo = bytes.Length > 3 && bytes[0] == 0x89 && bytes[1] == 0x50 ? ImagePartType.Png : ImagePartType.Jpeg;
        var parte = principal.AddImagePart(tipo);
        using (var fluxo = new MemoryStream(bytes))
            parte.FeedData(fluxo);
        var id = principal.GetIdOfPart(parte);
        var numero = _proximoDesenho++;
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
