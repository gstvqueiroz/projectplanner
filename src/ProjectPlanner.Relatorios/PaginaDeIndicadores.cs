using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Planejamento;
using SkiaSharp;
using static ProjectPlanner.Relatorios.PecasDoWord;

namespace ProjectPlanner.Relatorios;

/// <summary>Um indicador do painel: o nome, o valor, uma linha de detalhe e se ele pede atenção (em vermelho).</summary>
public sealed record CartaoDeIndicador(string Rotulo, string Valor, string Detalhe, bool Alerta = false);

/// <summary>Bloco 37: os textos do painel de indicadores, iguais na tela, no PDF e no Word.</summary>
public static class TextosDosIndicadores
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Abaixo disto, o IDP ou o IDC aparece em vermelho.</summary>
    public const double LimiteDeAlerta = 0.95;

    public const string SemValores =
        "Sem valores nas tarefas: o valor agregado e o financeiro precisam do orçamento (coluna Valor da grid ou Projeto > Importar valores…).";

    public static string Titulo(PainelDeIndicadores p) => $"Indicadores em {p.Data.ToString("dd/MM/yyyy", Brasil)}";

    /// <summary>"Base: Linha de base (LB0) · 2 tarefas atrasadas · 1 medição aprovada".</summary>
    public static string Subtitulo(PainelDeIndicadores p) =>
        $"Base: {p.ValorAgregado.Base} · " +
        (p.TotalDeAtrasadas == 1 ? "1 tarefa atrasada" : $"{p.TotalDeAtrasadas} tarefas atrasadas") + " · " +
        (p.MedicoesAprovadas == 1 ? "1 medição aprovada" : $"{p.MedicoesAprovadas} medições aprovadas");

    public static string Percentual(double valor) => valor.ToString("0.0", Brasil) + "%";

    public static string Indice(double? valor) => valor is { } v ? v.ToString("0.00", Brasil) : "—";

    public static string Data(DateOnly? data) => data is { } d ? d.ToString("dd/MM/yyyy", Brasil) : "—";

    /// <summary>Os oito indicadores do topo do painel.</summary>
    public static IReadOnlyList<CartaoDeIndicador> Cartoes(PainelDeIndicadores p)
    {
        var v = p.ValorAgregado;
        var idp = v.Idp;
        var idc = v.Idc;
        return
        [
            new("Físico previsto", Percentual(v.FisicoPrevisto), $"VP {Moeda.ComSimbolo(v.ValorPlanejado)}"),
            new("Físico realizado", Percentual(v.FisicoRealizado), $"VA {Moeda.ComSimbolo(v.ValorAgregado)}", v.FisicoRealizado < v.FisicoPrevisto - 0.05),
            new("Financeiro medido", Percentual(p.FinanceiroMedido), $"{Moeda.ComSimbolo(p.Medido)} de {Moeda.ComSimbolo(p.ValorContratado)}"),
            new("Custo real", Moeda.ComSimbolo(v.CustoReal), $"{Percentual(v.CustoPercentual)} do orçamento"),
            new("IDP (prazo)", Indice(idp),
                idp is null ? "sem nada planejado até a data" : idp < LimiteDeAlerta ? "atrás do planejado" : idp > 1.05 ? "à frente do planejado" : "no planejado",
                idp < LimiteDeAlerta),
            new("IDC (custo)", Indice(idc),
                idc is null ? "sem custos lançados" : idc < LimiteDeAlerta ? "gastando mais que o orçado" : "dentro do orçado", idc < LimiteDeAlerta),
            new("Estimativa no término", v.EstimativaNoTermino is { } ent ? Moeda.ComSimbolo(ent) : "—",
                v.VariacaoNoTermino is { } vnt ? $"variação {TextosDoBoletim.ComSinal(vnt)}" : "precisa de custos lançados", v.VariacaoNoTermino < 0),
            new("Término pela tendência", Data(v.TerminoPelaTendencia ?? v.TerminoAtual),
                $"atual {Data(v.TerminoAtual)}" + (v.TerminoPrevisto is { } previsto ? $" · previsto {Data(previsto)}" : ""),
                v.TerminoPelaTendencia > (v.TerminoPrevisto ?? v.TerminoAtual)),
        ];
    }

    public static readonly string[] ColunasDasEtapas = ["Etapa", "Orçamento (R$)", "Previsto", "Realizado", "Medido", "Custo real (R$)", "VA − VP (R$)"];

    /// <summary>As etapas em textos (e, no fim, os custos gerais, se houver).</summary>
    public static IReadOnlyList<string[]> Etapas(PainelDeIndicadores p)
    {
        var linhas = p.Etapas.Select(e => new[]
        {
            $"{e.Edt} {e.Nome}", Moeda.Formatar(e.Orcamento), Percentual(e.Previsto), Percentual(e.Realizado), Percentual(e.PercentualMedido),
            Moeda.Formatar(e.CustoReal), Moeda.Formatar(e.Desvio),
        }).ToList();
        if (p.CustosGerais > 0)
            linhas.Add(["Custos gerais (sem tarefa)", "", "", "", "", Moeda.Formatar(p.CustosGerais), ""]);
        return linhas;
    }

    /// <summary>"1.2 Sapatas: previsto 40%, feito 30%, termina em 16/10/2026 (5 dias depois da linha de base)".</summary>
    public static IReadOnlyList<string> Atrasos(PainelDeIndicadores p) =>
    [
        .. p.Atrasos.Select(a => $"{a.Edt} {a.Nome}: previsto {a.PercentualPrevisto}%, feito {a.PercentualConcluido}%, termina em {Data(a.Termino)}" +
            (a.DesvioDoTermino is > 0 and var d ? $" ({(d == 1 ? "1 dia" : $"{d} dias")} depois da linha de base)" : "")),
    ];
}

