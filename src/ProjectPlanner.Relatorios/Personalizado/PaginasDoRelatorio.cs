using SkiaSharp;

namespace ProjectPlanner.Relatorios.Personalizado;

/// <summary>Bloco 88: uma célula de tabela: o texto, ou uma barra de % (com o número), com uma cor própria se quiser.</summary>
internal readonly record struct Celula(string Texto, int? Percentual = null, SKColor? Cor = null, bool Negrito = false)
{
    public static implicit operator Celula(string texto) => new(texto);
}

/// <summary>Bloco 88: uma coluna de tabela: o título, o peso na largura e se o texto vai à direita (valores).</summary>
internal sealed record ColunaDaTabela(string Titulo, float Peso, bool Direita = false);

/// <summary>
/// Bloco 88: as páginas do relatório personalizado. Cada página é gravada uma vez (<see cref="SKPicture"/>) e depois vai para o PDF
/// ou para a prévia na tela, com o cabeçalho e o rodapé desenhados na saída (o número da página só se sabe no fim). As seções escrevem
/// de cima para baixo (<see cref="Y"/>); o que não cabe vai para a página seguinte (as tabelas repetem o cabeçalho).
/// </summary>
internal sealed class PaginasDoRelatorio : IDisposable
{
    public const float Margem = 40;

    private SKPictureRecorder? _gravador;
    private SKCanvas? _canvas;
    private bool _capa;

    public PaginasDoRelatorio(float largura, float altura, SKColor cor, Fontes fontes)
    {
        (Largura, Altura, Cor, F) = (largura, altura, cor, fontes);
        Clara = Misturar(cor, SKColors.White, 0.86f);
    }

    public float Largura { get; }
    public float Altura { get; }

    /// <summary>A cor do tema.</summary>
    public SKColor Cor { get; }

    /// <summary>A cor do tema bem clara (o fundo dos cabeçalhos das tabelas e dos cartões).</summary>
    public SKColor Clara { get; }

    public Fontes F { get; }

    public static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    public static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    public static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    public static readonly SKColor Faixa = new(0xF5, 0xF7, 0xF9);
    public static readonly SKColor Vermelho = new(0xC6, 0x28, 0x28);
    public static readonly SKColor Verde = new(0x2E, 0x7D, 0x32);

    /// <summary>Onde o conteúdo começa (abaixo do cabeçalho de cada página).</summary>
    public float Topo => Margem + 26;

    /// <summary>Onde o conteúdo termina (acima do rodapé).</summary>
    public float Base => Altura - Margem - 14;

    public float Esquerda => Margem;
    public float Direita => Largura - Margem;
    public float Util => Direita - Esquerda;

    /// <summary>Onde a próxima coisa é escrita.</summary>
    public float Y { get; set; }

    /// <summary>As páginas prontas: o conteúdo e se é a capa (sem cabeçalho nem rodapé).</summary>
    public List<(SKPicture Conteudo, bool Capa)> Prontas { get; } = [];

    public SKCanvas C => _canvas ?? throw new InvalidOperationException("Nenhuma página aberta.");

    /// <summary>A página aberta ainda não tem nada (para uma seção "em página nova" não deixar uma página em branco).</summary>
    public bool Vazia => _canvas is null || (!_capa && Y <= Topo + 0.5f);

    public void Nova(bool capa = false)
    {
        Fechar();
        _gravador = new SKPictureRecorder();
        _canvas = _gravador.BeginRecording(SKRect.Create(Largura, Altura));
        _capa = capa;
        Y = capa ? Margem : Topo;
    }

    /// <returns>Se precisou de uma página nova.</returns>
    public bool Caber(float altura)
    {
        if (_canvas is not null && Y + altura <= Base)
            return false;
        Nova();
        return true;
    }

    public void Fechar()
    {
        if (_gravador is null)
            return;
        Prontas.Add((_gravador.EndRecording(), _capa));
        _gravador.Dispose();
        (_gravador, _canvas) = (null, null);
    }

    public void Dispose()
    {
        Fechar();
        foreach (var (conteudo, _) in Prontas)
            conteudo.Dispose();
        Prontas.Clear();
    }

    // ---- As peças ----

