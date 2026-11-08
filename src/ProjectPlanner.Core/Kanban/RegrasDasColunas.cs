namespace ProjectPlanner.Core.Kanban;

/// <summary>Bloco 76: o que excluir uma coluna faz: quantos cards ela tem, para onde eles podem ir e, se não pode, o motivo.</summary>
public sealed record PlanoDeExclusaoDaColuna(ColunaDoQuadro Coluna, int Cards, IReadOnlyList<ColunaDoQuadro> Destinos, string? Motivo);

/// <summary>Bloco 76: o que mudou nas colunas (para a barra de status) e se algum % concluído mudou (aí o cronograma recalcula).</summary>
public sealed record ResultadoDaColuna(string Texto, bool MudouPercentual);

/// <summary>
/// Bloco 76 (o plano "Delete, arrastar e Kanban", K2): gerenciar as colunas do quadro: criar, renomear, colorir, pôr um limite de
/// cards (WIP), reordenar, mudar o tipo e excluir. Cada mudança é conferida antes de aplicar (recusada, nada muda), e o quadro
/// continua com pelo menos uma coluna de cada tipo e nomes diferentes. Mudar o tipo de uma coluna não muda o % de ninguém: só
/// vale se os cards dela já combinam com o tipo novo. Excluir uma coluna com cards pede para onde eles vão, e lá vale a regra de
/// sempre do % (K3).
/// </summary>
public static class RegrasDasColunas
{
    /// <summary>As cores que o menu da coluna oferece.</summary>
    public static IReadOnlyList<(string Nome, string Cor)> Paleta { get; } =
    [
        ("Cinza", "#8A8F98"), ("Azul", "#2F80ED"), ("Verde", "#30A46C"), ("Amarelo", "#F5A524"),
        ("Laranja", "#F76B15"), ("Vermelho", "#E5484D"), ("Roxo", "#8E4EC6"), ("Rosa", "#D6409F"),
    ];

