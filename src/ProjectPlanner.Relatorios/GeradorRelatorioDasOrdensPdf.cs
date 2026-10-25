using System.Globalization;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 64: o relatório das ordens de serviço em PDF (A4 paisagem): o cabeçalho com a obra e o filtro, os números (OS,
/// contratado, medido, pago, a pagar, por medir), as situações e os prazos, o que pede atenção (vencidas e para conferir), o
/// resumo por executante e a lista de todas as OS do filtro, em quantas páginas precisar. Bloco 65: o logotipo, os gráficos (por
/// executante; medido e pago por mês) e, se pedido, o detalhe de cada OS (itens com o medido, medições e pagamentos).
/// </summary>
public sealed class GeradorRelatorioDasOrdensPdf
{
    private const float Largura = 841.89f;
    private const float Altura = 595.28f;
    private const float Margem = 36f;
    private const float Limite = Largura - Margem;
    private const float Fundo = Altura - Margem - 24;
    private const float AlturaDaLinha = 16f;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor Destaque = new(0x15, 0x65, 0xC0);
    private static readonly SKColor Aviso = new(0xE6, 0x51, 0x00);

    // As colunas da lista: o título, a largura e se o texto vai à direita.
    private static readonly (string Titulo, float Largura, bool Direita)[] Colunas =
    [
        ("OS", 42, false), ("Serviço", 150, false), ("Executante", 104, false), ("Situação", 68, false), ("Emissão", 48, false),
        ("Prazo", 48, false), ("Conclusão", 52, false), ("Atraso", 36, true), ("Valor", 56, true), ("Medido", 56, true), ("Pago", 56, true),
        ("A pagar", 54, true), // somam a largura útil (770 pontos)
    ];

    private sealed class Pagina(SKDocument documento, Fontes fontes, string obra, string subtitulo, byte[]? logotipo)
    {
        public SKCanvas Tela { get; private set; } = null!;
        public float Y { get; set; }
        public int Numero { get; private set; }

        public void Nova()
        {
            if (Numero > 0)
                documento.EndPage();
            Tela = documento.BeginPage(Largura, Altura);
            Numero++;
            var direita = Limite;
            if (logotipo is { Length: > 0 })
            {
                GeradorBoletimPdf.DesenharLogotipo(Tela, logotipo, new SKRect(Limite - 130, Margem - 4, Limite, Margem + 40));
                direita = Limite - 140;
            }
            Escrever(Tela, "RELATÓRIO DAS ORDENS DE SERVIÇO", Margem, Margem + 4, fontes.Negrito(10), Cinza);
            Escrever(Tela, Cortar(obra, fontes.Negrito(16), direita - Margem), Margem, Margem + 26, fontes.Negrito(16), Texto);
            Escrever(Tela, Cortar(subtitulo, fontes.Normal(9), Limite - Margem - 60), Margem, Margem + 42, fontes.Normal(9), Cinza);
            Direita(Tela, $"Página {Numero}", Limite, Altura - Margem + 14, fontes.Normal(8), Cinza);
            Y = Margem + 64;
        }

        /// <summary>Uma página nova se o que vem não cabe nesta.</summary>
        public bool Garantir(float altura)
        {
            if (Y + altura <= Fundo)
                return false;
            Nova();
            return true;
        }

        public void Fim() => documento.EndPage();
    }

    /// <param name="tarefas">A EDT e o nome de cada tarefa, para o detalhe das OS.</param>
    /// <param name="detalhar">Bloco 65: inclui o detalhe de cada OS no fim.</param>
    public void Gerar(RelatorioDasOrdens relatorio, CabecalhoDoRelatorio cabecalho, IReadOnlyDictionary<Guid, string> tarefas, string geradoEm, Stream destino,
        bool detalhar = false)
    {
        ArgumentNullException.ThrowIfNull(relatorio);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);
        var (obra, dadosDaObra) = (cabecalho.Obra, cabecalho.DadosDaObra);
        using var f = new Fontes();
        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = $"Relatório das ordens de serviço · {obra}",
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
        });
        var contratante = dadosDaObra.Contratante is { } c ? $"{c} · " : "";
        var responsavel = string.Join(" · ", new[] { cabecalho.Responsavel, cabecalho.Registro }.Where(t => !string.IsNullOrWhiteSpace(t)));
        var p = new Pagina(documento, f, obra, $"{contratante}{relatorio.Filtro.Texto} · gerado em {geradoEm}" + (responsavel.Length > 0 ? $" · {responsavel}" : ""),
            cabecalho.Logotipo);
        p.Nova();
        Numeros(p, relatorio, f);
        if (relatorio.Vazio)
        {
            Escrever(p.Tela, "Nenhuma OS neste filtro.", Margem, p.Y + 14, f.Normal(10), Cinza);
            p.Fim();
            documento.Close();
            return;
        }
        Graficos(p, relatorio, f);
        Atencao(p, relatorio, f);
        PorExecutante(p, relatorio, f);
        Lista(p, relatorio, f);
        if (detalhar)
            Detalhe(p, relatorio, tarefas, f);
        p.Fim();
        documento.Close();
    }

    // Os cartões dos números, as situações e os prazos.
    private static void Numeros(Pagina p, RelatorioDasOrdens r, Fontes f)
    {
        var cartoes = new (string Rotulo, string Valor)[]
        {
            ("ORDENS DE SERVIÇO", r.Ordens.Count.ToString(Brasil)),
            ("CONTRATADO", Moeda.ComSimbolo(r.Valor)),
            ("MEDIDO", Moeda.ComSimbolo(r.Medido)),
            ("PAGO", Moeda.ComSimbolo(r.Pago)),
            ("A PAGAR", Moeda.ComSimbolo(r.APagar) + (r.Retido > 0 ? $" (retido {Moeda.Formatar(r.Retido)})" : "")),
            ("POR MEDIR", Moeda.ComSimbolo(r.PorMedir)),
        };
        var largura = (Limite - Margem - 5 * 8) / cartoes.Length;
        using var fundo = new SKPaint { Color = Faixa };
        for (var i = 0; i < cartoes.Length; i++)
        {
            var x = Margem + i * (largura + 8);
            p.Tela.DrawRoundRect(new SKRect(x, p.Y, x + largura, p.Y + 44), 4, 4, fundo);
            Escrever(p.Tela, cartoes[i].Rotulo, x + 8, p.Y + 15, f.Negrito(7), Cinza);
            Escrever(p.Tela, Cortar(cartoes[i].Valor, f.Negrito(i == 0 ? 14 : 11), largura - 14), x + 8, p.Y + 34, f.Negrito(i == 0 ? 14 : 11), i == 0 ? Destaque : Texto);
        }
        p.Y += 60;
        if (r.PorSituacao.Count > 0)
        {
            static string Plural(string texto, int n) => n > 1 && (texto.EndsWith('a') || texto.EndsWith('o')) ? texto + "s" : texto;
            var situacoes = string.Join(" · ", r.PorSituacao.Select(s => $"{s.Quantidade} {Plural(TextosDaOrdem.Texto(s.Situacao).ToLower(Brasil), s.Quantidade)}"));
            Escrever(p.Tela, Cortar($"Situação: {situacoes}", f.Normal(9), Limite - Margem), Margem, p.Y, f.Normal(9), Texto);
            p.Y += 14;
        }
        if (r.Prazos is { } prazos)
        {
            Escrever(p.Tela, prazos, Margem, p.Y, f.Normal(9), Texto);
            p.Y += 14;
        }
        p.Y += 6;
    }

    private static readonly SKColor CorDoContratado = new(0xBB, 0xDE, 0xFB);
    private static readonly SKColor CorDoMedido = new(0x42, 0xA5, 0xF5);
    private static readonly SKColor CorDoPago = new(0x15, 0x65, 0xC0);

    // Bloco 65: dois gráficos lado a lado: por executante (contratado, medido e pago) e o medido e o pago de cada mês.
    private static void Graficos(Pagina p, RelatorioDasOrdens r, Fontes f)
    {
        if (r.PorExecutante.Count == 0 && r.PorMes.Count == 0)
            return;
        const float altura = 150;
        p.Garantir(altura + 30);
        var meio = Margem + (Limite - Margem) / 2;
        var topo = p.Y;
        // A legenda, uma para os dois.
        var xl = Margem;
        foreach (var (texto, cor) in new[] { ("Contratado", CorDoContratado), ("Medido", CorDoMedido), ("Pago", CorDoPago) })
        {
            using var tinta = new SKPaint { Color = cor };
            p.Tela.DrawRect(new SKRect(xl, topo - 7, xl + 9, topo + 2), tinta);
            Escrever(p.Tela, texto, xl + 13, topo + 1, f.Normal(8), Texto);
            xl += 24 + f.Normal(8).MeasureText(texto);
        }
        topo += 12;
        if (r.PorExecutante.Count > 0)
            BarrasPorExecutante(p.Tela, r, new SKRect(Margem, topo, meio - 16, topo + altura), f);
        if (r.PorMes.Count > 0)
            ColunasPorMes(p.Tela, r, new SKRect(r.PorExecutante.Count > 0 ? meio + 16 : Margem, topo, Limite, topo + altura), f);
        p.Y = topo + altura + 18;
    }

    private static void BarrasPorExecutante(SKCanvas c, RelatorioDasOrdens r, SKRect area, Fontes f)
    {
        Escrever(c, "POR EXECUTANTE", area.Left, area.Top + 8, f.Negrito(8), Cinza);
        var lista = r.PorExecutante.OrderByDescending(e => e.Valor).Take(6).ToList();
        var maximo = Math.Max(1, lista.Max(e => e.Valor));
        var rotulo = 110f;
        var largura = area.Width - rotulo - 70;
        var faixa = Math.Min(28, (area.Height - 18) / lista.Count);
        var espessura = Math.Min(16, faixa - 6);
        for (var i = 0; i < lista.Count; i++)
        {
            var e = lista[i];
            var y = area.Top + 18 + i * faixa;
            Escrever(c, Cortar(e.Nome, f.Normal(8), rotulo - 6), area.Left, y + espessura / 2 + 3, f.Normal(8), Texto);
            float X(decimal v) => area.Left + rotulo + (float)(v / maximo) * largura;
            foreach (var (valor, cor, encolher) in new[] { (e.Valor, CorDoContratado, 0f), (e.Medido, CorDoMedido, espessura * 0.2f), (e.Pago, CorDoPago, espessura * 0.35f) })
            {
                using var tinta = new SKPaint { Color = cor, IsAntialias = true };
                c.DrawRect(new SKRect(area.Left + rotulo, y + encolher, Math.Max(area.Left + rotulo + 1, X(valor)), y + espessura - encolher), tinta);
            }
            Escrever(c, Abreviar(e.Valor), X(e.Valor) + 4, y + espessura / 2 + 3, f.Normal(7.5f), Cinza);
        }
    }

    private static void ColunasPorMes(SKCanvas c, RelatorioDasOrdens r, SKRect area, Fontes f)
    {
        Escrever(c, "MEDIDO E PAGO POR MÊS", area.Left, area.Top + 8, f.Negrito(8), Cinza);
        var meses = r.PorMes.TakeLast(12).ToList();
        var maximo = Math.Max(1, meses.Max(m => Math.Max(m.Medido, m.Pago)));
        var (topo, fundo) = (area.Top + 22, area.Bottom - 14);
        using var eixo = new SKPaint { Color = Linha, StrokeWidth = 0.6f };
        c.DrawLine(area.Left, fundo, area.Right, fundo, eixo);
        var largura = area.Width / meses.Count;
        var coluna = Math.Min(14, largura / 2 - 3);
        for (var i = 0; i < meses.Count; i++)
        {
            var m = meses[i];
            var centro = area.Left + largura * i + largura / 2;
            float Y(decimal v) => fundo - (float)(v / maximo) * (fundo - topo);
            using (var medido = new SKPaint { Color = CorDoMedido, IsAntialias = true })
                c.DrawRect(new SKRect(centro - coluna - 1, Y(m.Medido), centro - 1, fundo), medido);
            using (var pago = new SKPaint { Color = CorDoPago, IsAntialias = true })
                c.DrawRect(new SKRect(centro + 1, Y(m.Pago), centro + coluna + 1, fundo), pago);
            var mes = m.Mes.ToString("MMM/yy", Brasil).Replace(".", "");
            Escrever(c, mes, centro - f.Normal(7).MeasureText(mes) / 2, fundo + 10, f.Normal(7), Cinza);
            if (Math.Max(m.Medido, m.Pago) > 0)
            {
                var valor = Abreviar(Math.Max(m.Medido, m.Pago));
                Escrever(c, valor, centro - f.Normal(6.5f).MeasureText(valor) / 2, Y(Math.Max(m.Medido, m.Pago)) - 3, f.Normal(6.5f), Cinza);
            }
        }
    }

    /// <summary>"R$ 12,3 mil", "R$ 1,2 mi", "R$ 850".</summary>
    private static string Abreviar(decimal valor) => valor switch
    {
        >= 1_000_000 => $"R$ {(valor / 1_000_000).ToString("0.#", Brasil)} mi",
        >= 1_000 => $"R$ {(valor / 1_000).ToString("0.#", Brasil)} mil",
        _ => $"R$ {valor.ToString("0", Brasil)}",
    };

    // Bloco 65: o detalhe de cada OS: os itens com o medido e o %, as medições e os pagamentos.
    private static void Detalhe(Pagina p, RelatorioDasOrdens r, IReadOnlyDictionary<Guid, string> tarefas, Fontes f)
    {
        p.Nova();
        Escrever(p.Tela, "DETALHE DE CADA ORDEM DE SERVIÇO", Margem, p.Y + 4, f.Negrito(8), Cinza);
        p.Y += 14;
        (string, float, bool)[] colunas = [("Tarefa", 50, false), ("Descrição", 300, false), ("Un", 40, false), ("Contratado", 80, true), ("Medido", 80, true),
            ("%", 50, true), ("Preço un.", 80, true), ("Total", 90, true)];
        foreach (var o in r.Ordens)
        {
            p.Garantir(AlturaDaLinha * 4 + 20);
            Escrever(p.Tela, Cortar($"{o.Codigo} · {o.Dados.Titulo}", f.Negrito(10), Limite - Margem - 200), Margem, p.Y + 10, f.Negrito(10), Destaque);
            Direita(p.Tela, TextosDaOrdem.Texto(o.Situacao), Limite, p.Y + 10, f.Normal(9), Cinza);
            p.Y += 15;
            var linha = string.Join(" · ", new[]
            {
                o.Dados.Executante ?? "sem executante", TextosDaOrdem.Texto(o.Dados.FormaDePagamento).ToLower(Brasil),
                o.Dados.Prazo is { } prazo ? $"prazo {prazo.ToString("dd/MM/yyyy", Brasil)}" : null,
                o.Conclusao is { } fim ? $"concluída em {fim.ToString("dd/MM/yyyy", Brasil)}" : null,
                o.Dados.Retencao > 0 ? $"retenção {o.Dados.Retencao.ToString("0.##", Brasil)}%" : null,
            }.OfType<string>());
            Escrever(p.Tela, Cortar(linha, f.Normal(8.5f), Limite - Margem), Margem, p.Y + 8, f.Normal(8.5f), Texto);
            p.Y += 13;
            Cabecalho(p, colunas, f);
            for (var i = 0; i < o.Dados.Itens.Count; i++)
            {
                if (p.Garantir(AlturaDaLinha))
                    Cabecalho(p, colunas, f);
                var item = o.Dados.Itens[i];
                var medido = o.MedidoDoItem(i);
                var edt = item.TarefaId is { } id && tarefas.TryGetValue(id, out var t) ? t.Split(' ')[0] : "—";
                Celulas(p, colunas, [edt, item.Descricao, item.Unidade, Moeda.FormatarQuantidade(item.Quantidade), Moeda.FormatarQuantidade(medido),
                    item.Quantidade == 0 ? "—" : $"{(medido / item.Quantidade * 100).ToString("0", Brasil)}%", Moeda.Formatar(item.PrecoUnitario), Moeda.Formatar(item.Total)], f, Texto);
            }
            foreach (var m in o.Medicoes)
            {
                p.Garantir(13);
                var pago = m.Pagamento is { } pg ? $"paga em {pg.Data.ToString("dd/MM/yyyy", Brasil)}{(pg.Documento is { } doc ? $" ({doc})" : "")}" : "a pagar";
                var retida = o.RetencaoDe(m) > 0 ? $" · retido {Moeda.ComSimbolo(o.RetencaoDe(m))}" : "";
                Escrever(p.Tela, $"{m.Codigo} · {m.Data.ToString("dd/MM/yyyy", Brasil)} · {Moeda.ComSimbolo(m.Valor)} · {pago}{retida}", Margem + 8, p.Y + 10,
                    f.Normal(8.5f), Texto);
                p.Y += 13;
            }
            if (o.Liberacao is { } l)
            {
                p.Garantir(13);
                Escrever(p.Tela, $"Retenção de {Moeda.ComSimbolo(l.Valor)} liberada em {l.Data.ToString("dd/MM/yyyy", Brasil)}", Margem + 8, p.Y + 10, f.Normal(8.5f), Texto);
                p.Y += 13;
            }
            p.Y += 12;
        }
    }

    // As vencidas e as que esperam a conferência.
    private static void Atencao(Pagina p, RelatorioDasOrdens r, Fontes f)
    {
        if (r.Vencidas.Count == 0 && r.ParaConferir.Count == 0)
            return;
        p.Garantir(40);
        Escrever(p.Tela, "PEDEM ATENÇÃO", Margem, p.Y + 4, f.Negrito(8), Aviso);
        p.Y += 18;
        foreach (var o in r.Vencidas)
        {
            p.Garantir(AlturaDaLinha);
            var dias = r.DiasDeAtraso(o) is { } d ? $"{d} dia{(d == 1 ? "" : "s")} de atraso" : "vencida";
            Escrever(p.Tela, Cortar($"{o.Codigo} · {o.Dados.Titulo} · {o.Dados.Executante ?? "sem executante"} · prazo {Data(o.Dados.Prazo)} · {dias}",
                f.Normal(9), Limite - Margem), Margem, p.Y, f.Normal(9), Aviso);
            p.Y += 13;
        }
        foreach (var o in r.ParaConferir)
        {
            p.Garantir(AlturaDaLinha);
            Escrever(p.Tela, Cortar($"{o.Codigo} · {o.Dados.Titulo} · {o.Dados.Executante ?? "sem executante"} · concluída em {Data(o.Conclusao)}: esperando a conferência",
                f.Normal(9), Limite - Margem), Margem, p.Y, f.Normal(9), Texto);
            p.Y += 13;
        }
        p.Y += 8;
    }

    private static void PorExecutante(Pagina p, RelatorioDasOrdens r, Fontes f)
    {
        if (r.PorExecutante.Count == 0)
            return;
        p.Garantir(AlturaDaLinha * 3);
        Escrever(p.Tela, "POR EXECUTANTE", Margem, p.Y + 4, f.Negrito(8), Cinza);
        p.Y += 12;
        (string, float, bool)[] colunas = [("Executante", 160, false), ("OS", 40, true), ("Contratado", 80, true), ("Medido", 80, true), ("Pago", 80, true),
            ("A pagar", 80, true), ("Prazos", Limite - Margem - 520, false)];
        Cabecalho(p, colunas, f);
        foreach (var e in r.PorExecutante)
        {
            if (p.Garantir(AlturaDaLinha))
                Cabecalho(p, colunas, f);
            Celulas(p, colunas, [e.Nome, e.Ordens.ToString(Brasil), Moeda.Formatar(e.Valor), Moeda.Formatar(e.Medido), Moeda.Formatar(e.Pago),
                Moeda.Formatar(e.APagar), e.Prazos is { } pr ? pr["Prazos: ".Length..].TrimEnd('.') : "—"], f, Texto);
        }
        p.Y += 12;
    }

    private static void Lista(Pagina p, RelatorioDasOrdens r, Fontes f)
    {
        p.Garantir(AlturaDaLinha * 3);
        Escrever(p.Tela, "ORDENS DE SERVIÇO", Margem, p.Y + 4, f.Negrito(8), Cinza);
        p.Y += 12;
        Cabecalho(p, Colunas, f);
        foreach (var o in r.Ordens)
        {
            if (p.Garantir(AlturaDaLinha))
                Cabecalho(p, Colunas, f);
            var atraso = r.DiasDeAtraso(o);
            var cancelada = o.Situacao == SituacaoDaOrdem.Cancelada;
            Celulas(p, Colunas,
            [
                o.Codigo, o.Dados.Titulo, o.Dados.Executante ?? "—", TextosDaOrdem.Texto(o.Situacao), Data(o.Emissao), Data(o.Dados.Prazo), Data(o.Conclusao),
                atraso is > 0 ? atraso.Value.ToString(Brasil) : atraso == 0 ? "0" : "—",
                Moeda.Formatar(o.Total), Moeda.Formatar(o.TotalMedido), Moeda.Formatar(o.TotalPago), Moeda.Formatar(o.APagar),
            ], f, cancelada ? Cinza : o.Vencida(r.Hoje) ? Aviso : Texto);
        }
        // O total da lista (sem as canceladas).
        p.Garantir(AlturaDaLinha);
        using var fundo = new SKPaint { Color = Faixa };
        p.Tela.DrawRect(new SKRect(Margem, p.Y, Limite, p.Y + AlturaDaLinha), fundo);
        var x = Margem + Colunas.Take(8).Sum(c => c.Largura);
        Escrever(p.Tela, "Total (sem as canceladas)", Margem + 4, p.Y + 11.5f, f.Negrito(8), Texto);
        foreach (var (valor, i) in new[] { r.Valor, r.Medido, r.Pago, r.APagar }.Select((v, i) => (v, i)))
        {
            x += Colunas[8 + i].Largura;
            Direita(p.Tela, Moeda.Formatar(valor), x - 4, p.Y + 11.5f, f.Negrito(8));
        }
        p.Y += AlturaDaLinha;
    }

    private static void Cabecalho(Pagina p, IReadOnlyList<(string Titulo, float Largura, bool Direita)> colunas, Fontes f)
    {
        using var fundo = new SKPaint { Color = Faixa };
        p.Tela.DrawRect(new SKRect(Margem, p.Y, Margem + colunas.Sum(c => c.Largura), p.Y + AlturaDaLinha), fundo);
        var x = Margem;
        foreach (var (titulo, largura, direita) in colunas)
        {
            if (direita)
                Direita(p.Tela, titulo, x + largura - 4, p.Y + 11.5f, f.Negrito(8));
            else
                Escrever(p.Tela, titulo, x + 4, p.Y + 11.5f, f.Negrito(8), Texto);
            x += largura;
        }
        p.Y += AlturaDaLinha;
    }

    private static void Celulas(Pagina p, IReadOnlyList<(string Titulo, float Largura, bool Direita)> colunas, IReadOnlyList<string> valores, Fontes f, SKColor cor)
    {
        var x = Margem;
        for (var i = 0; i < colunas.Count; i++)
        {
            var (_, largura, direita) = colunas[i];
            var texto = Cortar(valores[i], f.Normal(8), largura - 8);
            if (direita)
                Direita(p.Tela, texto, x + largura - 4, p.Y + 11.5f, f.Normal(8), cor);
            else
                Escrever(p.Tela, texto, x + 4, p.Y + 11.5f, f.Normal(8), cor);
            x += largura;
        }
        p.Y += AlturaDaLinha;
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.5f };
        p.Tela.DrawLine(Margem, p.Y, x, p.Y, traco);
    }

    private static string Data(DateOnly? data) => data?.ToString("dd/MM/yy", Brasil) ?? "—";

    private static string Cortar(string texto, SKFont fonte, float largura)
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

    private static void Direita(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor? cor = null)
    {
        using var tinta = new SKPaint { Color = cor ?? Texto, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Right, fonte, tinta);
    }
}