/// <summary>Bloco 37: a página de indicadores no PDF (no relatório fotográfico, no boletim de medição e na tela de indicadores).</summary>
internal static class PaginaDeIndicadoresPdf
{
    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor Vermelho = new(0xC6, 0x28, 0x28);

    /// <summary>Desenha o painel na área: o título, os indicadores, a Curva S, as etapas e as tarefas mais atrasadas.</summary>
    public static void Desenhar(SKCanvas c, PainelDeIndicadores p, SKRect area, Fontes f)
    {
        var y = area.Top + 16;
        Escrever(c, TextosDosIndicadores.Titulo(p), area.Left, y, f.Negrito(14), Texto);
        y += 15;
        Escrever(c, TextosDosIndicadores.Subtitulo(p), area.Left, y, f.Normal(8.5f), Cinza);
        y += 12;
        if (!p.TemValores)
        {
            Escrever(c, TextosDosIndicadores.SemValores, area.Left, y + 4, f.Normal(8.5f), Vermelho);
            y += 12;
        }

        // Os indicadores: 8 numa linha (paisagem) ou 4 por linha (retrato).
        var cartoes = TextosDosIndicadores.Cartoes(p);
        var porLinha = area.Width > 700 ? 8 : 4;
        var largura = (area.Width - (porLinha - 1) * 6) / porLinha;
        using var fundo = new SKPaint { Color = Faixa };
        for (var i = 0; i < cartoes.Count; i++)
        {
            var (x, topo) = (area.Left + i % porLinha * (largura + 6), y + 6 + i / porLinha * 50);
            c.DrawRoundRect(new SKRoundRect(new SKRect(x, topo, x + largura, topo + 44), 4), fundo);
            var cartao = cartoes[i];
            Escrever(c, Truncar(cartao.Rotulo, largura - 10, f.Normal(7.5f)), x + 6, topo + 12, f.Normal(7.5f), Cinza);
            Escrever(c, Truncar(cartao.Valor, largura - 10, f.Negrito(11)), x + 6, topo + 27, f.Negrito(11), cartao.Alerta ? Vermelho : Texto);
            Escrever(c, Truncar(cartao.Detalhe, largura - 10, f.Normal(7)), x + 6, topo + 39, f.Normal(7), Cinza);
        }
        y += 6 + (cartoes.Count + porLinha - 1) / porLinha * 50 + 4;

        // A Curva S: um pouco menos da metade do que sobra.
        var etapas = TextosDosIndicadores.Etapas(p);
        var atrasos = TextosDosIndicadores.Atrasos(p);
        var baixo = 20 + (etapas.Count + 1) * 13 + (atrasos.Count > 0 ? 22 + atrasos.Count * 12 : 0);
        // Abaixo da curva: os rótulos e a legenda (até duas linhas), e depois as etapas e as atrasadas.
        var alturaDaCurva = Math.Max(120, Math.Min(260, area.Bottom - y - 28 - 80 - baixo));
        Escrever(c, "Curva S física e financeira (% acumulado)", area.Left, y + 14, f.Negrito(10), Texto);
        var curva = new SKRect(area.Left + 34, y + 28, area.Right - 8, y + 28 + alturaDaCurva);
        GeradorFisicoFinanceiroPdf.DesenharCurvaS(c, p.Curva, curva, f, titulo: false);
        y = curva.Bottom + 80;

        // As etapas.
        if (y + 30 < area.Bottom)
        {
            var colunas = new[] { 0.34f, 0.12f, 0.09f, 0.09f, 0.09f, 0.13f, 0.14f }.Select(v => v * area.Width).ToArray();
            c.DrawRect(new SKRect(area.Left, y - 10, area.Right, y + 4), fundo);
            Linhas(c, TextosDosIndicadores.ColunasDasEtapas, colunas, area.Left, y, f.Negrito(7.5f));
            y += 14;
            using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.5f };
            foreach (var linha in etapas)
            {
                if (y > area.Bottom - 6)
                    break;
                Linhas(c, linha, colunas, area.Left, y, f.Normal(7.5f));
                c.DrawLine(area.Left, y + 4, area.Right, y + 4, traco);
                y += 13;
            }
        }

