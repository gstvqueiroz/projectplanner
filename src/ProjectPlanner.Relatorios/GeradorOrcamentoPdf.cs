using ProjectPlanner.Core.Orcamento;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 33: o orçamento em PDF (A4 paisagem): a planilha sintética (itens na ordem da EDT, os grupos somando) com os totais e o
/// BDI, e depois a analítica (os insumos de cada composição com coeficiente, preço e parcial).
/// </summary>
public sealed class GeradorOrcamentoPdf
{
    private const float Largura = 841.89f;
    private const float Altura = 595.28f;
    private const float Margem = 36f;
    private const float AlturaDaLinha = 15f;
    private const float Topo = Margem + 62;
    private const float Fundo = Altura - Margem - 10;

    private static readonly float[] Larguras = Proporcionais([42, 52, 52, 300, 36, 62, 70, 70, 86]);
    private static readonly float[] LargurasDaAnalitica = Proporcionais([60, 330, 90, 40, 70, 70, 80]);

    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor Destaque = new(0xE3, 0xF2, 0xFD);

    /// <param name="subtitulo">"Residência Silva · gerado em 15/10/2026 10:00".</param>
    public void Gerar(PlanilhaOrcamentaria planilha, IReadOnlyDictionary<Guid, ComposicaoAberta> composicoes, string subtitulo, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(planilha);
        ArgumentNullException.ThrowIfNull(composicoes);
        ArgumentNullException.ThrowIfNull(destino);
        using var fontes = new Fontes();

        // Monta as páginas primeiro (para o "Página x de y") e desenha depois.
        var conteudo = new List<(float Altura, bool NovaPagina, Action<SKCanvas, float> Desenhar)>();
        void Item(float altura, Action<SKCanvas, float> desenhar, bool novaPagina = false) => conteudo.Add((altura, novaPagina, desenhar));

        Item(AlturaDaLinha + 4, (c, yy) => Cabecalho(c, yy, TextosDoOrcamento.Colunas, Larguras, [5, 6, 7, 8], fontes));
        foreach (var l in planilha.Linhas)
        {
            var linha = l;
            Item(AlturaDaLinha, (c, yy) => LinhaDaTabela(c, yy, TextosDoOrcamento.Celulas(linha), Larguras, [5, 6, 7, 8], linha.EhGrupo, linha.EhGrupo ? Faixa : null, fontes));
        }
        Item(AlturaDaLinha, (c, yy) => LinhaDaTabela(c, yy, ["", "", "", "Total", "", "", "", "", Core.Planejamento.Moeda.Formatar(planilha.Total)],
            Larguras, [8], true, Destaque, fontes));
        Item(10, (_, _) => { });
        foreach (var (rotulo, valor, forte) in TextosDoOrcamento.Totais(planilha))
            Item(14, (c, yy) =>
            {
                Escrever(c, rotulo, Margem + 420, yy + 10, forte ? fontes.Negrito(9) : fontes.Normal(9), Texto);
                Direita(c, valor, Largura - Margem - 4, yy + 10, forte ? fontes.Negrito(9) : fontes.Normal(9));
            });
        Item(16, (c, yy) => Escrever(c, TextosDoOrcamento.Rodape(planilha), Margem, yy + 12, fontes.Normal(8), Cinza));

        var analitica = TextosDoOrcamento.Analitica(planilha, composicoes).ToList();
        var primeira = true;
        foreach (var (linha, composicao) in analitica)
        {
            var (l, comp) = (linha, composicao);
            Item(30, (c, yy) =>
            {
                var titulo = TextosDoOrcamento.TituloDaComposicao(l, comp);
                titulo = Cortar(titulo, Largura - 2 * Margem - 8, fontes.Negrito(9));
                Escrever(c, titulo, Margem, yy + 22, fontes.Negrito(9), Texto);
            }, novaPagina: primeira);
            primeira = false;
            Item(AlturaDaLinha + 4, (c, yy) => Cabecalho(c, yy, TextosDoOrcamento.ColunasDaAnalitica, LargurasDaAnalitica, [4, 5, 6], fontes));
            foreach (var i in comp.Insumos)
            {
                var insumo = i;
                Item(AlturaDaLinha, (c, yy) => LinhaDaTabela(c, yy, TextosDoOrcamento.CelulasDoInsumo(insumo), LargurasDaAnalitica, [4, 5, 6], false, null, fontes));
            }
        }

        // Paginação.
        var y = Topo;
        var porPagina = new List<List<(float Y, Action<SKCanvas, float> Desenhar)>> { new() };
        var titulos = new List<string> { "ORÇAMENTO · PLANILHA SINTÉTICA" };
        foreach (var (altura, novaPagina, desenhar) in conteudo)
        {
            if (novaPagina || y + altura > Fundo)
            {
                porPagina.Add([]);
                titulos.Add(novaPagina ? "ORÇAMENTO · COMPOSIÇÕES (ANALÍTICA)" : titulos[^1]);
                y = Topo;
            }
            porPagina[^1].Add((y, desenhar));
            y += altura;
        }

        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata { Title = "Orçamento", Creator = "ProjectPlanner", Producer = "ProjectPlanner" });
        for (var p = 0; p < porPagina.Count; p++)
        {
            var c = documento.BeginPage(Largura, Altura);
            Escrever(c, titulos[p], Margem, Margem + 4, fontes.Negrito(10), Cinza);
            Escrever(c, subtitulo, Margem, Margem + 24, fontes.Negrito(14), Texto);
            foreach (var (yy, desenhar) in porPagina[p])
                desenhar(c, yy);
            var rodape = $"Página {p + 1} de {porPagina.Count}";
            Escrever(c, rodape, Largura - Margem - fontes.Normal(8).MeasureText(rodape), Altura - Margem + 14, fontes.Normal(8), Cinza);
            documento.EndPage();
        }
        documento.Close();
    }

    private static void Cabecalho(SKCanvas c, float y, string[] colunas, float[] larguras, int[] aDireita, Fontes f)
    {
        using var fundo = new SKPaint { Color = Faixa };
        c.DrawRect(new SKRect(Margem, y, Largura - Margem, y + AlturaDaLinha + 2), fundo);
        var x = Margem;
        for (var i = 0; i < colunas.Length; i++)
        {
            if (aDireita.Contains(i))
                Direita(c, colunas[i], x + larguras[i] - 4, y + 12, f.Negrito(8));
            else
                Escrever(c, colunas[i], x + 4, y + 12, f.Negrito(8), Texto);
            x += larguras[i];
        }
    }

    private static void LinhaDaTabela(SKCanvas c, float y, string[] celulas, float[] larguras, int[] aDireita, bool negrito, SKColor? fundo, Fontes f)
    {
        if (fundo is { } cor)
        {
            using var tinta = new SKPaint { Color = cor };
            c.DrawRect(new SKRect(Margem, y, Largura - Margem, y + AlturaDaLinha), tinta);
        }
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.5f };
        c.DrawLine(Margem, y + AlturaDaLinha, Largura - Margem, y + AlturaDaLinha, traco);
        var fonte = negrito ? f.Negrito(7.5f) : f.Normal(7.5f);
        var x = Margem;
        for (var i = 0; i < celulas.Length; i++)
        {
            var texto = Cortar(celulas[i], larguras[i] - 8, fonte);
            if (aDireita.Contains(i))
                Direita(c, texto, x + larguras[i] - 4, y + 11, fonte);
            else
                Escrever(c, texto, x + 4, y + 11, fonte, Texto);
            x += larguras[i];
        }
    }

    private static string Cortar(string texto, float largura, SKFont fonte)
    {
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        while (texto.Length > 2 && fonte.MeasureText(texto + "…") > largura)
            texto = texto[..^1];
        return texto.TrimEnd() + "…";
    }

    private static float[] Proporcionais(float[] pontos)
    {
        var total = pontos.Sum();
        return [.. pontos.Select(p => p / total * (Largura - 2 * Margem))];
    }

    private static void Escrever(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
    }

    private static void Direita(SKCanvas c, string texto, float x, float y, SKFont fonte)
    {
        using var tinta = new SKPaint { Color = Texto, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Right, fonte, tinta);
    }
}
