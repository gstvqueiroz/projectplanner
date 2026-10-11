namespace ProjectPlanner.Core.Edicao;

/// <summary>Uma alteração no histórico do Desfazer: o nome dela e os retratos do projeto antes e depois.</summary>
/// <remarks>É a marca que o assistente guarda de cada resposta aplicada (o mesmo objeto do desfazer ao refazer).</remarks>
public sealed class PassoDoHistorico
{
    internal PassoDoHistorico(string descricao, RetratoDoProjeto antes, RetratoDoProjeto depois) =>
        (Descricao, Antes, Depois) = (descricao, antes, depois);

    /// <summary>"Duração de "Escavação"", "Alterações do assistente"...</summary>
    public string Descricao { get; }

    internal RetratoDoProjeto Antes { get; }

    internal RetratoDoProjeto Depois { get; }
}

/// <summary>Um estado do projeto guardado no histórico, com um número para saber qual é o salvo.</summary>
internal sealed record RetratoDoProjeto(int Numero, Cronograma Cronograma);

/// <summary>Em que ponto do histórico está uma alteração.</summary>
public enum SituacaoNoHistorico
{
    /// <summary>Feita: dá para desfazer.</summary>
    Feita,

    /// <summary>Desfeita: dá para refazer.</summary>
    Desfeita,

    /// <summary>Fora do histórico (outro projeto, histórico cheio, ou desfeita e substituída por outra alteração).</summary>
    Esquecida,
}

/// <summary>
/// Bloco 30: o Desfazer e o Refazer em vários níveis. Depois de cada alteração, <see cref="Registrar"/> guarda um retrato
/// (uma cópia) do cronograma; desfazer devolve uma cópia do retrato de antes. Volta tudo o que é do cronograma: tarefas,
/// estrutura, vínculos, datas, %, notas, o nome e o início do projeto, e as fotos (os registros; as imagens ficam com
/// quem as guarda, ver <see cref="FotosNoHistorico"/>). Não voltam os dados do projeto que não são edições do
/// cronograma: a conversa com o assistente, os dados da obra, os locais e o alerta das fotos e o histórico dos
/// relatórios (ficam como estão no cronograma aberto).
/// </summary>
public sealed class HistoricoDeAlteracoes
{
    public const int NiveisPadrao = 100;

    private readonly int _niveis;
    private readonly LinkedList<PassoDoHistorico> _desfazer = new();
    private readonly Stack<PassoDoHistorico> _refazer = new();
    private RetratoDoProjeto _atual;
    private int _salvo;
    private int _proximoNumero;

    /// <param name="atual">O projeto como está agora (o ponto de partida, que conta como salvo).</param>
    /// <param name="niveis">Quantas alterações dá para desfazer; as mais antigas saem.</param>
    public HistoricoDeAlteracoes(Cronograma atual, int niveis = NiveisPadrao)
    {
        ArgumentNullException.ThrowIfNull(atual);
        ArgumentOutOfRangeException.ThrowIfLessThan(niveis, 1);
        _niveis = niveis;
        _atual = Retratar(atual);
        _salvo = _atual.Numero;
    }

    public bool PodeDesfazer => _desfazer.Count > 0;

    public bool PodeRefazer => _refazer.Count > 0;

    /// <summary>O nome da alteração que o Desfazer desfaz; nulo sem nenhuma.</summary>
    public string? ProximaADesfazer => _desfazer.Last?.Value.Descricao;

    /// <summary>O nome da alteração que o Refazer refaz; nulo sem nenhuma.</summary>
    public string? ProximaARefazer => _refazer.TryPeek(out var passo) ? passo.Descricao : null;

    /// <summary>O projeto está como foi salvo (ou aberto) pela última vez, considerando só o que o histórico guarda.</summary>
    public bool NoEstadoSalvo => _atual.Numero == _salvo;

    /// <summary>Recomeça do zero (outro projeto aberto ou novo): nada para desfazer nem refazer.</summary>
    public void Recomecar(Cronograma atual)
    {
        ArgumentNullException.ThrowIfNull(atual);
        _desfazer.Clear();
        _refazer.Clear();
        _atual = Retratar(atual);
        _salvo = _atual.Numero;
    }

