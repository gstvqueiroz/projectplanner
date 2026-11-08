namespace ProjectPlanner.Core.Kanban;

/// <summary>Bloco 75: uma tarefa cujo % concluído muda ao mover o card (K3).</summary>
public sealed record MudancaDoPercentual(Guid TarefaId, string Nome, int Antes, int Depois);

/// <summary>
/// Bloco 75: o que acontece ao soltar cards numa coluna, antes de aplicar: a coluna, o lugar entre os cards dela (contado sem
/// os que estão sendo movidos), os % que mudam, se muda algo e a dica que o arraste mostra.
/// </summary>
public sealed record PlanoDoQuadro(bool Valido, string? Motivo, IReadOnlyList<Guid> Tarefas, Guid ColunaId, int Posicao,
    IReadOnlyList<MudancaDoPercentual> Percentuais, bool MudaAlgo, string Dica)
{
    /// <summary>Algum % voltou a 0 (o card foi para A fazer com progresso): o aviso oferece o Desfazer (K3).</summary>
    public bool ZeraProgresso => Percentuais.Any(p => p.Depois == 0 && p.Antes > 0);

    /// <summary>Só a ordem dos cards muda (nenhum % muda): o cronograma não precisa ser recalculado.</summary>
    public bool SoOQuadro => Percentuais.Count == 0;

    internal static PlanoDoQuadro Invalido(string? motivo) => new(false, motivo, [], Guid.Empty, 0, [], false, motivo ?? "");
}

/// <summary>Bloco 75: o que mudou (para a barra de status) e o nome do passo no Desfazer.</summary>
public sealed record ResultadoDoQuadro(string Texto, string Descricao);