    /// <summary>O título da seção: a marca na cor do tema, o nome e um fio.</summary>
    public void Secao(string titulo, float precisaDepois = 60)
    {
        Caber(34 + precisaDepois);
        using (var marca = new SKPaint { Color = Cor, IsAntialias = true })
            C.DrawRoundRect(new SKRect(Esquerda, Y, Esquerda + 4, Y + 18), 2, 2, marca);
        Escrever(titulo, Esquerda + 12, Y + 14, F.Negrito(14), Texto);
        Y += 24;
        using (var fio = new SKPaint { Color = Linha, StrokeWidth = 0.75f })
            C.DrawLine(Esquerda, Y, Direita, Y, fio);
        Y += 12;
    }

    /// <summary>Um parágrafo quebrado na largura (para numa página nova, se precisar).</summary>
    public void Paragrafo(string texto, float tamanho = 10, SKColor? cor = null, float recuo = 0)
    {
        var fonte = F.Normal(tamanho);
        var altura = tamanho + 4.5f;
        foreach (var linha in GeradorPdf.Quebrar(texto, fonte, Util - recuo))
        {
            Caber(altura);
            Escrever(linha, Esquerda + recuo, Y + tamanho, fonte, cor ?? Texto);
            Y += altura;
        }
        Y += 4;
    }

    /// <summary>Um aviso em cinza (a seção sem o que mostrar).</summary>
    public void Aviso(string texto) => Paragrafo(texto, 9.5f, Cinza);

    /// <summary>Cartões lado a lado: o valor grande na cor do tema, o rótulo e um detalhe.</summary>
    public void Cartoes(IReadOnlyList<(string Valor, string Rotulo, string? Detalhe)> cartoes, int porLinha = 3)
    {
        const float altura = 66, espaco = 10;
        var largura = (Util - espaco * (porLinha - 1)) / porLinha;
        using var fundo = new SKPaint { Color = Clara, IsAntialias = true };
        for (var i = 0; i < cartoes.Count; i += porLinha)
        {
            Caber(altura + espaco);
            for (var j = 0; j < porLinha && i + j < cartoes.Count; j++)
            {
                var (valor, rotulo, detalhe) = cartoes[i + j];
                var x = Esquerda + j * (largura + espaco);
                C.DrawRoundRect(new SKRect(x, Y, x + largura, Y + altura), 8, 8, fundo);
                Escrever(Encurtar(rotulo, F.Normal(8.5f), largura - 20), x + 10, Y + 16, F.Normal(8.5f), Cinza);
                Escrever(Encurtar(valor, F.Negrito(18), largura - 20), x + 10, Y + 40, F.Negrito(18), Cor);
                if (detalhe is { Length: > 0 })
                    Escrever(Encurtar(detalhe, F.Normal(8), largura - 20), x + 10, Y + 56, F.Normal(8), Cinza);
            }
            Y += altura + espaco;
        }
    }

    /// <summary>A barra do avanço: o realizado na cor do tema e o previsto como um traço.</summary>
    public void Barra(string rotulo, double realizado, double? previsto)
    {
        Caber(36);
        Escrever(rotulo, Esquerda, Y + 10, F.Negrito(9.5f), Texto);
        var legenda = $"realizado {realizado:0}%" + (previsto is { } p ? $" · previsto {p:0}%" : "");
        Escrever(legenda, Direita - F.Normal(9).MeasureText(legenda), Y + 10, F.Normal(9), Cinza);
        var caixa = new SKRect(Esquerda, Y + 16, Direita, Y + 26);
        BarraEm(caixa, realizado, previsto);
        Y += 36;
    }

    public void BarraEm(SKRect caixa, double realizado, double? previsto)
    {
        using var fundo = new SKPaint { Color = new SKColor(0xEC, 0xEF, 0xF1), IsAntialias = true };
        using var cheio = new SKPaint { Color = Cor, IsAntialias = true };
        var raio = caixa.Height / 2;
        C.DrawRoundRect(caixa, raio, raio, fundo);
        if (realizado > 0)
            C.DrawRoundRect(new SKRect(caixa.Left, caixa.Top, caixa.Left + caixa.Width * (float)Math.Clamp(realizado, 0, 100) / 100f, caixa.Bottom), raio, raio, cheio);
        if (previsto is { } p)
        {
            using var marca = new SKPaint { Color = new SKColor(0xE6, 0x51, 0x00), StrokeWidth = 2 };
            var x = caixa.Left + caixa.Width * (float)Math.Clamp(p, 0, 100) / 100f;
            C.DrawLine(x, caixa.Top - 3, x, caixa.Bottom + 3, marca);
        }
    }

