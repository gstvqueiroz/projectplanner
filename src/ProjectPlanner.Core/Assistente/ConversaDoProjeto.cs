namespace ProjectPlanner.Core.Assistente;

/// <summary>
/// Como a conversa com o assistente está trabalhando (Bloco 26): ainda sem escolha, montando o cronograma direto ou
/// estudando o projeto com perguntas antes de montar.
/// </summary>
public enum ModoDaConversa
{
    NaoEscolhido,
    Direto,
    Estudo,
}

public enum AutorNaConversa
{
    Usuario,
    Assistente,
}

/// <summary>O tipo de uma mensagem do assistente: texto, a escolha do modo, perguntas ou o plano para aprovar.</summary>
public enum TipoDaMensagem
{
    Texto,
    EscolhaDeModo,
    Perguntas,
    Plano,
}

/// <summary>Um dado do projeto na ficha ("Área construída": "100 m²").</summary>
public sealed record ItemDaFicha(string Campo, string Valor);

/// <summary>
/// A ficha do projeto (Bloco 26): o que já foi definido sobre a obra (tipo, área, prazo, premissas), preenchida pelo
/// assistente ao estudar os documentos e ao receber as respostas, e corrigível pelo usuário. Vai junto de cada pedido
/// ao assistente e fica no <c>.pplan</c>. Um campo aparece uma vez só (sem diferença de maiúsculas); valor vazio apaga.
/// </summary>
public sealed class FichaDoProjeto
{
    public const int TamanhoMaximoDoCampo = 80;
    public const int TamanhoMaximoDoValor = 1000;
    public const int QuantidadeMaxima = 60;

    private readonly List<ItemDaFicha> _itens = [];

    public IReadOnlyList<ItemDaFicha> Itens => _itens.AsReadOnly();

    public bool Vazia => _itens.Count == 0;

    /// <summary>O valor de um campo; nulo se ele não está na ficha.</summary>
    public string? this[string campo] => Achar(campo) is { } i ? _itens[i].Valor : null;

    /// <summary>Acrescenta, troca ou (com valor vazio) apaga o campo.</summary>
    /// <returns>Se a ficha mudou.</returns>
    /// <exception cref="DomainValidationException">Campo vazio ou longo demais, valor longo demais ou ficha cheia.</exception>
    public bool Definir(string campo, string? valor)
    {
        var nome = Limpar(campo);
        if (nome.Length == 0)
            throw new DomainValidationException(nameof(campo), "O campo da ficha precisa de um nome.");
        if (nome.Length > TamanhoMaximoDoCampo)
            throw new DomainValidationException(nameof(campo), $"O nome do campo passa de {TamanhoMaximoDoCampo} caracteres.");
        var texto = (valor ?? "").Trim();
        if (texto.Length > TamanhoMaximoDoValor)
            throw new DomainValidationException(nameof(valor), $"O valor de \"{nome}\" passa de {TamanhoMaximoDoValor} caracteres.");

        var indice = Achar(nome);
        if (texto.Length == 0)
        {
            if (indice is not { } existente)
                return false;
            _itens.RemoveAt(existente);
            return true;
        }
        if (indice is { } i)
        {
            if (_itens[i].Valor == texto && _itens[i].Campo == nome)
                return false;
            _itens[i] = new ItemDaFicha(nome, texto);
            return true;
        }
        if (_itens.Count >= QuantidadeMaxima)
            throw new DomainValidationException(nameof(campo), $"A ficha já tem {QuantidadeMaxima} campos.");
        _itens.Add(new ItemDaFicha(nome, texto));
        return true;
    }

    /// <summary>Remove o campo, se estiver na ficha.</summary>
    public bool Remover(string campo) => Definir(campo, null);

    /// <summary>
    /// Aplica vários itens (os que o assistente mandou), ignorando os inválidos em vez de recusar todos.
    /// </summary>
    /// <returns>Quantos campos mudaram.</returns>
    public int Mesclar(IEnumerable<ItemDaFicha> itens)
    {
        ArgumentNullException.ThrowIfNull(itens);
        var mudancas = 0;
        foreach (var item in itens)
        {
            try
            {
                if (Definir(item.Campo, item.Valor))
                    mudancas++;
            }
            catch (DomainValidationException)
            {
                // Um item ruim do assistente (vazio, longo demais) não derruba os outros.
            }
        }
        return mudancas;
    }

    public void Esvaziar() => _itens.Clear();

