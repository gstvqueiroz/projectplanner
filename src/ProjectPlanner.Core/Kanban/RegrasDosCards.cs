namespace ProjectPlanner.Core.Kanban;

/// <summary>
/// Bloco 77 (o plano "Delete, arrastar e Kanban", K4 e K5): o conteúdo dos cards e as etiquetas do projeto. O checklist não muda o
/// % concluído (decisão do plano: "Não altera o %"); arquivar esconde o card do quadro, mas a tarefa continua no cronograma; as
/// etiquetas são do projeto (nome e cor) e cada card marca as suas. Tudo conferido antes de mudar: recusado, nada muda.
/// </summary>
public static class RegrasDosCards
{
    /// <summary>Quantos itens um checklist pode ter.</summary>
    public const int ItensMaximos = 50;

    /// <summary>Quantos caracteres um item do checklist pode ter.</summary>
    public const int TamanhoMaximoDoItem = 200;

    /// <summary>Quantas etiquetas um projeto pode ter.</summary>
    public const int EtiquetasMaximas = 30;

    /// <summary>A ordem em que as etiquetas novas ganham cor: as vivas primeiro, o cinza por último.</summary>
    public static IReadOnlyList<string> CoresDasEtiquetas { get; } =
        ["#E5484D", "#F76B15", "#F5A524", "#30A46C", "#2F80ED", "#8E4EC6", "#D6409F", "#8A8F98"];

