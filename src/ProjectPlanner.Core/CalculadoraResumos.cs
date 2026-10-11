namespace ProjectPlanner.Core;

/// <summary>
/// Preenche os resumos a partir das subtarefas (rollup), depois que o <see cref="CalculadoraCronograma"/>
/// calculou as tarefas comuns e os marcos.
/// </summary>
/// <remarks>
/// <para>
/// A lista do cronograma está em pré-ordem, então percorrê-la de trás para frente trata cada resumo
/// depois de todas as suas subtarefas: primeiro os resumos mais internos, depois os pais deles, e
/// assim até o primeiro nível.
/// </para>
/// <list type="bullet">
/// <item>Início e término (cedo): o início mais cedo e o término mais tarde entre as subtarefas. São
/// também o <see cref="Tarefa.DataInicio"/> e o <see cref="Tarefa.DataTermino"/> do resumo.</item>
/// <item>Duração: os dias úteis entre esse início e esse término (0 se só houver marcos no mesmo instante).</item>
/// <item>Início e término tarde: o menor início tarde e o maior término tarde entre as subtarefas.</item>
/// <item>Folga: a menor folga entre as subtarefas; o resumo é crítico quando alguma delas é.</item>
/// <item>Percentual concluído: média das tarefas-folha abaixo do resumo (em qualquer nível), ponderada
/// pela duração — dias concluídos ÷ dias totais, como no MS Project. Marcos pesam 0; se só houver
/// marcos, vale a média simples. Arredondado para o inteiro mais próximo.</item>
/// <item>Valor (Bloco 35): a soma dos valores das subtarefas; nulo se nenhuma tem valor.</item>
/// </list>
/// <para>
/// Os instantes são contados em meios-dias úteis a partir do início do projeto (o começo do dia d é
/// 2d; o fim dele, 2d + 1), para que um marco no fim de um dia e outro no começo do dia seguinte
/// fiquem na ordem certa.
/// </para>
/// </remarks>
internal static class CalculadoraResumos
{
    /// <exception cref="InvalidOperationException">Alguma tarefa comum ou marco ainda não foi calculado.</exception>
    public static void Atualizar(Cronograma cronograma)
    {
        var inicioDoProjeto = cronograma.DataInicio;
        var subtarefas = cronograma.Tarefas.Where(t => t.TarefaPaiId is not null).ToLookup(t => t.TarefaPaiId!.Value);
        var agregados = new Dictionary<Guid, Agregado>();

        for (var i = cronograma.Tarefas.Count - 1; i >= 0; i--)
        {
            var tarefa = cronograma.Tarefas[i];
            if (!tarefa.EhResumo)
            {
                agregados[tarefa.Id] = Agregado.DaFolha(tarefa, inicioDoProjeto);
                continue;
            }

            var agregado = subtarefas[tarefa.Id].Select(s => agregados[s.Id]).Aggregate(Agregado.Juntar);
            agregados[tarefa.Id] = agregado;
            Aplicar(tarefa, agregado, inicioDoProjeto);
        }
    }

    private static void Aplicar(Tarefa resumo, Agregado agregado, DateTime inicioDoProjeto)
    {
        DateTime Data(long instante) => DiasUteis.Adicionar(inicioDoProjeto, (int)(instante >> 1))!.Value;

        var inicio = Data(agregado.InicioCedo);
        var termino = Data(agregado.TerminoCedo);
        var duracao = (int)(Fronteira(agregado.TerminoCedo) - Fronteira(agregado.InicioCedo));
        var percentual = agregado.DiasDeTrabalho > 0
            ? Arredondar((double)agregado.DiasConcluidos / agregado.DiasDeTrabalho)
            : Arredondar((double)agregado.SomaDosPercentuais / agregado.Folhas);

        resumo.DefinirValoresDoResumo(inicio, termino, duracao, percentual);
        resumo.DefinirValorDoResumo(agregado.TemValor ? agregado.Valor : null);
        resumo.DefinirDatasCalculadas(inicio, termino, Data(agregado.InicioTarde), Data(agregado.TerminoTarde),
            agregado.Folga);
    }

    // A fronteira entre dias úteis em que o instante está: o começo do dia d e o fim do dia d − 1 são a fronteira d.
    private static long Fronteira(long instante) => (instante + 1) >> 1;

    private static int Arredondar(double valor) => (int)Math.Round(valor, MidpointRounding.AwayFromZero);

    private readonly record struct Agregado(
        long InicioCedo,
        long TerminoCedo,
        long InicioTarde,
        long TerminoTarde,
        int Folga,
        long DiasDeTrabalho,
        long DiasConcluidos,
        long SomaDosPercentuais,
        int Folhas,
        decimal Valor,
        bool TemValor)
    {
        public static Agregado DaFolha(Tarefa tarefa, DateTime inicioDoProjeto)
        {
            if (tarefa is not { InicioMaisCedo: { } inicioCedo, InicioMaisTarde: { } inicioTarde, Folga: { } folga })
                throw new InvalidOperationException("Calcule as tarefas antes dos resumos.");

            // Com prazo vencido, as datas tarde podem ficar antes do início do projeto (dia negativo).
            long Dia(DateTime data) => DiasUteis.Indice(inicioDoProjeto, data);
            var percentual = tarefa.PercentualConcluido;
            if (tarefa.Duracao == 0)
            {
                var cedo = 2 * Dia(inicioCedo) + (tarefa.MarcoNoFimDoDia ? 1 : 0);
                var tarde = 2 * Dia(inicioTarde) + (tarefa.MarcoTardeNoFimDoDia ? 1 : 0);
                return new Agregado(cedo, cedo, tarde, tarde, folga, 0, 0, percentual, 1, tarefa.Valor ?? 0, tarefa.Valor is not null);
            }

            return new Agregado(
                2 * Dia(inicioCedo), 2 * Dia(tarefa.TerminoMaisCedo!.Value) + 1,
                2 * Dia(inicioTarde), 2 * Dia(tarefa.TerminoMaisTarde!.Value) + 1,
                folga, tarefa.Duracao, (long)tarefa.Duracao * percentual, percentual, 1, tarefa.Valor ?? 0, tarefa.Valor is not null);
        }

        public static Agregado Juntar(Agregado a, Agregado b) => new(
            Math.Min(a.InicioCedo, b.InicioCedo),
            Math.Max(a.TerminoCedo, b.TerminoCedo),
            Math.Min(a.InicioTarde, b.InicioTarde),
            Math.Max(a.TerminoTarde, b.TerminoTarde),
            Math.Min(a.Folga, b.Folga),
            a.DiasDeTrabalho + b.DiasDeTrabalho,
            a.DiasConcluidos + b.DiasConcluidos,
            a.SomaDosPercentuais + b.SomaDosPercentuais,
            a.Folhas + b.Folhas,
            a.Valor + b.Valor,
            a.TemValor || b.TemValor);
    }
}
