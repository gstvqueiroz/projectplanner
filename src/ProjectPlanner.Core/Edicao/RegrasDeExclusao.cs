namespace ProjectPlanner.Core.Edicao;

/// <summary>Bloco 72: o que fazer com um resumo selecionado para excluir (E3).</summary>
public enum ModoDosResumos
{
    /// <summary>Some o resumo e tudo dentro dele, em todos os níveis (o padrão, como no MS Project).</summary>
    GrupoInteiro,

    /// <summary>As subtarefas ficam no mesmo lugar e sobem um nível.</summary>
    SoOResumo,
}

/// <summary>
/// Bloco 72: o plano de uma exclusão, antes de aplicar: o que sai, quantas subtarefas vão junto, quantas têm progresso ou
/// fotos e se é preciso perguntar. A janela mostra os números do plano; o comando só aplica depois do OK.
/// </summary>
/// <param name="Selecionadas">As selecionadas que contam (um resumo e subtarefas dele selecionados juntos contam uma vez), na
/// ordem do cronograma.</param>
/// <param name="Resumos">Os resumos entre as selecionadas.</param>
/// <param name="Subtarefas">As subtarefas (em todos os níveis) dos resumos selecionados.</param>
/// <param name="Total">Quantas tarefas saem excluindo os grupos inteiros.</param>
/// <param name="ComProgresso">Das que saem (grupos inteiros), as que têm % concluído acima de 0.</param>
/// <param name="Fotos">As fotos das que saem (grupos inteiros): saem junto, e o Desfazer as traz de volta.</param>
/// <param name="ComProgressoSoResumos">Excluindo só os resumos: as selecionadas que saem com progresso.</param>
/// <param name="FotosSoResumos">Excluindo só os resumos: as fotos das selecionadas que saem.</param>
public sealed record PlanoDeExclusao(
    IReadOnlyList<Tarefa> Selecionadas,
    IReadOnlyList<Tarefa> Resumos,
    int Subtarefas,
    int Total,
    int ComProgresso,
    int Fotos,
    int ComProgressoSoResumos,
    int FotosSoResumos)
{
    public bool Vazio => Selecionadas.Count == 0;

    public bool TemResumo => Resumos.Count > 0;

    /// <summary>Sem resumo, progresso, fotos nem mais de <see cref="RegrasDeExclusao.LimiteSemPerguntar"/> tarefas: exclui na hora (E2).</summary>
    public bool PedeConfirmacao => TemResumo || ComProgresso > 0 || Fotos > 0 || Total > RegrasDeExclusao.LimiteSemPerguntar;

    /// <summary>A pergunta da janela do resumo (E3): "Excluir 'Fundação' e suas 7 subtarefas?".</summary>
    public string PerguntaDoResumo
    {
        get
        {
            var subtarefas = Subtarefas == 1 ? "sua subtarefa" : $"suas {Subtarefas} subtarefas";
            var quais = Resumos.Count == 1 && Selecionadas.Count == 1
                ? $"Excluir \"{Resumos[0].Nome}\" e {subtarefas}?"
                : Resumos.Count == 1
                    ? $"Excluir \"{Resumos[0].Nome}\" e {subtarefas}, e mais {Selecionadas.Count - 1} {(Selecionadas.Count == 2 ? "tarefa" : "tarefas")}?"
                    : $"Excluir {Resumos.Count} resumos e {subtarefas}" + (Selecionadas.Count > Resumos.Count ? $", e mais {Selecionadas.Count - Resumos.Count} {(Selecionadas.Count - Resumos.Count == 1 ? "tarefa" : "tarefas")}?" : "?");
            return quais + Detalhes(ComProgresso, Fotos) +
                   $"\n\n\"{(Resumos.Count == 1 ? "Excluir só o resumo" : "Excluir só os resumos")}\" mantém as subtarefas no mesmo lugar, um nível acima.";
        }
    }

    /// <summary>A pergunta sem resumo: o progresso registrado, as fotos ou a quantidade (E2).</summary>
    public string PerguntaSemResumo =>
        (Total > RegrasDeExclusao.LimiteSemPerguntar ? $"Excluir {Total} tarefas de uma vez?" : Total == 1 ? $"Excluir \"{Selecionadas[0].Nome}\"?" : $"Excluir {Total} tarefas?")
        + Detalhes(ComProgresso, Fotos);

    private static string Detalhes(int progresso, int fotos) =>
        (progresso switch
        {
            0 => "",
            1 => "\n1 tarefa tem progresso registrado.",
            _ => $"\n{progresso} tarefas têm progresso registrado.",
        })
        + (fotos switch
        {
            0 => "",
            1 => "\nA foto sai junto (o Desfazer a traz de volta).",
            _ => $"\nAs {fotos} fotos saem junto (o Desfazer as traz de volta).",
        });
}