/// <summary>
/// Bloco 75 (o plano "Delete, arrastar e Kanban", K3 e K5): mover cards no quadro, entre colunas e dentro delas. O % concluído
/// segue o card: entrar numa coluna Concluída põe 100% (e guarda o % de antes); sair de Concluída volta ao % de antes (0% se
/// não houver); entrar numa A fazer põe 0%; entrar numa Em andamento não muda o %. Reordenar dentro da coluna não muda nada no
/// cronograma. Como as outras regras de edição, primeiro um plano (que a tela mostra durante o arraste) e só depois aplicar.
/// </summary>
public static class RegrasDoQuadro
{
    /// <summary>
    /// Os cards das tarefas soltos na coluna, na lacuna <paramref name="lacuna"/> entre os cards dela (0 é antes do primeiro; o
    /// número de cards, depois do último). Vários vão juntos, na ordem em que estão no quadro. Tarefas sem card no quadro
    /// (resumos, arquivadas) ficam de fora. Não muda nada.
    /// </summary>
    public static PlanoDoQuadro Planejar(Cronograma cronograma, IEnumerable<Guid> tarefas, Guid colunaId, int lacuna)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefas);
        var quadro = cronograma.Quadro;
        if (quadro.Coluna(colunaId) is not { } destino)
            return PlanoDoQuadro.Invalido("A coluna não existe mais no quadro.");
        var porColuna = quadro.CardsPorColuna();
        var pedidas = tarefas.ToHashSet();
        var movidas = quadro.Colunas.SelectMany(c => porColuna[c.Id]).Select(c => c.TarefaId).Where(pedidas.Contains).ToList();
        if (movidas.Count == 0)
            return PlanoDoQuadro.Invalido(null);

        var naColuna = porColuna[colunaId].Select(c => c.TarefaId).ToList();
        lacuna = Math.Clamp(lacuna, 0, naColuna.Count);
        var posicao = lacuna - naColuna.Take(lacuna).Count(pedidas.Contains);
        var depois = naColuna.Where(id => !pedidas.Contains(id)).ToList();
        depois.InsertRange(posicao, movidas);
        var mudamDeColuna = movidas.Where(id => quadro.CardDe(id)!.ColunaId != colunaId).ToList();
        var mudaAlgo = mudamDeColuna.Count > 0 || !depois.SequenceEqual(naColuna);

        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var percentuais = new List<MudancaDoPercentual>();
        foreach (var id in mudamDeColuna)
        {
            var card = quadro.CardDe(id)!;
            var tarefa = porId[id];
            var origem = quadro.Coluna(card.ColunaId)?.Tipo;
            var antes = tarefa.PercentualConcluido;
            var novo = destino.Tipo switch
            {
                TipoDeColuna.Concluida => 100,
                TipoDeColuna.AFazer => 0,
                _ => origem == TipoDeColuna.Concluida ? card.PercentualAnterior ?? 0 : antes,
            };
            if (novo != antes)
                percentuais.Add(new MudancaDoPercentual(id, tarefa.Nome, antes, novo));
        }

        return new PlanoDoQuadro(true, null, movidas, colunaId, posicao, percentuais, mudaAlgo,
            Dica(destino, mudamDeColuna.Count, percentuais));
    }

    /// <summary>Aplica o plano: o % das tarefas e o lugar dos cards. O cronograma não é recalculado aqui.</summary>
    /// <exception cref="InvalidOperationException">O plano é inválido.</exception>
    public static ResultadoDoQuadro Aplicar(Cronograma cronograma, PlanoDoQuadro plano)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(plano);
        if (!plano.Valido)
            throw new InvalidOperationException(plano.Motivo ?? "Não há cards para mover.");
        var quadro = cronograma.Quadro;
        var destino = quadro.Coluna(plano.ColunaId) ?? throw new InvalidOperationException("A coluna não existe mais no quadro.");
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        var mudamDeColuna = plano.Tarefas.Count(id => quadro.CardDe(id)?.ColunaId != plano.ColunaId);
        var anteriores = new Dictionary<Guid, int>();
        foreach (var mudanca in plano.Percentuais)
        {
            porId[mudanca.TarefaId].AtualizarPercentualConcluido(mudanca.Depois);
            if (mudanca.Antes < 100)
                anteriores[mudanca.TarefaId] = mudanca.Antes;
        }
        quadro.Colocar(plano.Tarefas, plano.ColunaId, plano.Posicao, anteriores);
        quadro.Sincronizar();

        var nome = porId[plano.Tarefas[0]].Nome;
        var um = plano.Tarefas.Count == 1;
        var coluna = destino.Nome;
        if (mudamDeColuna == 0)
            return new ResultadoDoQuadro(
                um ? $"\"{nome}\" agora é o {plano.Posicao + 1}º card de \"{coluna}\"." : $"{plano.Tarefas.Count} cards reordenados em \"{coluna}\".",
                um ? $"Reordenar \"{nome}\"" : $"Reordenar {plano.Tarefas.Count} cards");
        var texto = (um ? $"\"{nome}\" foi para \"{coluna}\"" : $"{plano.Tarefas.Count} cards foram para \"{coluna}\"")
                    + OQueMudouNoPercentual(destino, plano.Percentuais, um);
        return new ResultadoDoQuadro(texto, um ? $"Mover \"{nome}\" para {coluna}" : $"Mover {plano.Tarefas.Count} cards para {coluna}");
    }

    // ": 100% concluída." / ": o % voltou a 0 (era 40%)." / ": 40% (era 100%)." / "."
    private static string OQueMudouNoPercentual(ColunaDoQuadro destino, IReadOnlyList<MudancaDoPercentual> percentuais, bool um)
    {
        if (percentuais.Count == 0)
            return ".";
        if (um)
        {
            var p = percentuais[0];
            return destino.Tipo switch
            {
                TipoDeColuna.Concluida => ": 100% concluída.",
                TipoDeColuna.AFazer => $": o % voltou a 0 (era {p.Antes}%).",
                _ => $": {p.Depois}% (era {p.Antes}%).",
            };
        }
        return destino.Tipo switch
        {
            TipoDeColuna.Concluida => percentuais.Count == 1 ? ": 1 chegou a 100%." : $": {percentuais.Count} chegaram a 100%.",
            TipoDeColuna.AFazer => percentuais.Count == 1 ? ": o % de 1 voltou a 0." : $": o % de {percentuais.Count} voltou a 0.",
            _ => percentuais.Count == 1 ? ": 1 voltou ao % de antes de concluir." : $": {percentuais.Count} voltaram ao % de antes de concluir.",
        };
    }

    // A dica junto do cursor durante o arraste.
    private static string Dica(ColunaDoQuadro destino, int mudamDeColuna, IReadOnlyList<MudancaDoPercentual> percentuais)
    {
        if (mudamDeColuna == 0)
            return $"Reordenar em \"{destino.Nome}\"";
        var dica = $"Mover para \"{destino.Nome}\"";
        if (percentuais.Count == 0)
            return dica;
        return destino.Tipo switch
        {
            TipoDeColuna.Concluida => dica + " · 100%",
            TipoDeColuna.AFazer => dica + " · o % volta a 0",
            _ => dica + (percentuais.Count == 1 ? $" · volta a {percentuais[0].Depois}%" : " · voltam ao % de antes"),
        };
    }
}
