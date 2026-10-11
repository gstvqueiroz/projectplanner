using System.Globalization;

namespace ProjectPlanner.Core.Planejamento;

/// <summary>De quanto em quanto tempo é cada período do cronograma físico-financeiro (e, depois, de cada medição).</summary>
public enum Periodicidade
{
    /// <summary>Semanas de segunda a domingo.</summary>
    Semanal,

    /// <summary>Do dia 1 ao 15 e do 16 ao fim do mês.</summary>
    Quinzenal,

    /// <summary>Meses do calendário.</summary>
    Mensal,

    /// <summary>Datas de corte escolhidas pelo usuário (cada data é o último dia de um período).</summary>
    DatasDeCorte,
}

/// <summary>Um período do físico-financeiro, com os dias inclusivos.</summary>
public sealed record PeriodoFinanceiro(int Numero, DateOnly Inicio, DateOnly Fim)
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"out/26" num mês inteiro; senão, "05/10–11/10".</summary>
    public string Rotulo => Inicio.Day == 1 && Fim == new DateOnly(Inicio.Year, Inicio.Month, 1).AddMonths(1).AddDays(-1)
        ? $"{Inicio.ToString("MMM", Brasil).TrimEnd('.')}/{Inicio:yy}"
        : $"{Inicio:dd/MM}–{Fim:dd/MM}";

    public override string ToString() => Rotulo;
}

/// <summary>Bloco 35: monta os períodos de um intervalo.</summary>
public static class PeriodosFinanceiros
{
    /// <summary>Quantos períodos uma tabela pode ter (um projeto longo em semanas passaria disso).</summary>
    public const int Maximo = 520;

    /// <summary>
    /// Os períodos que cobrem de <paramref name="inicio"/> a <paramref name="fim"/>, alinhados ao calendário (o primeiro e
    /// o último podem ser parciais). Com <see cref="Periodicidade.DatasDeCorte"/>, cada data de <paramref name="cortes"/>
    /// fecha um período; as fora do intervalo são ignoradas, e o último período vai até o fim.
    /// </summary>
    /// <exception cref="DomainValidationException">Intervalo invertido ou períodos demais.</exception>
    public static IReadOnlyList<PeriodoFinanceiro> Gerar(DateOnly inicio, DateOnly fim, Periodicidade periodicidade,
        IEnumerable<DateOnly>? cortes = null)
    {
        if (fim < inicio)
            throw new DomainValidationException(nameof(fim), "O período termina antes de começar.");
        var fins = new List<DateOnly>();
        if (periodicidade == Periodicidade.DatasDeCorte)
            fins.AddRange((cortes ?? []).Where(c => c >= inicio && c < fim).Distinct().Order());
        else
            for (var atual = FimDoPeriodo(inicio, periodicidade); atual < fim; atual = FimDoPeriodo(atual.AddDays(1), periodicidade))
            {
                fins.Add(atual);
                if (fins.Count > Maximo)
                    break;
            }
        fins.Add(fim);
        if (fins.Count > Maximo)
            throw new DomainValidationException(nameof(periodicidade),
                $"São mais de {Maximo} períodos; escolha uma periodicidade maior.");
        var periodos = new List<PeriodoFinanceiro>();
        var de = inicio;
        foreach (var ate in fins)
        {
            periodos.Add(new PeriodoFinanceiro(periodos.Count + 1, de, ate));
            de = ate.AddDays(1);
        }
        return periodos;
    }

    private static DateOnly FimDoPeriodo(DateOnly dia, Periodicidade periodicidade) => periodicidade switch
    {
        Periodicidade.Semanal => dia.AddDays((7 - (int)dia.DayOfWeek) % 7), // até o domingo
        Periodicidade.Quinzenal => dia.Day <= 15 ? new DateOnly(dia.Year, dia.Month, 15) : UltimoDoMes(dia),
        _ => UltimoDoMes(dia),
    };

    private static DateOnly UltimoDoMes(DateOnly dia) => new DateOnly(dia.Year, dia.Month, 1).AddMonths(1).AddDays(-1);

