using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Recursos;

/// <summary>Bloco 34: o uso de um recurso num dia útil e as tarefas que o usam.</summary>
public sealed record UsoNoDia(DateOnly Dia, decimal Quantidade, IReadOnlyList<Guid> Tarefas);

/// <summary>Bloco 34: o uso de um recurso dia a dia, nos dias úteis em que alguma tarefa o usa.</summary>
public sealed record UsoDoRecurso(Recurso Recurso, IReadOnlyList<UsoNoDia> Dias)
{
    public decimal Pico => Dias.Count == 0 ? 0 : Dias.Max(d => d.Quantidade);

    /// <summary>Os dias acima do disponível (nenhum sem limite).</summary>
    public IReadOnlyList<UsoNoDia> AcimaDoLimite => Recurso.Disponivel is { } limite ? [.. Dias.Where(d => d.Quantidade > limite)] : [];
}

/// <summary>Bloco 34: dias seguidos em que um recurso passa do disponível, com o pico e as tarefas.</summary>
public sealed record Superalocacao(Recurso Recurso, DateOnly De, DateOnly Ate, int Dias, decimal Pico, decimal Limite, IReadOnlyList<string> Tarefas);

/// <summary>Bloco 34: quanto de um recurso o período pede (dias de trabalho, num profissional ou equipamento; a quantidade, num material) e quanto custa.</summary>
public sealed record NecessidadeNoPeriodo(PeriodoFinanceiro Periodo, Recurso Recurso, decimal Quantidade, decimal Custo);

/// <summary>Bloco 34: uma tarefa que o nivelamento atrasa (direto, por causa de um recurso, ou pelas predecessoras).</summary>
public sealed record MudancaDoNivelamento(Guid TarefaId, string Edt, string Nome, DateOnly InicioAtual, DateOnly InicioNovo, int Dias, bool Direta);

/// <summary>Bloco 34: o nivelamento sugerido: as tarefas que mudam, o término do projeto antes e depois e o que não deu para resolver.</summary>
public sealed record NivelamentoSugerido(IReadOnlyList<MudancaDoNivelamento> Mudancas, DateOnly? TerminoAtual, DateOnly? TerminoNovo, IReadOnlyList<string> NaoResolvidos)
{
    public bool NadaAMudar => Mudancas.Count == 0;
}

/// <summary>
/// Bloco 34: o uso dos recursos no tempo, pelas datas calculadas: profissionais e equipamentos ocupam a quantidade por dia em
/// cada dia útil da tarefa; materiais, o total dividido pelos dias úteis dela (um marco leva tudo no dia dele). Daí saem as
/// superalocações (acima do disponível por dia), as necessidades por período (equipes, equipamentos e compras) e o
/// nivelamento sugerido, que só atrasa tarefas ainda não iniciadas e só muda o cronograma se for aplicado.
/// </summary>
public static class UsoDeRecursos
{
    /// <summary>Quantas vezes o nivelamento tenta adiar uma tarefa antes de desistir.</summary>
    public const int TentativasDoNivelamento = 5000;

    /// <exception cref="DomainValidationException">Cronograma não calculado.</exception>
    public static IReadOnlyList<UsoDoRecurso> Calcular(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        if (cronograma.Tarefas.Any(t => t.InicioMaisCedo is null))
            throw new DomainValidationException(nameof(Cronograma.Tarefas), "Calcule o cronograma antes do uso de recursos.");
        var porRecurso = cronograma.Recursos.Lista.ToDictionary(r => r.Id, _ => new SortedDictionary<DateOnly, (decimal Quantidade, List<Guid> Tarefas)>());
        foreach (var tarefa in cronograma.Tarefas.Where(t => !t.EhResumo && t.Atribuicoes.Count > 0))
        {
            var dias = DiasUteisDa(tarefa);
            foreach (var a in tarefa.Atribuicoes)
            {
                if (cronograma.Recursos.Obter(a.RecursoId) is not { } recurso)
                    continue;
                var porDia = recurso.PorDia ? a.Quantidade : dias.Count == 0 ? 0 : a.Quantidade / dias.Count;
                var lista = recurso.PorDia || dias.Count > 0 ? dias : [DateOnly.FromDateTime(tarefa.InicioMaisCedo!.Value)];
                if (!recurso.PorDia && dias.Count == 0)
                    porDia = a.Quantidade; // material num marco: tudo no dia dele
                foreach (var dia in lista)
                {
                    var (q, tarefas) = porRecurso[recurso.Id].GetValueOrDefault(dia, (0m, []));
                    tarefas.Add(tarefa.Id);
                    porRecurso[recurso.Id][dia] = (q + porDia, tarefas);
                }
            }
        }
        return [.. cronograma.Recursos.Lista.Select(r => new UsoDoRecurso(r, [.. porRecurso[r.Id].Select(p => new UsoNoDia(p.Key, p.Value.Quantidade, p.Value.Tarefas))]))];
    }

