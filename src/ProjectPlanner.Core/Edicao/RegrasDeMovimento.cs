namespace ProjectPlanner.Core.Edicao;

/// <summary>
/// Bloco 73: para onde vão as tarefas arrastadas, antes de soltar: se vale (ou o motivo de não valer), o nível, o resumo
/// (pai) e a irmã antes da qual entram, e a dica para o arrasto ("Tornar subtarefa de 'Escavação'").
/// </summary>
/// <param name="Lacuna">O espaço entre as linhas visíveis onde a guia aparece (0 é antes da primeira).</param>
/// <param name="Nivel">O nível que as tarefas vão ter (0 é o primeiro nível), para a guia já sair recuada.</param>
/// <param name="AntesDe">A irmã antes da qual entram; nula: no fim do grupo.</param>
/// <param name="ViraResumo">A tarefa comum que recebe as arrastadas e vira resumo; nula se nenhuma.</param>
public sealed record PlanoDeMovimento(
    bool Valido, string? Motivo, int Lacuna, int Nivel, Guid? Pai, Guid? AntesDe, IReadOnlyList<Guid> Tarefas, bool MudaAlgo, string Dica,
    string? ViraResumo)
{
    public static PlanoDeMovimento Invalido(int lacuna, string? motivo) => new(false, motivo, lacuna, 0, null, null, [], false, motivo ?? "", null);
}

/// <summary>Bloco 73: o que o movimento fez, para a barra de status.</summary>
public sealed record ResultadoDoMovimento(string Texto);

/// <summary>
/// Bloco 73: as regras para mover arrastando (o plano "Delete, arrastar e Kanban", M3 a M6), as mesmas na grade e no Gantt:
/// arrastar muda só a organização (ordem e hierarquia), nunca datas nem vínculos.
/// </summary>
/// <remarks>
/// <para>
/// O nível (M3): a tarefa mantém o nível que tinha, se couber na nova posição; se não couber, fica no nível válido mais perto.
/// Numa posição, o nível vai do da linha de baixo (para ela continuar onde está) ao da linha de cima; ela nunca transforma
/// sozinha a tarefa de cima em resumo. Puxar para a direita (<c>puxao</c> 1) põe a tarefa como subtarefa da linha de cima;
/// para a esquerda (−1), sobe um nível.
/// </para>
/// <para>
/// Bloqueados (M4): soltar um resumo dentro dele mesmo; pôr subtarefas num marco ou numa tarefa com vínculos (resumos não têm
/// vínculos). Várias arrastadas (M6) vão juntas, na ordem em que estavam; as subtarefas vão com o resumo.
/// </para>
/// </remarks>
public static class RegrasDeMovimento
{
    public const string DentroDeSiMesmo = "Não dá para soltar um resumo dentro dele mesmo.";

    /// <param name="visiveis">As linhas visíveis, na ordem (as de um resumo recolhido não estão).</param>
    /// <param name="lacuna">O espaço entre as linhas visíveis onde soltar: 0 é antes da primeira; a quantidade, depois da última.</param>
    /// <param name="puxao">1: puxada para a direita (subtarefa da linha de cima); −1: para a esquerda (sobe um nível); 0: nenhum.</param>
    public static PlanoDeMovimento Planejar(Cronograma cronograma, IReadOnlyList<Guid> visiveis, IEnumerable<Guid> arrastadas, int lacuna, int puxao = 0)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(visiveis);
        ArgumentNullException.ThrowIfNull(arrastadas);
        if (lacuna < 0 || lacuna > visiveis.Count)
            return PlanoDeMovimento.Invalido(lacuna, null);
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var ids = arrastadas.Where(porId.ContainsKey).ToHashSet();
        bool TemAncestral(Tarefa t, HashSet<Guid> conjunto)
        {
            for (var pai = t.TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
                if (conjunto.Contains(id))
                    return true;
            return false;
        }
        var bloco = cronograma.Tarefas.Where(t => ids.Contains(t.Id) && !TemAncestral(t, ids)).ToList();
        if (bloco.Count == 0)
            return PlanoDeMovimento.Invalido(lacuna, null);
        var noBloco = bloco.Select(t => t.Id).ToHashSet();
        foreach (var t in bloco)
            foreach (var d in cronograma.DescendentesDe(t.Id))
                noBloco.Add(d.Id);

        // Dentro de um resumo arrastado: entre a linha dele e a última visível dele.
        foreach (var t in bloco.Where(t => t.EhResumo))
        {
            var primeira = IndiceVisivel(visiveis, t.Id);
            var ultima = visiveis.Select((id, i) => (id, i)).Where(x => x.id == t.Id || cronograma.DescendentesDe(t.Id).Any(d => d.Id == x.id))
                .Select(x => x.i).DefaultIfEmpty(-1).Max();
            if (primeira >= 0 && lacuna > primeira && lacuna <= ultima)
                return PlanoDeMovimento.Invalido(lacuna, DentroDeSiMesmo);
        }

        // As linhas de cima e de baixo que ficam (fora do que é arrastado).
        Tarefa? cima = null, baixo = null;
        for (var i = lacuna - 1; i >= 0 && cima is null; i--)
            if (!noBloco.Contains(visiveis[i]))
                cima = porId[visiveis[i]];
        for (var i = lacuna; i < visiveis.Count && baixo is null; i++)
            if (!noBloco.Contains(visiveis[i]))
                baixo = porId[visiveis[i]];
        var nivelDeCima = cima is null ? -1 : cronograma.NivelDe(cima.Id);
        var nivelDeBaixo = baixo is null ? 0 : cronograma.NivelDe(baixo.Id);
        var minimo = Math.Max(0, nivelDeBaixo);
        var maximo = Math.Max(nivelDeCima, minimo);
        var principal = bloco[0];
        var nivel = puxao > 0 && cima is not null
            ? nivelDeCima + 1
            : Math.Clamp(cronograma.NivelDe(principal.Id) + (puxao < 0 ? -1 : 0), minimo, maximo);

        // O resumo (pai) desse nível: a linha de cima (subtarefa dela) ou o antepassado dela um nível acima.
        Tarefa? pai = null;
        if (nivel > 0 && cima is not null)
        {
            pai = cima;
            while (pai is not null && cronograma.NivelDe(pai.Id) >= nivel)
                pai = pai.TarefaPaiId is { } p ? porId[p] : null;
        }
        if (pai is not null && noBloco.Contains(pai.Id))
            return PlanoDeMovimento.Invalido(lacuna, DentroDeSiMesmo);
        string? viraResumo = null;
        if (pai is not null && !pai.EhResumo)
        {
            if (cronograma.MotivoParaNaoTerSubtarefas(pai.Id) is { } motivo)
                return PlanoDeMovimento.Invalido(lacuna, motivo);
            viraResumo = pai.Nome;
        }
        // A irmã de baixo, se a linha de baixo é do mesmo grupo; senão, o fim do grupo.
        Guid? antesDe = baixo is not null && baixo.TarefaPaiId == pai?.Id && cronograma.NivelDe(baixo.Id) == nivel ? baixo.Id : null;

        var mudaAlgo = !(bloco.All(t => t.TarefaPaiId == pai?.Id) && JaEstaoAli(cronograma, bloco, pai?.Id, antesDe));
        var quais = bloco.Count == 1 ? $"\"{principal.Nome}\"" : $"{bloco.Count} tarefas";
        var dica = viraResumo is not null ? $"Tornar subtarefa de \"{viraResumo}\""
            : !mudaAlgo ? $"{quais} já está aqui"
            : bloco.Any(t => t.TarefaPaiId != pai?.Id) ? pai is null ? $"Mover {quais} para o primeiro nível" : $"Mover {quais} para \"{pai.Nome}\""
            : $"Mover {quais}";
        return new PlanoDeMovimento(true, null, lacuna, nivel, pai?.Id, antesDe, [.. bloco.Select(t => t.Id)], mudaAlgo, dica, viraResumo);
    }