    /// <summary>Lê datas de corte digitadas ("31/10/2026, 30/11/2026" ou uma por linha).</summary>
    /// <exception cref="FormatException">Alguma não é uma data dd/mm/aaaa.</exception>
    public static IReadOnlyList<DateOnly> LerCortes(string? texto)
    {
        var datas = new List<DateOnly>();
        foreach (var parte in (texto ?? "").Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            datas.Add(DateOnly.TryParseExact(parte, ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
                ? data
                : throw new FormatException($"\"{parte}\" não é uma data (use dd/mm/aaaa)."));
        return datas;
    }
}

/// <summary>De onde vêm as datas e os valores: o cronograma atual ou uma linha de base.</summary>
public enum BaseDoFisicoFinanceiro
{
    Atual,
    LinhaDeBase,
}

/// <summary>Como agrupar as linhas da tabela.</summary>
public enum LinhasDoFisicoFinanceiro
{
    /// <summary>Uma linha por tarefa do 1º nível (as etapas), somando o que está dentro.</summary>
    Etapas,

    /// <summary>Uma linha por tarefa que não é resumo.</summary>
    Tarefas,
}

/// <summary>Uma linha da tabela: o valor, o peso e quanto cai em cada período (R$ e % da linha).</summary>
/// <param name="Peso">O peso da linha no físico do projeto (o valor; sem valores, os dias de trabalho).</param>
public sealed record LinhaFisicoFinanceira(string Edt, string Nome, decimal Valor, double Peso, IReadOnlyList<decimal> ValorPorPeriodo,
    IReadOnlyList<double> PesoPorPeriodo)
{
    /// <summary>O % da linha que cai em cada período (0 a 100).</summary>
    public IReadOnlyList<double> PercentualPorPeriodo => [.. PesoPorPeriodo.Select(p => Peso == 0 ? 0 : 100 * p / Peso)];
}

/// <summary>
/// Bloco 35: o cronograma físico-financeiro de uma base (atual ou linha de base) nos períodos: por linha e no total, o que
/// cai em cada período e o acumulado, em R$ e em %.
/// </summary>
/// <param name="PorValor">O físico pesa pelo valor (há valores); falso: pelos dias de trabalho (não há valor nenhum).</param>
public sealed record CronogramaFisicoFinanceiro(
    BaseDoFisicoFinanceiro Base,
    IReadOnlyList<PeriodoFinanceiro> Periodos,
    IReadOnlyList<LinhaFisicoFinanceira> Linhas,
    bool PorValor)
{
    public decimal ValorTotal => Linhas.Sum(l => l.Valor);

    public double PesoTotal => Linhas.Sum(l => l.Peso);

    public IReadOnlyList<decimal> ValorDoPeriodo => [.. Periodos.Select((_, i) => Linhas.Sum(l => l.ValorPorPeriodo[i]))];

    public IReadOnlyList<decimal> ValorAcumulado => Acumular(ValorDoPeriodo);

    /// <summary>O % do projeto (físico) em cada período.</summary>
    public IReadOnlyList<double> PercentualDoPeriodo =>
        [.. Periodos.Select((_, i) => PesoTotal == 0 ? 0 : 100 * Linhas.Sum(l => l.PesoPorPeriodo[i]) / PesoTotal)];

    public IReadOnlyList<double> PercentualAcumulado => Acumular(PercentualDoPeriodo);

    private static IReadOnlyList<decimal> Acumular(IReadOnlyList<decimal> valores)
    {
        var soma = 0m;
        return [.. valores.Select(v => soma += v)];
    }

    private static IReadOnlyList<double> Acumular(IReadOnlyList<double> valores)
    {
        var soma = 0.0;
        return [.. valores.Select(v => Math.Min(100, soma += v))];
    }
}

/// <summary>O realizado na data de status: o % físico (pesado como o físico-financeiro) e o valor executado.</summary>
public sealed record RealizadoNaData(DateOnly Data, double Percentual, decimal Valor);

/// <summary>
/// Bloco 35: distribui o valor (e o peso físico) de cada tarefa pelos dias úteis dela, igual em cada dia (um marco leva
/// tudo no dia dele), e soma por período e por linha. Conta só as tarefas que não são resumo.
/// </summary>
public static class CalculadoraFisicoFinanceira
{
    /// <exception cref="DomainValidationException">Sem linha de base em uso (com a base da linha de base), ou cronograma não calculado.</exception>
    public static CronogramaFisicoFinanceiro Calcular(Cronograma cronograma, BaseDoFisicoFinanceiro @base,
        IReadOnlyList<PeriodoFinanceiro> periodos, LinhasDoFisicoFinanceiro agrupamento)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(periodos);
        var itens = Itens(cronograma, @base);
        var porValor = itens.Any(i => i.Valor > 0);
        var linhas = new List<LinhaFisicoFinanceira>();
        foreach (var grupo in itens.GroupBy(i => agrupamento == LinhasDoFisicoFinanceiro.Etapas ? i.Etapa : i.Linha))
        {
            var valorPorPeriodo = new decimal[periodos.Count];
            var pesoPorPeriodo = new double[periodos.Count];
            foreach (var item in grupo)
            {
                var peso = porValor ? (double)item.Valor : item.Dias;
                foreach (var (i, fracao) in Fracoes(item, periodos))
                {
                    valorPorPeriodo[i] += item.Valor * (decimal)fracao;
                    pesoPorPeriodo[i] += peso * fracao;
                }
            }
            var (edt, nome) = grupo.Key;
            linhas.Add(new LinhaFisicoFinanceira(edt, nome, grupo.Sum(i => i.Valor), grupo.Sum(i => porValor ? (double)i.Valor : i.Dias),
                EmCentavos(valorPorPeriodo), pesoPorPeriodo));
        }
        return new CronogramaFisicoFinanceiro(@base, periodos, linhas, porValor);
    }

    // Em centavos, sem perder nem sobrar: o último período com valor fica com a diferença do arredondamento.
    private static decimal[] EmCentavos(decimal[] valores)
    {
        var arredondados = valores.Select(v => Math.Round(v, 2, MidpointRounding.AwayFromZero)).ToArray();
        var ultimo = Array.FindLastIndex(arredondados, v => v != 0);
        if (ultimo >= 0)
            arredondados[ultimo] += Math.Round(valores.Sum(), 2, MidpointRounding.AwayFromZero) - arredondados.Sum();
        return arredondados;
    }

    /// <summary>
    /// O realizado na data: o % concluído de cada tarefa, pesado como no físico-financeiro (pelo valor ou pelos dias), e o
    /// valor executado (valor × % concluído).
    /// </summary>
    public static RealizadoNaData Realizado(Cronograma cronograma, DateOnly data)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var folhas = cronograma.Tarefas.Where(t => !t.EhResumo).ToList();
        var porValor = folhas.Any(t => t.Valor > 0);
        double Peso(Tarefa t) => porValor ? (double)(t.Valor ?? 0) : t.Duracao;
        var total = folhas.Sum(Peso);
        var feito = folhas.Sum(t => Peso(t) * t.PercentualConcluido / 100.0);
        var valor = folhas.Sum(t => (t.Valor ?? 0) * t.PercentualConcluido / 100m);
        return new RealizadoNaData(data, total == 0 ? 0 : 100 * feito / total, Math.Round(valor, 2));
    }

