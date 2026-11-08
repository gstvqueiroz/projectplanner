using System.Globalization;

namespace ProjectPlanner.Core.Kanban;

/// <summary>
/// Bloco 75 (o plano "Delete, arrastar e Kanban", K2): o tipo de uma coluna do quadro, que diz o que ela faz com o % concluído
/// da tarefa (K3).
/// </summary>
public enum TipoDeColuna
{
    /// <summary>Não iniciadas: o card só fica nela com 0%, e entrar nela põe o % em 0.</summary>
    AFazer,

    /// <summary>Em execução (também "Impedida" e uma "Em revisão"): de 0 a 99%. Entrar nela não muda o %, a não ser vindo de Concluída.</summary>
    EmAndamento,

    /// <summary>Concluídas: 100%. Entrar nela põe o % em 100; sair volta ao % de antes.</summary>
    Concluida,
}

/// <summary>Bloco 75: uma coluna do quadro, com o tipo, a cor (#RRGGBB; nula, a do tema) e o limite de cards (WIP; nulo, sem limite).</summary>
public sealed record ColunaDoQuadro(Guid Id, string Nome, TipoDeColuna Tipo, string? Cor = null, int? Limite = null);

/// <summary>Bloco 75 (usada no Bloco 77): uma etiqueta do projeto, com a cor (#RRGGBB).</summary>
public sealed record EtiquetaDoQuadro(Guid Id, string Nome, string Cor);

/// <summary>Bloco 75 (usado no Bloco 77): um item do checklist de um card. Marcar itens não muda o % concluído.</summary>
public sealed record ItemDoChecklist(string Texto, bool Feito);

/// <summary>Bloco 75 (usado no Bloco 78): como o quadro agrupa os cards em raias.</summary>
public enum ModoDasRaias
{
    /// <summary>Sem raias: as colunas de cima a baixo.</summary>
    Nenhuma,

    /// <summary>Uma raia por resumo (as tarefas no primeiro nível, numa raia "Sem resumo").</summary>
    PorResumo,

    /// <summary>Uma raia por responsável (os profissionais da tarefa).</summary>
    PorResponsavel,
}

/// <summary>
/// Bloco 75: o card de uma tarefa no quadro: a coluna, a posição nela (<see cref="Ordem"/>, do menor para o maior), o último %
/// abaixo de 100 (para voltar a ele quando o card sai de Concluída), se está arquivado, as etiquetas e o checklist. A descrição
/// é a nota da tarefa (<see cref="Tarefa.Notas"/>).
/// </summary>
public sealed record CardDoQuadro(Guid TarefaId, Guid ColunaId, int Ordem)
{
    /// <summary>O último % concluído abaixo de 100 que a tarefa teve; nulo se ela chegou ao quadro já com 100%.</summary>
    public int? PercentualAnterior { get; init; }

    /// <summary>Fora do quadro, mas a tarefa continua no cronograma (Bloco 77).</summary>
    public bool Arquivado { get; init; }

    /// <summary>As etiquetas do projeto que o card tem (Bloco 77).</summary>
    public IReadOnlyList<Guid> Etiquetas { get; init; } = [];

    /// <summary>O checklist (Bloco 77).</summary>
    public IReadOnlyList<ItemDoChecklist> Checklist { get; init; } = [];
}

/// <summary>
/// Bloco 75 (o plano "Delete, arrastar e Kanban", K1 a K7): o quadro Kanban do projeto, uma visão das mesmas tarefas do
/// cronograma: cada card é uma tarefa sem subtarefas (marcos também), e cada coluna é um status com um tipo (A fazer, Em
/// andamento ou Concluída) que combina com o % concluído. Fica no cronograma e volta no Desfazer, como as tarefas.
/// </summary>
/// <remarks>
/// <para>
/// A regra que liga os dois (K3): numa coluna A fazer o % é 0, numa Concluída é 100, numa Em andamento é de 0 a 99.
/// <see cref="Sincronizar"/> faz o card seguir o %: um card numa coluna que não combina vai para a primeira coluna do tipo
/// certo (o % subiu na grade, chegou a 100, caiu de 100). Mover o card faz o contrário, o % seguir o card (ver
/// <see cref="RegrasDoQuadro"/>).
/// </para>
/// <para>
/// O quadro tem sempre pelo menos uma coluna de cada tipo, para todo % ter onde ficar. O card de um resumo fica guardado
/// fora do quadro (com as etiquetas e o checklist) e volta se ele ficar sem subtarefas; o de uma tarefa excluída sai.
/// </para>
/// </remarks>
public sealed class QuadroDoProjeto
{
    /// <summary>Quantos caracteres o nome de uma coluna (ou de uma etiqueta) pode ter.</summary>
    public const int TamanhoMaximoDoNome = 40;