    /// <summary>O checklist do card (os textos sem espaços nas pontas). Não muda o % da tarefa.</summary>
    /// <exception cref="DomainValidationException">Item vazio ou longo demais, itens demais, ou a tarefa não tem card.</exception>
    public static void DefinirChecklist(Cronograma cronograma, Guid tarefaId, IEnumerable<ItemDoChecklist> itens)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(itens);
        var lista = itens.Select(i => i with { Texto = (i.Texto ?? "").Trim() }).ToList();
        if (lista.Exists(i => i.Texto.Length == 0))
            throw new DomainValidationException(nameof(CardDoQuadro.Checklist), "Um item do checklist precisa de um texto.");
        if (lista.FirstOrDefault(i => i.Texto.Length > TamanhoMaximoDoItem) is { } longo)
            throw new DomainValidationException(nameof(CardDoQuadro.Checklist),
                $"O item do checklist tem {longo.Texto.Length} caracteres; o máximo é {TamanhoMaximoDoItem}.");
        if (lista.Count > ItensMaximos)
            throw new DomainValidationException(nameof(CardDoQuadro.Checklist), $"O checklist pode ter até {ItensMaximos} itens.");
        cronograma.Quadro.AlterarCard(tarefaId, c => c with { Checklist = lista });
    }

    /// <summary>Marca ou desmarca uma etiqueta do projeto no card.</summary>
    /// <exception cref="DomainValidationException">A etiqueta não é do projeto, ou a tarefa não tem card.</exception>
    public static void MarcarEtiqueta(Cronograma cronograma, Guid tarefaId, Guid etiquetaId, bool marcada)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        if (!cronograma.Quadro.Etiquetas.Any(e => e.Id == etiquetaId))
            throw new DomainValidationException(nameof(QuadroDoProjeto.Etiquetas), "A etiqueta não existe mais no projeto.");
        cronograma.Quadro.AlterarCard(tarefaId, c => c with
        {
            Etiquetas = marcada ? [.. c.Etiquetas.Where(e => e != etiquetaId), etiquetaId] : [.. c.Etiquetas.Where(e => e != etiquetaId)],
        });
    }

    /// <summary>Esconde os cards do quadro (as tarefas continuam no cronograma, com o mesmo %).</summary>
    /// <returns>Quantos foram arquivados (os que já estavam ficam como estão).</returns>
    public static int Arquivar(Cronograma cronograma, IEnumerable<Guid> tarefas) => DefinirArquivado(cronograma, tarefas, true);

    /// <summary>Os cards voltam para o fim da coluna em que estavam (ou da coluna do %, se ela não combina mais).</summary>
    /// <returns>Quantos voltaram.</returns>
    public static int Desarquivar(Cronograma cronograma, IEnumerable<Guid> tarefas) => DefinirArquivado(cronograma, tarefas, false);

    private static int DefinirArquivado(Cronograma cronograma, IEnumerable<Guid> tarefas, bool arquivado)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefas);
        var quadro = cronograma.Quadro;
        var mudaram = 0;
        foreach (var id in tarefas.Distinct().ToList())
        {
            if (quadro.CardDe(id) is not { } card || card.Arquivado == arquivado)
                continue;
            quadro.AlterarCard(id, c => arquivado ? c with { Arquivado = true } : c with { Arquivado = false, Ordem = quadro.OrdemNoFimDe(c.ColunaId) });
            mudaram++;
        }
        quadro.Sincronizar();
        return mudaram;
    }

    /// <summary>Uma etiqueta nova do projeto, com uma cor ainda sem uso (ou a dada).</summary>
    /// <exception cref="DomainValidationException">Nome vazio, longo demais ou repetido, cor inválida, ou etiquetas demais.</exception>
    public static EtiquetaDoQuadro CriarEtiqueta(Cronograma cronograma, string nome, string? cor = null)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var etiquetas = cronograma.Quadro.Etiquetas.ToList();
        if (etiquetas.Count >= EtiquetasMaximas)
            throw new DomainValidationException(nameof(QuadroDoProjeto.Etiquetas), $"O projeto pode ter até {EtiquetasMaximas} etiquetas.");
        ConferirNome(cronograma, null, nome);
        var nova = new EtiquetaDoQuadro(Guid.NewGuid(), nome ?? "",
            cor ?? CoresDasEtiquetas.FirstOrDefault(c => !etiquetas.Exists(e => e.Cor == c)) ?? CoresDasEtiquetas[etiquetas.Count % CoresDasEtiquetas.Count]);
        cronograma.Quadro.TrocarEtiquetas([.. etiquetas, nova]);
        return cronograma.Quadro.Etiquetas.Single(e => e.Id == nova.Id);
    }

    /// <exception cref="DomainValidationException">Nome vazio, longo demais ou repetido.</exception>
    public static void RenomearEtiqueta(Cronograma cronograma, Guid etiquetaId, string nome)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ConferirNome(cronograma, etiquetaId, nome);
        TrocarEtiqueta(cronograma, etiquetaId, e => e with { Nome = nome ?? "" });
    }

    /// <exception cref="DomainValidationException">Cor inválida.</exception>
    public static void ColorirEtiqueta(Cronograma cronograma, Guid etiquetaId, string cor) =>
        TrocarEtiqueta(cronograma, etiquetaId, e => e with { Cor = cor });

    /// <summary>Exclui a etiqueta do projeto e tira de todos os cards.</summary>
    /// <returns>Em quantos cards ela estava.</returns>
    public static int ExcluirEtiqueta(Cronograma cronograma, Guid etiquetaId)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        Obter(cronograma, etiquetaId);
        var cards = cronograma.Quadro.Cards.Count(c => c.Etiquetas.Contains(etiquetaId));
        cronograma.Quadro.TrocarEtiquetas(cronograma.Quadro.Etiquetas.Where(e => e.Id != etiquetaId));
        return cards;
    }

    private static EtiquetaDoQuadro Obter(Cronograma cronograma, Guid etiquetaId) =>
        cronograma.Quadro.Etiquetas.FirstOrDefault(e => e.Id == etiquetaId)
        ?? throw new DomainValidationException(nameof(QuadroDoProjeto.Etiquetas), "A etiqueta não existe mais no projeto.");

    private static void TrocarEtiqueta(Cronograma cronograma, Guid etiquetaId, Func<EtiquetaDoQuadro, EtiquetaDoQuadro> mudanca)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        Obter(cronograma, etiquetaId);
        cronograma.Quadro.TrocarEtiquetas(cronograma.Quadro.Etiquetas.Select(e => e.Id == etiquetaId ? mudanca(e) : e));
    }

    private static void ConferirNome(Cronograma cronograma, Guid? etiquetaId, string? nome)
    {
        var limpo = (nome ?? "").Trim();
        if (cronograma.Quadro.Etiquetas.FirstOrDefault(e => e.Id != etiquetaId && string.Equals(e.Nome, limpo, StringComparison.CurrentCultureIgnoreCase)) is { } existente)
            throw new DomainValidationException(nameof(QuadroDoProjeto.Etiquetas), $"Já existe a etiqueta \"{existente.Nome}\" no projeto.");
    }
}
