using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// O relatório em PDF (A4 retrato), desenhado com o SkiaSharp: capa com os dados e o andamento, uma página (ou mais) por
/// grupo de fotos, com a legenda de cada foto, e o índice com as assinaturas. As fotos vão como o JPEG guardado no
/// projeto (sem recompressão).
/// </summary>
public sealed class GeradorPdf : IGeradorDeRelatorio
{
    private const float Largura = 595.28f;
    private const float Altura = 841.89f;
    private const float Margem = 42f;
    private const float Espaco = 12f;
    private const float AlturaDaLinhaDoIndice = 15f;

    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Destaque = new(0x15, 0x65, 0xC0);

    public string Extensao => "pdf";

    public void Gerar(RelatorioFotografico relatorio, Func<Guid, byte[]?> imagem, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(relatorio);
        ArgumentNullException.ThrowIfNull(imagem);
        ArgumentNullException.ThrowIfNull(destino);
        using var fontes = new Fontes();
        var indice = PaginasDoIndice(relatorio);
        var texto = PaginasDoTexto(relatorio, fontes);
        var total = 1 + texto.Count + (relatorio.Indicadores is null ? 0 : 1) + relatorio.Paginas.Count + relatorio.AntesEDepois.Count + indice.Count;
        var metadados = new SKDocumentPdfMetadata
        {
            Title = relatorio.Titulo,
            Author = relatorio.Cabecalho.Responsavel ?? "",
            Subject = relatorio.Cabecalho.Obra,
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
            // As fotos (JPEG) vão como estão; outra imagem sem transparência sai em JPEG 85, e não sem perda.
            EncodingQuality = 85,
            Creation = relatorio.GeradoEm,
            Modified = relatorio.GeradoEm,
        };
        using var documento = SKDocument.CreatePdf(destino, metadados);
        var pagina = 1;
        Capa(Pagina(documento), relatorio, fontes, pagina++, total);
        documento.EndPage();
        foreach (var linhas in texto)
        {
            PaginaDeTexto(Pagina(documento), relatorio, linhas, fontes, pagina++, total);
            documento.EndPage();
        }
        if (relatorio.Indicadores is { } painel)
        {
            var c = Pagina(documento);
            Escrever(c, TextosDoRelatorio.Cabecalho(relatorio), Margem, Margem, f: fontes);
            PaginaDeIndicadoresPdf.Desenhar(c, painel, new SKRect(Margem, Margem + 14, Largura - Margem, Altura - Margem - 14), fontes);
            Rodape(c, relatorio, fontes, pagina++, total);
            documento.EndPage();
        }
        foreach (var fotos in relatorio.Paginas)
        {
            PaginaDeFotos(Pagina(documento), relatorio, fotos, imagem, fontes, pagina++, total);
            documento.EndPage();
        }
        foreach (var comparacao in relatorio.AntesEDepois)
        {
            PaginaAntesEDepois(Pagina(documento), relatorio, comparacao, imagem, fontes, pagina++, total);
            documento.EndPage();
        }
        for (var i = 0; i < indice.Count; i++)
        {
            Indice(Pagina(documento), relatorio, indice[i], ultima: i == indice.Count - 1, fontes, pagina++, total);
            documento.EndPage();
        }
        documento.Close();
    }

    private static SKCanvas Pagina(SKDocument documento) => documento.BeginPage(Largura, Altura);

    // ---- Capa ----

