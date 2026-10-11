using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Indicadores;

/// <summary>
/// Bloco 37: o valor agregado numa data de status (o fim do dia): o orçamento (ONT), o valor planejado até a data (VP), o
/// valor agregado pelo que foi feito (VA = orçamento de cada tarefa × % concluído) e o custo real (CR, os custos realizados
/// até a data), com os índices de prazo (IDP = VA ÷ VP) e de custo (IDC = VA ÷ CR) e as projeções.
/// </summary>
/// <param name="Base">"Linha de base (LB0)" ou "Cronograma atual": de onde vêm o orçamento e o planejado.</param>
/// <param name="TerminoPrevisto">O término da base.</param>
/// <param name="TerminoAtual">O término do cronograma calculado.</param>
/// <param name="TerminoPelaTendencia">O término se o ritmo (IDP) continuar: a duração prevista ÷ IDP; nulo sem IDP.</param>
public sealed record IndicadoresDeValorAgregado(
    DateOnly Data,
    string Base,
    decimal Orcamento,
    decimal ValorPlanejado,
    decimal ValorAgregado,
    decimal CustoReal,
    DateOnly? TerminoPrevisto,
    DateOnly? TerminoAtual,
    DateOnly? TerminoPelaTendencia)
{
    /// <summary>Índice de desempenho de prazo (VA ÷ VP): abaixo de 1, atrasado; nulo sem nada planejado.</summary>
    public double? Idp => ValorPlanejado == 0 ? null : (double)(ValorAgregado / ValorPlanejado);

    /// <summary>Índice de desempenho de custo (VA ÷ CR): abaixo de 1, gastando mais que o orçado pelo feito; nulo sem custo.</summary>
    public double? Idc => CustoReal == 0 ? null : (double)(ValorAgregado / CustoReal);

    /// <summary>Variação de prazo em R$ (VA − VP).</summary>
    public decimal VariacaoDePrazo => ValorAgregado - ValorPlanejado;

    /// <summary>Variação de custo em R$ (VA − CR).</summary>
    public decimal VariacaoDeCusto => ValorAgregado - CustoReal;

    /// <summary>Estimativa no término (ENT = ONT ÷ IDC); nula sem IDC.</summary>
    public decimal? EstimativaNoTermino => Idc is > 0 ? Math.Round(Orcamento / (decimal)Idc.Value, 2) : null;

    /// <summary>Estimativa para terminar (ENT − CR).</summary>
    public decimal? EstimativaParaTerminar => EstimativaNoTermino - CustoReal;

    /// <summary>Variação no término (ONT − ENT): negativa, a obra vai custar mais que o orçado.</summary>
    public decimal? VariacaoNoTermino => Orcamento - EstimativaNoTermino;

    /// <summary>O % físico previsto até a data (VP ÷ ONT).</summary>
    public double FisicoPrevisto => Orcamento == 0 ? 0 : (double)(ValorPlanejado / Orcamento) * 100;

    /// <summary>O % físico realizado (VA ÷ ONT).</summary>
    public double FisicoRealizado => Orcamento == 0 ? 0 : (double)(ValorAgregado / Orcamento) * 100;

    /// <summary>O custo real em % do orçamento.</summary>
    public double CustoPercentual => Orcamento == 0 ? 0 : (double)(CustoReal / Orcamento) * 100;
}

/// <summary>Uma etapa (tarefa do 1º nível) no painel: o orçamento, o planejado, o agregado, o medido e o custo real.</summary>
public sealed record EtapaNoPainel(string Edt, string Nome, decimal Orcamento, decimal ValorPlanejado, decimal ValorAgregado, decimal Medido, decimal CustoReal)
{
    public double Previsto => Orcamento == 0 ? 0 : (double)(ValorPlanejado / Orcamento) * 100;

    public double Realizado => Orcamento == 0 ? 0 : (double)(ValorAgregado / Orcamento) * 100;