    /// <summary>O período e o intervalo que cobrem o projeto: do início do projeto ao maior término (atual ou da linha de base).</summary>
    public static (DateOnly Inicio, DateOnly Fim) Intervalo(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var inicio = DateOnly.FromDateTime(cronograma.DataInicio);
        var fins = cronograma.Tarefas.Select(t => t.TerminoMaisCedo ?? t.DataTermino)
            .Concat(cronograma.LinhaDeBaseEmUso?.Tarefas.Values.Select(t => t.Termino) ?? []);
        var fim = fins.Select(DateOnly.FromDateTime).DefaultIfEmpty(inicio).Max();
        var primeiro = cronograma.LinhaDeBaseEmUso?.Tarefas.Values.Select(t => DateOnly.FromDateTime(t.Inicio)).DefaultIfEmpty(inicio).Min() ?? inicio;
        return (primeiro < inicio ? primeiro : inicio, fim < inicio ? inicio : fim);
    }

    // Uma tarefa a distribuir: as datas, o valor, os dias de trabalho, e as chaves da linha (a etapa e a própria tarefa).
    private sealed record Item(DateTime Inicio, DateTime Termino, int Dias, decimal Valor, (string Edt, string Nome) Etapa, (string Edt, string Nome) Linha);

    private static List<Item> Itens(Cronograma cronograma, BaseDoFisicoFinanceiro @base)
    {
        var edt = cronograma.NumeracaoEdt();
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        (string, string) Etapa(Tarefa t)
        {
            var raiz = t;
            while (raiz.TarefaPaiId is { } pai)
                raiz = porId[pai];
            return (edt[raiz.Id], raiz.Nome);
        }

        if (@base == BaseDoFisicoFinanceiro.Atual)
        {
            if (cronograma.Tarefas.Any(t => t.InicioMaisCedo is null))
                throw new DomainValidationException(nameof(Cronograma.Tarefas), "Calcule o cronograma antes do físico-financeiro.");
            return [.. cronograma.Tarefas.Where(t => !t.EhResumo).Select(t => new Item(t.InicioMaisCedo!.Value, t.TerminoMaisCedo!.Value,
                t.Duracao, t.Valor ?? 0, Etapa(t), (edt[t.Id], t.Nome)))];
        }

        var linha = cronograma.LinhaDeBaseEmUso
                    ?? throw new DomainValidationException(nameof(Cronograma.LinhaDeBaseEmUso), "Não há linha de base em uso para comparar.");
        var removida = ("—", "Tarefas removidas depois da linha de base");
        return [.. linha.Tarefas
            // Era resumo quando foi salva (ou, numa linha de base da 0.31, é resumo agora): o valor dele é a soma das outras.
            .Where(t => !(t.Value.EhResumo ?? porId.GetValueOrDefault(t.Key)?.EhResumo ?? false))
            .Select(t => porId.TryGetValue(t.Key, out var tarefa)
                ? new Item(t.Value.Inicio, t.Value.Termino, t.Value.Duracao, t.Value.Valor ?? 0, Etapa(tarefa), (edt[t.Key], tarefa.Nome))
                : new Item(t.Value.Inicio, t.Value.Termino, t.Value.Duracao, t.Value.Valor ?? 0, removida, removida))];
    }

