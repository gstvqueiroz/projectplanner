using System.Globalization;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Relatorios;

/// <summary>Bloco 36: os textos do boletim de medição, iguais no PDF, no Word e no Excel.</summary>
public static class TextosDoBoletim
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>As colunas da tabela de itens.</summary>
    public static readonly string[] Colunas =
    [
        "Item", "Descrição", "Unid.", "Qtd. contratada", "Preço unit. (R$)", "Valor contratado (R$)", "Qtd. anterior", "Qtd. no período",
        "Qtd. acumulada", "Saldo (qtd.)", "No período (R$)", "Acumulado (R$)", "Saldo (R$)", "% acum.",
    ];

    /// <summary>Colunas de quantidade (vazias num grupo).</summary>
    public static readonly int[] DeQuantidade = [2, 3, 4, 6, 7, 8, 9];

    public static string Titulo(Medicao medicao) => $"BOLETIM DE MEDIÇÃO Nº {medicao.Numero:00}";

    /// <summary>"Medição nº 01 · Residência Silva · 28/09/2026 a 16/10/2026".</summary>
    public static string Cabecalho(BoletimDeMedicao b, CabecalhoDoRelatorio c) => $"{b.Medicao.Rotulo} · {c.Obra} · {b.Medicao.Periodo}";

    /// <summary>Os dados do topo: a obra, o contrato, o período, a situação e quem emite (só os que há).</summary>
    public static IReadOnlyList<(string Rotulo, string Valor)> Dados(BoletimDeMedicao b, CabecalhoDoRelatorio c, DateTime geradoEm)
    {
        var dados = new List<(string, string?)>
        {
            ("Contratante", c.DadosDaObra.Contratante),
            ("Contrato", c.DadosDaObra.Contrato),
            ("Endereço da obra", c.DadosDaObra.Endereco),
            ("Período", b.Medicao.Periodo),
            ("Situação", b.Medicao.TextoDaSituacao),
            ("Emitido em", geradoEm.ToString("dd/MM/yyyy HH:mm", Brasil)),
            ("Responsável técnico", string.Join(" · ", new[] { c.Responsavel, c.Registro }.OfType<string>()) is { Length: > 0 } r ? r : null),
        };
        return [.. dados.Where(d => d.Item2 is not null).Select(d => (d.Item1, d.Item2!))];
    }

    public static string Quantidade(decimal valor) => Moeda.FormatarQuantidade(valor);

    public static string Percentual(double valor) => valor.ToString("0.0", Brasil) + "%";

    /// <summary>A linha em textos, coluna a coluna (num grupo, só os valores).</summary>
    public static string[] Celulas(LinhaDoBoletim l)
    {
        var recuo = new string(' ', Math.Min(l.Nivel, 6) * 2);
        var descricao = recuo + l.Descricao + (l.Removida && !l.EhGrupo ? " (fora do cronograma)" : "");
        if (l.EhGrupo)
            return [l.Edt, descricao, "", "", "", Moeda.Formatar(l.ValorContratado), "", "", "", "", Moeda.Formatar(l.ValorNoPeriodo),
                Moeda.Formatar(l.ValorAcumulado), Moeda.Formatar(l.ValorSaldo), Percentual(l.PercentualAcumulado)];
        return [l.Edt, descricao, l.Unidade, Quantidade(l.QuantidadeContratada), Moeda.Formatar(l.PrecoUnitario), Moeda.Formatar(l.ValorContratado),
            Quantidade(l.QuantidadeAnterior), Quantidade(l.QuantidadeNoPeriodo), Quantidade(l.QuantidadeAcumulada), Quantidade(l.QuantidadeSaldo),
            Moeda.Formatar(l.ValorNoPeriodo), Moeda.Formatar(l.ValorAcumulado), Moeda.Formatar(l.ValorSaldo), Percentual(l.PercentualAcumulado)];
    }

    /// <summary>A linha de total da tabela.</summary>
    public static string[] Total(ResumoDoBoletim r) =>
    [
        "", "Total", "", "", "", Moeda.Formatar(r.ValorContratado), "", "", "", "", Moeda.Formatar(r.MedidoNoPeriodo), Moeda.Formatar(r.MedidoAcumulado),
        Moeda.Formatar(r.Saldo), Percentual(r.PercentualMedido),
    ];

    /// <summary>O resumo financeiro: do contrato ao valor a faturar (só as linhas que se aplicam); a última é a de destaque.</summary>
    public static IReadOnlyList<(string Rotulo, string Valor, bool Destaque)> Resumo(ResumoDoBoletim r)
    {
        var linhas = new List<(string, string, bool)>
        {
            ("Valor original do contrato", Moeda.ComSimbolo(r.ValorOriginal), false),
        };
        if (r.QuantidadeDeAditivos > 0)
        {
            linhas.Add((r.QuantidadeDeAditivos == 1 ? "Aditivo (1)" : $"Aditivos ({r.QuantidadeDeAditivos})", ComSinal(r.ValorDosAditivos), false));
            linhas.Add(("Valor atual do contrato", Moeda.ComSimbolo(r.ValorContratado), false));
        }
        linhas.Add(("Medido nas medições anteriores", Moeda.ComSimbolo(r.MedidoAnterior), false));
        linhas.Add(("Medido nesta medição", Moeda.ComSimbolo(r.MedidoNoPeriodo), true));
        linhas.Add(("Medido acumulado", $"{Moeda.ComSimbolo(r.MedidoAcumulado)} ({Percentual(r.PercentualMedido)})", false));
        linhas.Add(("Saldo do contrato", Moeda.ComSimbolo(r.Saldo), false));
        if (r.Fisico is { } fisico)
            linhas.Add(("Avanço físico do cronograma", Percentual(fisico), false));
        if (r.DescricaoDoReajuste is not null || r.Reajuste != 0)
            linhas.Add(($"Reajuste{(r.DescricaoDoReajuste is { } d ? $" ({d})" : "")}", ComSinal(r.Reajuste), false));
        if (r.Glosas > 0)
            linhas.Add(("Glosas", ComSinal(-r.Glosas), false));
        if (r.Reajuste != 0 || r.Glosas > 0)
            linhas.Add(("Valor bruto", Moeda.ComSimbolo(r.Bruto), false));
        if (r.PercentualDeRetencao > 0 || r.Retencao > 0)
            linhas.Add(($"Retenção ({r.PercentualDeRetencao.ToString("0.##", Brasil)}%)", ComSinal(-r.Retencao), false));
        if (r.Devolucao > 0)
            linhas.Add(("Devolução da retenção", ComSinal(r.Devolucao), false));
        linhas.Add(("Valor líquido a faturar", Moeda.ComSimbolo(r.Liquido), true));
        if (r.RetidoAcumulado > 0)
            linhas.Add(("Retido acumulado", Moeda.ComSimbolo(r.RetidoAcumulado), false));
        return linhas;
    }

    public static string ComSinal(decimal valor) => (valor < 0 ? "− " : "+ ") + Moeda.ComSimbolo(Math.Abs(valor));

    public static string NomeDoArquivo(BoletimDeMedicao b, string obra) => $"Boletim de medição {b.Medicao.Numero:00} - {obra}";
}
