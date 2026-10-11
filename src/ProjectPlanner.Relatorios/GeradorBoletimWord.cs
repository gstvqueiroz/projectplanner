using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Relatorios;
using static ProjectPlanner.Relatorios.PecasDoWord;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 36: o boletim de medição em Word (.docx, A4 paisagem), com o mesmo conteúdo do PDF, para ajustar antes de enviar: o
/// cabeçalho, a tabela de itens (o cabeçalho das colunas se repete em cada página), o resumo financeiro, as glosas, as
/// observações e as assinaturas.
/// </summary>
public sealed class GeradorBoletimWord
{
    private const int Margem = 720; // 1,27 cm
    private const int LarguraUtil = 16838 - 2 * Margem;

    private static readonly int[] Larguras = Proporcionais([30, 128, 26, 52, 52, 64, 50, 50, 52, 50, 64, 64, 64, 32]);

    private uint _proximoDesenho;

    /// <param name="paginasExtras">Bloco 37: o que vai depois do boletim (os indicadores), cada um numa página.</param>
    public void Gerar(BoletimDeMedicao boletim, CabecalhoDoRelatorio cabecalho, DateTime geradoEm, Stream destino,
        IReadOnlyList<Action<Body, MainDocumentPart>>? paginasExtras = null)
    {
        ArgumentNullException.ThrowIfNull(boletim);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);
        _proximoDesenho = 1;
        using var documento = WordprocessingDocument.Create(destino, WordprocessingDocumentType.Document);
        var principal = documento.AddMainDocumentPart();
        var corpo = new Body();
        principal.Document = new Document(corpo);
        documento.PackageProperties.Title = $"{boletim.Medicao.Rotulo} · {cabecalho.Obra}";
        documento.PackageProperties.Creator = cabecalho.Responsavel ?? "ProjectPlanner";