    // A fração de cada item em cada período: os dias úteis dele que caem no período ÷ os dias úteis dele. Um marco (ou
    // uma tarefa de 0 dias) cai inteiro no período do dia dele. Fora dos períodos, não conta.
    private static IEnumerable<(int Indice, double Fracao)> Fracoes(Item item, IReadOnlyList<PeriodoFinanceiro> periodos)
    {
        var (inicio, termino) = (DateOnly.FromDateTime(item.Inicio), DateOnly.FromDateTime(item.Termino));
        var dias = item.Dias > 0 ? DiasUteisEntre(inicio, termino) : 0;
        for (var i = 0; i < periodos.Count; i++)
        {
            var p = periodos[i];
            if (dias == 0)
            {
                if (inicio >= p.Inicio && inicio <= p.Fim)
                    yield return (i, 1);
                continue;
            }
            var (de, ate) = (inicio > p.Inicio ? inicio : p.Inicio, termino < p.Fim ? termino : p.Fim);
            if (de <= ate && DiasUteisEntre(de, ate) is var noPeriodo and > 0)
                yield return (i, (double)noPeriodo / dias);
        }
    }

    private static int DiasUteisEntre(DateOnly de, DateOnly ate)
    {
        var (a, b) = (DiasUteis.ProximoOuMesmo(de.ToDateTime(TimeOnly.MinValue)), DiasUteis.AnteriorOuMesmo(ate.ToDateTime(TimeOnly.MinValue)));
        return b < a ? 0 : DiasUteis.ContarInclusivo(a, b);
    }
}

/// <summary>Bloco 37: o que uma série da Curva S mostra (define a cor e o traço).</summary>
public enum TipoDaSerie
{
    /// <summary>A linha de base (tracejada).</summary>
    Prevista,

    /// <summary>O cronograma atual.</summary>
    Atual,

    /// <summary>O medido pelas medições aprovadas (% do valor do contrato).</summary>
    Medida,

    /// <summary>O custo realizado (% do orçamento).</summary>
    CustoReal,
}

/// <summary>
/// Uma série da Curva S: o % acumulado e o valor acumulado no fim de cada período. Uma série medida ou de custo pode ter
/// menos pontos que os períodos (para no último período com dado).
/// </summary>
public sealed record SerieDaCurvaS(string Nome, IReadOnlyList<double> PercentualAcumulado, IReadOnlyList<decimal> ValorAcumulado,
    TipoDaSerie Tipo = TipoDaSerie.Atual);

