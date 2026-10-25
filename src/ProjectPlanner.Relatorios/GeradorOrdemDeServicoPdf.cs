using System.Globalization;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>O que vai no cabeçalho da OS, além dela mesma.</summary>
/// <param name="Obra">O nome da obra.</param>
/// <param name="DadosDaObra">Contratante, contrato e endereço (os do relatório fotográfico).</param>
/// <param name="Tarefas">A EDT e o nome de cada tarefa ("2.3 Alvenaria"), para a coluna das tarefas.</param>
/// <param name="GeradoEm">"25/10/2026 15:00".</param>
public sealed record CabecalhoDaOrdem(string Obra, DadosDaObra DadosDaObra, IReadOnlyDictionary<Guid, string> Tarefas, string GeradoEm);

/// <summary>
/// Bloco 58: a ordem de serviço em PDF (A4 retrato), para imprimir e assinar: o número e a obra, o executante e as datas, o
/// escopo, a tabela dos itens (dividida em páginas se precisar), as observações, a lista de verificação, o histórico e as
/// assinaturas (emitente, executante e a conferência).
/// </summary>
public sealed class GeradorOrdemDeServicoPdf
{
    private const float Largura = 595.28f;
    private const float Altura = 841.89f;
    private const float Margem = 40f;
    private const float AlturaDoItem = 18f;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor Destaque = new(0x15, 0x65, 0xC0);

    private const float Limite = Largura - Margem;