        if (cabecalho.Logotipo is { Length: > 0 } logo && Dimensoes(logo) is { } tamanho)
        {
            var (l, a) = Encaixar(tamanho.Largura, tamanho.Altura, (long)(5 * 360_000), (long)(1.6 * 360_000));
            corpo.Append(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                new Run(Desenho(principal, logo, l, a, "Logotipo", _proximoDesenho++))));
        }
        corpo.Append(Paragrafo(TextosDoBoletim.Titulo(boletim.Medicao), 10, negrito: true, cor: Cinza));
        corpo.Append(Paragrafo(cabecalho.Obra, 16, negrito: true, depois: 120));
        var dados = Tabela(bordas: false, larguras: [2600, 12798]);
        foreach (var (rotulo, valor) in TextosDoBoletim.Dados(boletim, cabecalho, geradoEm))
            dados.Append(new TableRow(Celula(2600, Paragrafo(rotulo, 8.5, negrito: true, cor: Cinza, depois: 0)), Celula(12798, Paragrafo(valor, 9, depois: 0))));
        corpo.Append(dados);
        corpo.Append(Paragrafo("", 6));

        corpo.Append(Itens(boletim));
        corpo.Append(Paragrafo("", 6));
        Fim(corpo, boletim);

        foreach (var pagina in paginasExtras ?? [])
        {
            corpo.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            pagina(corpo, principal);
        }
        corpo.Append(Secao(principal, boletim));
        principal.Document.Save();
    }

    private static Table Itens(BoletimDeMedicao boletim)
    {
        var tabela = Tabela(bordas: true, larguras: Larguras);
        var cabecalho = new TableRow(new TableRowProperties(new TableHeader()));
        for (var i = 0; i < Larguras.Length; i++)
            cabecalho.Append(CelulaDaTabela(Larguras[i], TextosDoBoletim.Colunas[i], negrito: true, aDireita: i > 2, fundo: "F4F6F8"));
        tabela.Append(cabecalho);
        foreach (var linha in boletim.Linhas)
            tabela.Append(Linha(TextosDoBoletim.Celulas(linha), linha.EhGrupo, linha.EhGrupo ? "F4F6F8" : null));
        tabela.Append(Linha(TextosDoBoletim.Total(boletim.Resumo), negrito: true, fundo: "E3F2FD"));
        return tabela;
    }

    private static TableRow Linha(string[] celulas, bool negrito, string? fundo)
    {
        var linha = new TableRow(new TableRowProperties(new CantSplit()));
        for (var i = 0; i < celulas.Length; i++)
            linha.Append(CelulaDaTabela(Larguras[i], celulas[i].TrimStart(), negrito, aDireita: i > 2, fundo, recuo: i == 1 ? celulas[i].Length - celulas[i].TrimStart().Length : 0));
        return linha;
    }

    private static TableCell CelulaDaTabela(int largura, string texto, bool negrito, bool aDireita, string? fundo, int recuo = 0)
    {
        var propriedades = new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" });
        if (recuo > 0)
            propriedades.Append(new Indentation { Left = (recuo * 60).ToString() });
        if (aDireita)
            propriedades.Append(new Justification { Val = JustificationValues.Right });
        var celula = new TableCell(new TableCellProperties(new TableCellWidth { Width = largura.ToString(), Type = TableWidthUnitValues.Dxa }));
        if (fundo is not null)
            celula.TableCellProperties!.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fundo });
        celula.Append(new Paragraph(propriedades, Texto(texto, 7, negrito)));
        return celula;
    }

    private static void Fim(Body corpo, BoletimDeMedicao boletim)
    {
        corpo.Append(Paragrafo("Resumo financeiro", 11, negrito: true, antes: 120));
        var resumo = Tabela(bordas: false, larguras: [5200, 2600]);
        foreach (var (rotulo, valor, forte) in TextosDoBoletim.Resumo(boletim.Resumo))
            resumo.Append(new TableRow(
                Celula(5200, Paragrafo(rotulo, 9, negrito: forte, depois: 0)),
                Celula(2600, new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }, new Justification { Val = JustificationValues.Right }),
                    Texto(valor, 9, forte)))));
        corpo.Append(resumo);

        if (boletim.Medicao.Glosas.Count > 0)
        {
            corpo.Append(Paragrafo("Glosas", 11, negrito: true, antes: 200));
            foreach (var glosa in boletim.Medicao.Glosas)
                corpo.Append(Paragrafo($"• {glosa.Descricao}: {Moeda.ComSimbolo(glosa.Valor)}", 9));
        }
        if (boletim.Medicao.Observacoes is { } observacoes)
        {
            corpo.Append(Paragrafo("Observações", 11, negrito: true, antes: 200));
            corpo.Append(Paragrafo(observacoes, 9));
        }

        corpo.Append(Paragrafo("", 9, depois: 700));
        var assinaturas = Tabela(bordas: false, larguras: [6000, 1400, 6000]);
        assinaturas.Append(new TableRow(
            Celula(6000, Paragrafo("______________________________________________", 9, depois: 0), Paragrafo("Contratada (responsável técnico)", 8, cor: Cinza)),
            Celula(1400, Paragrafo("", 9)),
            Celula(6000, Paragrafo("______________________________________________", 9, depois: 0), Paragrafo("Contratante (fiscalização)", 8, cor: Cinza))));
        corpo.Append(assinaturas);
    }

    private static SectionProperties Secao(MainDocumentPart principal, BoletimDeMedicao boletim)
    {
        var rodape = principal.AddNewPart<FooterPart>();
        rodape.Footer = new Footer(new Paragraph(
            new ParagraphProperties(new Tabs(new TabStop { Val = TabStopValues.Right, Position = LarguraUtil })),
            Texto(boletim.Medicao.Rotulo, 8, cor: Cinza),
            new Run(new TabChar()),
            Texto("Página ", 8, cor: Cinza)));
        var paragrafo = rodape.Footer.GetFirstChild<Paragraph>()!;
        paragrafo.Append(Campo("PAGE"));
        paragrafo.Append(Texto(" de ", 8, cor: Cinza));
        paragrafo.Append(Campo("NUMPAGES"));
        rodape.Footer.Save();
        return new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = principal.GetIdOfPart(rodape) },
            new PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape },
            new PageMargin { Top = Margem, Bottom = Margem, Left = (uint)Margem, Right = (uint)Margem, Footer = 360, Header = 360, Gutter = 0 });
    }

    private static int[] Proporcionais(int[] pontos)
    {
        var total = pontos.Sum();
        return [.. pontos.Select(p => (int)Math.Floor((double)p / total * LarguraUtil))];
    }
}