    private static void Capa(SKCanvas c, RelatorioFotografico r, Fontes f, int pagina, int total)
    {
        var y = Margem;
        if (r.Cabecalho.Logotipo is { Length: > 0 } logo && SKImage.FromEncodedData(logo) is { } imagemDoLogo)
            using (imagemDoLogo)
            {
                var caixa = Encaixar(imagemDoLogo.Width, imagemDoLogo.Height, new SKRect(Margem, y, Margem + 160, y + 70), centralizar: false);
                c.DrawImage(imagemDoLogo, caixa);
            }
        y = 190;
        Centralizado(c, TextosDoRelatorio.Titulo, y, f.Negrito(13), Cinza);
        y += 30;
        foreach (var linha in Quebrar(r.Cabecalho.Obra, f.Negrito(24), Largura - 2 * Margem))
        {
            Centralizado(c, linha, y, f.Negrito(24), Texto);
            y += 30;
        }
        Centralizado(c, $"{TextosDoRelatorio.Numero(r)} · {TextosDoRelatorio.Periodo(r)}", y + 4, f.Normal(12), Cinza);
        y += 44;

        using (var traco = new SKPaint { Color = Linha, StrokeWidth = 1 })
            c.DrawLine(Margem, y, Largura - Margem, y, traco);
        y += 22;
        foreach (var (rotulo, valor) in TextosDoRelatorio.DadosDaCapa(r))
        {
            Escrever(c, rotulo, Margem, y, f.Negrito(10), Cinza);
            foreach (var linha in Quebrar(valor, f.Normal(11), Largura - 2 * Margem - 140))
            {
                Escrever(c, linha, Margem + 140, y, f.Normal(11), Texto);
                y += 16;
            }
            y += 4;
        }

        var andamento = TextosDoRelatorio.Andamento(r);
        if (andamento.Count > 0 && r.Resumo is { } resumo)
        {
            y += 18;
            Escrever(c, "Andamento da obra", Margem, y, f.Negrito(13), Texto);
            y += 14;
            Barra(c, new SKRect(Margem, y, Largura - Margem, y + 10), resumo.PercentualConcluido, resumo.PercentualPrevisto);
            y += 28;
            foreach (var linha in andamento)
            {
                Escrever(c, linha, Margem, y, f.Normal(10), Texto);
                y += 15;
            }
            y = Lista(c, "Atrasadas", TextosDoRelatorio.Atrasadas(r), y + 8, f);
            Lista(c, "Próximos marcos (30 dias)", TextosDoRelatorio.ProximosMarcos(r), y + 4, f);
        }
        Rodape(c, r, f, pagina, total);
    }

    private static float Lista(SKCanvas c, string titulo, IReadOnlyList<string> itens, float y, Fontes f)
    {
        if (itens.Count == 0)
            return y;
        Escrever(c, titulo, Margem, y, f.Negrito(10), Texto);
        y += 15;
        foreach (var item in itens)
        {
            Escrever(c, "• " + item, Margem + 8, y, f.Normal(9.5f), Texto);
            y += 14;
        }
        return y;
    }

    // Concluído em azul; o previsto como um traço vertical.
    private static void Barra(SKCanvas c, SKRect caixa, int concluido, int previsto)
    {
        using var fundo = new SKPaint { Color = new SKColor(0xEC, 0xEF, 0xF1), IsAntialias = true };
        using var cheio = new SKPaint { Color = Destaque, IsAntialias = true };
        using var marca = new SKPaint { Color = new SKColor(0xE6, 0x51, 0x00), StrokeWidth = 2 };
        c.DrawRoundRect(caixa, 3, 3, fundo);
        if (concluido > 0)
            c.DrawRoundRect(new SKRect(caixa.Left, caixa.Top, caixa.Left + caixa.Width * Math.Clamp(concluido, 0, 100) / 100f, caixa.Bottom), 3, 3, cheio);
        var x = caixa.Left + caixa.Width * Math.Clamp(previsto, 0, 100) / 100f;
        c.DrawLine(x, caixa.Top - 3, x, caixa.Bottom + 3, marca);
    }

    // ---- Fotos ----