/// <summary>
/// Bloco 35: a Curva S do projeto nos períodos: a prevista (linha de base em uso, se houver), a do cronograma atual, e o
/// realizado na data de status. Bloco 37: com medições aprovadas, a medida (% do contrato); com custos realizados, a do
/// custo real (% do orçamento); e o histórico do realizado (o avanço físico guardado em cada medição aprovada).
/// </summary>
public sealed record CurvaS(IReadOnlyList<PeriodoFinanceiro> Periodos, IReadOnlyList<SerieDaCurvaS> Series, RealizadoNaData? Realizado,
    bool PorValor)
{
    /// <summary>Bloco 37: o avanço físico no fim de cada medição aprovada (a linha do realizado passa por eles até hoje).</summary>
    public IReadOnlyList<RealizadoNaData> Historico { get; init; } = [];

    /// <summary>
    /// Onde a data fica no eixo dos períodos: 0 é o começo do primeiro, e i + 1 o fim do período i; dentro de um período,
    /// a fração dos dias corridos. Fora do intervalo, nulo.
    /// </summary>
    public double? PosicaoDe(DateOnly data)
    {
        for (var i = 0; i < Periodos.Count; i++)
        {
            var p = Periodos[i];
            if (data >= p.Inicio && data <= p.Fim)
                return i + (data.DayNumber - p.Inicio.DayNumber + 1) / (double)(p.Fim.DayNumber - p.Inicio.DayNumber + 1);
        }
        return null;
    }

    /// <summary>Monta a curva: "Linha de base (LB0)" e "Cronograma atual", e o realizado em <paramref name="hoje"/> (se cai nos períodos).</summary>
    public static CurvaS Montar(Cronograma cronograma, IReadOnlyList<PeriodoFinanceiro> periodos, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(periodos);
        var series = new List<SerieDaCurvaS>();
        var porValor = false;
        if (cronograma.LinhaDeBaseEmUso is { } linha)
        {
            var prevista = CalculadoraFisicoFinanceira.Calcular(cronograma, BaseDoFisicoFinanceiro.LinhaDeBase, periodos, LinhasDoFisicoFinanceiro.Etapas);
            series.Add(new SerieDaCurvaS($"Linha de base (LB{linha.Numero})", prevista.PercentualAcumulado, prevista.ValorAcumulado, TipoDaSerie.Prevista));
            porValor = prevista.PorValor;
        }
        var atual = CalculadoraFisicoFinanceira.Calcular(cronograma, BaseDoFisicoFinanceiro.Atual, periodos, LinhasDoFisicoFinanceiro.Etapas);
        series.Add(new SerieDaCurvaS("Cronograma atual", atual.PercentualAcumulado, atual.ValorAcumulado));
        var orcamento = series[0].ValorAcumulado.LastOrDefault();
        if (Medida(cronograma, periodos, hoje) is { } medida)
            series.Add(medida);
        if (CustoReal(cronograma, periodos, hoje, orcamento) is { } custo)
            series.Add(custo);
        var dentro = periodos.Count > 0 && hoje >= periodos[0].Inicio && hoje <= periodos[^1].Fim;
        var historico = cronograma.Contrato.Medicoes
            .Where(m => m.Travada && m.Resumo!.Fisico is not null && m.Ate >= periodos[0].Inicio && m.Ate <= periodos[^1].Fim && m.Ate < hoje)
            .Select(m => new RealizadoNaData(m.Ate, m.Resumo!.Fisico!.Value, 0)).ToList();
        return new CurvaS(periodos, series, dentro ? CalculadoraFisicoFinanceira.Realizado(cronograma, hoje) : null, porValor || atual.PorValor)
        {
            Historico = historico,
        };
    }

    // O medido acumulado pelas aprovadas que terminam até hoje (cada uma no período em que termina), em % do valor atual do
    // contrato, até o período da última; nula sem medição aprovada.
    private static SerieDaCurvaS? Medida(Cronograma cronograma, IReadOnlyList<PeriodoFinanceiro> periodos, DateOnly hoje)
    {
        var aprovadas = cronograma.Contrato.Medicoes.Where(m => m.Travada && m.Ate <= hoje).ToList();
        var contratado = cronograma.Contrato.ValorContratado;
        if (aprovadas.Count == 0 || contratado <= 0 || periodos.Count == 0)
            return null;
        var ultima = periodos.ToList().FindLastIndex(p => p.Inicio <= aprovadas[^1].Ate);
        var valores = new List<decimal>();
        for (var i = 0; i <= ultima; i++)
            valores.Add(aprovadas.Where(m => m.Ate <= periodos[i].Fim).Sum(m => m.Resumo!.MedidoNoPeriodo));
        return new SerieDaCurvaS("Medido", [.. valores.Select(v => (double)(v / contratado) * 100)], valores, TipoDaSerie.Medida);
    }

    // O custo realizado acumulado até o fim de cada período (o último, até hoje), em % do orçamento; nula sem custos.
    private static SerieDaCurvaS? CustoReal(Cronograma cronograma, IReadOnlyList<PeriodoFinanceiro> periodos, DateOnly hoje, decimal orcamento)
    {
        if (cronograma.Custos.Vazio || orcamento <= 0 || periodos.Count == 0 || hoje < periodos[0].Inicio)
            return null;
        var valores = periodos.TakeWhile(p => p.Inicio <= hoje).Select(p => cronograma.Custos.Ate(p.Fim < hoje ? p.Fim : hoje)).ToList();
        return new SerieDaCurvaS("Custo real", [.. valores.Select(v => (double)(v / orcamento) * 100)], valores, TipoDaSerie.CustoReal);
    }
}