/// <summary>Bloco 72: o que a exclusão fez, para o aviso com o Desfazer.</summary>
/// <param name="Excluidas">Quantas tarefas saíram.</param>
/// <param name="Subiram">Excluindo só o resumo: quantas subtarefas diretas subiram um nível.</param>
/// <param name="VinculosRefeitos">Os vínculos novos de uma sequência simples (E5), como "Escavação → Sapatas".</param>
/// <param name="SemPredecessora">As sucessoras que perderam uma predecessora (podem mudar de data).</param>
/// <param name="ViraramTarefa">Os resumos que ficaram vazios e viraram tarefa comum (E4).</param>
public sealed record ResultadoDaExclusao(
    int Excluidas, string Descricao, int Subiram, IReadOnlyList<string> VinculosRefeitos, IReadOnlyList<string> SemPredecessora,
    IReadOnlyList<string> ViraramTarefa)
{
    /// <summary>"Tarefa "Reboco" excluída. Vínculo refeito: Alvenaria → Pintura."</summary>
    public string Texto =>
        Descricao
        + (VinculosRefeitos.Count == 0 ? "" : $" {(VinculosRefeitos.Count == 1 ? "Vínculo refeito" : "Vínculos refeitos")}: {string.Join("; ", VinculosRefeitos)}.")
        + (SemPredecessora.Count == 0 ? "" : $" Podem mudar de data: {string.Join(", ", SemPredecessora)}.")
        + (ViraramTarefa.Count == 0 ? "" : $" {string.Join(", ", ViraramTarefa)} {(ViraramTarefa.Count == 1 ? "virou tarefa comum" : "viraram tarefas comuns")}.");
}

/// <summary>
/// Bloco 72: as regras para excluir tarefas (o plano "Excluir com Delete", E1 a E8), iguais na grade, no Gantt e no Kanban.
/// <see cref="Planejar"/> só olha o cronograma e devolve o <see cref="PlanoDeExclusao"/>; <see cref="Aplicar"/> muda o
/// cronograma (de preferência uma cópia, que entra no lugar como um passo só do Desfazer).
/// </summary>
/// <remarks>
/// Vínculos (E5): numa sequência simples (a tarefa excluída tem uma predecessora e uma sucessora, as duas Término-Início), as
/// pontas são religadas, com a defasagem que havia entre a excluída e a sucessora: em A → B → C, excluir B cria A → C. Nos
/// demais casos os vínculos saem, e o resultado lista as sucessoras que perderam a predecessora.
/// </remarks>
public static class RegrasDeExclusao
{
    /// <summary>Acima disto, pergunta antes (protege contra selecionar tudo e apertar Delete).</summary>
    public const int LimiteSemPerguntar = 10;

