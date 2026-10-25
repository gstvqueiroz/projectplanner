using System.Globalization;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 65: o boletim de medição de uma OS em PDF (A4 paisagem), para pagar o executante: a obra (com o logotipo), a OS e a
/// medição, os itens (contratado, anterior, nesta medição, acumulado, saldo, %, valores), o resumo (medido, retenção, líquido a
/// pagar, saldo) e as assinaturas do executante, da engenharia e da aprovação.
/// </summary>
public sealed class GeradorBoletimDaOrdemPdf
{
    private const float Largura = 841.89f;
    private const float Altura = 595.28f;
    private const float Margem = 36f;
    private const float Limite = Largura - Margem;
    private const float AlturaDaLinha = 17f;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor Destaque = new(0x15, 0x65, 0xC0);

    // Item, tarefa, descrição, un, contratado, preço un., anterior, nesta, acumulado, saldo, %, valor nesta, valor acumulado (770 pontos).
    private static readonly (string Titulo, float Largura, bool Direita)[] Colunas =
    [
        ("Item", 28, false), ("Tarefa", 40, false), ("Descrição", 178, false), ("Un", 30, false), ("Contratado", 56, true), ("Preço un.", 56, true),
        ("Anterior", 52, true), ("Nesta", 52, true), ("Acumulado", 56, true), ("Saldo", 52, true), ("%", 38, true), ("Valor nesta", 64, true),
        ("Valor acumulado", 68, true),
    ];

