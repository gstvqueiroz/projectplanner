using ProjectPlanner.Core.Planejamento;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 35: o cronograma físico-financeiro em PDF (A4 paisagem): a tabela, dividida em páginas pelos períodos e pelas
/// linhas (cada página repete a coluna das etapas e o valor), com os totais do período e o acumulado, e a página da Curva S.
/// </summary>
public sealed class GeradorFisicoFinanceiroPdf
{
    private const float Largura = 841.89f;
    private const float Altura = 595.28f;
    private const float Margem = 36f;
    private const float ColunaDoNome = 210f;
    private const float ColunaDoValor = 90f;
    private const float ColunaDoPeriodo = 66f;
    private const float AlturaDaLinha = 24f;
    private const float AlturaDoTotal = 15f;

    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor CorDoRealizado = new(0xE6, 0x51, 0x00);

    /// <summary>Quantos períodos cabem numa página.</summary>
    public static int PeriodosPorPagina => (int)((Largura - 2 * Margem - ColunaDoNome - ColunaDoValor) / ColunaDoPeriodo);

    /// <summary>Quantas linhas (etapas ou tarefas) cabem numa página.</summary>
    public static int LinhasPorPagina => (int)((Altura - 2 * Margem - 70 - 4 * AlturaDoTotal - 24) / AlturaDaLinha);