    private readonly Cronograma _cronograma;
    private readonly List<ColunaDoQuadro> _colunas = [];
    private readonly List<EtiquetaDoQuadro> _etiquetas = [];
    private readonly Dictionary<Guid, CardDoQuadro> _cards = [];

    /// <summary>O quadro de um projeto novo (ou de um arquivo de antes dele): as colunas padrão, sem cards.</summary>
    internal QuadroDoProjeto(Cronograma cronograma)
    {
        _cronograma = cronograma;
        _colunas.AddRange(ColunasPadrao());
    }

    /// <summary>
    /// K2: A fazer · Em andamento · Impedida · Concluída. "Impedida" é do tipo Em andamento (o card não perde o %).
    /// </summary>
    public static IReadOnlyList<ColunaDoQuadro> ColunasPadrao() =>
    [
        new(Guid.NewGuid(), "A fazer", TipoDeColuna.AFazer, "#8A8F98"),
        new(Guid.NewGuid(), "Em andamento", TipoDeColuna.EmAndamento, "#2F80ED"),
        new(Guid.NewGuid(), "Impedida", TipoDeColuna.EmAndamento, "#E5484D"),
        new(Guid.NewGuid(), "Concluída", TipoDeColuna.Concluida, "#30A46C"),
    ];

    /// <summary>"A fazer", "Em andamento", "Concluída".</summary>
    public static string NomeDoTipo(TipoDeColuna tipo) => tipo switch
    {
        TipoDeColuna.AFazer => "A fazer",
        TipoDeColuna.Concluida => "Concluída",
        _ => "Em andamento",
    };

    /// <summary>Se um card com este % pode ficar numa coluna deste tipo (K3).</summary>
    public static bool Aceita(TipoDeColuna tipo, int percentual) => tipo switch
    {
        TipoDeColuna.AFazer => percentual == 0,
        TipoDeColuna.Concluida => percentual == 100,
        _ => percentual < 100,
    };

    /// <summary>As colunas, da esquerda para a direita.</summary>
    public IReadOnlyList<ColunaDoQuadro> Colunas => _colunas.AsReadOnly();

    /// <summary>As etiquetas do projeto (Bloco 77).</summary>
    public IReadOnlyList<EtiquetaDoQuadro> Etiquetas => _etiquetas.AsReadOnly();

    /// <summary>Como o quadro agrupa os cards em raias (Bloco 78).</summary>
    public ModoDasRaias Raias { get; private set; }

    /// <summary>Todos os cards guardados, também os arquivados e os de resumos (fora do quadro).</summary>
    public IReadOnlyCollection<CardDoQuadro> Cards => _cards.Values;

    /// <summary>Sem nada a guardar no arquivo: as colunas padrão, sem cards, etiquetas nem raias.</summary>
    public bool Vazio => _cards.Count == 0 && _etiquetas.Count == 0 && Raias == ModoDasRaias.Nenhuma && ColunasSaoAsPadrao();

    /// <summary>O card da tarefa; nulo se ela não tem (ainda não sincronizado, ou não é do cronograma).</summary>
    public CardDoQuadro? CardDe(Guid tarefaId) => _cards.GetValueOrDefault(tarefaId);

    /// <summary>A coluna com este Id; nula se ela não existe.</summary>
    public ColunaDoQuadro? Coluna(Guid colunaId) => _colunas.Find(c => c.Id == colunaId);

    /// <summary>
    /// A coluna em que entra um card com este % (K7 e K3): 0% na primeira A fazer, 1 a 99% na primeira Em andamento, 100% na
    /// primeira Concluída.
    /// </summary>
    public ColunaDoQuadro ColunaPara(int percentual) => percentual switch
    {
        >= 100 => Primeira(TipoDeColuna.Concluida),
        <= 0 => Primeira(TipoDeColuna.AFazer),
        _ => Primeira(TipoDeColuna.EmAndamento),
    };

    /// <summary>A primeira coluna (da esquerda) do tipo.</summary>
    public ColunaDoQuadro Primeira(TipoDeColuna tipo) => _colunas.Find(c => c.Tipo == tipo)
        ?? throw new InvalidOperationException($"O quadro não tem coluna do tipo {NomeDoTipo(tipo)}.");

