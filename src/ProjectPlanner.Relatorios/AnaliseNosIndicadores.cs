using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Planejamento;
using SkiaSharp;
using static ProjectPlanner.Relatorios.PecasDoWord;

namespace ProjectPlanner.Relatorios;

/// <summary>Bloco 43: a análise escrita (pela IA, revisada pelo usuário): o resumo, os pontos de atenção e as recomendações.</summary>
public sealed record AnaliseEscrita(string Resumo, IReadOnlyList<string> Pontos, IReadOnlyList<string> Recomendacoes)
{
    public bool Vazia => string.IsNullOrWhiteSpace(Resumo) && Pontos.Count == 0 && Recomendacoes.Count == 0;
}

/// <summary>Bloco 43: os textos dos indicadores ampliados, iguais na tela, no PDF e no Word.</summary>
public static class TextosDaAnalise
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private static string R(decimal? v) => v is { } x ? Moeda.Formatar(x) : "—";

    private static string P(double? v) => v is { } x ? x.ToString("0.0", Brasil) + "%" : "—";

    public const string SemCustoOrcado =
        "Sem custo orçado: a margem e o desvio de custo precisam do orçamento (Projeto > Orçamento…) ou de recursos atribuídos às tarefas.";

    /// <summary>Os oito indicadores do topo da aba Custos e margem.</summary>
    public static IReadOnlyList<CartaoDeIndicador> Cartoes(AnaliseFinanceira a)
    {
        var t = a.Total;
        return
        [
            new("Contratado", Moeda.ComSimbolo(t.Contratado), a.Contrato.Aditivos switch { 0 => "sem aditivos", 1 => "com 1 aditivo", var n => $"com {n} aditivos" }),
            new("Custo orçado", t.CustoOrcado is { } co ? Moeda.ComSimbolo(co) : "—", "orçamento ou recursos"),
            new("Margem prevista", t.MargemPrevista is { } mp ? Moeda.ComSimbolo(mp) : "—",
                t.CustoParcial ? $"{P(t.MargemPrevistaPercentual)} de {Moeda.ComSimbolo(t.ContratadoComCusto)} com custo orçado" : P(t.MargemPrevistaPercentual) + " do contratado",
                t.MargemPrevista < 0),
            new("Custo real", Moeda.ComSimbolo(a.CustoRealTotal), a.CustosGerais > 0 ? $"com {Moeda.ComSimbolo(a.CustosGerais)} de custos gerais" : "nas tarefas"),
            new("Desvio de custo", t.DesvioDeCusto is { } d ? TextosDoBoletim.ComSinal(d) : "—",
                t.DesvioDeCusto is null ? "precisa de custo orçado e real" : t.DesvioDeCusto < 0 ? "gastando mais que o orçado pelo feito" : "dentro do orçado pelo feito",
                t.DesvioDeCusto < 0),
            new("Custo final projetado", t.CustoFinalProjetado is { } cf ? Moeda.ComSimbolo(cf) : "—",
                t.FaltaGastar is { } fg ? $"faltam {Moeda.ComSimbolo(fg)}" : "—", t.CustoFinalProjetado > t.CustoOrcado),
            new("Margem projetada", t.MargemProjetada is { } m ? Moeda.ComSimbolo(m) : "—",
                t.MargemProjetada is { } mm && t.ContratadoComCusto != 0 ? P((double)(mm / t.ContratadoComCusto) * 100) + (t.CustoParcial ? " do contratado com custo" : " do contratado") : "—",
                t.MargemProjetada < 0),
            new("Saldo de caixa", Moeda.ComSimbolo(a.Caixa.LastOrDefault(c => c.Realizado)?.SaldoAcumulado ?? 0), "medido líquido − custo real",
                a.Caixa.LastOrDefault(c => c.Realizado)?.SaldoAcumulado < 0),
        ];
    }

    public static readonly string[] ColunasDeCustos =
        ["Etapa", "Contratado", "Custo orçado", "Margem prevista", "Orçado do feito", "Custo real", "Desvio", "Custo final proj.", "Margem proj."];

    /// <summary>As etapas do orçado ao realizado, os custos gerais e o total.</summary>
    public static IReadOnlyList<string[]> Custos(AnaliseFinanceira a)
    {
        string[] Linha(EtapaFinanceira e, string nome) =>
            [nome, R(e.Contratado), R(e.CustoOrcado), e.MargemPrevista is { } m ? $"{R(m)} ({P(e.MargemPrevistaPercentual)})" : "—",
             e.CustoOrcado is null ? "—" : R(e.CustoOrcadoDoFeito), R(e.CustoReal), R(e.DesvioDeCusto), R(e.CustoFinalProjetado), R(e.MargemProjetada)];
        // O total é o das etapas (o orçado só existe nelas); os custos gerais ficam numa linha à parte, abaixo.
        var linhas = a.Etapas.Select(e => Linha(e, $"{e.Edt} {e.Nome}")).ToList();
        linhas.Add(Linha(a.Total, "Total"));
        if (a.CustosGerais > 0)
            linhas.Add(["Custos gerais (sem tarefa)", "", "", "", "", R(a.CustosGerais), "", "", ""]);
        return linhas;
    }

    public static readonly string[] ColunasDasMedicoes = ["Etapa", "Contratado", "Físico", "Medido", "Medido − físico", "Executado a medir"];

    public static IReadOnlyList<string[]> Medicoes(AnaliseFinanceira a)
    {
        string[] Linha(EtapaFinanceira e, string nome) =>
            [nome, R(e.Contratado), P(e.Fisico), $"{R(e.Medido)} ({P(e.PercentualMedido)})",
             e.Contratado == 0 ? "—" : (e.DiferencaDaMedicao >= 0 ? "+" : "−") + Math.Abs(e.DiferencaDaMedicao).ToString("0.0", Brasil) + " pontos", R(e.AMedir)];
        var total = Linha(a.Total, "Total");
        total[5] = R(a.Etapas.Sum(e => e.AMedir)); // a soma das etapas (o total não compensa uma etapa medida à frente com outra a medir)
        return [.. a.Etapas.Select(e => Linha(e, $"{e.Edt} {e.Nome}")), total];
    }

    /// <summary>O contrato na data, em pares (rótulo, valor).</summary>
    public static IReadOnlyList<(string Rotulo, string Valor)> Contrato(AnaliseFinanceira a)
    {
        var c = a.Contrato;
        return
        [
            ("Valor original", Moeda.ComSimbolo(c.ValorOriginal)),
            (c.Aditivos == 1 ? "Aditivo (1)" : $"Aditivos ({c.Aditivos})", $"{TextosDoBoletim.ComSinal(c.ValorDosAditivos)} ({P(c.AditivosPercentual)})"),
            ("Valor atual", Moeda.ComSimbolo(c.ValorAtual)),
            (c.MedicoesAprovadas == 1 ? "Medido (1 medição)" : $"Medido ({c.MedicoesAprovadas} medições)", Moeda.ComSimbolo(c.MedidoBruto)),
            ("Líquido faturado", Moeda.ComSimbolo(c.Liquido)),
            ("Retido acumulado", Moeda.ComSimbolo(c.Retido)),
            ("Saldo do contrato", Moeda.ComSimbolo(c.Saldo)),
        ];
    }

    public static readonly string[] ColunasDoCaixa = ["Período", "Previsto", "Medido (líquido)", "Custo real", "Saldo", "Saldo acumulado"];

    /// <summary>O fluxo de caixa por período (os seguintes à data só com o previsto).</summary>
    public static IReadOnlyList<string[]> Caixa(AnaliseFinanceira a) =>
    [
        .. a.Caixa.Select(p => p.Realizado
            ? new[] { p.Periodo.Rotulo, R(p.ReceitaPrevista), R(p.Medido), R(p.Custo), R(p.Saldo), R(p.SaldoAcumulado) }
            : [p.Periodo.Rotulo, R(p.ReceitaPrevista), "", "", "", ""]),
    ];

    public static string Nivel(NivelDoAlerta n) => n switch
    {
        NivelDoAlerta.Critico => "Crítico",
        NivelDoAlerta.Atencao => "Atenção",
        NivelDoAlerta.Ok => "Ok",
        _ => "Sem dados",
    };

    /// <summary>A cor do semáforo (a mesma na tela, no PDF e no Word).</summary>
    public static string Cor(NivelDoAlerta n) => n switch
    {
        NivelDoAlerta.Critico => "#C62828",
        NivelDoAlerta.Atencao => "#EF8F00",
        NivelDoAlerta.Ok => "#2E7D32",
        _ => "#90A4AE",
    };

    /// <summary>
    /// Os indicadores em linhas de texto para a IA analisar (Bloco 43) e para o chat consultar (Bloco 41): prazo, custo e
    /// margem, contrato, medições por etapa, caixa e o semáforo.
    /// </summary>
    public static IReadOnlyList<string> ParaAIA(PainelDeIndicadores p, AnaliseFinanceira a)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(a);
        var v = p.ValorAgregado;
        var linhas = new List<string>
        {
            $"Prazo: físico previsto {P(v.FisicoPrevisto)}, realizado {P(v.FisicoRealizado)}; IDP {TextosDosIndicadores.Indice(v.Idp)}; " +
            $"{p.TotalDeAtrasadas} tarefa(s) atrasada(s); término atual {TextosDosIndicadores.Data(v.TerminoAtual)}, previsto " +
            $"{TextosDosIndicadores.Data(v.TerminoPrevisto)}, pela tendência {TextosDosIndicadores.Data(v.TerminoPelaTendencia)}.",
        };
        foreach (var atraso in TextosDosIndicadores.Atrasos(p))
            linhas.Add("Atrasada: " + atraso);
        foreach (var cartao in Cartoes(a))
            linhas.Add($"{cartao.Rotulo}: {cartao.Valor} ({cartao.Detalhe})");
        linhas.Add("Contrato: " + string.Join("; ", Contrato(a).Select(c => $"{c.Rotulo} {c.Valor}")) + ".");
        foreach (var e in a.Etapas)
            linhas.Add($"Etapa {e.Edt} {e.Nome}: contratado {R(e.Contratado)}; custo orçado {R(e.CustoOrcado)}; físico {P(e.Fisico)}; " +
                       $"medido {P(e.PercentualMedido)}; custo real {R(e.CustoReal)}; desvio de custo {R(e.DesvioDeCusto)}; margem projetada {R(e.MargemProjetada)}.");
        foreach (var c in a.Caixa.Where(c => c.Realizado))
            linhas.Add($"Caixa {c.Periodo.Rotulo}: previsto {R(c.ReceitaPrevista)}; medido líquido {R(c.Medido)}; custo {R(c.Custo)}; saldo acumulado {R(c.SaldoAcumulado)}.");
        foreach (var alerta in a.Alertas)
            linhas.Add($"Alerta {alerta.Area} ({Nivel(alerta.Nivel)}): {alerta.Titulo}. {alerta.Detalhe}");
        return linhas;
    }
}

