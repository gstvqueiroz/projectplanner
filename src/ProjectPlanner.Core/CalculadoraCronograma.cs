using System.Diagnostics;

namespace ProjectPlanner.Core;

/// <summary>
/// Calcula o cronograma pelo método do caminho crítico (CPM).
/// </summary>
/// <remarks>
/// <para>
/// O cálculo usa números de dia útil contados a partir de <see cref="Cronograma.DataInicio"/>
/// (dia 0) e as datas inclusivas da <see cref="Tarefa"/>: uma tarefa de duração d que começa no dia
/// s termina no dia s + d − 1. Cada vínculo aplica a regra do seu <see cref="TipoDependencia"/>,
/// com o lag em dias úteis. As datas informadas das tarefas não entram no cálculo; desde o Bloco 31, entram a
/// restrição "não iniciar antes de" (<see cref="Tarefa.NaoIniciarAntesDe"/>) e o prazo
/// (<see cref="Tarefa.Prazo"/>, o da tarefa e o dos resumos em que ela está).
/// </para>
/// <list type="number">
/// <item>Ordenação topológica. Entre tarefas liberadas ao mesmo tempo, vale a ordem do cronograma.</item>
/// <item>Ida (forward pass): ES é o maior valor entre o dia 0, o dia da restrição de início e o que cada
/// predecessora exige, e EF = ES + duração − 1. Nenhuma tarefa começa antes do início do projeto, nem com lead.</item>
/// <item>Fim do projeto: o maior EF entre todas as tarefas.</item>
/// <item>Volta (backward pass): LF é o menor valor entre o fim do projeto, o dia do prazo (o mais cedo entre o da
/// tarefa e o dos resumos acima dela) e o que cada sucessora permite, e LS = LF − duração + 1.</item>
/// <item>Folga = LS − ES, e a tarefa é crítica quando a folga é zero ou negativa. A folga só fica negativa por causa
/// de um prazo que não cabe: na própria tarefa e nas que a empurram.</item>
/// </list>
/// <para>
/// Só as tarefas comuns e os marcos (as folhas da estrutura) entram no cálculo. Os resumos não têm
/// vínculos e recebem, no final, os valores agregados das subtarefas (<see cref="CalculadoraResumos"/>).
/// </para>
/// <para>
/// Um marco entra com duração 0 nas mesmas fórmulas: no dia útil p, ele é a fronteira entre o fim do
/// dia p − 1 e o começo do dia p (o mesmo instante de trabalho), e EF = ES − 1. Para mostrar uma data,
/// ele fica no instante de quem o determina, como no MS Project: ligado ao término de outra tarefa
/// (FS ou FF), no fim do dia em que ela termina (<see cref="Tarefa.MarcoNoFimDoDia"/>); ligado ao
/// início (SS ou SF), sem predecessora ou no início do projeto, no começo do dia. No empate, vale o
/// começo do dia, que é mais tarde no relógio. As datas tarde do marco usam o mesmo lado do dia, sem
/// passar do fim do projeto. Um marco depois de uma tarefa que termina na sexta fica na sexta, e a
/// sucessora dele começa na segunda; um marco no começo de um dia é seguido no mesmo dia.
/// </para>
/// <para>
/// O fim do projeto é o instante mais tarde em que alguma folha termina: o fim do último dia de
/// trabalho, ou o começo do dia de um marco que venha depois dele.
/// </para>
/// <para>
/// As tarefas só recebem os resultados depois que todo o cálculo deu certo. Em caso de erro,
/// nenhuma é alterada.
/// </para>
/// </remarks>
public static class CalculadoraCronograma
{
    /// <exception cref="DomainValidationException">
    /// O fim do projeto passaria de 31/12/9999.
    /// </exception>
    public static ResultadoCronograma Calcular(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var folhas = cronograma.Tarefas.Where(t => !t.EhResumo).ToList();
        if (folhas.Count == 0)
            return new ResultadoCronograma(cronograma.DataInicio, [], []);

        var ordem = OrdenarTopologicamente(folhas, cronograma.Predecessoras);
        var predecessorasDe = cronograma.Predecessoras.ToLookup(p => p.TarefaDestinoId);
        var sucessorasDe = cronograma.Predecessoras.ToLookup(p => p.TarefaOrigemId);

        long DiaDaRestricao(Tarefa tarefa) => tarefa.NaoIniciarAntesDe is { } data
            ? Math.Max(0, DiasUteis.Indice(cronograma.DataInicio, DiasUteis.ProximoOuMesmo(data.Date)))
            : 0;
        var cedo = new Dictionary<Guid, Periodo>();
        foreach (var tarefa in ordem)
        {
            var inicio = DiaDaRestricao(tarefa);
            foreach (var vinculo in predecessorasDe[tarefa.Id])
                inicio = Math.Max(inicio, InicioMinimo(vinculo, cedo[vinculo.TarefaOrigemId], tarefa.Duracao));
            cedo[tarefa.Id] = Periodo.ComInicio(inicio, tarefa.Duracao);
        }

        var porId = ordem.ToDictionary(t => t.Id);
        var marcoNoFimDoDia = new Dictionary<Guid, bool>();
        foreach (var marco in ordem.Where(t => t.Duracao == 0))
            marcoNoFimDoDia[marco.Id] = DiaDaRestricao(marco) is var restricao && restricao > 0 && restricao == cedo[marco.Id].Inicio
                ? false // a restrição põe o marco no começo do dia
                : FicaNoFimDoDia(marco, predecessorasDe[marco.Id], cedo, marcoNoFimDoDia, porId);

        // Instantes em meios-dias: o começo do dia útil d é 2d, e o fim dele é 2d + 1.
        long InstanteDoTermino(Tarefa tarefa) => tarefa.Duracao > 0
            ? 2 * cedo[tarefa.Id].Termino + 1
            : Instante(cedo[tarefa.Id].Inicio, marcoNoFimDoDia[tarefa.Id]);
        var fimDoProjeto = ordem.Max(InstanteDoTermino);
        var diaDoFim = fimDoProjeto >> 1;
        if (diaDoFim > int.MaxValue || DiasUteis.Adicionar(cronograma.DataInicio, (int)diaDoFim) is null)
            throw new DomainValidationException(nameof(Tarefa.TerminoMaisCedo),
                "O término calculado do projeto ultrapassa a maior data suportada (31/12/9999).");

        var ultimoDia = cedo.Values.Max(p => p.Termino);
        var prazos = Prazos(cronograma);
        var tarde = new Dictionary<Guid, Periodo>();
        foreach (var tarefa in Enumerable.Reverse(ordem))
        {
            var termino = ultimoDia;
            if (prazos.TryGetValue(tarefa.Id, out var prazo))
            {
                // O último dia útil até o prazo. Um marco no começo do dia ocupa a fronteira do dia: para a data dele
                // não passar do prazo, a fronteira fica até o dia do prazo (o "término" do marco é a fronteira − 1).
                var diaDoPrazo = DiasUteis.Indice(cronograma.DataInicio, DiasUteis.AnteriorOuMesmo(prazo));
                termino = Math.Min(termino, tarefa.Duracao == 0 && !marcoNoFimDoDia[tarefa.Id] ? diaDoPrazo - 1 : diaDoPrazo);
            }
            foreach (var vinculo in sucessorasDe[tarefa.Id])
                termino = Math.Min(termino, TerminoMaximo(vinculo, tarde[vinculo.TarefaDestinoId], tarefa.Duracao));
            tarde[tarefa.Id] = Periodo.ComTermino(termino, tarefa.Duracao);
        }

        // Todo dia cedo fica entre 0 e o dia do fim do projeto, que já cabe numa data; um dia tarde pode ficar antes do
        // início do projeto (prazo que não cabe), e um prazo absurdo (antes do ano 1) é recusado.
        DateTime Data(long dia) => DiasUteis.Adicionar(cronograma.DataInicio, (int)Math.Max(dia, int.MinValue))
            ?? throw new DomainValidationException(nameof(Tarefa.Prazo), "Um prazo está antes da menor data suportada.");
        foreach (var tarefa in ordem)
        {
            var (c, t) = (cedo[tarefa.Id], tarde[tarefa.Id]);
            var folga = (int)(t.Inicio - c.Inicio);
            if (tarefa.Duracao > 0)
            {
                tarefa.DefinirDatasCalculadas(Data(c.Inicio), Data(c.Termino), Data(t.Inicio), Data(t.Termino), folga);
                continue;
            }

            var noFim = marcoNoFimDoDia[tarefa.Id];
            var instanteCedo = Instante(c.Inicio, noFim);
            var instanteTarde = Math.Min(Instante(t.Inicio, noFim), fimDoProjeto);
            // ">> 1" e "& 1": o dia e o lado do dia também num instante negativo (prazo antes do início do projeto).
            tarefa.DefinirDatasCalculadas(Data(instanteCedo >> 1), Data(instanteCedo >> 1),
                Data(instanteTarde >> 1), Data(instanteTarde >> 1), folga, noFim, (instanteTarde & 1) == 1);
        }

        CalculadoraResumos.Atualizar(cronograma);

        var criticas = ordem.Where(t => t.EhCritica).ToList();
        return new ResultadoCronograma(Data(diaDoFim), criticas, CaminhosCriticos(criticas, sucessorasDe, cedo));
    }