    private static void PaginaDeFotos(SKCanvas c, RelatorioFotografico r, PaginaDeFotos pagina, Func<Guid, byte[]?> imagem, Fontes f,
        int numero, int total)
    {
        Escrever(c, TextosDoRelatorio.Cabecalho(r), Margem, Margem, f.Normal(8), Cinza);
        var y = Margem + 24;
        foreach (var linha in Quebrar(TextosDoRelatorio.TituloDaPagina(pagina), f.Negrito(14), Largura - 2 * Margem).Take(2))
        {
            Escrever(c, linha, Margem, y, f.Negrito(14), Texto);
            y += 18;
        }
        if (pagina.Grupo.Detalhe is { } detalhe && !pagina.Continuacao)
        {
            Escrever(c, detalhe, Margem, y, f.Normal(9), Cinza);
            y += 12;
        }
        var area = new SKRect(Margem, y + 6, Largura - Margem, Altura - Margem - 18);
        var (colunas, linhas) = r.Opcoes.Grade;
        var larguraDaColuna = (area.Width - Espaco * (colunas - 1)) / colunas;
        var alturaDaLinha = (area.Height - Espaco * (linhas - 1)) / linhas;
        int linhaAtual = 0, colunaAtual = 0;
        foreach (var (foto, ocupa) in pagina.Fotos)
        {
            if (ocupa == colunas && colunaAtual != 0)
                (linhaAtual, colunaAtual) = (linhaAtual + 1, 0);
            var x = area.Left + colunaAtual * (larguraDaColuna + Espaco);
            var topo = area.Top + linhaAtual * (alturaDaLinha + Espaco);
            Foto(c, foto, new SKRect(x, topo, x + larguraDaColuna * ocupa + Espaco * (ocupa - 1), topo + alturaDaLinha), imagem, f);
            colunaAtual += ocupa;
            if (colunaAtual >= colunas)
                (linhaAtual, colunaAtual) = (linhaAtual + 1, 0);
        }
        Rodape(c, r, f, numero, total);
    }

    private static void Foto(SKCanvas c, FotoNoRelatorio foto, SKRect caixa, Func<Guid, byte[]?> imagem, Fontes f)
    {
        var textos = new List<(string Linha, SKFont Fonte, SKColor Cor)>();
        if (foto.Titulo is { } titulo)
            textos.AddRange(Quebrar(titulo, f.Negrito(9), caixa.Width).Take(2).Select(l => (l, f.Negrito(9), Texto)));
        textos.AddRange(Quebrar(foto.Legenda, f.Normal(8), caixa.Width).Take(2).Select(l => (l, f.Normal(8), Cinza)));
        if (foto.Texto is { } legenda)
            textos.AddRange(Cortar(Quebrar(legenda, f.Normal(8.5f), caixa.Width), 3, f.Normal(8.5f), caixa.Width).Select(l => (l, f.Normal(8.5f), Texto)));
        var alturaDosTextos = textos.Count * 11f + 4;
        var areaDaFoto = new SKRect(caixa.Left, caixa.Top, caixa.Right, Math.Max(caixa.Top + 20, caixa.Bottom - alturaDosTextos));

        using var moldura = new SKPaint { Color = Linha, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f };
        if (imagem(foto.Foto.Id) is { Length: > 0 } bytes && SKImage.FromEncodedData(bytes) is { } img)
            using (img)
            {
                // Apoiada na base da área: as legendas da mesma linha começam juntas, logo abaixo das fotos.
                var desenho = Encaixar(img.Width, img.Height, areaDaFoto, centralizar: true);
                desenho.Offset(0, areaDaFoto.Bottom - desenho.Bottom);
                c.DrawImage(img, desenho);
                c.DrawRect(desenho, moldura);
            }
        else
        {
            using var vazio = new SKPaint { Color = new SKColor(0xEC, 0xEF, 0xF1) };
            c.DrawRect(areaDaFoto, vazio);
            Centralizado(c, "Imagem indisponível", areaDaFoto.MidY, f.Normal(9), Cinza, areaDaFoto.MidX);
        }

        var y = areaDaFoto.Bottom + 12;
        foreach (var (linha, fonte, cor) in textos)
        {
            Escrever(c, linha, caixa.Left, y, fonte, cor);
            y += 11;
        }
    }

    // ---- Bloco 29: texto do andamento ----

    private sealed record LinhaDeTexto(string Texto, bool Titulo, bool Negrito, float Tamanho, float Antes);