/// <summary>Bloco 43: o gráfico do fluxo de caixa: barras do medido (verde) e do custo (vermelho) por período e a linha do saldo acumulado.</summary>
public static class ImagemDoCaixa
{
    private static readonly SKColor Medido = new(0x2E, 0x7D, 0x32);
    private static readonly SKColor Custo = new(0xC6, 0x28, 0x28);
    private static readonly SKColor Previsto = new(0x90, 0xA4, 0xAE);
    private static readonly SKColor Saldo = new(0x15, 0x65, 0xC0);
    private static readonly SKColor Grade = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);

    /// <summary>O gráfico num PNG (largura e altura em pixels, o dobro para a nitidez na tela e no Word).</summary>
    public static byte[] Png(AnaliseFinanceira a, int largura, int altura)
    {
        using var bitmap = new SKBitmap(largura * 2, altura * 2);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(2);
            using var fontes = new Fontes();
            Desenhar(canvas, a, new SKRect(0, 0, largura, altura), fontes);
        }
        using var imagem = SKImage.FromBitmap(bitmap);
        using var dados = imagem.Encode(SKEncodedImageFormat.Png, 90);
        return dados.ToArray();
    }

    internal static void Desenhar(SKCanvas c, AnaliseFinanceira a, SKRect area, Fontes f)
    {
        var periodos = a.Caixa;
        var grafico = new SKRect(area.Left + 70, area.Top + 10, area.Right - 10, area.Bottom - 42);
        if (periodos.Count == 0)
            return;
        var valores = periodos.SelectMany(p => new[] { p.ReceitaPrevista, p.Medido, p.Custo, p.SaldoAcumulado }).ToList();
        // A escala em números redondos (1, 2 ou 5 × 10ⁿ por divisão), do mínimo (zero ou o saldo negativo) ao máximo.
        var (menor, maior) = (Math.Min(0, valores.Min()), Math.Max(1, valores.Max()));
        var bruto = (double)(maior - menor) / 4;
        var potencia = Math.Pow(10, Math.Floor(Math.Log10(bruto)));
        var passoDaEscala = (decimal)(new[] { 1, 2, 5, 10 }.Select(m => m * potencia).First(v => v >= bruto));
        var (minimo, maximo) = (Math.Floor(menor / passoDaEscala) * passoDaEscala, Math.Ceiling(maior / passoDaEscala) * passoDaEscala);
        float Y(decimal v) => grafico.Bottom - (float)((v - minimo) / (maximo - minimo)) * grafico.Height;
        using var grade = new SKPaint { Color = Grade, StrokeWidth = 0.5f };
        using var texto = new SKPaint { Color = Cinza, IsAntialias = true };
        for (var v = minimo; v <= maximo; v += passoDaEscala)
        {
            c.DrawLine(grafico.Left, Y(v), grafico.Right, Y(v), grade);
            c.DrawText(v.ToString("#,##0", CultureInfo.GetCultureInfo("pt-BR")), grafico.Left - 6, Y(v) + 3, SKTextAlign.Right, f.Normal(7.5f), texto);
        }
        var largura = grafico.Width / periodos.Count;
        var barra = Math.Max(2, largura * 0.22f);
        using var previsto = new SKPaint { Color = Previsto, IsAntialias = true };
        using var medido = new SKPaint { Color = Medido, IsAntialias = true };
        using var custo = new SKPaint { Color = Custo, IsAntialias = true };
        using var saldo = new SKPaint { Color = Saldo, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var pontoDoSaldo = new SKPaint { Color = Saldo, IsAntialias = true };
        var passo = Math.Max(1, (int)Math.Ceiling(periodos.Count / (grafico.Width / 46)));
        SKPoint? anterior = null;
        for (var i = 0; i < periodos.Count; i++)
        {
            var p = periodos[i];
            var x = grafico.Left + i * largura + largura / 2;
            c.DrawRect(SKRect.Create(x - barra * 1.5f, Y(p.ReceitaPrevista), barra, Y(0) - Y(p.ReceitaPrevista)), previsto);
            if (p.Realizado)
            {
                c.DrawRect(SKRect.Create(x - barra * 0.5f, Y(p.Medido), barra, Y(0) - Y(p.Medido)), medido);
                c.DrawRect(SKRect.Create(x + barra * 0.5f, Y(p.Custo), barra, Y(0) - Y(p.Custo)), custo);
                var ponto = new SKPoint(x, Y(p.SaldoAcumulado));
                if (anterior is { } a0)
                    c.DrawLine(a0, ponto, saldo);
                c.DrawCircle(ponto, 2.5f, pontoDoSaldo);
                anterior = ponto;
            }
            if (i % passo == 0)
                c.DrawText(p.Periodo.Rotulo, x, grafico.Bottom + 12, SKTextAlign.Center, f.Normal(7.5f), texto);
        }
        var y = area.Bottom - 12;
        var xl = grafico.Left;
        foreach (var (nome, cor) in new[] { ("Previsto", Previsto), ("Medido (líquido)", Medido), ("Custo real", Custo), ("Saldo acumulado", Saldo) })
        {
            using var tinta = new SKPaint { Color = cor, IsAntialias = true };
            c.DrawRect(SKRect.Create(xl, y - 8, 10, 8), tinta);
            c.DrawText(nome, xl + 14, y, SKTextAlign.Left, f.Normal(8), texto);
            xl += 22 + f.Normal(8).MeasureText(nome);
        }
    }
}