    /// <param name="titulo">"Residência Silva".</param>
    /// <param name="subtitulo">"Cronograma atual · mensal · gerado em 11/10/2026 15:00".</param>
    public void Gerar(CronogramaFisicoFinanceiro tabela, CurvaS curva, string titulo, string subtitulo, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentNullException.ThrowIfNull(curva);
        ArgumentNullException.ThrowIfNull(destino);
        using var fontes = new Fontes();
        var blocosDeColunas = Blocos(tabela.Periodos.Count, PeriodosPorPagina);
        var blocosDeLinhas = Blocos(Math.Max(1, tabela.Linhas.Count), LinhasPorPagina);
        var total = blocosDeColunas.Count * blocosDeLinhas.Count + 1;
        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = $"Cronograma físico-financeiro · {titulo}",
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
        });
        var pagina = 1;
        foreach (var colunas in blocosDeColunas)
            foreach (var linhas in blocosDeLinhas)
            {
                var c = documento.BeginPage(Largura, Altura);
                Tabela(c, tabela, colunas, linhas, ultimaDasLinhas: linhas.Fim == Math.Max(1, tabela.Linhas.Count), fontes);
                Cabecalho(c, titulo, subtitulo, fontes);
                Rodape(c, pagina++, total, fontes);
                documento.EndPage();
            }
        var pc = documento.BeginPage(Largura, Altura);
        Cabecalho(pc, titulo, subtitulo, fontes);
        DesenharCurvaS(pc, curva, new SKRect(Margem + 40, Margem + 100, Largura - Margem - 10, Altura - Margem - 60), fontes);
        Rodape(pc, pagina, total, fontes);
        documento.EndPage();
        documento.Close();
    }

    private sealed record Bloco(int Inicio, int Fim);

    private static List<Bloco> Blocos(int quantidade, int porBloco)
    {
        var blocos = new List<Bloco>();
        for (var i = 0; i < Math.Max(quantidade, 1); i += porBloco)
            blocos.Add(new Bloco(i, Math.Min(quantidade, i + porBloco)));
        return blocos;
    }

    private static void Cabecalho(SKCanvas c, string titulo, string subtitulo, Fontes f)
    {
        Escrever(c, "CRONOGRAMA FÍSICO-FINANCEIRO", Margem, Margem + 4, f.Negrito(10), Cinza);
        Escrever(c, titulo, Margem, Margem + 26, f.Negrito(16), Texto);
        Escrever(c, subtitulo, Margem, Margem + 42, f.Normal(9), Cinza);
    }

    private static void Rodape(SKCanvas c, int pagina, int total, Fontes f)
    {
        var texto = $"Página {pagina} de {total}";
        Escrever(c, texto, Largura - Margem - f.Normal(8).MeasureText(texto), Altura - Margem + 14, f.Normal(8), Cinza);
    }

    private static void Tabela(SKCanvas c, CronogramaFisicoFinanceiro t, Bloco colunas, Bloco linhas, bool ultimaDasLinhas, Fontes f)
    {
        var y = Margem + 70;
        float X(int coluna) => Margem + ColunaDoNome + ColunaDoValor + (coluna - colunas.Inicio) * ColunaDoPeriodo;
        var direita = X(colunas.Fim);
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.75f };
        using var fundo = new SKPaint { Color = Faixa };

        // Cabeçalho das colunas.
        c.DrawRect(new SKRect(Margem, y - 13, direita, y + 5), fundo);
        Escrever(c, "Etapa", Margem + 4, y, f.Negrito(8.5f), Texto);
        Direita(c, t.PorValor ? "Valor (R$)" : "Peso (dias)", Margem + ColunaDoNome + ColunaDoValor - 4, y, f.Negrito(8.5f));
        for (var i = colunas.Inicio; i < colunas.Fim; i++)
            Direita(c, t.Periodos[i].Rotulo, X(i + 1) - 4, y, f.Negrito(8.5f));
        y += 9;

        for (var l = linhas.Inicio; l < linhas.Fim && l < t.Linhas.Count; l++)
        {
            var linha = t.Linhas[l];
            c.DrawLine(Margem, y, direita, y, traco);
            var nome = $"{linha.Edt} {linha.Nome}";
            while (nome.Length > 4 && f.Normal(8.5f).MeasureText(nome) > ColunaDoNome - 8)
                nome = nome[..^2] + "…";
            Escrever(c, nome, Margem + 4, y + 15, f.Normal(8.5f), Texto);
            Direita(c, t.PorValor ? Moeda.Formatar(linha.Valor) : linha.Peso.ToString("0"), Margem + ColunaDoNome + ColunaDoValor - 4, y + 15, f.Normal(8.5f));
            for (var i = colunas.Inicio; i < colunas.Fim; i++)
            {
                if (linha.PesoPorPeriodo[i] == 0)
                    continue;
                if (t.PorValor)
                    Direita(c, Moeda.Formatar(linha.ValorPorPeriodo[i]), X(i + 1) - 4, y + 10, f.Normal(7.5f));
                Direita(c, $"{linha.PercentualPorPeriodo[i]:0.0}%", X(i + 1) - 4, y + (t.PorValor ? 20 : 15), f.Normal(7.5f), Cinza);
            }
            y += AlturaDaLinha;
        }
        c.DrawLine(Margem, y, direita, y, traco);
        if (!ultimaDasLinhas)
            return;

        // Totais do projeto: o do período e o acumulado, em R$ e em %.
        y += 4;
        var totais = new List<(string Rotulo, Func<int, string> Valor)>();
        if (t.PorValor)
            totais.Add(("Total do período (R$)", i => Moeda.Formatar(t.ValorDoPeriodo[i])));
        totais.Add(("% do período", i => $"{t.PercentualDoPeriodo[i]:0.0}%"));
        if (t.PorValor)
            totais.Add(("Acumulado (R$)", i => Moeda.Formatar(t.ValorAcumulado[i])));
        totais.Add(("% acumulado", i => $"{t.PercentualAcumulado[i]:0.0}%"));
        foreach (var (rotulo, valor) in totais)
        {
            c.DrawRect(new SKRect(Margem, y, direita, y + AlturaDoTotal - 1), fundo);
            Escrever(c, rotulo, Margem + 4, y + 11, f.Negrito(8), Texto);
            if (rotulo.StartsWith("Total") || rotulo.StartsWith("Acumulado"))
                Direita(c, Moeda.Formatar(t.ValorTotal), Margem + ColunaDoNome + ColunaDoValor - 4, y + 11, f.Negrito(8));
            for (var i = colunas.Inicio; i < colunas.Fim; i++)
                Direita(c, valor(i), X(i + 1) - 4, y + 11, f.Normal(7.5f));
            y += AlturaDoTotal;
        }
    }

    // ---- Curva S ----

    /// <summary>A cor de cada tipo de série (a mesma na tela, no PDF e no Word).</summary>
    internal static SKColor CorDa(TipoDaSerie tipo) => tipo switch
    {
        TipoDaSerie.Prevista => new SKColor(0x90, 0xA4, 0xAE),
        TipoDaSerie.Medida => new SKColor(0x2E, 0x7D, 0x32),
        TipoDaSerie.CustoReal => new SKColor(0x8E, 0x24, 0xAA),
        _ => new SKColor(0x15, 0x65, 0xC0),
    };

    /// <summary>
    /// Desenha a Curva S na área (também nas páginas de indicadores do Bloco 37): as séries (a prevista tracejada, a do custo
    /// pontilhada), o realizado (a linha laranja pelo avanço guardado em cada medição até o ponto de hoje) e a legenda.
    /// </summary>
    internal static void DesenharCurvaS(SKCanvas c, CurvaS curva, SKRect area, Fontes f, bool titulo = true)
    {
        if (titulo)
            Escrever(c, "Curva S (% acumulado)", area.Left - 40, area.Top - 24, f.Negrito(12), Texto);
        using var grade = new SKPaint { Color = Linha, StrokeWidth = 0.5f };
        var n = Math.Max(1, curva.Periodos.Count);
        float X(double posicao) => area.Left + (float)(posicao / n) * area.Width;
        float Y(double percentual) => area.Bottom - (float)(Math.Clamp(percentual, 0, 100) / 100) * area.Height;
        for (var p = 0; p <= 100; p += 20)
        {
            c.DrawLine(area.Left, Y(p), area.Right, Y(p), grade);
            Direita(c, $"{p}%", area.Left - 6, Y(p) + 3, f.Normal(8), Cinza);
        }
        var passo = Math.Max(1, (int)Math.Ceiling(curva.Periodos.Count / Math.Max(1, area.Width / 44.0)));
        for (var i = 0; i < curva.Periodos.Count; i += passo)
        {
            var rotulo = curva.Periodos[i].Rotulo;
            Escrever(c, rotulo, X(i + 0.5) - f.Normal(7.5f).MeasureText(rotulo) / 2, area.Bottom + 14, f.Normal(7.5f), Cinza);
        }

        var legenda = area.Bottom + 34;
        var xLegenda = area.Left;
        void Legenda(string texto, Action<float, float> marca)
        {
            var largura = 40 + f.Normal(9).MeasureText(texto);
            if (xLegenda + largura > area.Right && xLegenda > area.Left)
                (xLegenda, legenda) = (area.Left, legenda + 14);
            marca(xLegenda, legenda - 3);
            Escrever(c, texto, xLegenda + 28, legenda, f.Normal(9), Texto);
            xLegenda += largura;
        }

        foreach (var serie in curva.Series)
        {
            using var tinta = new SKPaint { Color = CorDa(serie.Tipo), StrokeWidth = 2.2f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            if (serie.Tipo == TipoDaSerie.Prevista)
                tinta.PathEffect = SKPathEffect.CreateDash([6, 4], 0);
            else if (serie.Tipo == TipoDaSerie.CustoReal)
                tinta.PathEffect = SKPathEffect.CreateDash([2, 3], 0);
            using var caminho = new SKPath();
            caminho.MoveTo(X(0), Y(0));
            for (var i = 0; i < serie.PercentualAcumulado.Count; i++)
                caminho.LineTo(X(i + 1), Y(serie.PercentualAcumulado[i]));
            c.DrawPath(caminho, tinta);
            Legenda(serie.Nome, (x, y) => c.DrawLine(x, y, x + 22, y, tinta));
        }
        if (curva.Realizado is { } realizado && curva.PosicaoDe(realizado.Data) is { } posicao)
        {
            using var ponto = new SKPaint { Color = CorDoRealizado, IsAntialias = true };
            using var linha = new SKPaint { Color = CorDoRealizado, StrokeWidth = 1.6f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            var pontos = curva.Historico.Select(h => (Posicao: curva.PosicaoDe(h.Data), h.Percentual)).Where(h => h.Posicao is not null)
                .Select(h => new SKPoint(X(h.Posicao!.Value), Y(h.Percentual))).Append(new SKPoint(X(posicao), Y(realizado.Percentual))).ToList();
            for (var i = 1; i < pontos.Count; i++)
                c.DrawLine(pontos[i - 1], pontos[i], linha);
            foreach (var p in pontos.SkipLast(1))
                c.DrawCircle(p, 2.8f, ponto);
            c.DrawCircle(pontos[^1], 4.5f, ponto);
            var texto = $"Realizado em {realizado.Data:dd/MM/yyyy}: {realizado.Percentual:0.0}%" +
                        (curva.PorValor ? $" (R$ {Moeda.Formatar(realizado.Valor)})" : "");
            Legenda(texto, (x, y) => c.DrawCircle(x + 11, y, 4.5f, ponto));
        }
    }

    private static void Escrever(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
    }

    private static void Direita(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor? cor = null)
    {
        using var tinta = new SKPaint { Color = cor ?? Texto, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Right, fonte, tinta);
    }
}