    // O texto em linhas já quebradas, distribuídas pelas páginas (um texto longo continua na seguinte).
    private static List<List<LinhaDeTexto>> PaginasDoTexto(RelatorioFotografico r, Fontes f)
    {
        var paginas = new List<List<LinhaDeTexto>>();
        if (r.Texto is not { } texto)
            return paginas;
        var largura = Largura - 2 * Margem;
        var linhas = new List<LinhaDeTexto> { new(TextosDoRelatorio.TituloDoTexto, true, true, 14, 0) };
        foreach (var paragrafo in texto.Paragrafos)
            linhas.AddRange(Quebrar(paragrafo, f.Normal(10.5f), largura).Select((l, i) => new LinhaDeTexto(l, false, false, 10.5f, i == 0 ? 8 : 0)));
        if (texto.PontosDeAtencao.Count > 0)
        {
            linhas.Add(new LinhaDeTexto(TextosDoRelatorio.TituloDosPontos, false, true, 11.5f, 16));
            foreach (var ponto in texto.PontosDeAtencao)
                linhas.AddRange(Quebrar(ponto, f.Normal(10), largura - 14)
                    .Select((l, i) => new LinhaDeTexto((i == 0 ? "• " : "   ") + l, false, false, 10, i == 0 ? 4 : 0)));
        }
        var limite = Altura - Margem - 24;
        var atual = new List<LinhaDeTexto>();
        var y = Margem + 24f;
        foreach (var linha in linhas)
        {
            var altura = linha.Antes + linha.Tamanho * 1.45f;
            if (y + altura > limite && atual.Count > 0)
            {
                paginas.Add(atual);
                (atual, y) = ([], Margem + 24f);
            }
            atual.Add(linha);
            y += altura;
        }
        paginas.Add(atual);
        return paginas;
    }

    private static void PaginaDeTexto(SKCanvas c, RelatorioFotografico r, List<LinhaDeTexto> linhas, Fontes f, int numero, int total)
    {
        Escrever(c, TextosDoRelatorio.Cabecalho(r), Margem, Margem, f.Normal(8), Cinza);
        var y = Margem + 24f;
        foreach (var linha in linhas)
        {
            y += linha.Antes;
            Escrever(c, linha.Texto, Margem, y + linha.Tamanho, linha.Negrito ? f.Negrito(linha.Tamanho) : f.Normal(linha.Tamanho), Texto);
            y += linha.Tamanho * 1.45f;
        }
        Rodape(c, r, f, numero, total);
    }

    // ---- Bloco 29: antes e depois ----

    private static void PaginaAntesEDepois(SKCanvas c, RelatorioFotografico r, AntesEDepoisNoRelatorio comparacao, Func<Guid, byte[]?> imagem,
        Fontes f, int numero, int total)
    {
        Escrever(c, TextosDoRelatorio.Cabecalho(r), Margem, Margem, f.Normal(8), Cinza);
        var y = Margem + 24;
        foreach (var linha in Quebrar(TextosDoRelatorio.TituloAntesEDepois(comparacao), f.Negrito(14), Largura - 2 * Margem).Take(2))
        {
            Escrever(c, linha, Margem, y, f.Negrito(14), Texto);
            y += 18;
        }
        Escrever(c, comparacao.Evolucao, Margem, y, f.Normal(10), Cinza);
        var largura = (Largura - 2 * Margem - Espaco) / 2;
        var topo = y + 16;
        var lados = new[] { (comparacao.Antes, comparacao.LegendaAntes), (comparacao.Depois, comparacao.LegendaDepois) };
        for (var i = 0; i < lados.Length; i++)
        {
            var (foto, legenda) = lados[i];
            var x = Margem + i * (largura + Espaco);
            var area = new SKRect(x, topo, x + largura, topo + 420);
            using var moldura = new SKPaint { Color = Linha, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f };
            var desenho = area;
            if (imagem(foto.Id) is { Length: > 0 } bytes && SKImage.FromEncodedData(bytes) is { } img)
                using (img)
                {
                    desenho = Encaixar(img.Width, img.Height, area, centralizar: true);
                    desenho.Offset(0, area.Top - desenho.Top);
                    c.DrawImage(img, desenho);
                    c.DrawRect(desenho, moldura);
                }
            else
            {
                using var vazio = new SKPaint { Color = new SKColor(0xEC, 0xEF, 0xF1) };
                desenho = new SKRect(area.Left, area.Top, area.Right, area.Top + largura * 0.75f);
                c.DrawRect(desenho, vazio);
                Centralizado(c, "Imagem indisponível", desenho.MidY, f.Normal(9), Cinza, desenho.MidX);
            }
            var yLegenda = desenho.Bottom + 14;
            foreach (var linha in Quebrar(legenda, f.Normal(8.5f), largura).Take(3))
            {
                Escrever(c, linha, x, yLegenda, f.Normal(8.5f), Texto);
                yLegenda += 11;
            }
            if (foto.Titulo is { } titulo)
                foreach (var linha in Quebrar(titulo, f.Negrito(9), largura).Take(2))
                {
                    Escrever(c, linha, x, yLegenda, f.Negrito(9), Texto);
                    yLegenda += 11;
                }
        }
        Rodape(c, r, f, numero, total);
    }