    public void Gerar(OrdemDeServico ordem, CabecalhoDaOrdem cabecalho, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);
        using var f = new Fontes();
        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = $"{ordem.Codigo} · {ordem.Dados.Titulo}",
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
        });
        // A primeira página: o cabeçalho, os dados e o começo dos itens; as seguintes continuam os itens; o fim (observações,
        // verificação, histórico e assinaturas) vai onde couber.
        var itens = ordem.Dados.Itens.Select((item, i) => (Indice: i + 1, Item: item)).ToList();
        var blocos = new List<List<(int, ItemDaOrdem)>>();
        const float topoDosItensNaPrimeira = 300;
        var cabemNaPrimeira = (int)((Altura - Margem - 40 - topoDosItensNaPrimeira) / AlturaDoItem) - 2;
        var cabemNasOutras = (int)((Altura - 2 * Margem - 100) / AlturaDoItem) - 2;
        var resto = itens;
        blocos.Add([.. resto.Take(cabemNaPrimeira)]);
        resto = [.. resto.Skip(cabemNaPrimeira)];
        while (resto.Count > 0)
        {
            blocos.Add([.. resto.Take(cabemNasOutras)]);
            resto = [.. resto.Skip(cabemNasOutras)];
        }

        // Quanto o fim precisa, e se cabe depois dos últimos itens.
        var alturaDoFim = AlturaDoFim(ordem, f);
        var ultimoTopo = blocos.Count == 1 ? topoDosItensNaPrimeira : Margem + 70;
        var fimDosItens = ultimoTopo + (blocos[^1].Count + 2) * AlturaDoItem + 14;
        var fimNaMesma = fimDosItens + alturaDoFim <= Altura - Margem - 24;
        var total = blocos.Count + (fimNaMesma ? 0 : 1);

        for (var b = 0; b < blocos.Count; b++)
        {
            var c = documento.BeginPage(Largura, Altura);
            float y;
            if (b == 0)
                y = Primeira(c, ordem, cabecalho, f);
            else
            {
                Continuacao(c, ordem, cabecalho, f);
                y = Margem + 70;
            }
            y = Itens(c, ordem, blocos[b], cabecalho, y, ultimo: b == blocos.Count - 1, f);
            if (b == blocos.Count - 1 && fimNaMesma)
                Fim(c, ordem, y + 14, f);
            Rodape(c, cabecalho, b + 1, total, f);
            documento.EndPage();
        }
        if (!fimNaMesma)
        {
            var c = documento.BeginPage(Largura, Altura);
            Continuacao(c, ordem, cabecalho, f);
            Fim(c, ordem, Margem + 70, f);
            Rodape(c, cabecalho, total, total, f);
            documento.EndPage();
        }
        documento.Close();
    }

    private float Primeira(SKCanvas c, OrdemDeServico o, CabecalhoDaOrdem cab, Fontes f)
    {
        var d = o.Dados;
        Escrever(c, "ORDEM DE SERVIÇO", Margem, Margem + 6, f.Negrito(10), Cinza);
        Direita(c, o.Codigo, Limite, Margem + 22, f.Negrito(22), Destaque);
        Escrever(c, Cortar(cab.Obra, f.Negrito(16), Limite - Margem - 130), Margem, Margem + 28, f.Negrito(16), Texto);
        var y = Margem + 44f;
        foreach (var linha in new[] { cab.DadosDaObra.Contratante is { } ct ? $"Contratante: {ct}" : null, cab.DadosDaObra.Contrato is { } ctr ? $"Contrato: {ctr}" : null,
                     cab.DadosDaObra.Endereco is { } end ? $"Endereço: {end}" : null }.OfType<string>())
        {
            Escrever(c, Cortar(linha, f.Normal(8.5f), Limite - Margem), Margem, y, f.Normal(8.5f), Cinza);
            y += 12;
        }
        Direita(c, $"Situação: {TextosDaOrdem.Texto(o.Situacao)}", Limite, Margem + 40, f.Normal(8.5f), Cinza);

        // O quadro dos dados.
        y += 6;
        using var fundo = new SKPaint { Color = Faixa };
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.75f, Style = SKPaintStyle.Stroke };
        var quadro = new SKRect(Margem, y, Limite, y + 96);
        c.DrawRect(quadro, fundo);
        c.DrawRect(quadro, traco);
        var meio = Margem + (Limite - Margem) / 2;
        void Campo(string rotulo, string? valor, float x, float yy, float largura)
        {
            Escrever(c, rotulo, x, yy, f.Negrito(7.5f), Cinza);
            Escrever(c, Cortar(valor ?? "—", f.Normal(9.5f), largura), x, yy + 12, f.Normal(9.5f), Texto);
        }
        Campo("EXECUTANTE", d.Executante, Margem + 10, y + 16, meio - Margem - 20);
        Campo("TIPO", TextosDaOrdem.Texto(d.Tipo), meio + 10, y + 16, Limite - meio - 20);
        Campo("CONTATO", d.Contato, Margem + 10, y + 46, meio - Margem - 20);
        Campo("PAGAMENTO", TextosDaOrdem.Texto(d.FormaDePagamento) + (d.Retencao > 0 ? $" · retenção de {d.Retencao.ToString("0.##", Brasil)}%" : ""),
            meio + 10, y + 46, Limite - meio - 20);
        var terco = (Limite - Margem) / 3;
        Campo("EMISSÃO", Data(o.Emissao), Margem + 10, y + 76 - 0, terco - 20);
        Campo("INÍCIO PREVISTO", Data(d.InicioPrevisto), Margem + terco + 10, y + 76, terco - 20);
        Campo("PRAZO", Data(d.Prazo), Margem + 2 * terco + 10, y + 76, terco - 20);
        y = quadro.Bottom + 22;

        Escrever(c, "SERVIÇO", Margem, y, f.Negrito(7.5f), Cinza);
        y += 14;
        foreach (var linha in Quebrar(d.Titulo, f.Negrito(12), Limite - Margem).Take(2))
        {
            Escrever(c, linha, Margem, y, f.Negrito(12), Texto);
            y += 15;
        }
        return Math.Max(y + 6, 300 - AlturaDoItem);
    }

    private void Continuacao(SKCanvas c, OrdemDeServico o, CabecalhoDaOrdem cab, Fontes f)
    {
        Escrever(c, "ORDEM DE SERVIÇO (continuação)", Margem, Margem + 6, f.Negrito(10), Cinza);
        Direita(c, o.Codigo, Limite, Margem + 22, f.Negrito(18), Destaque);
        Escrever(c, Cortar($"{cab.Obra} · {o.Dados.Titulo}", f.Normal(10), Limite - Margem - 110), Margem, Margem + 26, f.Normal(10), Texto);
    }

    // A tabela: item, tarefa, descrição, unidade, quantidade, preço unitário e total; no último bloco, o total da OS.
    private float Itens(SKCanvas c, OrdemDeServico o, List<(int Indice, ItemDaOrdem Item)> itens, CabecalhoDaOrdem cab, float y, bool ultimo, Fontes f)
    {
        float[] x = [Margem, Margem + 26, Margem + 76, Limite - 196, Limite - 164, Limite - 112, Limite - 56];
        using var fundo = new SKPaint { Color = Faixa };
        using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.6f };
        c.DrawRect(new SKRect(Margem, y, Limite, y + AlturaDoItem), fundo);
        var cabecalho = new[] { "Item", "Tarefa", "Descrição", "Un", "Quant.", "Preço un.", "Total" };
        for (var i = 0; i < cabecalho.Length; i++)
        {
            if (i >= 4)
                Direita(c, cabecalho[i], (i + 1 < x.Length ? x[i + 1] : Limite) - 4, y + 12.5f, f.Negrito(8));
            else
                Escrever(c, cabecalho[i], x[i] + 3, y + 12.5f, f.Negrito(8), Texto);
        }
        y += AlturaDoItem;
        foreach (var (indice, item) in itens)
        {
            var edt = item.TarefaId is { } id && cab.Tarefas.TryGetValue(id, out var tarefa) ? tarefa.Split(' ')[0] : "—";
            Escrever(c, indice.ToString(Brasil), x[0] + 3, y + 12.5f, f.Normal(8), Texto);
            Escrever(c, Cortar(edt, f.Normal(8), x[2] - x[1] - 6), x[1] + 3, y + 12.5f, f.Normal(8), Texto);
            Escrever(c, Cortar(item.Descricao, f.Normal(8), x[3] - x[2] - 6), x[2] + 3, y + 12.5f, f.Normal(8), Texto);
            Escrever(c, Cortar(item.Unidade, f.Normal(8), x[4] - x[3] - 4), x[3] + 3, y + 12.5f, f.Normal(8), Texto);
            Direita(c, Numero(item.Quantidade), x[5] - 4, y + 12.5f, f.Normal(8));
            Direita(c, Moeda.Formatar(item.PrecoUnitario), x[6] - 4, y + 12.5f, f.Normal(8));
            Direita(c, Moeda.Formatar(item.Total), Limite - 4, y + 12.5f, f.Normal(8));
            y += AlturaDoItem;
            c.DrawLine(Margem, y, Limite, y, traco);
        }
        if (itens.Count == 0)
        {
            Escrever(c, "Nenhum item.", x[2] + 3, y + 12.5f, f.Normal(8), Cinza);
            y += AlturaDoItem;
        }
        if (ultimo)
        {
            c.DrawRect(new SKRect(Margem, y, Limite, y + AlturaDoItem), fundo);
            Escrever(c, "Total da OS", x[2] + 3, y + 12.5f, f.Negrito(9), Texto);
            Direita(c, Moeda.ComSimbolo(o.Total), Limite - 4, y + 12.5f, f.Negrito(9));
            y += AlturaDoItem;
        }
        return y;
    }

    private float AlturaDoFim(OrdemDeServico o, Fontes f)
    {
        var altura = 0f;
        if (o.Dados.Observacoes is { } obs)
            altura += 22 + Quebrar(obs, f.Normal(9), Limite - Margem).Count * 12;
        if (o.Dados.Verificacao.Count > 0)
            altura += 22 + o.Dados.Verificacao.Count * 14;
        if (o.Historico.Count > 1)
            altura += 22 + Math.Min(o.Historico.Count, 12) * 11;
        if (o.Medicoes.Count > 0)
            altura += 34 + o.Medicoes.Count * 12;
        return altura + 130; // as assinaturas
    }

    private void Fim(SKCanvas c, OrdemDeServico o, float y, Fontes f)
    {
        var d = o.Dados;
        if (d.Observacoes is { } obs)
        {
            Escrever(c, "OBSERVAÇÕES", Margem, y + 8, f.Negrito(7.5f), Cinza);
            y += 22;
            foreach (var linha in Quebrar(obs, f.Normal(9), Limite - Margem))
            {
                Escrever(c, linha, Margem, y, f.Normal(9), Texto);
                y += 12;
            }
        }
        if (d.Verificacao.Count > 0)
        {
            Escrever(c, "LISTA DE VERIFICAÇÃO", Margem, y + 8, f.Negrito(7.5f), Cinza);
            y += 22;
            using var caixa = new SKPaint { Color = Texto, StrokeWidth = 0.8f, Style = SKPaintStyle.Stroke, IsAntialias = true };
            foreach (var item in d.Verificacao)
            {
                c.DrawRect(new SKRect(Margem, y - 8, Margem + 9, y + 1), caixa);
                if (item.Feito)
                {
                    c.DrawLine(Margem + 1.5f, y - 3.5f, Margem + 4, y - 1, caixa);
                    c.DrawLine(Margem + 4, y - 1, Margem + 8, y - 7, caixa);
                }
                Escrever(c, Cortar(item.Texto, f.Normal(9), Limite - Margem - 16), Margem + 16, y, f.Normal(9), Texto);
                y += 14;
            }
        }
        if (o.Medicoes.Count > 0) // Bloco 60
        {
            Escrever(c, "MEDIÇÕES E PAGAMENTOS", Margem, y + 8, f.Negrito(7.5f), Cinza);
            y += 22;
            foreach (var m in o.Medicoes)
            {
                Escrever(c, $"{m.Codigo} · {m.Data.ToString("dd/MM/yyyy", Brasil)}", Margem, y, f.Normal(9), Texto);
                Direita(c, Moeda.ComSimbolo(m.Valor), Margem + 250, y, f.Normal(9));
                Escrever(c, m.Pagamento is { } p ? $"paga em {p.Data.ToString("dd/MM/yyyy", Brasil)}{(p.Documento is { } doc ? $" ({doc})" : "")}" : "a pagar",
                    Margem + 270, y, f.Normal(9), Cinza);
                y += 12;
            }
            Escrever(c, $"Medido {Moeda.ComSimbolo(o.TotalMedido)} · pago {Moeda.ComSimbolo(o.TotalPago)} · a pagar {Moeda.ComSimbolo(o.APagar)}" +
                (o.Retido > 0 ? $" (retido {Moeda.ComSimbolo(o.Retido)})" : "") +
                (o.Liberacao is { } l ? $" · retenção de {Moeda.ComSimbolo(l.Valor)} liberada em {l.Data.ToString("dd/MM/yyyy", Brasil)}" : ""),
                Margem, y + 2, f.Negrito(8.5f), Texto);
            y += 12;
        }
        if (o.Historico.Count > 1)
        {
            Escrever(c, "HISTÓRICO", Margem, y + 8, f.Negrito(7.5f), Cinza);
            y += 22;
            foreach (var e in o.Historico.TakeLast(12))
            {
                var texto = $"{e.Quando.ToString("dd/MM/yyyy HH:mm", Brasil)}  {e.Descricao}{(e.Autor is null ? "" : $" ({e.Autor})")}";
                Escrever(c, Cortar(texto, f.Normal(8), Limite - Margem), Margem, y, f.Normal(8), Cinza);
                y += 11;
            }
        }

        // As assinaturas.
        y = Math.Max(y + 40, Altura - Margem - 120);
        using var tracoDaAssinatura = new SKPaint { Color = Texto, StrokeWidth = 0.7f };
        var largura = (Limite - Margem - 30) / 2;
        void Assinatura(float x, string rotulo, string? nome)
        {
            c.DrawLine(x, y, x + largura, y, tracoDaAssinatura);
            Escrever(c, rotulo, x, y + 12, f.Negrito(8), Texto);
            if (nome is not null)
                Escrever(c, Cortar(nome, f.Normal(8), largura), x, y + 23, f.Normal(8), Cinza);
            Escrever(c, "Data: ____/____/______", x, y + (nome is null ? 24 : 35), f.Normal(8), Cinza);
        }
        Assinatura(Margem, "Emitente (engenharia da obra)", null);
        Assinatura(Margem + largura + 30, "Executante: de acordo", d.Executante);
        y += 62;
        c.DrawLine(Margem, y, Margem + largura, y, tracoDaAssinatura);
        Escrever(c, "Conferido e aprovado por", Margem, y + 12, f.Negrito(8), Texto);
        Escrever(c, "Data: ____/____/______", Margem, y + 24, f.Normal(8), Cinza);
    }

    private void Rodape(SKCanvas c, CabecalhoDaOrdem cab, int pagina, int total, Fontes f)
    {
        var y = Altura - Margem + 16;
        Escrever(c, $"Gerado pelo ProjectPlanner em {cab.GeradoEm}", Margem, y, f.Normal(7.5f), Cinza);
        Direita(c, $"Página {pagina} de {total}", Limite, y, f.Normal(7.5f), Cinza);
    }

    // ---- Peças ----

    private static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", Brasil) ?? "—";

    private static string Numero(decimal valor) => valor.ToString(valor == Math.Round(valor) ? "#,##0" : "#,##0.###", Brasil);

    private static List<string> Quebrar(string texto, SKFont fonte, float largura)
    {
        var linhas = new List<string>();
        foreach (var paragrafo in texto.Replace("\r", "").Split('\n'))
        {
            var atual = "";
            foreach (var palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tentativa = atual.Length == 0 ? palavra : $"{atual} {palavra}";
                if (fonte.MeasureText(tentativa) <= largura || atual.Length == 0)
                    atual = tentativa;
                else
                {
                    linhas.Add(atual);
                    atual = palavra;
                }
            }
            linhas.Add(atual);
        }
        return linhas;
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