    public void Gerar(BoletimDaOrdem boletim, CabecalhoDoRelatorio cabecalho, IReadOnlyDictionary<Guid, string> tarefas, string geradoEm, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(boletim);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);
        using var f = new Fontes();
        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = boletim.Titulo,
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
        });
        var linhasPorPagina = (int)((Altura - Margem - 210 - 150) / AlturaDaLinha);
        var blocos = boletim.Linhas.Chunk(Math.Max(5, linhasPorPagina)).ToList();
        if (blocos.Count == 0)
            blocos.Add([]);
        for (var b = 0; b < blocos.Count; b++)
        {
            var c = documento.BeginPage(Largura, Altura);
            var y = Cabecalho(c, boletim, cabecalho, f);
            y = Tabela(c, boletim, blocos[b], tarefas, y, ultima: b == blocos.Count - 1, f);
            if (b == blocos.Count - 1)
                Resumo(c, boletim, cabecalho, y + 16, f);
            Escrever(c, $"Gerado pelo ProjectPlanner em {geradoEm}", Margem, Altura - Margem + 14, f.Normal(7.5f), Cinza);
            Direita(c, $"Página {b + 1} de {blocos.Count}", Limite, Altura - Margem + 14, f.Normal(7.5f), Cinza);
            documento.EndPage();
        }
        documento.Close();
    }

    private static float Cabecalho(SKCanvas c, BoletimDaOrdem b, CabecalhoDoRelatorio cab, Fontes f)
    {
        var o = b.Ordem;
        var direitaDoTexto = Limite;
        if (cab.Logotipo is { Length: > 0 } logo)
        {
            GeradorBoletimPdf.DesenharLogotipo(c, logo, new SKRect(Limite - 130, Margem - 4, Limite, Margem + 40));
            direitaDoTexto = Limite - 140;
        }
        Escrever(c, "BOLETIM DE MEDIÇÃO DA ORDEM DE SERVIÇO", Margem, Margem + 4, f.Negrito(10), Cinza);
        Escrever(c, Cortar(cab.Obra, f.Negrito(16), direitaDoTexto - Margem - 160), Margem, Margem + 26, f.Negrito(16), Texto);
        Direita(c, $"{o.Codigo} · {b.Medicao.Codigo}", direitaDoTexto, Margem + 26, f.Negrito(16), Destaque);
        var endereco = string.Join(" · ", new[] { cab.DadosDaObra.Contratante, cab.DadosDaObra.Endereco }.OfType<string>());
        if (endereco.Length > 0)
            Escrever(c, Cortar(endereco, f.Normal(8.5f), direitaDoTexto - Margem), Margem, Margem + 42, f.Normal(8.5f), Cinza);

        // O quadro da OS e da medição.
        var y = Margem + 54f;
        using var fundo = new SKPaint { Color = Faixa };
        c.DrawRoundRect(new SKRect(Margem, y, Limite, y + 64), 4, 4, fundo);
        var quarto = (Limite - Margem) / 4;
        void Campo(string rotulo, string valor, int coluna, int linha)
        {
            var x = Margem + 10 + coluna * quarto;
            Escrever(c, rotulo, x, y + 16 + linha * 28, f.Negrito(7), Cinza);
            Escrever(c, Cortar(valor, f.Normal(9.5f), quarto - 16), x, y + 28 + linha * 28, f.Normal(9.5f), Texto);
        }
        Campo("SERVIÇO", o.Dados.Titulo, 0, 0);
        Campo("EXECUTANTE", $"{o.Dados.Executante ?? "—"} ({TextosDaOrdem.TipoCurto(o.Dados.Tipo)})", 1, 0);
        Campo("MEDIÇÃO", $"{b.Medicao.Codigo} de {b.Medicao.Data.ToString("dd/MM/yyyy", Brasil)}", 2, 0);
        Campo("PAGAMENTO", TextosDaOrdem.Texto(o.Dados.FormaDePagamento) + (o.Dados.Retencao > 0 ? $" · retenção {o.Dados.Retencao.ToString("0.##", Brasil)}%" : ""), 3, 0);
        Campo("EMISSÃO DA OS", o.Emissao?.ToString("dd/MM/yyyy", Brasil) ?? "—", 0, 1);
        Campo("PRAZO", o.Dados.Prazo?.ToString("dd/MM/yyyy", Brasil) ?? "—", 1, 1);
        Campo("SITUAÇÃO DA OS", TextosDaOrdem.Texto(o.Situacao), 2, 1);
        Campo("CONTATO", o.Dados.Contato ?? "—", 3, 1);
        return y + 80;
    }

    private static float Tabela(SKCanvas c, BoletimDaOrdem b, IReadOnlyList<LinhaDoBoletimDaOrdem> linhas, IReadOnlyDictionary<Guid, string> tarefas, float y,
        bool ultima, Fontes f)
    {
        using var fundo = new SKPaint { Color = Faixa };
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.5f };
        c.DrawRect(new SKRect(Margem, y, Limite, y + AlturaDaLinha), fundo);
        var x = Margem;
        foreach (var (titulo, largura, direita) in Colunas)
        {
            if (direita)
                Direita(c, titulo, x + largura - 3, y + 12, f.Negrito(7.5f));
            else
                Escrever(c, titulo, x + 3, y + 12, f.Negrito(7.5f), Texto);
            x += largura;
        }
        y += AlturaDaLinha;
        string Q(decimal v) => Moeda.FormatarQuantidade(v);
        foreach (var l in linhas)
        {
            var edt = l.Item.TarefaId is { } id && tarefas.TryGetValue(id, out var t) ? t.Split(' ')[0] : "—";
            string[] valores =
            [
                l.Numero.ToString(Brasil), edt, l.Item.Descricao, l.Item.Unidade, Q(l.Item.Quantidade), Moeda.Formatar(l.Item.PrecoUnitario),
                Q(l.Anterior), l.NestaMedicao == 0 ? "—" : Q(l.NestaMedicao), Q(l.Acumulado), Q(l.Saldo), $"{l.Percentual.ToString("0.0", Brasil)}%",
                Moeda.Formatar(l.ValorNestaMedicao), Moeda.Formatar(l.ValorAcumulado),
            ];
            x = Margem;
            for (var i = 0; i < Colunas.Length; i++)
            {
                var (_, largura, direita) = Colunas[i];
                var fonte = i == 7 || i == 11 ? f.Negrito(8) : f.Normal(8);
                var texto = Cortar(valores[i], fonte, largura - 6);
                if (direita)
                    Direita(c, texto, x + largura - 3, y + 12, fonte);
                else
                    Escrever(c, texto, x + 3, y + 12, fonte, Texto);
                x += largura;
            }
            y += AlturaDaLinha;
            c.DrawLine(Margem, y, Limite, y, traco);
        }
        if (ultima)
        {
            c.DrawRect(new SKRect(Margem, y, Limite, y + AlturaDaLinha), fundo);
            Escrever(c, "Total", Margem + 3 + 68, y + 12, f.Negrito(8.5f), Texto);
            Direita(c, Moeda.Formatar(b.ValorNestaMedicao), Limite - 68 - 3, y + 12, f.Negrito(8.5f));
            Direita(c, Moeda.Formatar(b.MedidoAcumulado), Limite - 3, y + 12, f.Negrito(8.5f));
            y += AlturaDaLinha;
        }
        return y;
    }

    // O resumo à direita e as assinaturas à esquerda.
    private static void Resumo(SKCanvas c, BoletimDaOrdem b, CabecalhoDoRelatorio cab, float y, Fontes f)
    {
        var x = Limite - 270;
        var linhas = new List<(string Rotulo, string Valor, bool Forte)>
        {
            ("Valor da OS", Moeda.ComSimbolo(b.Ordem.Total), false),
            ("Medido nas medições anteriores", Moeda.ComSimbolo(b.MedidoAnterior), false),
            ($"Medido nesta medição ({b.Medicao.Codigo})", Moeda.ComSimbolo(b.ValorNestaMedicao), true),
            ($"Medido acumulado ({b.PercentualAcumulado.ToString("0.0", Brasil)}%)", Moeda.ComSimbolo(b.MedidoAcumulado), false),
            ("Saldo da OS", Moeda.ComSimbolo(b.Saldo), false),
        };
        if (b.Retencao > 0)
            linhas.Add(($"Retenção de garantia ({b.Ordem.Dados.Retencao.ToString("0.##", Brasil)}%)", "− " + Moeda.ComSimbolo(b.Retencao), false));
        linhas.Add(("Líquido a pagar", Moeda.ComSimbolo(b.Liquido), true));
        using var fundo = new SKPaint { Color = Faixa };
        c.DrawRoundRect(new SKRect(x - 10, y - 4, Limite, y + linhas.Count * 15 + 26), 4, 4, fundo);
        var yy = y + 10;
        foreach (var (rotulo, valor, forte) in linhas)
        {
            Escrever(c, rotulo, x, yy, forte ? f.Negrito(9) : f.Normal(9), Texto);
            Direita(c, valor, Limite - 8, yy, forte ? f.Negrito(9) : f.Normal(9), forte ? Destaque : Texto);
            yy += 15;
        }
        var pagamento = b.Medicao.Pagamento is { } p
            ? $"Paga em {p.Data.ToString("dd/MM/yyyy", Brasil)}" + (p.Documento is { } doc ? $" ({doc})" : "")
            : "Pagamento ainda não registrado";
        Escrever(c, pagamento, x, yy + 6, f.Normal(8), Cinza);

        // As assinaturas.
        var largura = (x - 20 - Margem - 2 * 20) / 3;
        var ya = Math.Max(y + 70, Altura - Margem - 50);
        using var linha = new SKPaint { Color = Texto, StrokeWidth = 0.7f };
        void Assinatura(int i, string rotulo, string? nome)
        {
            var xa = Margem + i * (largura + 20);
            c.DrawLine(xa, ya, xa + largura, ya, linha);
            Escrever(c, rotulo, xa, ya + 11, f.Negrito(8), Texto);
            if (nome is not null)
                Escrever(c, Cortar(nome, f.Normal(7.5f), largura), xa, ya + 21, f.Normal(7.5f), Cinza);
        }
        Assinatura(0, "Executante: de acordo", b.Ordem.Dados.Executante);
        var responsavel = string.Join(" · ", new[] { cab.Responsavel, cab.Registro }.Where(t => !string.IsNullOrWhiteSpace(t)));
        Assinatura(1, "Engenharia (medido por)", responsavel.Length > 0 ? responsavel : null);
        Assinatura(2, "Aprovado para pagamento", null);
    }

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