    // ---- Índice e assinaturas ----

    // As fotos do índice em páginas; as assinaturas ficam embaixo da última (ou numa página só delas).
    private static List<IReadOnlyList<FotoNoRelatorio>> PaginasDoIndice(RelatorioFotografico r)
    {
        var paginas = new List<IReadOnlyList<FotoNoRelatorio>>();
        if (!r.Opcoes.ComIndice || r.TotalDeFotos == 0)
            return r.Opcoes.ComIndice || r.Opcoes.ComAssinaturas ? [[]] : paginas;
        var porPagina = (int)((Altura - 2 * Margem - 90) / AlturaDaLinhaDoIndice);
        var fotos = r.Fotos.ToList();
        for (var i = 0; i < fotos.Count; i += porPagina)
            paginas.Add(fotos.Skip(i).Take(porPagina).ToList());
        // Sem lugar para as assinaturas embaixo da última: uma página a mais.
        if (r.Opcoes.ComAssinaturas && paginas[^1].Count > porPagina - 11)
            paginas.Add([]);
        return paginas;
    }

    private static void Indice(SKCanvas c, RelatorioFotografico r, IReadOnlyList<FotoNoRelatorio> fotos, bool ultima, Fontes f, int numero, int total)
    {
        Escrever(c, TextosDoRelatorio.Cabecalho(r), Margem, Margem, f.Normal(8), Cinza);
        var y = Margem + 24;
        float[] colunas = [Margem, Margem + 34, Margem + 124, Margem + 330];
        if (fotos.Count > 0)
        {
            Escrever(c, "Índice das fotos", Margem, y, f.Negrito(14), Texto);
            y += 22;
            for (var i = 0; i < colunas.Length; i++)
                Escrever(c, TextosDoRelatorio.ColunasDoIndice[i], colunas[i], y, f.Negrito(8.5f), Cinza);
            y += 6;
            using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.75f };
            c.DrawLine(Margem, y, Largura - Margem, y, traco);
            y += 11;
            foreach (var foto in fotos)
            {
                var celulas = TextosDoRelatorio.LinhaDoIndice(foto);
                for (var i = 0; i < colunas.Length; i++)
                {
                    var limite = (i + 1 < colunas.Length ? colunas[i + 1] : Largura - Margem) - colunas[i] - 6;
                    Escrever(c, Cortar(Quebrar(celulas[i], f.Normal(8.5f), limite), 1, f.Normal(8.5f), limite)[0], colunas[i], y, f.Normal(8.5f), Texto);
                }
                y += AlturaDaLinhaDoIndice;
            }
        }
        if (ultima && r.Opcoes.ComAssinaturas)
            Assinaturas(c, r, f);
        Rodape(c, r, f, numero, total);
    }

    private static void Assinaturas(SKCanvas c, RelatorioFotografico r, Fontes f)
    {
        var y = Altura - Margem - 70;
        var assinaturas = TextosDoRelatorio.Assinaturas(r);
        var largura = (Largura - 2 * Margem - Espaco * 3) / assinaturas.Count;
        using var traco = new SKPaint { Color = Texto, StrokeWidth = 0.75f };
        for (var i = 0; i < assinaturas.Count; i++)
        {
            var x = Margem + i * (largura + Espaco * 3);
            c.DrawLine(x, y, x + largura, y, traco);
            Centralizado(c, assinaturas[i].Nome ?? "", y + 14, f.Normal(9), Texto, x + largura / 2);
            Centralizado(c, assinaturas[i].Cargo, y + 27, f.Normal(8.5f), Cinza, x + largura / 2);
        }
    }

    private static void Rodape(SKCanvas c, RelatorioFotografico r, Fontes f, int pagina, int total)
    {
        var texto = TextosDoRelatorio.Rodape(pagina, total);
        Escrever(c, texto, Largura - Margem - f.Normal(8).MeasureText(texto), Altura - Margem + 6, f.Normal(8), Cinza);
        Escrever(c, $"Relatório fotográfico nº {r.Numero:00}", Margem, Altura - Margem + 6, f.Normal(8), Cinza);
    }

    // ---- Texto e imagem ----

    private static void Escrever(SKCanvas c, string texto, float x, float y, Fontes f) => Escrever(c, texto, x, y, f.Normal(8), Cinza);

    private static void Escrever(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
    }

    private static void Centralizado(SKCanvas c, string texto, float y, SKFont fonte, SKColor cor, float? centro = null)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(texto, centro ?? Largura / 2, y, SKTextAlign.Center, fonte, tinta);
    }

    /// <summary>Quebra o texto em linhas que cabem na largura (palavras longas demais são cortadas).</summary>
    internal static List<string> Quebrar(string texto, SKFont fonte, float largura)
    {
        var linhas = new List<string>();
        foreach (var paragrafo in texto.Replace("\r\n", "\n").Split('\n'))
        {
            var atual = "";
            foreach (var palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
                if (fonte.MeasureText(tentativa) <= largura)
                {
                    atual = tentativa;
                    continue;
                }
                if (atual.Length > 0)
                    linhas.Add(atual);
                atual = palavra;
                while (fonte.MeasureText(atual) > largura && atual.Length > 1)
                {
                    var corte = atual.Length - 1;
                    while (corte > 1 && fonte.MeasureText(atual[..corte]) > largura)
                        corte--;
                    linhas.Add(atual[..corte]);
                    atual = atual[corte..];
                }
            }
            linhas.Add(atual);
        }
        return linhas;
    }

    /// <summary>No máximo <paramref name="maximo"/> linhas; a última ganha "…" quando sobrou texto.</summary>
    private static List<string> Cortar(List<string> linhas, int maximo, SKFont fonte, float largura)
    {
        if (linhas.Count <= maximo)
            return linhas;
        var cortadas = linhas.Take(maximo).ToList();
        var ultima = cortadas[^1];
        while (ultima.Length > 0 && fonte.MeasureText(ultima + "…") > largura)
            ultima = ultima[..^1];
        cortadas[^1] = ultima.TrimEnd() + "…";
        return cortadas;
    }

    /// <summary>O retângulo da imagem dentro da caixa, sem distorcer.</summary>
    internal static SKRect Encaixar(int largura, int altura, SKRect caixa, bool centralizar)
    {
        var escala = Math.Min(caixa.Width / largura, caixa.Height / altura);
        var (l, a) = (largura * escala, altura * escala);
        var x = centralizar ? caixa.MidX - l / 2 : caixa.Left;
        var y = centralizar ? caixa.MidY - a / 2 : caixa.Top;
        return new SKRect(x, y, x + l, y + a);
    }
}

