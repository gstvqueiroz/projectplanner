namespace ProjectPlanner.Core.Edicao;

/// <summary>Uma tarefa copiada: o que vai para a tarefa nova ao colar (sem Id, sem fotos).</summary>
/// <param name="Original">O Id da tarefa de onde veio (para refazer a estrutura e os vínculos entre as copiadas).</param>
/// <param name="PaiOriginal">O pai dela entre as copiadas; nulo nas que estão no topo do trecho.</param>
/// <param name="NaoIniciarAntesDe">Bloco 31: a restrição de início.</param>
/// <param name="Prazo">Bloco 31: o prazo.</param>
/// <param name="Valor">Bloco 35: o valor (só nas tarefas que não são resumo).</param>
/// <param name="Unidade">Bloco 36: a unidade (só nas tarefas que não são resumo).</param>
/// <param name="Quantidade">Bloco 36: a quantidade (só nas tarefas que não são resumo).</param>
/// <param name="Atribuicoes">Bloco 32: os recursos (ao colar em outro projeto, só os que existem nele).</param>
public sealed record TarefaCopiada(
    Guid Original, Guid? PaiOriginal, string Nome, DateTime DataInicio, int Duracao, int PercentualConcluido, string? Notas, bool EhResumo,
    DateTime? NaoIniciarAntesDe = null, DateTime? Prazo = null, decimal? Valor = null, string? Unidade = null, decimal? Quantidade = null,
    IReadOnlyList<Recursos.AtribuicaoDeRecurso>? Atribuicoes = null);

/// <summary>
/// Bloco 30: tarefas copiadas, com as subtarefas e os vínculos entre elas (os vínculos com tarefas de fora ficam de
/// fora). Não guarda as tarefas em si: dá para colar em outro projeto, e mudar o original depois não muda a cópia.
/// </summary>
public sealed record TrechoCopiado(IReadOnlyList<TarefaCopiada> Tarefas, IReadOnlyList<Predecessora> Vinculos)
{
    /// <summary>As tarefas do topo do trecho (as que foram copiadas, sem contar as subtarefas delas).</summary>
    public int Raizes => Tarefas.Count(t => t.PaiOriginal is null);

    /// <summary>""Escavação"", ou ""Fundação" (com 3 subtarefas)", ou "3 tarefas".</summary>
    public string Descricao
    {
        get
        {
            if (Raizes != 1)
                return $"{Tarefas.Count} tarefas";
            var nome = $"\"{Tarefas[0].Nome}\"";
            return (Tarefas.Count - 1) switch
            {
                0 => nome,
                1 => $"{nome} (com 1 subtarefa)",
                var n => $"{nome} (com {n} subtarefas)",
            };
        }
    }
}

/// <summary>Bloco 30: copiar, colar e duplicar tarefas (com as subtarefas e os vínculos internos; sem as fotos).</summary>
public static class CopiaDeTarefas
{
    /// <summary>
    /// Copia as tarefas pedidas, cada uma com as subtarefas, na ordem do cronograma. Uma tarefa pedida que já está
    /// dentro de outra pedida vai uma vez só (com a de cima).
    /// </summary>
    /// <exception cref="DomainValidationException">Nenhuma tarefa, ou uma tarefa que não é do cronograma.</exception>
    public static TrechoCopiado Copiar(Cronograma cronograma, IEnumerable<Guid> tarefas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefas);
        var pedidas = tarefas.ToHashSet();
        if (pedidas.Count == 0)
            throw new DomainValidationException(nameof(Cronograma.Tarefas), "Selecione uma tarefa para copiar.");
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        foreach (var id in pedidas.Where(id => !porId.ContainsKey(id)))
            throw new DomainValidationException(nameof(Cronograma.Tarefas), $"A tarefa {id} não é deste cronograma.");

        bool DentroDeOutraPedida(Tarefa tarefa)
        {
            for (var pai = tarefa.TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
                if (pedidas.Contains(id))
                    return true;
            return false;
        }

        var raizes = pedidas.Select(id => porId[id]).Where(t => !DentroDeOutraPedida(t)).Select(t => t.Id).ToHashSet();
        var incluidas = new HashSet<Guid>();
        var copiadas = new List<TarefaCopiada>();
        foreach (var tarefa in cronograma.Tarefas) // na ordem do cronograma: o resumo antes das subtarefas
        {
            var ehRaiz = raizes.Contains(tarefa.Id);
            if (!ehRaiz && !(tarefa.TarefaPaiId is { } pai && incluidas.Contains(pai)))
                continue;
            incluidas.Add(tarefa.Id);
            copiadas.Add(new TarefaCopiada(tarefa.Id, ehRaiz ? null : tarefa.TarefaPaiId, tarefa.Nome, tarefa.DataInicio, tarefa.Duracao,
                tarefa.PercentualConcluido, tarefa.Notas, tarefa.EhResumo, tarefa.NaoIniciarAntesDe, tarefa.Prazo,
                tarefa.EhResumo ? null : tarefa.Valor, tarefa.EhResumo ? null : tarefa.Unidade, tarefa.EhResumo ? null : tarefa.Quantidade,
                tarefa.Atribuicoes.Count == 0 ? null : [.. tarefa.Atribuicoes]));
        }
        var vinculos = cronograma.Predecessoras
            .Where(p => incluidas.Contains(p.TarefaOrigemId) && incluidas.Contains(p.TarefaDestinoId))
            .ToList();
        return new TrechoCopiado(copiadas, vinculos);
    }