    // Os dias úteis da tarefa pelas datas calculadas (nenhum num marco).
    private static List<DateOnly> DiasUteisDa(Tarefa tarefa)
    {
        var dias = new List<DateOnly>();
        if (tarefa.Duracao == 0)
            return dias;
        for (var dia = tarefa.InicioMaisCedo!.Value.Date; dia <= tarefa.TerminoMaisCedo!.Value.Date; dia = dia.AddDays(1))
            if (dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                dias.Add(DateOnly.FromDateTime(dia));
        return dias;
    }

    /// <summary>Os trechos de dias seguidos (úteis) em que cada recurso passa do disponível.</summary>
    /// <exception cref="DomainValidationException">Cronograma não calculado.</exception>
    public static IReadOnlyList<Superalocacao> Superalocacoes(Cronograma cronograma)
    {
        var edt = cronograma.NumeracaoEdt();
        var nomes = cronograma.Tarefas.ToDictionary(t => t.Id, t => $"{edt[t.Id]} {t.Nome}");
        var lista = new List<Superalocacao>();
        foreach (var uso in Calcular(cronograma))
        {
            var acima = uso.AcimaDoLimite;
            for (var i = 0; i < acima.Count;)
            {
                var j = i;
                while (j + 1 < acima.Count && ProximoDiaUtil(acima[j].Dia) == acima[j + 1].Dia)
                    j++;
                var trecho = acima.Skip(i).Take(j - i + 1).ToList();
                lista.Add(new Superalocacao(uso.Recurso, trecho[0].Dia, trecho[^1].Dia, trecho.Count, trecho.Max(d => d.Quantidade), uso.Recurso.Disponivel!.Value,
                    [.. trecho.SelectMany(d => d.Tarefas).Distinct().Select(id => nomes[id])]));
                i = j + 1;
            }
        }
        return [.. lista.OrderBy(s => s.De).ThenBy(s => s.Recurso.Nome)];
    }

    private static DateOnly ProximoDiaUtil(DateOnly dia) =>
        DateOnly.FromDateTime(DiasUteis.ProximoOuMesmo(dia.AddDays(1).ToDateTime(TimeOnly.MinValue)));

    /// <summary>O que cada recurso pede em cada período (só os que pedem algo), com o custo.</summary>
    /// <exception cref="DomainValidationException">Cronograma não calculado.</exception>
    public static IReadOnlyList<NecessidadeNoPeriodo> Necessidades(Cronograma cronograma, IReadOnlyList<PeriodoFinanceiro> periodos)
    {
        ArgumentNullException.ThrowIfNull(periodos);
        var lista = new List<NecessidadeNoPeriodo>();
        var usos = Calcular(cronograma);
        foreach (var periodo in periodos)
            foreach (var uso in usos)
            {
                var quantidade = uso.Dias.Where(d => d.Dia >= periodo.Inicio && d.Dia <= periodo.Fim).Sum(d => d.Quantidade);
                if (quantidade > 0)
                    lista.Add(new NecessidadeNoPeriodo(periodo, uso.Recurso, Math.Round(quantidade, 4, MidpointRounding.AwayFromZero),
                        Math.Round(quantidade * uso.Recurso.CustoUnitario, 2, MidpointRounding.AwayFromZero)));
            }
        return lista;
    }

    /// <summary>
    /// Sugere um nivelamento sem mudar o cronograma: numa cópia, enquanto um recurso passa do disponível num dia, adia em um
    /// dia útil a tarefa que começa por último entre as que o usam nesse dia (só as não iniciadas, que sozinhas cabem no
    /// disponível), e recalcula. As tarefas adiadas ganham "não iniciar antes de" ao aplicar.
    /// </summary>
    /// <exception cref="DomainValidationException">Cronograma não calculado.</exception>
    public static NivelamentoSugerido Sugerir(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        Calcular(cronograma); // confere o cálculo
        var copia = cronograma.Copiar();
        CalculadoraCronograma.Calcular(copia);
        var naoResolvidos = new List<string>();
        var ignorar = new HashSet<(Guid Recurso, DateOnly Dia)>();
        var adiadas = new HashSet<Guid>();
        var tentativas = 0;
        while (true)
        {
            var conflito = Calcular(copia)
                .SelectMany(u => u.AcimaDoLimite.Select(d => (Uso: u, Dia: d)))
                .Where(c => !ignorar.Contains((c.Uso.Recurso.Id, c.Dia.Dia)))
                .OrderBy(c => c.Dia.Dia).FirstOrDefault();
            if (conflito.Uso is null)
                break;
            var recurso = conflito.Uso.Recurso;
            var porId = copia.Tarefas.ToDictionary(t => t.Id);
            var candidatas = conflito.Dia.Tarefas.Distinct().Select(id => porId[id])
                .Where(t => t.PercentualConcluido == 0 && t.Atribuicoes.First(a => a.RecursoId == recurso.Id).Quantidade <= recurso.Disponivel)
                .OrderByDescending(t => t.InicioMaisCedo).ThenByDescending(t => t.Folga).ToList();
            if (candidatas.Count == 0 || ++tentativas > TentativasDoNivelamento)
            {
                ignorar.Add((recurso.Id, conflito.Dia.Dia));
                var motivo = candidatas.Count == 0 ? "as tarefas já começaram ou pedem sozinhas mais do que o disponível" : "o nivelamento parou depois de muitas tentativas";
                var texto = $"{recurso.Nome} em {conflito.Dia.Dia:dd/MM/yyyy}: {motivo}";
                if (!naoResolvidos.Any(n => n.StartsWith($"{recurso.Nome} em ")))
                    naoResolvidos.Add(texto); // um aviso por recurso basta
                if (tentativas > TentativasDoNivelamento)
                    break;
                continue;
            }
            var adiada = candidatas[0];
            var proximo = DiasUteis.Adicionar(adiada.InicioMaisCedo!.Value, 1)!.Value;
            adiada.DefinirRestricaoDeInicio(proximo);
            adiadas.Add(adiada.Id);
            CalculadoraCronograma.Calcular(copia);
        }

        var edt = cronograma.NumeracaoEdt();
        var depois = copia.Tarefas.ToDictionary(t => t.Id);
        var mudancas = cronograma.Tarefas
            .Where(t => !t.EhResumo && depois[t.Id].InicioMaisCedo != t.InicioMaisCedo)
            .Select(t => new MudancaDoNivelamento(t.Id, edt[t.Id], t.Nome, DateOnly.FromDateTime(t.InicioMaisCedo!.Value),
                DateOnly.FromDateTime(depois[t.Id].InicioMaisCedo!.Value),
                (int)DiasUteis.Indice(t.InicioMaisCedo.Value, depois[t.Id].InicioMaisCedo!.Value), adiadas.Contains(t.Id)))
            .ToList();
        DateOnly? Termino(Cronograma c) => c.Tarefas.Select(t => t.TerminoMaisCedo).OfType<DateTime>().Select(DateOnly.FromDateTime).DefaultIfEmpty().Max() is var d && d != default ? d : null;
        return new NivelamentoSugerido(mudancas, Termino(cronograma), Termino(copia), naoResolvidos);
    }

    /// <summary>Aplica a sugestão: as tarefas adiadas direto ganham "não iniciar antes de" no novo início (as outras andam pelas predecessoras).</summary>
    /// <returns>Quantas tarefas ganharam a restrição.</returns>
    public static int Aplicar(Cronograma cronograma, NivelamentoSugerido sugestao)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(sugestao);
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var aplicadas = 0;
        foreach (var mudanca in sugestao.Mudancas.Where(m => m.Direta && porId.ContainsKey(m.TarefaId)))
        {
            porId[mudanca.TarefaId].DefinirRestricaoDeInicio(mudanca.InicioNovo.ToDateTime(TimeOnly.MinValue));
            aplicadas++;
        }
        return aplicadas;
    }
}