// As fontes do sistema (Helvetica no Mac, Arial no Windows), com os acentos do português.
internal sealed class Fontes : IDisposable
{
    private readonly SKTypeface _normal = Familia(SKFontStyle.Normal);
    private readonly SKTypeface _negrito = Familia(SKFontStyle.Bold);
    private readonly Dictionary<(bool, float), SKFont> _fontes = [];

    private static SKTypeface Familia(SKFontStyle estilo)
    {
        foreach (var nome in new[] { "Helvetica Neue", "Helvetica", "Arial", "Segoe UI" })
            if (SKTypeface.FromFamilyName(nome, estilo) is { } achada && string.Equals(achada.FamilyName, nome, StringComparison.OrdinalIgnoreCase))
                return achada;
        return SKTypeface.FromFamilyName(null, estilo) ?? SKTypeface.Default;
    }

    public SKFont Normal(float tamanho) => Fonte(false, tamanho);
    public SKFont Negrito(float tamanho) => Fonte(true, tamanho);

    private SKFont Fonte(bool negrito, float tamanho)
    {
        if (!_fontes.TryGetValue((negrito, tamanho), out var fonte))
            _fontes[(negrito, tamanho)] = fonte = new SKFont(negrito ? _negrito : _normal, tamanho) { Subpixel = true };
        return fonte;
    }

    public void Dispose()
    {
        foreach (var fonte in _fontes.Values)
            fonte.Dispose();
    }
}