        // As tarefas mais atrasadas.
        if (atrasos.Count > 0 && y + 30 < area.Bottom)
        {
            y += 12;
            Escrever(c, p.TotalDeAtrasadas > atrasos.Count ? $"Mais atrasadas ({atrasos.Count} de {p.TotalDeAtrasadas})" : "Atrasadas", area.Left, y, f.Negrito(10), Texto);
            y += 14;
            foreach (var atraso in atrasos)
            {
                if (y > area.Bottom)
                    break;
                Escrever(c, Truncar("• " + atraso, area.Width, f.Normal(8.5f)), area.Left, y, f.Normal(8.5f), Texto);
                y += 12;
            }
        }
    }

    private static void Linhas(SKCanvas c, string[] celulas, float[] larguras, float x, float y, SKFont fonte)
    {
        using var tinta = new SKPaint { Color = Texto, IsAntialias = true };
        for (var i = 0; i < celulas.Length; i++)
        {
            var texto = Truncar(celulas[i], larguras[i] - 6, fonte);
            if (i == 0)
                c.DrawText(texto, x + 3, y, SKTextAlign.Left, fonte, tinta);
            else
                c.DrawText(texto, x + larguras[i] - 3, y, SKTextAlign.Right, fonte, tinta);
            x += larguras[i];
        }
    }

    private static string Truncar(string texto, float largura, SKFont fonte)
    {
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        while (texto.Length > 1 && fonte.MeasureText(texto + "…") > largura)
            texto = texto[..^1];
        return texto.TrimEnd() + "…";
    }

    private static void Escrever(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
    }
}