    /// <summary>
    /// Uma alteração acabou de ser feita em <paramref name="atual"/>: ela entra no histórico (o Refazer some, como em
    /// todo programa).
    /// </summary>
    /// <returns>O passo, que identifica a alteração (o assistente guarda o de cada resposta).</returns>
    public PassoDoHistorico Registrar(string descricao, Cronograma atual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descricao);
        ArgumentNullException.ThrowIfNull(atual);
        var passo = new PassoDoHistorico(descricao, _atual, Retratar(atual));
        _desfazer.AddLast(passo);
        if (_desfazer.Count > _niveis)
            _desfazer.RemoveFirst();
        _refazer.Clear();
        _atual = passo.Depois;
        return passo;
    }

    /// <summary>Desfaz a última alteração.</summary>
    /// <param name="atual">O cronograma aberto, de onde vêm os dados que não voltam (a conversa, a obra...).</param>
    /// <returns>O cronograma de antes dela (uma cópia nova, para pôr no lugar do aberto) e o nome da alteração.</returns>
    /// <exception cref="InvalidOperationException">Não há o que desfazer.</exception>
    public (Cronograma Cronograma, string Descricao) Desfazer(Cronograma atual)
    {
        ArgumentNullException.ThrowIfNull(atual);
        var passo = _desfazer.Last?.Value ?? throw new InvalidOperationException("Não há o que desfazer.");
        _desfazer.RemoveLast();
        _refazer.Push(passo);
        _atual = passo.Antes;
        return (Restaurar(passo.Antes, atual), passo.Descricao);
    }

    /// <summary>Refaz a última alteração desfeita.</summary>
    /// <exception cref="InvalidOperationException">Não há o que refazer.</exception>
    public (Cronograma Cronograma, string Descricao) Refazer(Cronograma atual)
    {
        ArgumentNullException.ThrowIfNull(atual);
        if (!_refazer.TryPop(out var passo))
            throw new InvalidOperationException("Não há o que refazer.");
        _desfazer.AddLast(passo);
        _atual = passo.Depois;
        return (Restaurar(passo.Depois, atual), passo.Descricao);
    }

    public SituacaoNoHistorico Situacao(PassoDoHistorico passo) =>
        _desfazer.Contains(passo) ? SituacaoNoHistorico.Feita
        : _refazer.Contains(passo) ? SituacaoNoHistorico.Desfeita
        : SituacaoNoHistorico.Esquecida;

    /// <summary>Quantas alterações foram feitas depois desta (que um Desfazer até ela também desfaria); -1 se ela não está feita.</summary>
    public int AlteracoesDepoisDe(PassoDoHistorico passo)
    {
        var posicao = 0;
        for (var no = _desfazer.Last; no is not null; no = no.Previous, posicao++)
            if (ReferenceEquals(no.Value, passo))
                return posicao;
        return -1;
    }

    /// <summary>O projeto acabou de ser salvo: este é o estado salvo.</summary>
    public void MarcarSalvo() => _salvo = _atual.Numero;

    /// <summary>
    /// As fotos de todos os estados guardados (para desfazer e refazer): as imagens delas precisam continuar disponíveis
    /// mesmo depois de a foto sair do projeto aberto e do arquivo.
    /// </summary>
    public IReadOnlySet<Guid> FotosNoHistorico() =>
        _desfazer.SelectMany(p => new[] { p.Antes, p.Depois }).Concat(_refazer.SelectMany(p => new[] { p.Antes, p.Depois })).Append(_atual)
            .Distinct()
            .SelectMany(r => r.Cronograma.Tarefas.SelectMany(t => t.FotosNaOrdemDeCadastro).Select(f => f.Id))
            .ToHashSet();

    private RetratoDoProjeto Retratar(Cronograma cronograma) => new(++_proximoNumero, cronograma.Copiar());

    // Uma cópia do retrato (ele continua no histórico intacto), com os dados que não voltam tirados do aberto.
    private static Cronograma Restaurar(RetratoDoProjeto retrato, Cronograma atual)
    {
        var restaurado = retrato.Cronograma.Copiar();
        restaurado.ManterDadosDoProjetoDe(atual);
        return restaurado;
    }
}