    /// <summary>
    /// Uma tabela: o cabeçalho na cor clara do tema, as linhas em zebra, até 3 linhas de texto por célula; numa página nova, o
    /// cabeçalho se repete.
    /// </summary>
    public void Tabela(IReadOnlyList<ColunaDaTabela> colunas, IReadOnlyList<Celula[]> linhas)
    {
        const float tamanho = 8.5f, entreLinhas = 11f, folga = 6f;
        var soma = colunas.Sum(c => c.Peso);
        var larguras = colunas.Select(c => Util * c.Peso / soma).ToArray();
        void Cabecalho()
        {
            using var fundo = new SKPaint { Color = Clara };
            C.DrawRect(new SKRect(Esquerda, Y, Direita, Y + 18), fundo);
            var x = Esquerda;
            for (var i = 0; i < colunas.Count; i++)
            {
                var titulo = Encurtar(colunas[i].Titulo, F.Negrito(8), larguras[i] - 2 * folga);
                var px = colunas[i].Direita ? x + larguras[i] - folga - F.Negrito(8).MeasureText(titulo) : x + folga;
                Escrever(titulo, px, Y + 12.5f, F.Negrito(8), Texto);
                x += larguras[i];
            }
            Y += 18;
        }
        Caber(18 + 16);
        Cabecalho();
        var zebra = false;
        foreach (var linha in linhas)
        {
            var conteudo = linha.Select((celula, i) => celula.Percentual is not null ? [""]
                : Limitar(GeradorPdf.Quebrar(celula.Texto, celula.Negrito ? F.Negrito(tamanho) : F.Normal(tamanho), larguras[i] - 2 * folga), 3,
                    celula.Negrito ? F.Negrito(tamanho) : F.Normal(tamanho), larguras[i] - 2 * folga)).ToList();
            var altura = Math.Max(16, conteudo.Max(c => c.Count) * entreLinhas + 5);
            if (Caber(altura))
                Cabecalho();
            if (zebra)
                using (var faixa = new SKPaint { Color = Faixa })
                    C.DrawRect(new SKRect(Esquerda, Y, Direita, Y + altura), faixa);
            zebra = !zebra;
            var x = Esquerda;
            for (var i = 0; i < colunas.Count && i < linha.Length; i++)
            {
                var celula = linha[i];
                if (celula.Percentual is { } pct)
                {
                    var barra = new SKRect(x + folga, Y + altura / 2 - 3, x + larguras[i] - folga - 26, Y + altura / 2 + 3);
                    BarraEm(barra, pct, null);
                    var numero = $"{pct}%";
                    Escrever(numero, x + larguras[i] - folga - F.Normal(tamanho).MeasureText(numero), Y + altura / 2 + 3, F.Normal(tamanho), Texto);
                }
                else
                {
                    var fonte = celula.Negrito ? F.Negrito(tamanho) : F.Normal(tamanho);
                    var ly = Y + 11;
                    foreach (var texto in conteudo[i])
                    {
                        var px = colunas[i].Direita ? x + larguras[i] - folga - fonte.MeasureText(texto) : x + folga;
                        Escrever(texto, px, ly, fonte, celula.Cor ?? Texto);
                        ly += entreLinhas;
                    }
                }
                x += larguras[i];
            }
            Y += altura;
        }
        using (var fio = new SKPaint { Color = Linha, StrokeWidth = 0.75f })
            C.DrawLine(Esquerda, Y, Direita, Y, fio);
        Y += 12;
    }

    public void Escrever(string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        C.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
    }

    public void Centralizado(string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        C.DrawText(texto, x, y, SKTextAlign.Center, fonte, tinta);
    }

    /// <summary>O texto cortado com "…" para caber na largura.</summary>
    public static string Encurtar(string texto, SKFont fonte, float largura)
    {
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        var corte = texto;
        while (corte.Length > 0 && fonte.MeasureText(corte + "…") > largura)
            corte = corte[..^1];
        return corte.TrimEnd() + "…";
    }

    private static List<string> Limitar(List<string> linhas, int maximo, SKFont fonte, float largura)
    {
        if (linhas.Count <= maximo)
            return linhas;
        var cortadas = linhas.Take(maximo).ToList();
        cortadas[^1] = Encurtar(cortadas[^1] + " …", fonte, largura);
        return cortadas;
    }

    /// <summary>A cor <paramref name="a"/> misturada com <paramref name="b"/> (0 = só a; 1 = só b).</summary>
    public static SKColor Misturar(SKColor a, SKColor b, float quanto) =>
        new((byte)(a.Red + (b.Red - a.Red) * quanto), (byte)(a.Green + (b.Green - a.Green) * quanto), (byte)(a.Blue + (b.Blue - a.Blue) * quanto));
}
