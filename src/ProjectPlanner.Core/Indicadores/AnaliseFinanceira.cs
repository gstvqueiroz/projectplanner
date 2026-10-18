using ProjectPlanner.Core.Fotos;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Indicadores;

/// <summary>
/// Bloco 43: uma etapa (ou o total) do orçado ao realizado: o contratado (o Valor das tarefas), o custo orçado (do
/// orçamento ou dos recursos), o físico, o medido e o custo real, com a margem prevista, o desvio de custo e as projeções.
/// </summary>
/// <param name="CustoOrcado">O custo direto orçado; nulo sem orçamento nem recursos em nenhuma tarefa da etapa.</param>
/// <param name="CustoOrcadoDoFeito">O custo orçado × o % concluído de cada tarefa (o que já devia ter custado).</param>
/// <param name="Fisico">O % físico realizado (as tarefas pesadas pelo Valor, ou pelo custo orçado, ou pela duração).</param>
/// <param name="ContratadoComCusto">
/// O contratado só das tarefas com custo orçado: a margem compara o preço e o custo das mesmas tarefas (uma tarefa sem custo
/// orçado não entra como margem de 100%).
/// </param>
public sealed record EtapaFinanceira(
    string Edt,
    string Nome,
    decimal Contratado,
    decimal? CustoOrcado,
    decimal CustoOrcadoDoFeito,
    double Fisico,
    decimal Medido,
    decimal CustoReal,
    decimal ContratadoComCusto)
{
    /// <summary>Há tarefas com Valor e sem custo orçado (a margem vale só para as outras).</summary>
    public bool CustoParcial => CustoOrcado is not null && ContratadoComCusto < Contratado;

    /// <summary>Contratado (das tarefas com custo orçado) − custo orçado.</summary>
    public decimal? MargemPrevista => CustoOrcado is { } c ? ContratadoComCusto - c : null;

    /// <summary>A margem prevista em % do contratado das tarefas com custo orçado.</summary>
    public double? MargemPrevistaPercentual => MargemPrevista is { } m && ContratadoComCusto != 0 ? (double)(m / ContratadoComCusto) * 100 : null;

    /// <summary>Custo orçado do feito − custo real: negativo, a etapa está gastando mais que o orçado pelo que fez.</summary>
    public decimal? DesvioDeCusto => CustoOrcado is null || CustoReal == 0 ? null : CustoOrcadoDoFeito - CustoReal;

    /// <summary>O índice de custo pelo custo orçado (orçado do feito ÷ real); nulo sem custo real ou nada feito.</summary>
    public double? IndiceDeCusto => CustoOrcado is null || CustoReal == 0 || CustoOrcadoDoFeito == 0 ? null : (double)(CustoOrcadoDoFeito / CustoReal);

    /// <summary>O custo no fim se o desempenho de custo continuar (orçado ÷ índice); sem índice, o orçado (ou o real, se já passou).</summary>
    public decimal? CustoFinalProjetado => CustoOrcado is not { } orcado ? null
        : IndiceDeCusto is { } i ? Math.Round(orcado / (decimal)i, 2) : Math.Max(orcado, CustoReal);

    /// <summary>Contratado (das tarefas com custo orçado) − custo final projetado.</summary>
    public decimal? MargemProjetada => CustoFinalProjetado is { } c ? ContratadoComCusto - c : null;

    /// <summary>Quanto falta gastar pela projeção.</summary>
    public decimal? FaltaGastar => CustoFinalProjetado is { } c ? Math.Max(0, c - CustoReal) : null;

    /// <summary>O % do contratado medido.</summary>
    public double PercentualMedido => Contratado == 0 ? 0 : (double)(Medido / Contratado) * 100;

    /// <summary>Medido − físico, em pontos de %: positivo, medido à frente do executado; negativo, executado a medir.</summary>
    public double DiferencaDaMedicao => Contratado == 0 ? 0 : PercentualMedido - Fisico;

    /// <summary>O executado ainda não medido, em R$ (o físico × o contratado − o medido); zero se medido à frente.</summary>
    public decimal AMedir => Math.Max(0, Math.Round(Contratado * (decimal)Fisico / 100 - Medido, 2));
}