    /// <summary>
    /// Cola o trecho como irmãs das tarefas de <paramref name="paiId"/> (nulo = primeiro nível), a partir da posição
    /// <paramref name="posicao"/> entre elas (a contagem de <see cref="Cronograma.InserirTarefaNaPosicao"/>), em tarefas
    /// novas (Ids novos), com as subtarefas e os vínculos entre elas. Se algo for recusado, o cronograma não muda. Com o
    /// contrato travado (Bloco 36: há medição aprovada), as cópias vêm sem valor e sem quantidade (itens novos só por aditivo).
    /// </summary>
    /// <returns>As tarefas novas, na ordem do trecho (a primeira é a primeira do topo).</returns>
    /// <exception cref="DomainValidationException">A posição não existe, ou <paramref name="paiId"/> não pode ter subtarefas.</exception>
    public static IReadOnlyList<Tarefa> Colar(Cronograma cronograma, TrechoCopiado trecho, Guid? paiId, int posicao)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(trecho);
        if (trecho.Tarefas.Count == 0)
            return [];
        var irmas = cronograma.Tarefas.Count(t => t.TarefaPaiId == paiId);
        if (posicao < 0 || posicao > irmas)
            throw new DomainValidationException(nameof(Cronograma.Tarefas),
                $"A posição {posicao} não existe: o grupo tem {irmas} tarefa(s), então vai de 0 a {irmas}.");

        var novas = new Dictionary<Guid, Tarefa>();
        var criadas = new List<Tarefa>();
        try
        {
            var raiz = 0;
            foreach (var copiada in trecho.Tarefas)
            {
                // Um resumo nasce como tarefa de 1 dia e vira resumo ao receber a primeira subtarefa.
                var tarefa = new Tarefa(copiada.Nome, copiada.DataInicio, copiada.EhResumo ? 1 : copiada.Duracao);
                tarefa.AlterarNotas(copiada.Notas);
                if (copiada.PaiOriginal is { } paiOriginal)
                    cronograma.AdicionarTarefa(tarefa, novas[paiOriginal].Id);
                else
                    cronograma.InserirTarefaNaPosicao(tarefa, paiId, posicao + raiz++);
                novas[copiada.Original] = tarefa;
                criadas.Add(tarefa);
            }
            foreach (var copiada in trecho.Tarefas)
            {
                var nova = novas[copiada.Original];
                nova.DefinirPrazo(copiada.Prazo);
                if (copiada.EhResumo)
                    continue;
                nova.AtualizarPercentualConcluido(copiada.PercentualConcluido);
                nova.DefinirRestricaoDeInicio(copiada.NaoIniciarAntesDe);
                nova.DefinirUnidade(copiada.Unidade);
                if (copiada.Atribuicoes is { } atribuicoes)
                    cronograma.Recursos.Atribuir(nova, atribuicoes.Where(a => cronograma.Recursos.Obter(a.RecursoId) is not null));
                if (cronograma.Contrato.Travado)
                    continue;
                nova.DefinirValor(copiada.Valor);
                nova.DefinirQuantidade(copiada.Quantidade);
            }
            foreach (var vinculo in trecho.Vinculos)
                cronograma.AdicionarPredecessora(new Predecessora(novas[vinculo.TarefaOrigemId].Id, novas[vinculo.TarefaDestinoId].Id,
                    vinculo.Tipo, vinculo.Lag));
        }
        catch
        {
            // Tira o que entrou (as raízes levam as subtarefas e os vínculos junto).
            foreach (var tarefa in criadas.Where(t => cronograma.Tarefas.Contains(t) && (t.TarefaPaiId is null || !criadas.Any(c => c.Id == t.TarefaPaiId))))
                cronograma.RemoverTarefa(tarefa.Id);
            throw;
        }
        return criadas;
    }
}
