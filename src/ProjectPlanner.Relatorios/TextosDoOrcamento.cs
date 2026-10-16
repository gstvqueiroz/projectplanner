using System.Globalization;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Relatorios;

/// <summary>Bloco 33: os textos do orçamento, iguais no PDF, no Word e no Excel.</summary>
public static class TextosDoOrcamento
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly string[] Colunas = ["Item", "Código", "Fonte", "Descrição", "Unid.", "Quant.", "Custo unit.", "Preço unit.", "Total"];

    public static readonly string[] ColunasDaAnalitica = ["Código", "Insumo", "Tipo", "Unid.", "Coeficiente", "Preço", "Parcial"];

    public static string Percentual(decimal valor) => valor.ToString("0.##", Brasil) + "%";

    /// <summary>"BDI 25% (materiais 15%)".</summary>
    public static string Bdi(ConfiguracaoDoOrcamento c) =>
        $"BDI {Percentual(c.Bdi)}" + (c.BdiDeMateriais is { } m ? $" (materiais {Percentual(m)})" : "");

    public static string[] Celulas(LinhaDoOrcamento l) =>
    [
        l.Edt, l.SemComposicao ? "" : l.Codigo, l.SemComposicao ? "informado" : l.Fonte, new string(' ', Math.Min(l.Nivel, 6) * 2) + l.Descricao, l.Unidade,
        l.Quantidade is { } q ? Moeda.FormatarQuantidade(q) : "", l.CustoUnitario is { } c ? Moeda.Formatar(c) : "",
        l.PrecoUnitario is { } p ? Moeda.Formatar(p) : "", Moeda.Formatar(l.Total),
    ];

    public static IEnumerable<(string Rotulo, string Valor, bool Forte)> Totais(PlanilhaOrcamentaria p)
    {
        yield return ("Custo direto (composições)", Moeda.ComSimbolo(p.CustoDireto), false);
        yield return (Bdi(p.Configuracao), Moeda.ComSimbolo(p.ValorDoBdi), false);
        if (p.Informado > 0)
            yield return ("Tarefas sem composição (valor informado)", Moeda.ComSimbolo(p.Informado), false);
        yield return ("Total do orçamento", Moeda.ComSimbolo(p.Total), true);
    }

    /// <summary>"Preços: SINAPI SP 09/2026 desonerado · BDI 25%".</summary>
    public static string Rodape(PlanilhaOrcamentaria p) =>
        (p.Bases.Count == 0 ? "" : "Preços: " + string.Join(", ", p.Bases) + " · ") + Bdi(p.Configuracao);

    /// <summary>Os serviços com composição, na ordem da planilha, com a composição de cada um.</summary>
    public static IEnumerable<(LinhaDoOrcamento Linha, ComposicaoAberta Composicao)> Analitica(PlanilhaOrcamentaria p, IReadOnlyDictionary<Guid, ComposicaoAberta> composicoes) =>
        p.Servicos.Where(l => !l.SemComposicao && l.TarefaId is { } id && composicoes.ContainsKey(id)).Select(l => (l, composicoes[l.TarefaId!.Value]));

    /// <summary>"1.2 · SINAPI 87292 — ARGAMASSA… (m³) · custo 52,53 · preço 63,69".</summary>
    public static string TituloDaComposicao(LinhaDoOrcamento l, ComposicaoAberta c) =>
        $"{l.Edt} {l.Descricao.Trim()} — {c.Base.Fonte} {c.Codigo}: {c.Descricao} ({c.Unidade}) · custo {Moeda.Formatar(c.CustoUnitario)} · preço com BDI {Moeda.Formatar(l.PrecoUnitario ?? 0)}";

    public static string[] CelulasDoInsumo(InsumoNaComposicao i) =>
    [
        i.Codigo, i.Descricao, TiposDeInsumo.Texto(i.Tipo), i.Unidade, i.Coeficiente.ToString("0.0000###", Brasil), Moeda.Formatar(i.Preco),
        Moeda.Formatar(Math.Round(i.Parcial, 2, MidpointRounding.AwayFromZero)),
    ];
}