/// <summary>Bloco 43: um período do fluxo de caixa: o previsto (o planejado do contrato), o medido a receber, o custo e os saldos.</summary>
/// <param name="ReceitaPrevista">O valor das tarefas planejado no período (o cronograma ou a linha de base).</param>
/// <param name="Medido">O líquido das medições aprovadas que terminam no período (o que se fatura).</param>
/// <param name="Custo">Os custos realizados no período.</param>
/// <param name="Realizado">O período já começou na data de status (os seguintes só têm o previsto).</param>
public sealed record PeriodoDoCaixa(PeriodoFinanceiro Periodo, decimal ReceitaPrevista, decimal Medido, decimal Custo, decimal SaldoAcumulado, bool Realizado)
{
    public decimal Saldo => Medido - Custo;
}

/// <summary>Bloco 43: o nível de um alerta do semáforo.</summary>
public enum NivelDoAlerta
{
    SemDados,
    Ok,
    Atencao,
    Critico,
}

/// <summary>Bloco 43: um item do semáforo da obra (prazo, custo, medição, recursos, fotos).</summary>
public sealed record AlertaDaObra(string Area, NivelDoAlerta Nivel, string Titulo, string Detalhe);

/// <summary>Bloco 43: o contrato na data: o original, os aditivos, o medido (bruto e líquido) e o retido.</summary>
public sealed record ContratoNaData(decimal ValorOriginal, int Aditivos, decimal ValorDosAditivos, decimal ValorAtual, decimal MedidoBruto,
    decimal Liquido, decimal Retido, int MedicoesAprovadas)
{
    public decimal Saldo => ValorAtual - MedidoBruto;

    /// <summary>Os aditivos em % do valor original.</summary>
    public double AditivosPercentual => ValorOriginal == 0 ? 0 : (double)(ValorDosAditivos / ValorOriginal) * 100;
}