    /// <summary>
    /// Os cards que aparecem no quadro, por coluna (todas as colunas, mesmo as vazias), na ordem de cada uma: as tarefas sem
    /// subtarefas que não estão arquivadas.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<CardDoQuadro>> CardsPorColuna()
    {
        var posicao = PosicoesNoCronograma();
        var porColuna = _cards.Values
            .Where(c => !c.Arquivado && posicao.ContainsKey(c.TarefaId))
            .GroupBy(c => c.ColunaId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Ordem).ThenBy(c => posicao[c.TarefaId]).ToList());
        return _colunas.ToDictionary(c => c.Id,
            c => (IReadOnlyList<CardDoQuadro>)(porColuna.TryGetValue(c.Id, out var cards) ? cards : []));
    }

    /// <summary>Os cards que aparecem na coluna, na ordem dela.</summary>
    public IReadOnlyList<CardDoQuadro> CardsDa(Guid colunaId) => CardsPorColuna().TryGetValue(colunaId, out var cards) ? cards : [];

    /// <summary>
    /// K1, K3 e K7: deixa o quadro de acordo com as tarefas. Toda tarefa sem subtarefas tem um card: as que ainda não têm entram
    /// no fim da coluna do % delas (<see cref="ColunaPara"/>), na ordem do cronograma (um arquivo de antes do quadro abre assim);
    /// o card de uma tarefa excluída sai; o de um resumo fica guardado, fora do quadro. Um card numa coluna que não combina com o
    /// % vai para o fim da coluna do % (o % subiu acima de 0 em A fazer, chegou a 100, caiu de 100). O % nunca muda aqui.
    /// </summary>
    /// <returns>Se o quadro mudou.</returns>
    public bool Sincronizar()
    {
        var mudou = false;
        var existentes = _cronograma.Tarefas.Select(t => t.Id).ToHashSet();
        foreach (var id in _cards.Keys.Where(id => !existentes.Contains(id)).ToList())
            mudou |= _cards.Remove(id);

        foreach (var tarefa in _cronograma.Tarefas)
        {
            if (tarefa.EhResumo)
                continue;
            var percentual = tarefa.PercentualConcluido;
            var card = _cards.GetValueOrDefault(tarefa.Id);
            CardDoQuadro novo;
            if (card is null || Coluna(card.ColunaId) is not { } atual || !Aceita(atual.Tipo, percentual))
            {
                var coluna = ColunaPara(percentual);
                novo = card is null
                    ? new CardDoQuadro(tarefa.Id, coluna.Id, ProximaOrdem(coluna.Id))
                    : card with { ColunaId = coluna.Id, Ordem = ProximaOrdem(coluna.Id) };
            }
            else
                novo = card;
            if (percentual < 100 && novo.PercentualAnterior != percentual)
                novo = novo with { PercentualAnterior = percentual };
            if (novo != card)
            {
                _cards[tarefa.Id] = novo;
                mudou = true;
            }
        }
        return mudou;
    }

    /// <summary>
    /// Põe as colunas, as etiquetas, os cards e as raias guardados no arquivo. Cards de tarefas que não existem ficam de fora;
    /// os de colunas que não existem vão para a coluna do % no próximo <see cref="Sincronizar"/>.
    /// </summary>
    /// <exception cref="DomainValidationException">Colunas ou etiquetas que o quadro não aceita.</exception>
    public void Restaurar(IEnumerable<ColunaDoQuadro> colunas, IEnumerable<EtiquetaDoQuadro> etiquetas, IEnumerable<CardDoQuadro> cards,
        ModoDasRaias raias)
    {
        ArgumentNullException.ThrowIfNull(colunas);
        ArgumentNullException.ThrowIfNull(etiquetas);
        ArgumentNullException.ThrowIfNull(cards);
        var validas = ValidarColunas(colunas);
        var lidas = ValidarEtiquetas(etiquetas);
        var existentes = _cronograma.Tarefas.Select(t => t.Id).ToHashSet();
        var ids = lidas.Select(e => e.Id).ToHashSet();
        _colunas.Clear();
        _colunas.AddRange(validas);
        _etiquetas.Clear();
        _etiquetas.AddRange(lidas);
        _cards.Clear();
        foreach (var card in cards.Where(c => existentes.Contains(c.TarefaId)))
            _cards[card.TarefaId] = card with
            {
                PercentualAnterior = card.PercentualAnterior is { } p ? Math.Clamp(p, 0, 99) : null,
                Etiquetas = [.. card.Etiquetas.Where(ids.Contains).Distinct()],
                Checklist = [.. card.Checklist.Where(i => !string.IsNullOrWhiteSpace(i.Texto))],
            };
        Raias = Enum.IsDefined(raias) ? raias : ModoDasRaias.Nenhuma;
    }

    /// <summary>
    /// Põe os cards na coluna, no lugar <paramref name="posicao"/> entre os cards dela que aparecem no quadro (contados sem os
    /// que estão sendo movidos), na ordem dada. Com <paramref name="anteriores"/>, o último % abaixo de 100 de cada um.
    /// </summary>
    internal void Colocar(IReadOnlyList<Guid> tarefas, Guid colunaId, int posicao, IReadOnlyDictionary<Guid, int>? anteriores = null)
    {
        var movidas = tarefas.ToHashSet();
        var naColuna = CardsDa(colunaId).Select(c => c.TarefaId).Where(id => !movidas.Contains(id)).ToList();
        naColuna.InsertRange(Math.Clamp(posicao, 0, naColuna.Count), tarefas);
        foreach (var id in tarefas)
        {
            var card = _cards[id];
            _cards[id] = card with
            {
                ColunaId = colunaId,
                PercentualAnterior = anteriores is not null && anteriores.TryGetValue(id, out var anterior) ? anterior : card.PercentualAnterior,
            };
        }
        // Os que não aparecem (arquivados, de resumos) ficam depois, na ordem em que estavam.
        var fora = _cards.Values.Where(c => c.ColunaId == colunaId && !naColuna.Contains(c.TarefaId)).OrderBy(c => c.Ordem).Select(c => c.TarefaId);
        var ordem = 0;
        foreach (var id in naColuna.Concat(fora.ToList()))
            _cards[id] = _cards[id] with { Ordem = ordem++ };
    }

    /// <summary>Bloco 76: põe as colunas (já com a mudança), com as regras de <see cref="ValidarColunas"/>; recusadas, nada muda.</summary>
    /// <exception cref="DomainValidationException">As colunas não seguem as regras.</exception>
    internal void TrocarColunas(IEnumerable<ColunaDoQuadro> colunas)
    {
        var validas = ValidarColunas(colunas);
        _colunas.Clear();
        _colunas.AddRange(validas);
    }

    /// <summary>Bloco 77: troca o card da tarefa pela versão mudada (checklist, etiquetas, arquivado).</summary>
    /// <exception cref="DomainValidationException">A tarefa não tem card no quadro.</exception>
    internal void AlterarCard(Guid tarefaId, Func<CardDoQuadro, CardDoQuadro> mudanca)
    {
        var card = _cards.GetValueOrDefault(tarefaId)
                   ?? throw new DomainValidationException(nameof(Cards), "A tarefa não tem card no quadro (é um resumo, ou não existe mais).");
        _cards[tarefaId] = mudanca(card);
    }

    /// <summary>Bloco 77: põe as etiquetas do projeto (já com a mudança); as que saíram somem dos cards.</summary>
    /// <exception cref="DomainValidationException">Nome vazio ou longo demais, cor inválida, ids repetidos.</exception>
    internal void TrocarEtiquetas(IEnumerable<EtiquetaDoQuadro> etiquetas)
    {
        var validas = ValidarEtiquetas(etiquetas);
        var ids = validas.Select(e => e.Id).ToHashSet();
        _etiquetas.Clear();
        _etiquetas.AddRange(validas);
        foreach (var card in _cards.Values.Where(c => c.Etiquetas.Any(e => !ids.Contains(e))).ToList())
            _cards[card.TarefaId] = card with { Etiquetas = [.. card.Etiquetas.Where(ids.Contains)] };
    }

    /// <summary>Bloco 77: um card desarquivado volta para o fim da coluna dele.</summary>
    internal int OrdemNoFimDe(Guid colunaId) => ProximaOrdem(colunaId);

    /// <summary>Bloco 76: os cards que não aparecem (arquivados, de resumos) de uma coluna que vai sair passam para outra.</summary>
    internal void MoverOcultos(Guid de, Guid para)
    {
        foreach (var card in _cards.Values.Where(k => k.ColunaId == de).ToList())
            _cards[card.TarefaId] = card with { ColunaId = para, Ordem = ProximaOrdem(para) };
    }

    /// <summary>Um quadro igual a este, para a cópia do cronograma (o Desfazer guarda cópias); os cards são registros imutáveis.</summary>
    internal QuadroDoProjeto CopiarPara(Cronograma copia)
    {
        var quadro = new QuadroDoProjeto(copia) { Raias = Raias };
        quadro._colunas.Clear();
        quadro._colunas.AddRange(_colunas);
        quadro._etiquetas.AddRange(_etiquetas);
        foreach (var (id, card) in _cards)
            quadro._cards[id] = card;
        return quadro;
    }

    private int ProximaOrdem(Guid colunaId) =>
        _cards.Values.Where(c => c.ColunaId == colunaId).Select(c => c.Ordem + 1).DefaultIfEmpty(0).Max();

    private Dictionary<Guid, int> PosicoesNoCronograma()
    {
        var posicao = new Dictionary<Guid, int>();
        var i = 0;
        foreach (var tarefa in _cronograma.Tarefas)
        {
            if (!tarefa.EhResumo)
                posicao[tarefa.Id] = i;
            i++;
        }
        return posicao;
    }

    private bool ColunasSaoAsPadrao() =>
        _colunas.Select(c => (c.Nome, c.Tipo, c.Cor, c.Limite)).SequenceEqual(ColunasPadrao().Select(c => (c.Nome, c.Tipo, c.Cor, c.Limite)));

    /// <summary>As regras das colunas: nome (único) e tipo, pelo menos uma de cada tipo, cor #RRGGBB e limite de pelo menos 1.</summary>
    /// <exception cref="DomainValidationException">Alguma regra não é cumprida.</exception>
    public static IReadOnlyList<ColunaDoQuadro> ValidarColunas(IEnumerable<ColunaDoQuadro> colunas)
    {
        ArgumentNullException.ThrowIfNull(colunas);
        var lista = colunas.Select(c => c with { Nome = (c.Nome ?? "").Trim(), Cor = string.IsNullOrWhiteSpace(c.Cor) ? null : c.Cor.Trim() }).ToList();
        foreach (var coluna in lista)
        {
            if (coluna.Nome.Length == 0)
                throw new DomainValidationException(nameof(Colunas), "Toda coluna do quadro precisa de um nome.");
            if (coluna.Nome.Length > TamanhoMaximoDoNome)
                throw new DomainValidationException(nameof(Colunas),
                    $"O nome da coluna \"{coluna.Nome}\" tem {coluna.Nome.Length} caracteres; o máximo é {TamanhoMaximoDoNome}.");
            if (!Enum.IsDefined(coluna.Tipo))
                throw new DomainValidationException(nameof(Colunas), $"A coluna \"{coluna.Nome}\" tem um tipo desconhecido.");
            if (coluna.Limite is < 1)
                throw new DomainValidationException(nameof(Colunas),
                    $"O limite de cards da coluna \"{coluna.Nome}\" precisa ser de pelo menos 1 (vazio: sem limite).");
            if (coluna.Cor is { } cor && !CorValida(cor))
                throw new DomainValidationException(nameof(Colunas), $"A cor da coluna \"{coluna.Nome}\" não é uma cor (#RRGGBB).");
        }
        if (lista.Select(c => c.Id).Distinct().Count() != lista.Count || lista.Exists(c => c.Id == Guid.Empty))
            throw new DomainValidationException(nameof(Colunas), "As colunas do quadro precisam de ids diferentes.");
        if (lista.GroupBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } repetida)
            throw new DomainValidationException(nameof(Colunas), $"Já existe a coluna \"{repetida.Key}\" no quadro.");
        foreach (var tipo in Enum.GetValues<TipoDeColuna>())
            if (!lista.Exists(c => c.Tipo == tipo))
                throw new DomainValidationException(nameof(Colunas),
                    $"O quadro precisa de pelo menos uma coluna do tipo {NomeDoTipo(tipo)}: é para onde vão as tarefas com " +
                    tipo switch { TipoDeColuna.AFazer => "0%.", TipoDeColuna.Concluida => "100%.", _ => "1 a 99%." });
        return lista;
    }

    private static List<EtiquetaDoQuadro> ValidarEtiquetas(IEnumerable<EtiquetaDoQuadro> etiquetas)
    {
        var lista = etiquetas.Select(e => e with { Nome = (e.Nome ?? "").Trim(), Cor = (e.Cor ?? "").Trim() }).ToList();
        foreach (var etiqueta in lista)
        {
            if (etiqueta.Nome.Length is 0 or > TamanhoMaximoDoNome)
                throw new DomainValidationException(nameof(Etiquetas),
                    $"O nome de uma etiqueta precisa ter de 1 a {TamanhoMaximoDoNome} caracteres.");
            if (!CorValida(etiqueta.Cor))
                throw new DomainValidationException(nameof(Etiquetas), $"A cor da etiqueta \"{etiqueta.Nome}\" não é uma cor (#RRGGBB).");
        }
        if (lista.Select(e => e.Id).Distinct().Count() != lista.Count)
            throw new DomainValidationException(nameof(Etiquetas), "As etiquetas precisam de ids diferentes.");
        return lista;
    }

    /// <summary>"#RRGGBB".</summary>
    public static bool CorValida(string cor) =>
        cor.Length == 7 && cor[0] == '#' && int.TryParse(cor.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
}