    /// <summary>Aplica o plano (no cronograma em que foi feito, ou numa cópia dele: vale pelos Ids).</summary>
    /// <exception cref="DomainValidationException">O domínio recusou algo (use uma cópia antes).</exception>
    public static ResultadoDoMovimento Aplicar(Cronograma cronograma, PlanoDeMovimento plano)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(plano);
        if (!plano.Valido || !plano.MudaAlgo)
            return new ResultadoDoMovimento("");
        var resumos = ResumosAntesDaMudanca.Guardar(cronograma);
        foreach (var id in plano.Tarefas)
        {
            var tarefa = cronograma.Tarefas.First(t => t.Id == id);
            if (tarefa.TarefaPaiId != plano.Pai)
                cronograma.DefinirPai(id, plano.Pai);
            var restantes = cronograma.Tarefas.Where(t => t.TarefaPaiId == plano.Pai && t.Id != id).ToList();
            var posicao = plano.AntesDe is { } antes ? restantes.FindIndex(t => t.Id == antes) : restantes.Count;
            cronograma.MoverEntreIrmas(id, posicao < 0 ? restantes.Count : posicao);
        }
        var viraram = resumos.Aplicar(cronograma);
        var edt = cronograma.NumeracaoEdt();
        var primeira = cronograma.Tarefas.First(t => t.Id == plano.Tarefas[0]);
        var texto = plano.Tarefas.Count == 1 ? $"\"{primeira.Nome}\" agora é a {edt[primeira.Id]}." : $"{plano.Tarefas.Count} tarefas movidas (a partir da {edt[primeira.Id]}).";
        if (plano.ViraResumo is { } resumo)
            texto += $" \"{resumo}\" virou resumo.";
        if (viraram.Count > 0)
            texto += $" {string.Join(", ", viraram.Select(n => $"\"{n}\""))} {(viraram.Count == 1 ? "virou tarefa comum" : "viraram tarefas comuns")}.";
        return new ResultadoDoMovimento(texto);
    }

    private static int IndiceVisivel(IReadOnlyList<Guid> visiveis, Guid id)
    {
        for (var i = 0; i < visiveis.Count; i++)
            if (visiveis[i] == id)
                return i;
        return -1;
    }

    // As tarefas já estão juntas, na ordem, no grupo e antes da irmã indicada (ou no fim).
    private static bool JaEstaoAli(Cronograma cronograma, IReadOnlyList<Tarefa> bloco, Guid? pai, Guid? antesDe)
    {
        var irmas = cronograma.Tarefas.Where(t => t.TarefaPaiId == pai).ToList();
        var inicio = irmas.IndexOf(bloco[0]);
        for (var i = 0; i < bloco.Count; i++)
            if (inicio + i >= irmas.Count || irmas[inicio + i] != bloco[i])
                return false;
        var depois = inicio + bloco.Count;
        return antesDe is { } id ? depois < irmas.Count && irmas[depois].Id == id : depois == irmas.Count;
    }
}