    /// <summary>
    /// Uma coluna nova do tipo Em andamento (como uma "Em revisão"), antes da primeira Concluída, com uma cor que ainda não está em
    /// uso. O tipo e o lugar mudam depois pelo menu da coluna.
    /// </summary>
    /// <exception cref="DomainValidationException">Nome vazio, longo demais ou de outra coluna.</exception>
    public static ColunaDoQuadro Adicionar(Cronograma cronograma, string nome)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ConferirNome(cronograma, null, nome);
        var colunas = cronograma.Quadro.Colunas.ToList();
        var nova = new ColunaDoQuadro(Guid.NewGuid(), nome ?? "", TipoDeColuna.EmAndamento,
            Paleta.Select(p => p.Cor).FirstOrDefault(cor => !colunas.Exists(c => c.Cor == cor)) ?? Paleta[1].Cor);
        var concluida = colunas.FindIndex(c => c.Tipo == TipoDeColuna.Concluida);
        colunas.Insert(concluida < 0 ? colunas.Count : concluida, nova);
        cronograma.Quadro.TrocarColunas(colunas);
        return cronograma.Quadro.Coluna(nova.Id)!;
    }

    /// <exception cref="DomainValidationException">Nome vazio, longo demais ou de outra coluna.</exception>
    public static void Renomear(Cronograma cronograma, Guid colunaId, string nome)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ConferirNome(cronograma, colunaId, nome);
        Trocar(cronograma, colunaId, c => c with { Nome = nome ?? "" });
    }

    // O nome de outra coluna (sem diferença de maiúsculas): o aviso diz o nome como ela está no quadro.
    private static void ConferirNome(Cronograma cronograma, Guid? colunaId, string? nome)
    {
        var limpo = (nome ?? "").Trim();
        if (cronograma.Quadro.Colunas.FirstOrDefault(c => c.Id != colunaId && string.Equals(c.Nome, limpo, StringComparison.CurrentCultureIgnoreCase)) is { } existente)
            throw new DomainValidationException(nameof(QuadroDoProjeto.Colunas), $"Já existe a coluna \"{existente.Nome}\" no quadro.");
    }

    /// <summary>A cor (#RRGGBB); nula, a cinza do tema.</summary>
    /// <exception cref="DomainValidationException">Não é uma cor.</exception>
    public static void Colorir(Cronograma cronograma, Guid colunaId, string? cor) => Trocar(cronograma, colunaId, c => c with { Cor = cor });

    /// <summary>O limite de cards (WIP): passou dele, a coluna fica destacada; nulo, sem limite.</summary>
    /// <exception cref="DomainValidationException">Limite menor que 1.</exception>
    public static void DefinirLimite(Cronograma cronograma, Guid colunaId, int? limite) =>
        Trocar(cronograma, colunaId, c => c with { Limite = limite });

    /// <summary>Leva a coluna uma posição para a esquerda (−1) ou para a direita (+1).</summary>
    /// <returns>Se mudou (na ponta, não muda).</returns>
    public static bool Mover(Cronograma cronograma, Guid colunaId, int passo)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var colunas = cronograma.Quadro.Colunas.ToList();
        var de = colunas.FindIndex(c => c.Id == colunaId);
        var para = de + Math.Sign(passo);
        if (de < 0 || passo == 0 || para < 0 || para >= colunas.Count)
            return false;
        (colunas[de], colunas[para]) = (colunas[para], colunas[de]);
        cronograma.Quadro.TrocarColunas(colunas);
        return true;
    }

    /// <summary>Muda o tipo da coluna, se os cards dela já combinam com ele (nenhum % muda por isso).</summary>
    /// <exception cref="DomainValidationException">Um card não combina com o tipo novo, ou era a última coluna do tipo antigo.</exception>
    public static void MudarTipo(Cronograma cronograma, Guid colunaId, TipoDeColuna tipo)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var coluna = Obter(cronograma, colunaId);
        if (coluna.Tipo == tipo)
            return;
        var tarefas = cronograma.Tarefas.Where(t => !t.EhResumo).ToDictionary(t => t.Id);
        var fora = cronograma.Quadro.Cards.Where(c => c.ColunaId == colunaId && tarefas.ContainsKey(c.TarefaId))
            .Select(c => tarefas[c.TarefaId]).FirstOrDefault(t => !QuadroDoProjeto.Aceita(tipo, t.PercentualConcluido));
        if (fora is not null)
            throw new DomainValidationException(nameof(QuadroDoProjeto.Colunas),
                $"A coluna \"{coluna.Nome}\" tem cards que não combinam com o tipo {QuadroDoProjeto.NomeDoTipo(tipo)} (\"{fora.Nome}\" está com " +
                $"{fora.PercentualConcluido}%). Mova esses cards antes de mudar o tipo.");
        Trocar(cronograma, colunaId, c => c with { Tipo = tipo });
    }

    /// <summary>O que excluir a coluna faria: os cards dela e para onde podem ir (primeiro as do mesmo tipo), ou o motivo de não poder.</summary>
    public static PlanoDeExclusaoDaColuna PlanejarExclusao(Cronograma cronograma, Guid colunaId)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var coluna = Obter(cronograma, colunaId);
        var outras = cronograma.Quadro.Colunas.Where(c => c.Id != colunaId).ToList();
        var motivo = outras.Exists(c => c.Tipo == coluna.Tipo) ? null
            : $"\"{coluna.Nome}\" é a única coluna do tipo {QuadroDoProjeto.NomeDoTipo(coluna.Tipo)}, e o quadro precisa de uma: é para onde vão " +
              $"as tarefas com {coluna.Tipo switch { TipoDeColuna.AFazer => "0%", TipoDeColuna.Concluida => "100%", _ => "1 a 99%" }}.";
        var destinos = outras.Where(c => c.Tipo == coluna.Tipo).Concat(outras.Where(c => c.Tipo != coluna.Tipo)).ToList();
        return new PlanoDeExclusaoDaColuna(coluna, cronograma.Quadro.CardsDa(colunaId).Count, destinos, motivo);
    }

    /// <summary>
    /// Exclui a coluna; os cards dela vão para o fim de <paramref name="destinoId"/>, com a regra do % da coluna de destino (K3). Os que
    /// não aparecem no quadro (arquivados) vão junto.
    /// </summary>
    /// <exception cref="DomainValidationException">Era a última coluna do tipo, ou tem cards e não há destino.</exception>
    public static ResultadoDaColuna Excluir(Cronograma cronograma, Guid colunaId, Guid? destinoId)
    {
        var plano = PlanejarExclusao(cronograma, colunaId);
        if (plano.Motivo is { } motivo)
            throw new DomainValidationException(nameof(QuadroDoProjeto.Colunas), motivo);
        var destino = destinoId is { } id && id != colunaId ? cronograma.Quadro.Coluna(id) : null;
        if (plano.Cards > 0 && destino is null)
            throw new DomainValidationException(nameof(QuadroDoProjeto.Colunas),
                $"Escolha para onde {(plano.Cards == 1 ? "vai o card" : $"vão os {plano.Cards} cards")} da coluna \"{plano.Coluna.Nome}\".");
        var quadro = cronograma.Quadro;
        var restantes = quadro.Colunas.Where(c => c.Id != colunaId).ToList();
        QuadroDoProjeto.ValidarColunas(restantes); // antes de mexer nos cards

        var texto = $"Coluna \"{plano.Coluna.Nome}\" excluída.";
        var mudouPercentual = false;
        if (plano.Cards > 0)
        {
            var movimento = RegrasDoQuadro.Planejar(cronograma, quadro.CardsDa(colunaId).Select(c => c.TarefaId), destino!.Id, int.MaxValue);
            mudouPercentual = movimento.Percentuais.Count > 0;
            texto += " " + RegrasDoQuadro.Aplicar(cronograma, movimento).Texto;
        }
        quadro.MoverOcultos(colunaId, (destino ?? plano.Destinos[0]).Id);
        quadro.TrocarColunas(restantes);
        quadro.Sincronizar();
        return new ResultadoDaColuna(texto, mudouPercentual);
    }

    private static ColunaDoQuadro Obter(Cronograma cronograma, Guid colunaId) =>
        cronograma.Quadro.Coluna(colunaId) ?? throw new DomainValidationException(nameof(QuadroDoProjeto.Colunas), "A coluna não existe mais no quadro.");

    private static void Trocar(Cronograma cronograma, Guid colunaId, Func<ColunaDoQuadro, ColunaDoQuadro> mudanca)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        Obter(cronograma, colunaId);
        cronograma.Quadro.TrocarColunas(cronograma.Quadro.Colunas.Select(c => c.Id == colunaId ? mudanca(c) : c));
    }
}
