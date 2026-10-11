using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 36: o boletim de medição em PDF (A4 paisagem): o cabeçalho com a obra e o contrato, a tabela de itens (com os grupos
/// e o total; continua nas páginas seguintes, repetindo o cabeçalho das colunas), o resumo financeiro, as glosas, as
/// observações e as assinaturas.
/// </summary>
public sealed class GeradorBoletimPdf
{
    internal const float Largura = 841.89f;
    internal const float Altura = 595.28f;
    internal const float Margem = 26f;
    private const float AlturaDaLinha = 13.5f;
    private const float AlturaDoCabecalhoDaTabela = 22f;
    private const float TopoDaPrimeira = 150f;
    private const float TopoDasOutras = 56f;
    private const float Rodape = 24f;

    private static readonly float[] Larguras = [30, 128, 26, 52, 52, 64, 50, 50, 52, 50, 64, 64, 64, 32];

    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor Destaque = new(0xE3, 0xF2, 0xFD);

    /// <summary>Quantas linhas cabem na primeira página e nas seguintes.</summary>
    internal static int LinhasNaPrimeira => (int)((Altura - TopoDaPrimeira - AlturaDoCabecalhoDaTabela - Rodape - Margem) / AlturaDaLinha);

    internal static int LinhasNasOutras => (int)((Altura - TopoDasOutras - AlturaDoCabecalhoDaTabela - Rodape - Margem) / AlturaDaLinha);

    /// <param name="indicadores">Bloco 37: o painel de indicadores, numa página no fim; nulo sem ela.</param>
    public void Gerar(BoletimDeMedicao boletim, CabecalhoDoRelatorio cabecalho, DateTime geradoEm, Stream destino,
        Core.Indicadores.PainelDeIndicadores? indicadores = null)
    {
        ArgumentNullException.ThrowIfNull(boletim);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);
        using var fontes = new Fontes();
        IReadOnlyList<Action<SKCanvas, SKRect>> extras = indicadores is null ? [] : [(c, area) => PaginaDeIndicadoresPdf.Desenhar(c, indicadores, area, fontes)];

        // As linhas da tabela (com a de total) divididas em páginas; o fim vai junto da última se couber.
        var linhas = boletim.Linhas.Select(l => (Celulas: TextosDoBoletim.Celulas(l), Grupo: l.EhGrupo, Total: false))
            .Append((Celulas: TextosDoBoletim.Total(boletim.Resumo), Grupo: true, Total: true)).ToList();
        var paginas = new List<List<(string[] Celulas, bool Grupo, bool Total)>>();
        for (var i = 0; i < linhas.Count;)
        {
            var cabem = paginas.Count == 0 ? LinhasNaPrimeira : LinhasNasOutras;
            paginas.Add(linhas.Skip(i).Take(cabem).ToList());
            i += cabem;
        }
        var topoDaUltima = paginas.Count == 1 ? TopoDaPrimeira : TopoDasOutras;
        var fimDaTabela = topoDaUltima + AlturaDoCabecalhoDaTabela + paginas[^1].Count * AlturaDaLinha;
        var fimNaUltima = fimDaTabela + 16 + AlturaDoFim(boletim, fontes) <= Altura - Rodape - Margem;
        var total = paginas.Count + (fimNaUltima ? 0 : 1) + extras.Count;

        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = $"{boletim.Medicao.Rotulo} · {cabecalho.Obra}",
            Author = cabecalho.Responsavel ?? "",
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
        });
        var numero = 1;
        for (var p = 0; p < paginas.Count; p++)
        {
            var c = documento.BeginPage(Largura, Altura);
            var topo = p == 0 ? Topo(c, boletim, cabecalho, geradoEm, fontes) : CabecalhoCompacto(c, boletim, cabecalho, fontes);
            var y = Tabela(c, paginas[p], topo, fontes);
            if (p == paginas.Count - 1 && fimNaUltima)
                Fim(c, boletim, y + 16, fontes);
            NumeroDaPagina(c, numero++, total, fontes);
            documento.EndPage();
        }
        if (!fimNaUltima)
        {
            var c = documento.BeginPage(Largura, Altura);
            Fim(c, boletim, CabecalhoCompacto(c, boletim, cabecalho, fontes), fontes);
            NumeroDaPagina(c, numero++, total, fontes);
            documento.EndPage();
        }
        foreach (var desenhar in extras)
        {
            var c = documento.BeginPage(Largura, Altura);
            var topo = CabecalhoCompacto(c, boletim, cabecalho, fontes);
            desenhar(c, new SKRect(Margem, topo, Largura - Margem, Altura - Rodape - Margem));
            NumeroDaPagina(c, numero++, total, fontes);
            documento.EndPage();
        }
        documento.Close();
    }

    // ---- Cabeçalhos ----

    private static float Topo(SKCanvas c, BoletimDeMedicao b, CabecalhoDoRelatorio cabecalho, DateTime geradoEm, Fontes f)
    {
        if (cabecalho.Logotipo is { Length: > 0 } logo)
            DesenharLogotipo(c, logo, new SKRect(Largura - Margem - 150, Margem - 4, Largura - Margem, Margem + 46));
        Escrever(c, TextosDoBoletim.Titulo(b.Medicao), Margem, Margem + 8, f.Negrito(10), Cinza);
        Escrever(c, cabecalho.Obra, Margem, Margem + 30, f.Negrito(16), Texto);
        var dados = TextosDoBoletim.Dados(b, cabecalho, geradoEm);
        var meio = (dados.Count + 1) / 2;
        for (var i = 0; i < dados.Count; i++)
        {
            var (x, y) = (i < meio ? Margem : Margem + 390, Margem + 52 + (i % meio) * 14);
            Escrever(c, dados[i].Rotulo, x, y, f.Negrito(8), Cinza);
            Escrever(c, Truncar(dados[i].Valor, 280, f.Normal(8.5f)), x + 100, y, f.Normal(8.5f), Texto);
        }
        return TopoDaPrimeira;
    }

    private static float CabecalhoCompacto(SKCanvas c, BoletimDeMedicao b, CabecalhoDoRelatorio cabecalho, Fontes f)
    {
        Escrever(c, TextosDoBoletim.Titulo(b.Medicao), Margem, Margem + 8, f.Negrito(9), Cinza);
        Escrever(c, TextosDoBoletim.Cabecalho(b, cabecalho), Margem, Margem + 24, f.Normal(9), Texto);
        return TopoDasOutras;
    }

    private static void NumeroDaPagina(SKCanvas c, int pagina, int total, Fontes f)
    {
        var texto = $"Página {pagina} de {total}";
        Escrever(c, texto, Largura - Margem - f.Normal(8).MeasureText(texto), Altura - Margem + 10, f.Normal(8), Cinza);
    }

    // ---- Tabela ----

    private static float Tabela(SKCanvas c, IReadOnlyList<(string[] Celulas, bool Grupo, bool Total)> linhas, float topo, Fontes f)
    {
        var direita = Margem + Larguras.Sum();
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.6f };
        using var faixa = new SKPaint { Color = Faixa };
        using var destaque = new SKPaint { Color = Destaque };
        c.DrawRect(new SKRect(Margem, topo, direita, topo + AlturaDoCabecalhoDaTabela), faixa);
        var x = Margem;
        for (var i = 0; i < Larguras.Length; i++)
        {
            var partes = Quebrar(TextosDoBoletim.Colunas[i], Larguras[i] - 6, f.Negrito(6.5f));
            for (var k = 0; k < partes.Count; k++)
                Alinhar(c, partes[k], x, Larguras[i], topo + (partes.Count == 1 ? 13 : 9 + k * 8), f.Negrito(6.5f), Texto, i > 2);
            x += Larguras[i];
        }
        var y = topo + AlturaDoCabecalhoDaTabela;
        foreach (var (celulas, grupo, total) in linhas)
        {
            if (total)
                c.DrawRect(new SKRect(Margem, y, direita, y + AlturaDaLinha), destaque);
            else if (grupo)
                c.DrawRect(new SKRect(Margem, y, direita, y + AlturaDaLinha), faixa);
            var fonte = grupo ? f.Negrito(6.8f) : f.Normal(6.8f);
            x = Margem;
            for (var i = 0; i < Larguras.Length; i++)
            {
                var texto = Truncar(celulas[i], Larguras[i] - 5, fonte);
                Alinhar(c, texto, x, Larguras[i], y + 9.5f, fonte, Texto, i > 2);
                x += Larguras[i];
            }
            y += AlturaDaLinha;
            c.DrawLine(Margem, y, direita, y, traco);
        }
        return y;
    }

    // ---- Resumo, glosas, observações e assinaturas ----

    // A altura do fim: a coluna mais alta (o resumo, ou as glosas e as observações) e as assinaturas.
    private static float AlturaDoFim(BoletimDeMedicao b, Fontes f)
    {
        var resumo = 26 + TextosDoBoletim.Resumo(b.Resumo).Count * 13;
        var direita = (b.Medicao.Glosas.Count > 0 ? 22 + Math.Min(b.Medicao.Glosas.Count, 6) * 12 : 0)
                      + (b.Medicao.Observacoes is { } o ? 16 + Math.Min(Quebrar(o.Replace('\n', ' '), Largura - 2 * Margem - 400, f.Normal(8.5f)).Count, 7) * 11 : 0);
        return Math.Max(resumo, direita) + 62;
    }

    private static void Fim(SKCanvas c, BoletimDeMedicao b, float topo, Fontes f)
    {
        var resumo = TextosDoBoletim.Resumo(b.Resumo);
        Escrever(c, "Resumo financeiro", Margem, topo + 10, f.Negrito(10), Texto);
        var y = topo + 26;
        using var destaque = new SKPaint { Color = Destaque };
        foreach (var (rotulo, valor, forte) in resumo)
        {
            if (forte)
                c.DrawRect(new SKRect(Margem, y - 9, Margem + 370, y + 4), destaque);
            Escrever(c, Truncar(rotulo, 230, f.Normal(8.5f)), Margem + 4, y, forte ? f.Negrito(8.5f) : f.Normal(8.5f), Texto);
            Direita(c, valor, Margem + 366, y, forte ? f.Negrito(8.5f) : f.Normal(8.5f));
            y += 13;
        }

        var xDireita = Margem + 400;
        var yd = topo + 10;
        if (b.Medicao.Glosas.Count > 0)
        {
            Escrever(c, "Glosas", xDireita, yd, f.Negrito(10), Texto);
            yd += 16;
            foreach (var glosa in b.Medicao.Glosas.Take(6))
            {
                Escrever(c, Truncar(glosa.Descricao, 260, f.Normal(8.5f)), xDireita, yd, f.Normal(8.5f), Texto);
                Direita(c, Core.Planejamento.Moeda.ComSimbolo(glosa.Valor), Largura - Margem, yd, f.Normal(8.5f));
                yd += 12;
            }
            yd += 6;
        }
        if (b.Medicao.Observacoes is { } observacoes)
        {
            Escrever(c, "Observações", xDireita, yd, f.Negrito(10), Texto);
            yd += 16;
            foreach (var linha in Quebrar(observacoes.Replace('\n', ' '), Largura - Margem - xDireita, f.Normal(8.5f)).Take(7))
            {
                Escrever(c, linha, xDireita, yd, f.Normal(8.5f), Texto);
                yd += 11;
            }
        }

        // Assinaturas: a contratada (quem emite) e o contratante (a fiscalização).
        var yAssinatura = Math.Max(y, yd) + 46;
        using var traco = new SKPaint { Color = Cinza, StrokeWidth = 0.6f };
        foreach (var (x, rotulo) in new[] { (Margem + 20f, "Contratada (responsável técnico)"), (Margem + 420f, "Contratante (fiscalização)") })
        {
            c.DrawLine(x, yAssinatura, x + 300, yAssinatura, traco);
            Escrever(c, rotulo, x, yAssinatura + 12, f.Normal(8), Cinza);
        }
    }

    // ---- Peças ----

    internal static void DesenharLogotipo(SKCanvas c, byte[] bytes, SKRect caixa)
    {
        using var imagem = SKImage.FromEncodedData(bytes);
        if (imagem is null)
            return;
        var escala = Math.Min(caixa.Width / imagem.Width, caixa.Height / imagem.Height);
        var (l, a) = (imagem.Width * escala, imagem.Height * escala);
        c.DrawImage(imagem, new SKRect(caixa.Right - l, caixa.Top, caixa.Right, caixa.Top + a), new SKSamplingOptions(SKFilterMode.Linear));
    }

    private static List<string> Quebrar(string texto, float largura, SKFont fonte)
    {
        var linhas = new List<string>();
        var atual = "";
        foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
            if (fonte.MeasureText(tentativa) > largura && atual.Length > 0)
            {
                linhas.Add(atual);
                atual = palavra;
            }
            else
                atual = tentativa;
        }
        if (atual.Length > 0)
            linhas.Add(atual);
        return linhas;
    }

    private static string Truncar(string texto, float largura, SKFont fonte)
    {
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        while (texto.Length > 1 && fonte.MeasureText(texto + "…") > largura)
            texto = texto[..^1];
        return texto.TrimEnd() + "…";
    }

    private static void Alinhar(SKCanvas c, string texto, float x, float largura, float y, SKFont fonte, SKColor cor, bool aDireita)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        if (aDireita)
            c.DrawText(texto, x + largura - 3, y, SKTextAlign.Right, fonte, tinta);
        else
            c.DrawText(texto, x + 3, y, SKTextAlign.Left, fonte, tinta);
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