/// <summary>
/// Bloco 43: os indicadores ampliados numa data de status: do orçado ao realizado por etapa (margem prevista, desvio de
/// custo, custo final e margem projetados), as medições contra o físico, o contrato, o fluxo de caixa e o semáforo.
/// </summary>
public sealed record AnaliseFinanceira(
    DateOnly Data,
    IReadOnlyList<EtapaFinanceira> Etapas,
    EtapaFinanceira Total,
    decimal CustosGerais,
    ContratoNaData Contrato,
    IReadOnlyList<PeriodoDoCaixa> Caixa,
    IReadOnlyList<AlertaDaObra> Alertas)
{
    /// <summary>O custo real com os custos gerais (sem tarefa).</summary>
    public decimal CustoRealTotal => Total.CustoReal + CustosGerais;

    /// <summary>Há custo orçado em alguma tarefa (orçamento ou recursos).</summary>
    public bool TemCustoOrcado => Total.CustoOrcado is not null;

    /// <summary>As etapas com maior desvio de custo (as que gastam mais que o orçado pelo feito), no máximo 5.</summary>
    public IReadOnlyList<EtapaFinanceira> MaioresDesvios =>
        [.. Etapas.Where(e => e.DesvioDeCusto is < 0).OrderBy(e => e.DesvioDeCusto).Take(5)];

    // ---- Limites do semáforo ----

    public const double IdpAtencao = 0.95, IdpCritico = 0.85;
    public const double IndiceDeCustoAtencao = 0.97, IndiceDeCustoCritico = 0.90;

    /// <summary>Pontos de % entre o medido e o físico de uma etapa para o alerta.</summary>
    public const double DiferencaDaMedicaoAtencao = 5, DiferencaDaMedicaoCritica = 10;

    /// <summary>Quantos dias à frente o alerta de recursos olha.</summary>
    public const int DiasDosRecursos = 30;

    /// <summary>Monta a análise (o cronograma calculado; o painel do Bloco 37 vem junto para o prazo).</summary>
    /// <exception cref="DomainValidationException">Cronograma não calculado, ou períodos demais.</exception>
    public static AnaliseFinanceira Montar(Cronograma cronograma, PainelDeIndicadores painel, Periodicidade periodicidade)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(painel);
        var data = painel.Data;
        var edt = cronograma.NumeracaoEdt();
        var etapaDe = new Dictionary<Guid, Tarefa>();
        foreach (var t in cronograma.Tarefas) // em pré-ordem, o pai vem antes
            etapaDe[t.Id] = t.TarefaPaiId is { } pai ? etapaDe[pai] : t;

        // Por tarefa: contratado, custo orçado, peso para o físico.
        var porEtapa = cronograma.Tarefas.Where(t => t.TarefaPaiId is null).ToDictionary(t => t.Id, _ => new Acumulador());
        foreach (var t in cronograma.Tarefas.Where(t => !t.EhResumo))
        {
            var a = porEtapa[etapaDe[t.Id].Id];
            var custo = CustoOrcado(cronograma, t);
            var peso = t.Valor is > 0 ? t.Valor.Value : custo is > 0 ? custo.Value : Math.Max(t.Duracao, 1);
            a.Contratado += t.Valor ?? 0;
            if (custo is { } c)
            {
                a.CustoOrcado = (a.CustoOrcado ?? 0) + c;
                a.ContratadoComCusto += t.Valor ?? 0;
                a.CustoOrcadoDoFeito += c * t.PercentualConcluido / 100m;
            }
            a.Peso += peso;
            a.Feito += peso * t.PercentualConcluido / 100m;
        }

        // Medido (aprovadas até a data) e custo real, por etapa.
        var aprovadas = cronograma.Contrato.Medicoes.Where(m => m.Travada && m.Ate <= data).ToList();
        foreach (var linha in aprovadas.SelectMany(m => m.Linhas!).Where(l => !l.EhGrupo && l.TarefaId is { } id && etapaDe.ContainsKey(id)))
            porEtapa[etapaDe[linha.TarefaId!.Value].Id].Medido += linha.ValorNoPeriodo;
        decimal custosGerais = 0;
        foreach (var l in cronograma.Custos.Lancamentos.Where(l => l.Data <= data))
            if (l.TarefaId is { } id && etapaDe.TryGetValue(id, out var etapa))
                porEtapa[etapa.Id].CustoReal += l.Valor;
            else
                custosGerais += l.Valor;

        var etapas = cronograma.Tarefas.Where(t => t.TarefaPaiId is null)
            .Select(t => porEtapa[t.Id].Etapa(edt[t.Id], t.Nome))
            .Where(e => e.Contratado != 0 || e.CustoOrcado is not null || e.Medido != 0 || e.CustoReal != 0)
            .ToList();
        var soma = new Acumulador();
        foreach (var a in porEtapa.Values)
            soma.Somar(a);
        var total = soma.Etapa("", "Total");

        var contrato = ContratoNa(cronograma, data, aprovadas);
        var caixa = CaixaPorPeriodo(cronograma, painel, periodicidade);
        var analise = new AnaliseFinanceira(data, etapas, total, custosGerais, contrato, caixa, []);
        return analise with { Alertas = Alertar(cronograma, painel, analise) };
    }

    /// <summary>
    /// O custo orçado da tarefa: pelo orçamento (a quantidade × o custo direto da composição), senão pelos recursos atribuídos;
    /// nulo sem nenhum dos dois.
    /// </summary>
    public static decimal? CustoOrcado(Cronograma cronograma, Tarefa tarefa)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefa);
        if (cronograma.Orcamento.ComposicaoDe(tarefa.Id) is { } c && tarefa.Quantidade is { } q)
            return Math.Round(q * c.CustoUnitario, 2, MidpointRounding.AwayFromZero);
        return cronograma.Recursos.CustoOrcado(tarefa);
    }

    private sealed class Acumulador
    {
        public decimal Contratado, CustoOrcadoDoFeito, Medido, CustoReal, Peso, Feito, ContratadoComCusto;
        public decimal? CustoOrcado;

        public void Somar(Acumulador a)
        {
            (Contratado, CustoOrcadoDoFeito, Medido, CustoReal, Peso, Feito, ContratadoComCusto) =
                (Contratado + a.Contratado, CustoOrcadoDoFeito + a.CustoOrcadoDoFeito, Medido + a.Medido, CustoReal + a.CustoReal, Peso + a.Peso, Feito + a.Feito,
                 ContratadoComCusto + a.ContratadoComCusto);
            if (a.CustoOrcado is { } c)
                CustoOrcado = (CustoOrcado ?? 0) + c;
        }

        public EtapaFinanceira Etapa(string edt, string nome) => new(edt, nome, Math.Round(Contratado, 2), CustoOrcado is { } c ? Math.Round(c, 2) : null,
            Math.Round(CustoOrcadoDoFeito, 2), Peso == 0 ? 0 : (double)(Feito / Peso) * 100, Math.Round(Medido, 2), Math.Round(CustoReal, 2),
            Math.Round(ContratadoComCusto, 2));
    }

    private static ContratoNaData ContratoNa(Cronograma cronograma, DateOnly data, IReadOnlyList<Medicoes.Medicao> aprovadas)
    {
        var c = cronograma.Contrato;
        var aditivos = c.Aditivos.Where(a => a.Data <= data).ToList();
        var original = c.ValorContratado - c.ValorDosAditivos;
        var valorDosAditivos = aditivos.Sum(a => a.Valor);
        return new ContratoNaData(original, aditivos.Count, valorDosAditivos, original + valorDosAditivos,
            aprovadas.Sum(m => m.Resumo!.MedidoNoPeriodo), aprovadas.Sum(m => m.Resumo!.Liquido),
            aprovadas.Sum(m => m.Resumo!.Retencao - m.Resumo.Devolucao), aprovadas.Count);
    }

    // O caixa por período: o previsto vem da curva prevista (o valor planejado do período), o medido do líquido das medições
    // aprovadas que terminam nele, o custo dos lançamentos. O saldo acumulado só conta o realizado (até a data).
    private static List<PeriodoDoCaixa> CaixaPorPeriodo(Cronograma cronograma, PainelDeIndicadores painel, Periodicidade periodicidade)
    {
        var data = painel.Data;
        var (inicio, fim) = CalculadoraFisicoFinanceira.Intervalo(cronograma);
        foreach (var m in cronograma.Contrato.Medicoes)
            (inicio, fim) = (m.De < inicio ? m.De : inicio, m.Ate > fim ? m.Ate : fim);
        foreach (var l in cronograma.Custos.Lancamentos)
            (inicio, fim) = (l.Data < inicio ? l.Data : inicio, l.Data > fim ? l.Data : fim);
        if (data > fim)
            fim = data;
        var periodos = PeriodosFinanceiros.Gerar(inicio, fim, periodicidade);
        var series = CurvaS.Montar(cronograma, periodos, data).Series;
        var previsto = series.FirstOrDefault(s => s.Tipo == TipoDaSerie.Prevista) ?? series.FirstOrDefault(s => s.Tipo == TipoDaSerie.Atual);
        decimal Planejado(int i) => previsto is null || i >= previsto.ValorAcumulado.Count ? 0
            : previsto.ValorAcumulado[i] - (i == 0 ? 0 : previsto.ValorAcumulado[i - 1]);
        var aprovadas = cronograma.Contrato.Medicoes.Where(m => m.Travada && m.Ate <= data).ToList();
        var lista = new List<PeriodoDoCaixa>();
        decimal saldo = 0;
        for (var i = 0; i < periodos.Count; i++)
        {
            var p = periodos[i];
            var realizado = p.Inicio <= data;
            var medido = aprovadas.Where(m => m.Ate >= p.Inicio && m.Ate <= p.Fim).Sum(m => m.Resumo!.Liquido);
            var custo = cronograma.Custos.Lancamentos.Where(l => l.Data >= p.Inicio && l.Data <= p.Fim && l.Data <= data).Sum(l => l.Valor);
            if (realizado)
                saldo += medido - custo;
            lista.Add(new PeriodoDoCaixa(p, Planejado(i), medido, custo, saldo, realizado));
        }
        return lista;
    }

    private static IReadOnlyList<AlertaDaObra> Alertar(Cronograma cronograma, PainelDeIndicadores painel, AnaliseFinanceira a)
    {
        static string Indice(double v) => v.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        static string Reais(decimal v) => Moeda.ComSimbolo(v);
        var alertas = new List<AlertaDaObra>();

        // Prazo: o IDP, as atrasadas e o término pela tendência.
        var va = painel.ValorAgregado;
        if (va.Idp is not { } idp)
            alertas.Add(new("Prazo", painel.TotalDeAtrasadas > 0 ? NivelDoAlerta.Atencao : NivelDoAlerta.SemDados,
                painel.TotalDeAtrasadas > 0 ? $"{painel.TotalDeAtrasadas} tarefa(s) atrasada(s)" : "Sem valores para o IDP",
                "Informe o Valor das tarefas para medir o prazo pelo valor agregado."));
        else
        {
            var nivel = idp < IdpCritico ? NivelDoAlerta.Critico : idp < IdpAtencao ? NivelDoAlerta.Atencao : NivelDoAlerta.Ok;
            var termino = va.TerminoPelaTendencia is { } t && (va.TerminoPrevisto ?? va.TerminoAtual) is { } previsto && t > previsto
                ? $" No ritmo atual, termina em {t:dd/MM/yyyy} (previsto {previsto:dd/MM/yyyy})." : "";
            alertas.Add(new("Prazo", nivel, $"IDP {Indice(idp)}" + (painel.TotalDeAtrasadas > 0 ? $" · {painel.TotalDeAtrasadas} atrasada(s)" : ""),
                (nivel == NivelDoAlerta.Ok ? "Dentro do planejado." : "O feito está abaixo do planejado para a data.") + termino));
        }

        // Custo: o índice pelo custo orçado e a margem projetada.
        var total = a.Total;
        if (total.CustoOrcado is null)
            alertas.Add(new("Custo", NivelDoAlerta.SemDados, "Sem custo orçado",
                "Faça o orçamento (Projeto > Orçamento…) ou atribua recursos para comparar o custo real com o orçado."));
        else if (total.IndiceDeCusto is not { } ic)
            alertas.Add(new("Custo", NivelDoAlerta.SemDados, "Sem custo real",
                "Lance os custos realizados (Projeto > Custos realizados…) para comparar com o orçado."));
        else
        {
            var nivel = ic < IndiceDeCustoCritico || total.MargemProjetada < 0 ? NivelDoAlerta.Critico
                : ic < IndiceDeCustoAtencao ? NivelDoAlerta.Atencao : NivelDoAlerta.Ok;
            var piores = a.MaioresDesvios.Take(2).Select(e => $"{e.Edt} {e.Nome} ({Reais(e.DesvioDeCusto!.Value)})").ToList();
            alertas.Add(new("Custo", nivel, $"Índice de custo {Indice(ic)}",
                $"Custo final projetado {Reais(total.CustoFinalProjetado!.Value)}; margem projetada {Reais(total.MargemProjetada!.Value)}" +
                (total.CustoParcial ? $" (sobre os {Reais(total.ContratadoComCusto)} com custo orçado)." : ".") +
                (piores.Count > 0 ? $" Maiores desvios: {string.Join("; ", piores)}." : "")));
        }

        // Medição: o medido contra o físico, por etapa.
        if (a.Contrato.MedicoesAprovadas == 0)
            alertas.Add(new("Medição", NivelDoAlerta.SemDados, "Nenhuma medição aprovada", "As medições aparecem aqui depois do primeiro boletim aprovado."));
        else
        {
            var frente = a.Etapas.Where(e => e.DiferencaDaMedicao > DiferencaDaMedicaoAtencao).OrderByDescending(e => e.DiferencaDaMedicao).ToList();
            var aMedir = a.Etapas.Sum(e => e.AMedir);
            var maior = frente.FirstOrDefault();
            var nivel = maior?.DiferencaDaMedicao > DiferencaDaMedicaoCritica ? NivelDoAlerta.Critico
                : maior is not null || a.Etapas.Any(e => e.DiferencaDaMedicao < -DiferencaDaMedicaoCritica) ? NivelDoAlerta.Atencao : NivelDoAlerta.Ok;
            var detalhe = maior is not null
                ? $"Medido à frente do executado em {frente.Count} etapa(s), como {maior.Edt} {maior.Nome} ({maior.DiferencaDaMedicao:0} pontos)." +
                  (aMedir > 0 ? $" Em outras, {Reais(aMedir)} executado a medir." : "")
                : aMedir > 0 ? "Há serviço executado ainda não medido: inclua na próxima medição." : "Medido acompanha o executado.";
            var titulo = maior is not null ? $"Medido à frente em {frente.Count} etapa(s)"
                : aMedir > 0 ? $"{Reais(aMedir)} executado a medir" : "Medição em dia";
            alertas.Add(new("Medição", nivel, titulo,
                detalhe + (a.Contrato.Aditivos > 0 ? $" Aditivos: {a.Contrato.Aditivos} ({a.Contrato.AditivosPercentual:0.#}% do original)." : "")));
        }

        // Recursos: superalocações nos próximos dias.
        if (cronograma.Recursos.Vazio)
            alertas.Add(new("Recursos", NivelDoAlerta.SemDados, "Sem recursos", "Cadastre e atribua recursos (Projeto > Recursos…) para ver as superalocações."));
        else
        {
            var ate = a.Data.AddDays(DiasDosRecursos);
            var proximas = UsoDeRecursos.Superalocacoes(cronograma).Where(s => s.Ate >= a.Data && s.De <= ate).ToList();
            alertas.Add(proximas.Count == 0
                ? new("Recursos", NivelDoAlerta.Ok, "Sem superalocação", $"Nenhum recurso passa do disponível nos próximos {DiasDosRecursos} dias.")
                : new("Recursos", NivelDoAlerta.Atencao, $"{proximas.Count} superalocação(ões)",
                    $"Nos próximos {DiasDosRecursos} dias: {string.Join("; ", proximas.Take(3).Select(s => $"{s.Recurso.Nome} de {s.De:dd/MM} a {s.Ate:dd/MM}"))}."));
        }

        // Fotos: tarefas em andamento sem foto recente.
        var semFoto = AlertaDeFotos.Calcular(cronograma, a.Data.ToDateTime(TimeOnly.MinValue));
        alertas.Add(cronograma.ConfiguracaoDasFotos.DiasSemFotoParaAlerta == 0
            ? new("Fotos", NivelDoAlerta.SemDados, "Alerta de fotos desligado", "Ligue em Fotos do projeto > Alerta.")
            : semFoto.Count == 0
                ? new("Fotos", NivelDoAlerta.Ok, "Fotos em dia", "Todas as tarefas em andamento têm foto recente.")
                : new("Fotos", NivelDoAlerta.Atencao, $"{semFoto.Count} tarefa(s) sem foto recente",
                    string.Join("; ", semFoto.Take(3).Select(s => $"{s.Edt} {s.Tarefa.Nome} ({s.Dias} dias)")) + "."));
        return alertas;
    }
}