/// <summary>Bloco 37: a Curva S como imagem PNG (para o Word).</summary>
public static class ImagemDaCurvaS
{
    /// <summary>A curva num PNG de <paramref name="largura"/> × <paramref name="altura"/> pontos (desenhado em dobro, para ficar nítido).</summary>
    public static byte[] Png(CurvaS curva, int largura, int altura)
    {
        ArgumentNullException.ThrowIfNull(curva);
        using var superficie = SKSurface.Create(new SKImageInfo(largura * 2, altura * 2));
        var c = superficie.Canvas;
        c.Clear(SKColors.White);
        c.Scale(2);
        using (var fontes = new Fontes())
            GeradorFisicoFinanceiroPdf.DesenharCurvaS(c, curva, new SKRect(40, 10, largura - 10, altura - 64), fontes, titulo: false);
        using var imagem = superficie.Snapshot();
        using var png = imagem.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}

/// <summary>Bloco 37: o painel de indicadores no Word (no relatório fotográfico e no boletim de medição).</summary>
internal static class IndicadoresNoWord
{
    /// <param name="larguraUtil">A largura da página menos as margens, em twips.</param>
    /// <param name="proximoDesenho">O número do próximo desenho do documento.</param>
    public static void Adicionar(Body corpo, MainDocumentPart principal, PainelDeIndicadores p, int larguraUtil, Func<uint> proximoDesenho)
    {
        corpo.Append(Paragrafo(TextosDosIndicadores.Titulo(p), 14, negrito: true, depois: 0));
        corpo.Append(Paragrafo(TextosDosIndicadores.Subtitulo(p), 8.5, cor: Cinza, depois: 120));
        if (!p.TemValores)
            corpo.Append(Paragrafo(TextosDosIndicadores.SemValores, 9, cor: "C62828"));

        var cartoes = TextosDosIndicadores.Cartoes(p);
        var porLinha = larguraUtil > 12000 ? 8 : 4;
        var largura = larguraUtil / porLinha;
        var tabela = Tabela(bordas: true, larguras: [.. Enumerable.Repeat(largura, porLinha)]);
        foreach (var linha in cartoes.Chunk(porLinha))
            tabela.Append(new TableRow(linha.Select(cartao => Celula(largura,
                Paragrafo(cartao.Rotulo, 7.5, cor: Cinza, depois: 0),
                Paragrafo(cartao.Valor, 11, negrito: true, cor: cartao.Alerta ? "C62828" : null, depois: 0),
                Paragrafo(cartao.Detalhe, 7, cor: Cinza, depois: 40)))));
        corpo.Append(tabela);

        corpo.Append(Paragrafo("Curva S física e financeira (% acumulado)", 10, negrito: true, antes: 160));
        var (larguraDaCurva, alturaDaCurva) = (larguraUtil / 20, porLinha == 8 ? 230 : 280); // em pontos (1 pt = 20 twips = 12.700 EMU)
        var png = ImagemDaCurvaS.Png(p.Curva, larguraDaCurva, alturaDaCurva);
        corpo.Append(new Paragraph(new Run(Desenho(principal, png, larguraDaCurva * 12_700L, alturaDaCurva * 12_700L, "Curva S", proximoDesenho()))));

        var colunas = new[] { 0.34, 0.12, 0.09, 0.09, 0.09, 0.13, 0.14 }.Select(v => (int)(v * larguraUtil)).ToArray();
        var etapas = Tabela(bordas: true, larguras: colunas);
        etapas.Append(new TableRow(TextosDosIndicadores.ColunasDasEtapas.Select((t, i) => Celula(colunas[i], Paragrafo(t, 7.5, negrito: true, depois: 0)))));
        foreach (var linha in TextosDosIndicadores.Etapas(p))
            etapas.Append(new TableRow(linha.Select((t, i) => Celula(colunas[i], Paragrafo(t, 7.5, depois: 0)))));
        corpo.Append(Paragrafo("Etapas", 10, negrito: true, antes: 160));
        corpo.Append(etapas);

        var atrasos = TextosDosIndicadores.Atrasos(p);
        if (atrasos.Count == 0)
            return;
        corpo.Append(Paragrafo(p.TotalDeAtrasadas > atrasos.Count ? $"Mais atrasadas ({atrasos.Count} de {p.TotalDeAtrasadas})" : "Atrasadas", 10, negrito: true, antes: 160));
        foreach (var atraso in atrasos)
            corpo.Append(Paragrafo("• " + atraso, 9));
    }
}

/// <summary>Bloco 37: Projeto > Indicadores… exportado em PDF (A4 paisagem, uma página: o painel inteiro).</summary>
public sealed class GeradorIndicadoresPdf
{
    public void Gerar(PainelDeIndicadores painel, string obra, DateTime geradoEm, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(painel);
        ArgumentNullException.ThrowIfNull(destino);
        const float largura = 841.89f, altura = 595.28f, margem = 30f;
        using var fontes = new Fontes();
        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = $"Indicadores · {obra}", Creator = "ProjectPlanner", Producer = "ProjectPlanner", Creation = geradoEm, Modified = geradoEm,
        });
        var c = documento.BeginPage(largura, altura);
        using (var tinta = new SKPaint { Color = new SKColor(0x6B, 0x6B, 0x6B), IsAntialias = true })
        {
            c.DrawText($"PAINEL DE INDICADORES · {obra}", margem, margem + 6, SKTextAlign.Left, fontes.Negrito(9), tinta);
            var gerado = $"Gerado em {geradoEm.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))}";
            c.DrawText(gerado, largura - margem, margem + 6, SKTextAlign.Right, fontes.Normal(8), tinta);
        }
        PaginaDeIndicadoresPdf.Desenhar(c, painel, new SKRect(margem, margem + 12, largura - margem, altura - margem), fontes);
        documento.EndPage();
        documento.Close();
    }
}

/// <summary>Bloco 37: Projeto > Indicadores… exportado em Word (.docx, A4 paisagem).</summary>
public sealed class GeradorIndicadoresWord
{
    public void Gerar(PainelDeIndicadores painel, string obra, DateTime geradoEm, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(painel);
        ArgumentNullException.ThrowIfNull(destino);
        const int margem = 720, larguraUtil = 16838 - 2 * margem;
        using var documento = WordprocessingDocument.Create(destino, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var principal = documento.AddMainDocumentPart();
        var corpo = new Body();
        principal.Document = new Document(corpo);
        documento.PackageProperties.Title = $"Indicadores · {obra}";
        corpo.Append(Paragrafo($"PAINEL DE INDICADORES · {obra}", 9, negrito: true, cor: Cinza, depois: 0));
        corpo.Append(Paragrafo($"Gerado em {geradoEm.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))}", 8, cor: Cinza, depois: 120));
        uint desenho = 1;
        IndicadoresNoWord.Adicionar(corpo, principal, painel, larguraUtil, () => desenho++);
        corpo.Append(new SectionProperties(
            new PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape },
            new PageMargin { Top = margem, Bottom = margem, Left = margem, Right = margem, Footer = 360, Header = 360, Gutter = 0 }));
        principal.Document.Save();
    }
}