    private int? Achar(string campo)
    {
        var nome = Limpar(campo);
        var i = _itens.FindIndex(item => string.Equals(item.Campo, nome, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : i;
    }

    private static string Limpar(string? texto) => string.Join(' ', (texto ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Um documento anexado numa mensagem: só a referência (o arquivo não vai para dentro do projeto).</summary>
public sealed record AnexoNaConversa(string Caminho, long Tamanho)
{
    public string Nome => Path.GetFileName(Caminho);
}

/// <summary>Uma pergunta do assistente, com as respostas sugeridas para clicar (pode não ter nenhuma).</summary>
public sealed record PerguntaNaConversa(string Texto, IReadOnlyList<string> Opcoes);

/// <summary>Uma etapa do plano proposto: o nome, a duração estimada em dias úteis e uma observação opcional.</summary>
public sealed record EtapaDoPlano(string Nome, int DuracaoEmDias, string? Observacao = null);

/// <summary>O plano que o assistente apresenta para aprovação antes de montar o cronograma (Bloco 26).</summary>
public sealed record PlanoNaConversa(IReadOnlyList<EtapaDoPlano> Etapas, string? TerminoEstimado, IReadOnlyList<string> Premissas);

/// <summary>Uma mensagem guardada da conversa (os avisos locais, como "Resposta cancelada", não são guardados).</summary>
/// <param name="ResultadoDasAlteracoes">O que a resposta fez no cronograma, como o painel mostrou ("Cronograma alterado: …").</param>
/// <param name="AlteracoesAplicadas">Se as alterações foram aplicadas (para o destaque de sucesso no painel).</param>
public sealed record MensagemDaConversa(
    AutorNaConversa Autor,
    string Texto,
    DateTimeOffset DataHora,
    TipoDaMensagem Tipo = TipoDaMensagem.Texto,
    string? ResultadoDasAlteracoes = null,
    bool AlteracoesAplicadas = false)
{
    public IReadOnlyList<AnexoNaConversa> Anexos { get; init; } = [];

    public IReadOnlyList<PerguntaNaConversa> Perguntas { get; init; } = [];

    public PlanoNaConversa? Plano { get; init; }
}

/// <summary>
/// A conversa com o assistente e a ficha do projeto (Bloco 26), guardadas no <c>.pplan</c>. É do projeto, não de uma
/// versão do cronograma: <see cref="Cronograma.Copiar"/> usa a mesma conversa, então aplicar ou desfazer alterações do
/// assistente não a muda.
/// </summary>
public sealed class ConversaDoProjeto
{
    private readonly List<MensagemDaConversa> _mensagens = [];

    public ModoDaConversa Modo { get; private set; }

    /// <summary>No modo de estudo: o usuário aprovou o plano e o cronograma ainda não foi montado.</summary>
    public bool PlanoAprovado { get; private set; }

    public FichaDoProjeto Ficha { get; } = new();

    public IReadOnlyList<MensagemDaConversa> Mensagens => _mensagens.AsReadOnly();

    /// <summary>Nada a guardar: sem mensagens, ficha vazia e sem modo escolhido.</summary>
    public bool Vazia => _mensagens.Count == 0 && Ficha.Vazia && Modo == ModoDaConversa.NaoEscolhido && !PlanoAprovado;

    /// <summary>Rodadas de perguntas do assistente desde que o estudo começou (desde a última limpeza).</summary>
    public int RodadasDePerguntas => _mensagens.Count(m => m.Tipo == TipoDaMensagem.Perguntas);

    public void Acrescentar(MensagemDaConversa mensagem)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        _mensagens.Add(mensagem);
    }

    /// <summary>Troca uma mensagem já guardada (o resultado das alterações mudou: desfeitas).</summary>
    public void Substituir(int indice, MensagemDaConversa mensagem)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        if (indice < 0 || indice >= _mensagens.Count)
            throw new ArgumentOutOfRangeException(nameof(indice), indice, "Não há mensagem nessa posição.");
        _mensagens[indice] = mensagem;
    }

    /// <returns>Se mudou.</returns>
    public bool DefinirModo(ModoDaConversa modo)
    {
        if (!Enum.IsDefined(modo))
            throw new ArgumentOutOfRangeException(nameof(modo), modo, "Modo desconhecido.");
        if (Modo == modo)
            return false;
        Modo = modo;
        if (modo != ModoDaConversa.Estudo)
            PlanoAprovado = false;
        return true;
    }

    /// <returns>Se mudou.</returns>
    /// <exception cref="InvalidOperationException">Aprovar fora do modo de estudo.</exception>
    public bool DefinirPlanoAprovado(bool aprovado)
    {
        if (aprovado && Modo != ModoDaConversa.Estudo)
            throw new InvalidOperationException("Só há plano para aprovar no modo de estudo.");
        if (PlanoAprovado == aprovado)
            return false;
        PlanoAprovado = aprovado;
        return true;
    }

    /// <summary>"Limpar conversa": as mensagens e o modo recomeçam; a ficha fica (é do projeto).</summary>
    public void LimparMensagens()
    {
        _mensagens.Clear();
        Modo = ModoDaConversa.NaoEscolhido;
        PlanoAprovado = false;
    }
}