    public double PercentualMedido => Orcamento == 0 ? 0 : (double)(Medido / Orcamento) * 100;

    /// <summary>VA − VP: negativo, a etapa está atrás do planejado.</summary>
    public decimal Desvio => ValorAgregado - ValorPlanejado;
}

/// <summary>Uma tarefa atrasada no painel: o % que devia ter e o que tem, o término e o desvio da linha de base.</summary>
public sealed record AtrasoNoPainel(string Edt, string Nome, int PercentualPrevisto, int PercentualConcluido, DateOnly Termino, int? DesvioDoTermino)
{
    public int Diferenca => PercentualPrevisto - PercentualConcluido;
}

/// <summary>
/// Bloco 37: os indicadores do projeto numa data de status: o valor agregado, as etapas, as tarefas mais atrasadas, o
/// contrato (medido × saldo) e a Curva S física e financeira (prevista × atual × medida × custo real, com o realizado).
/// </summary>
/// <param name="CustosGerais">Custos realizados sem tarefa (ou de tarefas que saíram) até a data.</param>
public sealed record PainelDeIndicadores(
    IndicadoresDeValorAgregado ValorAgregado,
    IReadOnlyList<EtapaNoPainel> Etapas,
    decimal CustosGerais,
    IReadOnlyList<AtrasoNoPainel> Atrasos,
    int TotalDeAtrasadas,
    decimal ValorContratado,
    decimal Medido,
    decimal Retido,
    int MedicoesAprovadas,
    CurvaS Curva)
{
    public DateOnly Data => ValorAgregado.Data;

    /// <summary>O % do contrato medido até a data.</summary>
    public double FinanceiroMedido => ValorContratado == 0 ? 0 : (double)(Medido / ValorContratado) * 100;

    public decimal Saldo => ValorContratado - Medido;

    /// <summary>As tarefas têm valor (sem valor, não há valor agregado nem financeiro).</summary>
    public bool TemValores => ValorAgregado.Orcamento > 0;

    /// <summary>Quantas tarefas atrasadas o painel mostra (as de maior diferença entre o previsto e o feito).</summary>
    public const int AtrasosNoPainel = 5;

    /// <summary>Monta o painel. A Curva S usa a periodicidade escolhida, no intervalo do projeto.</summary>
    /// <exception cref="DomainValidationException">Cronograma não calculado, ou períodos demais.</exception>
    public static PainelDeIndicadores Montar(Cronograma cronograma, DateOnly data, Periodicidade periodicidade, IEnumerable<DateOnly>? cortes = null)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        if (cronograma.Tarefas.Any(t => t.InicioMaisCedo is null))
            throw new DomainValidationException(nameof(Cronograma.Tarefas), "Calcule o cronograma antes dos indicadores.");
        var (itens, nomeDaBase) = Itens(cronograma);
        var etapaDe = EtapaDe(cronograma);
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);

        // Valor agregado: cada item com o orçamento da base, a parte planejada até a data e o % concluído de hoje.
        var etapas = new Dictionary<(string Edt, string Nome), (decimal Orcamento, decimal Vp, decimal Va, decimal Medido, decimal Custo)>();
        foreach (var e in cronograma.Tarefas.Where(t => t.TarefaPaiId is null).Select(t => etapaDe[t.Id]))
            etapas[e] = default;
        decimal orcamento = 0, vp = 0, va = 0;
        foreach (var item in itens)
        {
            var planejado = item.Valor * (decimal)FracaoPlanejada(item, data);
            var agregado = item.Valor * (porId.GetValueOrDefault(item.TarefaId)?.PercentualConcluido ?? 0) / 100m;
            (orcamento, vp, va) = (orcamento + item.Valor, vp + planejado, va + agregado);
            var (o, p, a, m, c) = etapas.GetValueOrDefault(item.Etapa);
            etapas[item.Etapa] = (o + item.Valor, p + planejado, a + agregado, m, c);
        }

        // O medido (aprovadas até a data) e o custo real, por etapa.
        var aprovadas = cronograma.Contrato.Medicoes.Where(m => m.Travada && m.Ate <= data).ToList();
        foreach (var linha in aprovadas.SelectMany(m => m.Linhas!).Where(l => !l.EhGrupo && l.TarefaId is { } id && etapaDe.ContainsKey(id)))
        {
            var chave = etapaDe[linha.TarefaId!.Value];
            var (o, p, a, m, c) = etapas.GetValueOrDefault(chave);
            etapas[chave] = (o, p, a, m + linha.ValorNoPeriodo, c);
        }
        decimal custosGerais = 0;
        foreach (var lancamento in cronograma.Custos.Lancamentos.Where(l => l.Data <= data))
        {
            if (lancamento.TarefaId is { } id && etapaDe.TryGetValue(id, out var chave))
            {
                var (o, p, a, m, c) = etapas.GetValueOrDefault(chave);
                etapas[chave] = (o, p, a, m, c + lancamento.Valor);
            }
            else
                custosGerais += lancamento.Valor;
        }

        var custoReal = cronograma.Custos.Ate(data);
        var (terminoPrevisto, terminoAtual) = Terminos(cronograma, itens);
        var indicadores = new IndicadoresDeValorAgregado(data, nomeDaBase, Math.Round(orcamento, 2), Math.Round(vp, 2), Math.Round(va, 2), custoReal,
            terminoPrevisto, terminoAtual, Tendencia(itens, vp, va, terminoPrevisto ?? terminoAtual));

        var (inicio, fim) = CalculadoraFisicoFinanceira.Intervalo(cronograma);
        foreach (var medicao in cronograma.Contrato.Medicoes)
            (inicio, fim) = (medicao.De < inicio ? medicao.De : inicio, medicao.Ate > fim ? medicao.Ate : fim);
        if (data > fim)
            fim = data;
        var curva = CurvaS.Montar(cronograma, PeriodosFinanceiros.Gerar(inicio, fim, periodicidade, cortes), data);

        var andamento = AcompanhamentoDoCronograma.Calcular(cronograma, data.ToDateTime(TimeOnly.MinValue));
        var edt = cronograma.NumeracaoEdt();
        var atrasadas = cronograma.Tarefas
            .Where(t => !t.EhResumo && andamento.TryGetValue(t.Id, out var a) && a.Situacao == SituacaoDaTarefa.Atrasada)
            .Select(t => new AtrasoNoPainel(edt[t.Id], t.Nome, andamento[t.Id].PercentualPrevisto, t.PercentualConcluido,
                DateOnly.FromDateTime(t.TerminoMaisCedo!.Value), DesvioDaLinhaDeBase.DoTermino(t, cronograma.LinhaDeBaseEmUso)))
            .ToList();

        return new PainelDeIndicadores(indicadores,
            [.. etapas.Where(p => p.Value != default).Select(p => new EtapaNoPainel(p.Key.Edt, p.Key.Nome, Math.Round(p.Value.Orcamento, 2), Math.Round(p.Value.Vp, 2), Math.Round(p.Value.Va, 2),
                p.Value.Medido, p.Value.Custo))],
            custosGerais,
            [.. atrasadas.OrderByDescending(a => a.Diferenca).ThenByDescending(a => a.DesvioDoTermino ?? 0).Take(AtrasosNoPainel)],
            atrasadas.Count,
            cronograma.Contrato.ValorContratado,
            aprovadas.Sum(m => m.Resumo!.MedidoNoPeriodo),
            aprovadas.Sum(m => m.Resumo!.Retencao - m.Resumo.Devolucao),
            aprovadas.Count,
            curva);
    }

    private sealed record Item(Guid TarefaId, DateTime Inicio, DateTime Termino, int Dias, decimal Valor, (string Edt, string Nome) Etapa);

    // A etapa (tarefa do 1º nível) de cada tarefa, pela estrutura atual.
    private static Dictionary<Guid, (string Edt, string Nome)> EtapaDe(Cronograma cronograma)
    {
        var edt = cronograma.NumeracaoEdt();
        var etapa = new Dictionary<Guid, (string, string)>();
        foreach (var tarefa in cronograma.Tarefas) // em pré-ordem, o pai vem antes
            etapa[tarefa.Id] = tarefa.TarefaPaiId is { } pai ? etapa[pai] : (edt[tarefa.Id], tarefa.Nome);
        return etapa;
    }

    // Os itens da base: com linha de base em uso, as tarefas dela (sem os resumos); senão, as do cronograma atual.
    private static (List<Item> Itens, string Base) Itens(Cronograma cronograma)
    {
        var etapaDe = EtapaDe(cronograma);
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        if (cronograma.LinhaDeBaseEmUso is not { } linha)
            return ([.. cronograma.Tarefas.Where(t => !t.EhResumo).Select(t => new Item(t.Id, t.InicioMaisCedo!.Value, t.TerminoMaisCedo!.Value, t.Duracao,
                t.Valor ?? 0, etapaDe[t.Id]))], "Cronograma atual");
        var removidas = ("—", "Tarefas removidas depois da linha de base");
        return ([.. linha.Tarefas
            .Where(t => !(t.Value.EhResumo ?? porId.GetValueOrDefault(t.Key)?.EhResumo ?? false))
            .Select(t => new Item(t.Key, t.Value.Inicio, t.Value.Termino, t.Value.Duracao, t.Value.Valor ?? 0,
                etapaDe.TryGetValue(t.Key, out var e) ? e : removidas))], $"Linha de base (LB{linha.Numero})");
    }

    // A parte de um item planejada até o fim do dia: os dias úteis dele até a data ÷ os dias úteis dele (um marco: tudo no dia).
    private static double FracaoPlanejada(Item item, DateOnly data)
    {
        var dia = data.ToDateTime(TimeOnly.MinValue);
        var (inicio, termino) = (item.Inicio.Date, item.Termino.Date);
        if (item.Dias == 0)
            return dia >= inicio ? 1 : 0;
        if (dia < inicio)
            return 0;
        if (dia >= termino)
            return 1;
        var ate = DiasUteis.AnteriorOuMesmo(dia);
        return ate < inicio ? 0 : (double)DiasUteis.ContarInclusivo(inicio, ate) / DiasUteis.ContarInclusivo(inicio, termino);
    }

    private static (DateOnly? Previsto, DateOnly? Atual) Terminos(Cronograma cronograma, IReadOnlyList<Item> itens)
    {
        var atual = cronograma.Tarefas.Select(t => t.TerminoMaisCedo).OfType<DateTime>().Select(DateOnly.FromDateTime).DefaultIfEmpty().Max();
        var previsto = cronograma.LinhaDeBaseEmUso?.Termino is { } termino ? DateOnly.FromDateTime(termino) : (DateOnly?)null;
        return (previsto, itens.Count == 0 ? null : atual);
    }

    // Se o ritmo continuar: a duração prevista (do primeiro dia da base ao término) ÷ IDP, em dias úteis.
    private static DateOnly? Tendencia(IReadOnlyList<Item> itens, decimal vp, decimal va, DateOnly? termino)
    {
        if (vp <= 0 || va <= 0 || termino is not { } fim || itens.Count == 0)
            return null;
        var inicio = itens.Min(i => i.Inicio).Date;
        var duracao = DiasUteis.ContarInclusivo(inicio, DiasUteis.AnteriorOuMesmo(fim.ToDateTime(TimeOnly.MinValue)));
        var projetada = (int)Math.Ceiling(duracao / (double)(va / vp));
        return DiasUteis.Adicionar(inicio, Math.Max(projetada, 1) - 1) is { } data ? DateOnly.FromDateTime(data) : null;
    }
}