    // O prazo que vale para cada tarefa-folha: o mais cedo entre o dela e o dos resumos acima dela (Bloco 31).
    private static Dictionary<Guid, DateTime> Prazos(Cronograma cronograma)
    {
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var prazos = new Dictionary<Guid, DateTime>();
        foreach (var tarefa in cronograma.Tarefas.Where(t => !t.EhResumo))
        {
            DateTime? menor = tarefa.Prazo;
            for (var pai = tarefa.TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
                if (porId[id].Prazo is { } doResumo && (menor is null || doResumo < menor))
                    menor = doResumo;
            if (menor is { } prazo)
                prazos[tarefa.Id] = prazo;
        }
        return prazos;
    }

    // Instante de um marco na fronteira p (em meios-dias): o fim do dia p − 1 ou o começo do dia p.
    private static long Instante(long fronteira, bool noFimDoDia) => noFimDoDia ? 2 * fronteira - 1 : 2 * fronteira;

    // Um marco fica no instante de quem fixa o seu início: o término de uma tarefa comum (FS, FF) é o
    // fim de um dia; o início dela (SS, SF), o começo; um marco predecessor passa o seu próprio lado.
    // No início do projeto, e no empate, vale o começo do dia.
    private static bool FicaNoFimDoDia(Tarefa marco, IEnumerable<Predecessora> vinculos,
        Dictionary<Guid, Periodo> cedo, Dictionary<Guid, bool> marcoNoFimDoDia, Dictionary<Guid, Tarefa> porId)
    {
        var inicio = cedo[marco.Id].Inicio;
        if (inicio == 0)
            return false;

        var noFim = false;
        foreach (var vinculo in vinculos)
        {
            var origem = porId[vinculo.TarefaOrigemId];
            if (InicioMinimo(vinculo, cedo[origem.Id], 0) != inicio)
                continue;
            var doTermino = vinculo.Tipo is TipoDependencia.FS or TipoDependencia.FF;
            if (!(origem.Duracao == 0 ? marcoNoFimDoDia[origem.Id] : doTermino))
                return false;
            noFim = true;
        }
        return noFim;
    }

    /// <summary>
    /// Ordena as tarefas de modo que cada uma venha depois de todas as suas predecessoras
    /// (algoritmo de Kahn). Entre tarefas liberadas ao mesmo tempo, vale a ordem de <paramref name="tarefas"/>.
    /// </summary>
    /// <exception cref="CircularDependencyException">Os vínculos formam um ciclo.</exception>
    internal static IReadOnlyList<Tarefa> OrdenarTopologicamente(
        IReadOnlyCollection<Tarefa> tarefas, IReadOnlyCollection<Predecessora> vinculos)
    {
        var posicao = tarefas.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var porId = tarefas.ToDictionary(t => t.Id);
        var sucessoras = vinculos.ToLookup(v => v.TarefaOrigemId, v => v.TarefaDestinoId);
        var predecessorasPendentes = tarefas.ToDictionary(t => t.Id, _ => 0);
        foreach (var vinculo in vinculos)
            predecessorasPendentes[vinculo.TarefaDestinoId]++;

        var liberadas = new PriorityQueue<Tarefa, int>(
            tarefas.Where(t => predecessorasPendentes[t.Id] == 0).Select(t => (t, posicao[t.Id])));
        var ordem = new List<Tarefa>(tarefas.Count);
        while (liberadas.TryDequeue(out var tarefa, out _))
        {
            ordem.Add(tarefa);
            foreach (var sucessora in sucessoras[tarefa.Id])
                if (--predecessorasPendentes[sucessora] == 0)
                    liberadas.Enqueue(porId[sucessora], posicao[sucessora]);
        }

        if (ordem.Count < tarefas.Count)
            throw CicloEntre(tarefas.Where(t => predecessorasPendentes[t.Id] > 0).ToList(), vinculos);
        return ordem;
    }

    // Toda tarefa que sobrou na ordenação tem uma predecessora que também sobrou. Voltando de
    // predecessora em predecessora, alguma tarefa se repete, e o trecho entre as repetições é um ciclo.
    private static CircularDependencyException CicloEntre(
        IReadOnlyList<Tarefa> restantes, IReadOnlyCollection<Predecessora> vinculos)
    {
        var porId = restantes.ToDictionary(t => t.Id);
        var predecessoras = vinculos.ToLookup(v => v.TarefaDestinoId, v => v.TarefaOrigemId);

        var visitadas = new List<Guid>();
        var atual = restantes[0].Id;
        while (!visitadas.Contains(atual))
        {
            visitadas.Add(atual);
            atual = predecessoras[atual].First(porId.ContainsKey);
        }

        var ciclo = visitadas.Skip(visitadas.IndexOf(atual)).Append(atual).Reverse().ToList();
        var nomes = string.Join(" → ", ciclo.Select(id => porId[id].Nome));
        return new CircularDependencyException(ciclo,
            $"O cronograma tem a dependência circular {nomes} e não pode ser calculado.");
    }

    // Menor início da sucessora permitido pelo vínculo: a regra de TipoDependencia isolando Início(B).
    private static long InicioMinimo(Predecessora vinculo, Periodo predecessora, int duracaoSucessora) =>
        vinculo.Tipo switch
        {
            TipoDependencia.FS => predecessora.Termino + 1 + vinculo.Lag,
            TipoDependencia.SS => predecessora.Inicio + vinculo.Lag,
            TipoDependencia.FF => predecessora.Termino + vinculo.Lag - (duracaoSucessora - 1),
            TipoDependencia.SF => predecessora.Inicio - 1 + vinculo.Lag - (duracaoSucessora - 1),
            _ => throw new UnreachableException(),
        };

    // Maior término da predecessora permitido pelo vínculo: a mesma regra isolando Término(A).
    private static long TerminoMaximo(Predecessora vinculo, Periodo sucessora, int duracaoPredecessora) =>
        vinculo.Tipo switch
        {
            TipoDependencia.FS => sucessora.Inicio - 1 - vinculo.Lag,
            TipoDependencia.SS => sucessora.Inicio - vinculo.Lag + (duracaoPredecessora - 1),
            TipoDependencia.FF => sucessora.Termino - vinculo.Lag,
            TipoDependencia.SF => sucessora.Termino + 1 - vinculo.Lag + (duracaoPredecessora - 1),
            _ => throw new UnreachableException(),
        };

    // Percorre, a partir de cada crítica sem vínculo crítico de entrada, todos os encadeamentos de
    // vínculos críticos. Um vínculo é crítico quando liga duas críticas e é ele que fixa o início
    // mais cedo da sucessora. O número de caminhos cresce com as ramificações críticas empatadas.
    private static List<IReadOnlyList<Tarefa>> CaminhosCriticos(
        IReadOnlyList<Tarefa> criticas, ILookup<Guid, Predecessora> sucessorasDe, Dictionary<Guid, Periodo> cedo)
    {
        var porId = criticas.ToDictionary(t => t.Id);
        var posicao = criticas.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var seguintes = criticas.ToDictionary(t => t.Id, t => sucessorasDe[t.Id]
            .Where(v => porId.TryGetValue(v.TarefaDestinoId, out var s)
                && InicioMinimo(v, cedo[t.Id], s.Duracao) == cedo[s.Id].Inicio)
            .Select(v => porId[v.TarefaDestinoId])
            .OrderBy(s => posicao[s.Id])
            .ToList());
        var comEntrada = seguintes.Values.SelectMany(s => s).Select(s => s.Id).ToHashSet();

        var caminhos = new List<IReadOnlyList<Tarefa>>();
        var atual = new List<Tarefa>();
        void Percorrer(Tarefa tarefa)
        {
            atual.Add(tarefa);
            if (seguintes[tarefa.Id].Count == 0)
                caminhos.Add(atual.ToList());
            foreach (var seguinte in seguintes[tarefa.Id])
                Percorrer(seguinte);
            atual.RemoveAt(atual.Count - 1);
        }

        foreach (var inicio in criticas.Where(t => !comEntrada.Contains(t.Id)))
            Percorrer(inicio);
        return caminhos;
    }

    // Dias úteis contados a partir do início do projeto (dia 0), com término inclusivo.
    private readonly record struct Periodo(long Inicio, long Termino)
    {
        public static Periodo ComInicio(long inicio, int duracao) => new(inicio, inicio + duracao - 1);

        public static Periodo ComTermino(long termino, int duracao) => new(termino - duracao + 1, termino);
    }
}