/// <summary>Bloco 43: as páginas dos indicadores ampliados no PDF (custos e margem, medições e caixa, alertas e análise).</summary>
internal static class PaginasDaAnalisePdf
{
    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);

    /// <summary>Desenha as páginas; cada uma começa com <paramref name="novaPagina"/> (que já põe o cabeçalho e devolve a área).</summary>
    public static void Desenhar(Func<SKCanvas> novaPagina, Func<SKRect> area, AnaliseFinanceira a, AnaliseEscrita? escrita, Fontes f)
    {
        // Custos e margem
        var c = novaPagina();
        var r = area();
        var y = Titulo(c, "Custos e margem: do orçado ao realizado", r, f);
        if (!a.TemCustoOrcado)
        {
            Escrever(c, TextosDaAnalise.SemCustoOrcado, r.Left, y, f.Normal(8.5f), new SKColor(0xC6, 0x28, 0x28));
            y += 14;
        }
        y = Cartoes(c, TextosDaAnalise.Cartoes(a), r, y, f);
        y = Tabela(ref c, novaPagina, area, TextosDaAnalise.ColunasDeCustos, TextosDaAnalise.Custos(a), [0.2f, 0.1f, 0.1f, 0.12f, 0.1f, 0.1f, 0.09f, 0.1f, 0.09f], r, y + 8, f);

        // Medições e caixa
        c = novaPagina();
        r = area();
        y = Titulo(c, "Medições e fluxo de caixa", r, f);
        var contrato = TextosDaAnalise.Contrato(a);
        var x = r.Left;
        foreach (var (rotulo, valor) in contrato)
        {
            Escrever(c, rotulo, x, y, f.Normal(7.5f), Cinza);
            Escrever(c, valor, x, y + 12, f.Negrito(9), Texto);
            x += r.Width / contrato.Count;
        }
        y += 28;
        y = Tabela(ref c, novaPagina, area, TextosDaAnalise.ColunasDasMedicoes, TextosDaAnalise.Medicoes(a), [0.34f, 0.13f, 0.1f, 0.17f, 0.12f, 0.14f], r, y, f);
        if (y + 200 > r.Bottom)
        {
            c = novaPagina();
            y = area().Top;
        }
        Escrever(c, "Fluxo de caixa", r.Left, y + 12, f.Negrito(10), Texto);
        ImagemDoCaixa.Desenhar(c, a, new SKRect(r.Left, y + 18, r.Right, y + 190), f);
        y += 198;
        Tabela(ref c, novaPagina, area, TextosDaAnalise.ColunasDoCaixa, TextosDaAnalise.Caixa(a), [0.2f, 0.16f, 0.16f, 0.16f, 0.16f, 0.16f], r, y, f);

        // Alertas e análise
        c = novaPagina();
        r = area();
        y = Titulo(c, "Alertas e análise", r, f);
        foreach (var alerta in a.Alertas)
        {
            using var bola = new SKPaint { Color = SKColor.Parse(TextosDaAnalise.Cor(alerta.Nivel)), IsAntialias = true };
            c.DrawCircle(r.Left + 5, y - 3, 4.5f, bola);
            Escrever(c, $"{alerta.Area} · {TextosDaAnalise.Nivel(alerta.Nivel)} · {alerta.Titulo}", r.Left + 16, y, f.Negrito(9), Texto);
            y = Paragrafo(c, alerta.Detalhe, r.Left + 16, y + 12, r.Width - 16, f.Normal(8.5f), Cinza) + 6;
        }
        if (escrita is null || escrita.Vazia)
            return;
        y += 8;
        Escrever(c, "Análise", r.Left, y, f.Negrito(11), Texto);
        y = Paragrafo(c, escrita.Resumo, r.Left, y + 16, r.Width, f.Normal(9), Texto) + 6;
        foreach (var (titulo, itens) in new[] { ("Pontos de atenção", escrita.Pontos), ("Recomendações", escrita.Recomendacoes) })
        {
            if (itens.Count == 0)
                continue;
            if (y + 40 > r.Bottom)
            {
                c = novaPagina();
                y = area().Top + 10;
            }
            Escrever(c, titulo, r.Left, y + 6, f.Negrito(9.5f), Texto);
            y += 20;
            foreach (var item in itens)
                y = Paragrafo(c, "• " + item, r.Left, y, r.Width, f.Normal(8.5f), Texto) + 3;
        }
    }

    private static float Titulo(SKCanvas c, string titulo, SKRect r, Fontes f)
    {
        Escrever(c, titulo, r.Left, r.Top + 16, f.Negrito(14), Texto);
        return r.Top + 36;
    }

    private static float Cartoes(SKCanvas c, IReadOnlyList<CartaoDeIndicador> cartoes, SKRect r, float y, Fontes f)
    {
        var largura = r.Width / 4;
        using var fundo = new SKPaint { Color = Faixa };
        for (var i = 0; i < cartoes.Count; i++)
        {
            var (x, topo) = (r.Left + i % 4 * largura, y + i / 4 * 48);
            c.DrawRoundRect(new SKRoundRect(SKRect.Create(x, topo, largura - 6, 42), 4), fundo);
            var cartao = cartoes[i];
            Escrever(c, cartao.Rotulo, x + 6, topo + 12, f.Normal(7.5f), Cinza);
            Escrever(c, cartao.Valor, x + 6, topo + 27, f.Negrito(11), cartao.Alerta ? new SKColor(0xC6, 0x28, 0x28) : Texto);
            Escrever(c, Truncar(cartao.Detalhe, largura - 14, f.Normal(7)), x + 6, topo + 38, f.Normal(7), Cinza);
        }
        return y + (cartoes.Count + 3) / 4 * 48;
    }

    // Uma tabela que continua na página seguinte (com o cabeçalho de novo) quando não cabe.
    private static float Tabela(ref SKCanvas c, Func<SKCanvas> novaPagina, Func<SKRect> area, string[] colunas, IReadOnlyList<string[]> linhas, float[] fracoes,
        SKRect r, float y, Fontes f)
    {
        var larguras = fracoes.Select(p => p * r.Width).ToArray();
        float Cabecalho(SKCanvas canvas, float topo)
        {
            using var fundo = new SKPaint { Color = Faixa };
            canvas.DrawRect(SKRect.Create(r.Left, topo, r.Width, 16), fundo);
            Celulas(canvas, colunas, larguras, r.Left, topo + 11, f.Negrito(7.5f));
            return topo + 16;
        }
        y = Cabecalho(c, y);
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.5f };
        for (var i = 0; i < linhas.Count; i++)
        {
            if (y + 14 > r.Bottom)
            {
                c = novaPagina();
                y = Cabecalho(c, area().Top + 4);
            }
            Celulas(c, linhas[i], larguras, r.Left, y + 10, linhas[i][0] == "Total" ? f.Negrito(7.5f) : f.Normal(7.5f));
            c.DrawLine(r.Left, y + 14, r.Right, y + 14, traco);
            y += 14;
        }
        return y + 10;
    }

    private static void Celulas(SKCanvas c, string[] celulas, float[] larguras, float x, float y, SKFont fonte)
    {
        for (var i = 0; i < celulas.Length; i++)
        {
            var texto = Truncar(celulas[i], larguras[i] - 8, fonte);
            if (i == 0)
                Escrever(c, texto, x + 4, y, fonte, Texto);
            else
            {
                using var tinta = new SKPaint { Color = Texto, IsAntialias = true };
                c.DrawText(texto, x + larguras[i] - 4, y, SKTextAlign.Right, fonte, tinta);
            }
            x += larguras[i];
        }
    }

    private static float Paragrafo(SKCanvas c, string texto, float x, float y, float largura, SKFont fonte, SKColor cor)
    {
        foreach (var paragrafo in texto.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = "";
            foreach (var palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tentativa = linha.Length == 0 ? palavra : linha + " " + palavra;
                if (fonte.MeasureText(tentativa) > largura && linha.Length > 0)
                {
                    Escrever(c, linha, x, y, fonte, cor);
                    (y, linha) = (y + fonte.Size * 1.35f, palavra);
                }
                else
                    linha = tentativa;
            }
            Escrever(c, linha, x, y, fonte, cor);
            y += fonte.Size * 1.35f;
        }
        return y;
    }

    private static string Truncar(string texto, float largura, SKFont fonte)
    {
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        while (texto.Length > 2 && fonte.MeasureText(texto + "…") > largura)
            texto = texto[..^1];
        return texto.TrimEnd() + "…";
    }

    private static void Escrever(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
    }
}