    public static PlanoDeExclusao Planejar(Cronograma cronograma, IEnumerable<Guid> selecionadas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(selecionadas);
        var ids = selecionadas.ToHashSet();
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        bool TemAncestralSelecionado(Tarefa t)
        {
            for (var pai = t.TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
                if (ids.Contains(id))
                    return true;
            return false;
        }
        var contam = cronograma.Tarefas.Where(t => ids.Contains(t.Id) && !TemAncestralSelecionado(t)).ToList();
        var resumos = contam.Where(t => t.EhResumo).ToList();
        var subtarefas = resumos.SelectMany(r => cronograma.DescendentesDe(r.Id)).ToList();
        var saem = contam.Concat(subtarefas).ToList();
        var soResumos = contam.Where(t => !t.EhResumo).ToList();
        return new PlanoDeExclusao(contam, resumos, subtarefas.Count, saem.Count,
            saem.Count(t => !t.EhResumo && t.PercentualConcluido > 0), saem.Sum(t => t.Fotos.Count),
            soResumos.Count(t => t.PercentualConcluido > 0), soResumos.Sum(t => t.Fotos.Count));
    }

    /// <summary>Aplica o plano (feito sobre este mesmo cronograma ou uma cópia dele: vale pelos Ids).</summary>
    /// <exception cref="DomainValidationException">O domínio recusou algo (o cronograma pode ter ficado pela metade: use uma cópia).</exception>
    public static ResultadoDaExclusao Aplicar(Cronograma cronograma, PlanoDeExclusao plano, ModoDosResumos modo)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(plano);
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var selecionadas = plano.Selecionadas.Where(t => porId.ContainsKey(t.Id)).Select(t => porId[t.Id]).ToList();
        var resumosAntes = ResumosAntesDaMudanca.Guardar(cronograma);
        var refeitos = new List<string>();
        var perderam = new List<Guid>();

        // As folhas que saem, na ordem do cronograma (e os resumos que saem inteiros, depois delas).
        var resumos = selecionadas.Where(t => t.EhResumo).ToList();
        var folhas = modo == ModoDosResumos.GrupoInteiro
            ? selecionadas.SelectMany(t => t.EhResumo ? cronograma.DescendentesDe(t.Id).Where(d => !d.EhResumo) : [t]).ToList()
            : selecionadas.Where(t => !t.EhResumo).ToList();
        var saem = new HashSet<Guid>(folhas.Select(t => t.Id));
        if (modo == ModoDosResumos.GrupoInteiro)
            foreach (var r in resumos)
            {
                saem.Add(r.Id);
                foreach (var d in cronograma.DescendentesDe(r.Id))
                    saem.Add(d.Id);
            }

        var subiram = 0;
        if (modo == ModoDosResumos.SoOResumo)
            foreach (var r in resumos)
                subiram += cronograma.RemoverSoOResumo(r.Id);

        foreach (var b in folhas)
        {
            var antes = cronograma.PredecessorasDe(b.Id);
            var depois = cronograma.Predecessoras.Where(p => p.TarefaOrigemId == b.Id).ToList();
            if (antes is [{ Tipo: TipoDependencia.FS } entrada] && depois is [{ Tipo: TipoDependencia.FS } saida]
                && !cronograma.Predecessoras.Any(p => p.TarefaOrigemId == entrada.TarefaOrigemId && p.TarefaDestinoId == saida.TarefaDestinoId))
            {
                cronograma.RemoverTarefa(b.Id);
                cronograma.AdicionarPredecessora(new Predecessora(entrada.TarefaOrigemId, saida.TarefaDestinoId, TipoDependencia.FS, saida.Lag));
                if (!saem.Contains(entrada.TarefaOrigemId) && !saem.Contains(saida.TarefaDestinoId))
                    refeitos.Add($"{porId[entrada.TarefaOrigemId].Nome} → {porId[saida.TarefaDestinoId].Nome}");
                continue;
            }
            perderam.AddRange(depois.Select(p => p.TarefaDestinoId));
            cronograma.RemoverTarefa(b.Id);
        }
        if (modo == ModoDosResumos.GrupoInteiro)
            foreach (var r in resumos.Where(r => cronograma.Tarefas.Any(t => t.Id == r.Id)))
                cronograma.RemoverTarefa(r.Id);

        var viraram = resumosAntes.Aplicar(cronograma);
        var semPredecessora = perderam.Distinct().Where(id => !saem.Contains(id) && cronograma.Tarefas.Any(t => t.Id == id))
            .Select(id => porId[id].Nome).ToList();
        var excluidas = modo == ModoDosResumos.GrupoInteiro ? saem.Count : folhas.Count + resumos.Count;
        return new ResultadoDaExclusao(excluidas, Descrever(selecionadas, resumos, modo, excluidas, subiram), subiram, refeitos, semPredecessora, viraram);
    }

    private static string Descrever(IReadOnlyList<Tarefa> selecionadas, IReadOnlyList<Tarefa> resumos, ModoDosResumos modo, int excluidas, int subiram)
    {
        if (modo == ModoDosResumos.SoOResumo && resumos.Count > 0)
        {
            var quais = resumos.Count == 1 ? $"Resumo \"{resumos[0].Nome}\" excluído" : $"{resumos.Count} resumos excluídos";
            var outras = selecionadas.Count - resumos.Count;
            return quais + (outras == 0 ? "" : outras == 1 ? " (e mais 1 tarefa)" : $" (e mais {outras} tarefas)")
                   + $"; {(subiram == 1 ? "1 subtarefa subiu" : $"{subiram} subtarefas subiram")} um nível.";
        }
        if (selecionadas.Count == 1)
        {
            var t = selecionadas[0];
            var junto = excluidas - 1;
            return resumos.Contains(t) // o resumo já virou tarefa ao perder as subtarefas
                ? $"\"{t.Nome}\" e {(junto == 1 ? "1 subtarefa excluídos" : $"{junto} subtarefas excluídos")}."
                : $"{(t.EhMarco ? "Marco" : "Tarefa")} \"{t.Nome}\" excluíd{(t.EhMarco ? "o" : "a")}.";
        }
        return $"{excluidas} tarefas excluídas.";
    }
}