/// <summary>Bloco 43: os indicadores ampliados no Word.</summary>
internal static class AnaliseNoWord
{
    public static void Adicionar(Body corpo, MainDocumentPart principal, AnaliseFinanceira a, AnaliseEscrita? escrita, int larguraUtil, Func<uint> proximoDesenho)
    {
        void TabelaDe(string[] colunas, IReadOnlyList<string[]> linhas, double[] fracoes)
        {
            var larguras = fracoes.Select(v => (int)(v * larguraUtil)).ToArray();
            var tabela = Tabela(bordas: true, larguras: larguras);
            var cabecalho = new TableRow(new TableRowProperties(new TableHeader())); // repete em cada página
            foreach (var (t, i) in colunas.Select((t, i) => (t, i)))
                cabecalho.Append(Celula(larguras[i], Paragrafo(t, 7.5, negrito: true, depois: 0)));
            tabela.Append(cabecalho);
            foreach (var linha in linhas)
                tabela.Append(new TableRow(linha.Select((t, i) => Celula(larguras[i], Paragrafo(t, 7.5, negrito: linha[0] == "Total", depois: 0)))));
            corpo.Append(tabela);
        }

        corpo.Append(QuebraDePagina());
        corpo.Append(Paragrafo("Custos e margem: do orçado ao realizado", 14, negrito: true, depois: 120));
        if (!a.TemCustoOrcado)
            corpo.Append(Paragrafo(TextosDaAnalise.SemCustoOrcado, 9, cor: "C62828"));
        var cartoes = TextosDaAnalise.Cartoes(a);
        var largura = larguraUtil / 4;
        var tabelaDeCartoes = Tabela(bordas: true, larguras: [.. Enumerable.Repeat(largura, 4)]);
        foreach (var linha in cartoes.Chunk(4))
            tabelaDeCartoes.Append(new TableRow(linha.Select(cartao => Celula(largura,
                Paragrafo(cartao.Rotulo, 7.5, cor: Cinza, depois: 0),
                Paragrafo(cartao.Valor, 11, negrito: true, cor: cartao.Alerta ? "C62828" : null, depois: 0),
                Paragrafo(cartao.Detalhe, 7, cor: Cinza, depois: 40)))));
        corpo.Append(tabelaDeCartoes);
        corpo.Append(Paragrafo("", 6));
        TabelaDe(TextosDaAnalise.ColunasDeCustos, TextosDaAnalise.Custos(a), [0.2, 0.1, 0.1, 0.12, 0.1, 0.1, 0.09, 0.1, 0.09]);

        corpo.Append(QuebraDePagina());
        corpo.Append(Paragrafo("Medições e fluxo de caixa", 14, negrito: true, depois: 120));
        corpo.Append(Paragrafo(string.Join(" · ", TextosDaAnalise.Contrato(a).Select(c => $"{c.Rotulo}: {c.Valor}")), 9, depois: 120));
        TabelaDe(TextosDaAnalise.ColunasDasMedicoes, TextosDaAnalise.Medicoes(a), [0.34, 0.13, 0.1, 0.17, 0.12, 0.14]);
        corpo.Append(Paragrafo("Fluxo de caixa", 10, negrito: true, antes: 160));
        var (l, h) = (larguraUtil / 20, 210);
        corpo.Append(new Paragraph(new Run(Desenho(principal, ImagemDoCaixa.Png(a, l, h), l * 12_700L, h * 12_700L, "Fluxo de caixa", proximoDesenho()))));
        TabelaDe(TextosDaAnalise.ColunasDoCaixa, TextosDaAnalise.Caixa(a), [0.2, 0.16, 0.16, 0.16, 0.16, 0.16]);

        corpo.Append(QuebraDePagina());
        corpo.Append(Paragrafo("Alertas e análise", 14, negrito: true, depois: 120));
        foreach (var alerta in a.Alertas)
        {
            corpo.Append(Paragrafo($"● {alerta.Area} · {TextosDaAnalise.Nivel(alerta.Nivel)} · {alerta.Titulo}", 9, negrito: true,
                cor: TextosDaAnalise.Cor(alerta.Nivel).TrimStart('#'), depois: 0));
            corpo.Append(Paragrafo(alerta.Detalhe, 8.5, cor: Cinza));
        }
        if (escrita is null || escrita.Vazia)
            return;
        corpo.Append(Paragrafo("Análise", 11, negrito: true, antes: 200));
        corpo.Append(Paragrafo(escrita.Resumo, 9));
        foreach (var (titulo, itens) in new[] { ("Pontos de atenção", escrita.Pontos), ("Recomendações", escrita.Recomendacoes) })
        {
            if (itens.Count == 0)
                continue;
            corpo.Append(Paragrafo(titulo, 9.5, negrito: true, antes: 120));
            foreach (var item in itens)
                corpo.Append(Paragrafo("• " + item, 9));
        }
    }
}
