namespace ProjectPlanner.Core.OrdensDeServico;

/// <summary>
/// Bloco 58: onde a ordem de serviço está no fluxo. Rascunho → Emitida → Aceita → Em execução → Concluída → Conferida →
/// Paga; a emitida pode ser recusada (e volta a rascunho para corrigir), a concluída pode ser reprovada na conferência (e
/// volta à execução), e qualquer uma antes da conferência pode ser cancelada.
/// </summary>
public enum SituacaoDaOrdem
{
    Rascunho,
    Emitida,
    Aceita,
    Recusada,
    EmExecucao,
    Concluida,
    Conferida,
    Paga,
    Cancelada,
}

/// <summary>Bloco 58: quem executa a OS.</summary>
public enum TipoDeExecutante
{
    EquipePropria,
    Terceiro,
}

/// <summary>Bloco 58: como a OS é paga.</summary>
public enum FormaDePagamentoDaOrdem
{
    /// <summary>Preço fechado pelo serviço todo.</summary>
    Empreitada,

    /// <summary>Pelo que foi medido (quantidade executada × preço unitário).</summary>
    PorMedicao,

    /// <summary>Por dia de trabalho.</summary>
    Diaria,
}

/// <summary>Os textos das situações, dos executantes e das formas de pagamento, como aparecem na tela e no PDF.</summary>
public static class TextosDaOrdem
{
    public static string Texto(SituacaoDaOrdem situacao) => situacao switch
    {
        SituacaoDaOrdem.Rascunho => "Rascunho",
        SituacaoDaOrdem.Emitida => "Emitida",
        SituacaoDaOrdem.Aceita => "Aceita",
        SituacaoDaOrdem.Recusada => "Recusada",
        SituacaoDaOrdem.EmExecucao => "Em execução",
        SituacaoDaOrdem.Concluida => "Concluída",
        SituacaoDaOrdem.Conferida => "Conferida",
        SituacaoDaOrdem.Paga => "Paga",
        _ => "Cancelada",
    };

    public static string Texto(TipoDeExecutante tipo) => tipo == TipoDeExecutante.EquipePropria ? "Equipe própria" : "Terceiro (empreiteiro)";

    public static string Texto(FormaDePagamentoDaOrdem forma) => forma switch
    {
        FormaDePagamentoDaOrdem.Empreitada => "Empreitada (preço fechado)",
        FormaDePagamentoDaOrdem.PorMedicao => "Por medição",
        _ => "Diária",
    };

    /// <summary>Emitida, aceita, em execução ou concluída: está na rua, esperando alguém.</summary>
    public static bool EmAndamento(SituacaoDaOrdem situacao) =>
        situacao is SituacaoDaOrdem.Emitida or SituacaoDaOrdem.Aceita or SituacaoDaOrdem.EmExecucao or SituacaoDaOrdem.Concluida;
}

/// <summary>Bloco 58: um item da OS: o serviço, a unidade, a quantidade e o preço unitário (o que se paga ao executante).</summary>
public sealed record ItemDaOrdem
{
    public const int TamanhoMaximoDaDescricao = 200;
    public const int TamanhoMaximoDaUnidade = 12;

    /// <exception cref="DomainValidationException">Sem descrição ou unidade, textos longos, quantidade zero ou negativa, preço negativo.</exception>
    public ItemDaOrdem(Guid? tarefaId, string? descricao, string? unidade, decimal quantidade, decimal precoUnitario)
    {
        var d = descricao?.Trim();
        if (string.IsNullOrEmpty(d))
            throw new DomainValidationException(nameof(Descricao), "Escreva a descrição do item.");
        if (d.Length > TamanhoMaximoDaDescricao)
            throw new DomainValidationException(nameof(Descricao), $"A descrição do item tem {d.Length} caracteres; o máximo é {TamanhoMaximoDaDescricao}.");
        var u = unidade?.Trim();
        if (string.IsNullOrEmpty(u))
            throw new DomainValidationException(nameof(Unidade), $"Informe a unidade de \"{d}\" (m², m³, un, vb…).");
        if (u.Length > TamanhoMaximoDaUnidade)
            throw new DomainValidationException(nameof(Unidade), $"A unidade \"{u}\" passa de {TamanhoMaximoDaUnidade} caracteres.");
        if (quantidade <= 0)
            throw new DomainValidationException(nameof(Quantidade), $"A quantidade de \"{d}\" precisa ser maior que zero.");
        if (precoUnitario < 0)
            throw new DomainValidationException(nameof(PrecoUnitario), $"O preço de \"{d}\" não pode ser negativo.");
        if (quantidade * precoUnitario > Tarefa.ValorMaximo)
            throw new DomainValidationException(nameof(PrecoUnitario), $"O total de \"{d}\" passa do máximo aceito.");
        (TarefaId, Descricao, Unidade, Quantidade, PrecoUnitario) =
            (tarefaId, d, u, quantidade, Math.Round(precoUnitario, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>A tarefa do cronograma de onde o item veio; nula num item avulso.</summary>
    public Guid? TarefaId { get; }

    public string Descricao { get; }

    public string Unidade { get; }

    public decimal Quantidade { get; }

    public decimal PrecoUnitario { get; }

    public decimal Total => Math.Round(Quantidade * PrecoUnitario, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Bloco 58: um ponto da lista de verificação ("limpeza feita", "teste de estanqueidade").</summary>
public sealed record ItemDeVerificacao
{
    public const int TamanhoMaximo = 150;

    /// <exception cref="DomainValidationException">Texto vazio ou longo demais.</exception>
    public ItemDeVerificacao(string? texto, bool feito = false)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t))
            throw new DomainValidationException(nameof(Texto), "O item da verificação está vazio.");
        if (t.Length > TamanhoMaximo)
            throw new DomainValidationException(nameof(Texto), $"O item da verificação passa de {TamanhoMaximo} caracteres.");
        (Texto, Feito) = (t, feito);
    }

    public string Texto { get; }

    public bool Feito { get; }
}

/// <summary>Bloco 58: um passo do histórico da OS: quando, para que situação, o que aconteceu e quem fez.</summary>
/// <param name="Descricao">"Emitida", "Recusada: o preço não cobre o material", "Aceita pelo Telegram".</param>
/// <param name="Autor">Quem fez (no Telegram, o nome da pessoa); nulo quando foi no programa.</param>
public sealed record EventoDaOrdem(DateTime Quando, SituacaoDaOrdem Situacao, string Descricao, string? Autor = null);

/// <summary>
/// Bloco 58: o que se edita numa OS em rascunho: o título (o escopo em poucas palavras), o executante, as tarefas e os itens,
/// as datas, a forma de pagamento, as observações e a lista de verificação.
/// </summary>
public sealed record DadosDaOrdem
{
    public const int TamanhoMaximoDoTitulo = 120;
    public const int TamanhoMaximoDoExecutante = 120;
    public const int TamanhoMaximoDoContato = 80;
    public const int TamanhoMaximoDasObservacoes = 2000;
    public const int MaximoDeItens = 200;

    /// <exception cref="DomainValidationException">Sem título, textos longos, prazo antes do início, itens demais.</exception>
    public DadosDaOrdem(string? titulo, TipoDeExecutante tipo, string? executante, Guid? recursoId, string? contato, IEnumerable<Guid>? tarefas,
        IEnumerable<ItemDaOrdem>? itens, DateOnly? inicioPrevisto, DateOnly? prazo, FormaDePagamentoDaOrdem formaDePagamento, string? observacoes,
        IEnumerable<ItemDeVerificacao>? verificacao)
    {
        var t = titulo?.Trim();
        if (string.IsNullOrEmpty(t))
            throw new DomainValidationException(nameof(Titulo), "Dê um título à OS (o serviço em poucas palavras).");
        if (t.Length > TamanhoMaximoDoTitulo)
            throw new DomainValidationException(nameof(Titulo), $"O título tem {t.Length} caracteres; o máximo é {TamanhoMaximoDoTitulo}.");
        if (!Enum.IsDefined(tipo))
            throw new DomainValidationException(nameof(Tipo), "Tipo de executante desconhecido.");
        if (!Enum.IsDefined(formaDePagamento))
            throw new DomainValidationException(nameof(FormaDePagamento), "Forma de pagamento desconhecida.");
        var e = Opcional(executante, TamanhoMaximoDoExecutante, "O executante");
        var c = Opcional(contato, TamanhoMaximoDoContato, "O contato");
        var o = Opcional(observacoes, TamanhoMaximoDasObservacoes, "As observações");
        if (inicioPrevisto is { } inicio && prazo is { } fim && fim < inicio)
            throw new DomainValidationException(nameof(Prazo), "O prazo é antes do início previsto.");
        var listaDeItens = (itens ?? []).ToList();
        if (listaDeItens.Count > MaximoDeItens)
            throw new DomainValidationException(nameof(Itens), $"A OS tem {listaDeItens.Count} itens; o máximo é {MaximoDeItens}.");
        (Titulo, Tipo, Executante, RecursoId, Contato, Tarefas, Itens, InicioPrevisto, Prazo, FormaDePagamento, Observacoes, Verificacao) =
            (t, tipo, e, recursoId, c, [.. (tarefas ?? []).Distinct()], listaDeItens, inicioPrevisto, prazo, formaDePagamento, o, [.. verificacao ?? []]);
    }

    public string Titulo { get; }

    public TipoDeExecutante Tipo { get; }

    /// <summary>O nome da equipe ou do empreiteiro; obrigatório para emitir.</summary>
    public string? Executante { get; }

    /// <summary>O recurso do projeto que executa (quando foi escolhido da lista).</summary>
    public Guid? RecursoId { get; }

    /// <summary>Telefone ou e-mail do executante.</summary>
    public string? Contato { get; }

    /// <summary>As tarefas do cronograma que a OS cobre.</summary>
    public IReadOnlyList<Guid> Tarefas { get; }

    public IReadOnlyList<ItemDaOrdem> Itens { get; }

    public DateOnly? InicioPrevisto { get; }

    public DateOnly? Prazo { get; }

    public FormaDePagamentoDaOrdem FormaDePagamento { get; }

    public string? Observacoes { get; }

    public IReadOnlyList<ItemDeVerificacao> Verificacao { get; }

    public decimal Total => Itens.Sum(i => i.Total);

    /// <summary>A mesma OS com a lista de verificação trocada (marcar o que foi feito, durante a execução).</summary>
    internal DadosDaOrdem ComVerificacao(IReadOnlyList<ItemDeVerificacao> verificacao) =>
        new(Titulo, Tipo, Executante, RecursoId, Contato, Tarefas, Itens, InicioPrevisto, Prazo, FormaDePagamento, Observacoes, verificacao);

    private static string? Opcional(string? texto, int maximo, string nome)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        return t.Length > maximo ? throw new DomainValidationException(nome, $"{nome} tem {t.Length} caracteres; o máximo é {maximo}.") : t;
    }
}

/// <summary>
/// Bloco 58: uma ordem de serviço: o número na obra (OS-001, OS-002…), os dados, a situação no fluxo e o histórico. É imutável:
/// cada passo devolve a OS nova (e a lista do projeto troca a antiga), para o Desfazer voltar a qualquer ponto.
/// </summary>
public sealed record OrdemDeServico
{
    private OrdemDeServico(Guid id, int numero, DadosDaOrdem dados, SituacaoDaOrdem situacao, DateOnly? emissao, DateOnly? conclusao,
        IReadOnlyList<EventoDaOrdem> historico)
    {
        (Id, Numero, Dados, Situacao, Emissao, Conclusao, Historico) = (id, numero, dados, situacao, emissao, conclusao, historico);
    }

    /// <summary>Uma OS nova, em rascunho.</summary>
    /// <exception cref="DomainValidationException">Id vazio ou número menor que 1.</exception>
    public static OrdemDeServico Nova(Guid id, int numero, DadosDaOrdem dados, DateTime criadaEm)
    {
        ArgumentNullException.ThrowIfNull(dados);
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id da OS é obrigatório.");
        if (numero < 1)
            throw new DomainValidationException(nameof(Numero), "O número da OS começa em 1.");
        return new OrdemDeServico(id, numero, dados, SituacaoDaOrdem.Rascunho, null, null, [new EventoDaOrdem(criadaEm, SituacaoDaOrdem.Rascunho, "Criada")]);
    }

    /// <summary>A OS como estava no arquivo (sem conferir o fluxo).</summary>
    /// <exception cref="DomainValidationException">Id vazio, número menor que 1 ou situação desconhecida.</exception>
    public static OrdemDeServico Restaurar(Guid id, int numero, DadosDaOrdem dados, SituacaoDaOrdem situacao, DateOnly? emissao, DateOnly? conclusao,
        IEnumerable<EventoDaOrdem> historico)
    {
        ArgumentNullException.ThrowIfNull(dados);
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id da OS é obrigatório.");
        if (numero < 1)
            throw new DomainValidationException(nameof(Numero), "O número da OS começa em 1.");
        if (!Enum.IsDefined(situacao))
            throw new DomainValidationException(nameof(Situacao), "Situação de OS desconhecida.");
        return new OrdemDeServico(id, numero, dados, situacao, emissao, conclusao, [.. historico]);
    }

    public Guid Id { get; }

    public int Numero { get; }

    /// <summary>"OS-007".</summary>
    public string Codigo => $"OS-{Numero:000}";

    public DadosDaOrdem Dados { get; private init; }

    public SituacaoDaOrdem Situacao { get; private init; }

    public DateOnly? Emissao { get; private init; }

    /// <summary>Quando o executante disse que terminou.</summary>
    public DateOnly? Conclusao { get; private init; }

    public IReadOnlyList<EventoDaOrdem> Historico { get; private init; }

    public decimal Total => Dados.Total;

    public bool PodeEditar => Situacao == SituacaoDaOrdem.Rascunho;

    /// <summary>Passou do prazo sem terminar (emitida, aceita ou em execução).</summary>
    public bool Vencida(DateOnly hoje) =>
        Dados.Prazo is { } prazo && prazo < hoje && Situacao is SituacaoDaOrdem.Emitida or SituacaoDaOrdem.Aceita or SituacaoDaOrdem.EmExecucao;

    // ---- O fluxo ----

    /// <exception cref="DomainValidationException">A OS não está em rascunho.</exception>
    public OrdemDeServico Editar(DadosDaOrdem dados)
    {
        ArgumentNullException.ThrowIfNull(dados);
        Exigir("editar", SituacaoDaOrdem.Rascunho);
        return this with { Dados = dados };
    }

    /// <exception cref="DomainValidationException">Fora do rascunho, sem executante ou sem itens.</exception>
    public OrdemDeServico Emitir(DateTime quando, string? autor = null)
    {
        Exigir("emitir", SituacaoDaOrdem.Rascunho);
        if (Dados.Executante is null)
            throw new DomainValidationException(nameof(DadosDaOrdem.Executante), $"Para emitir a {Codigo}, informe o executante (a equipe ou o empreiteiro).");
        if (Dados.Itens.Count == 0)
            throw new DomainValidationException(nameof(DadosDaOrdem.Itens), $"Para emitir a {Codigo}, ponha pelo menos um item (o serviço, a quantidade e o preço).");
        return Passo(SituacaoDaOrdem.Emitida, quando, "Emitida", autor) with { Emissao = DateOnly.FromDateTime(quando) };
    }

    /// <summary>O executante aceitou.</summary>
    public OrdemDeServico Aceitar(DateTime quando, string? autor = null)
    {
        Exigir("aceitar", SituacaoDaOrdem.Emitida);
        return Passo(SituacaoDaOrdem.Aceita, quando, "Aceita", autor);
    }

    /// <summary>O executante recusou (o motivo vai no histórico).</summary>
    public OrdemDeServico Recusar(DateTime quando, string? motivo, string? autor = null)
    {
        Exigir("recusar", SituacaoDaOrdem.Emitida);
        return Passo(SituacaoDaOrdem.Recusada, quando, ComMotivo("Recusada", motivo), autor);
    }

    /// <summary>Volta a rascunho para corrigir (uma recusada, ou uma emitida que ainda ninguém aceitou).</summary>
    public OrdemDeServico VoltarARascunho(DateTime quando, string? autor = null)
    {
        Exigir("voltar a rascunho", SituacaoDaOrdem.Recusada, SituacaoDaOrdem.Emitida);
        return Passo(SituacaoDaOrdem.Rascunho, quando, "Voltou a rascunho para corrigir", autor) with { Emissao = null };
    }

    /// <summary>Começou o serviço (de uma emitida, vale como aceita).</summary>
    public OrdemDeServico Iniciar(DateTime quando, string? autor = null)
    {
        Exigir("iniciar", SituacaoDaOrdem.Emitida, SituacaoDaOrdem.Aceita);
        return Passo(SituacaoDaOrdem.EmExecucao, quando, Situacao == SituacaoDaOrdem.Emitida ? "Aceita e iniciada" : "Iniciada", autor);
    }

    /// <summary>O executante terminou; falta a conferência.</summary>
    public OrdemDeServico Concluir(DateTime quando, string? autor = null)
    {
        Exigir("concluir", SituacaoDaOrdem.Aceita, SituacaoDaOrdem.EmExecucao);
        return Passo(SituacaoDaOrdem.Concluida, quando, "Concluída", autor) with { Conclusao = DateOnly.FromDateTime(quando) };
    }

    /// <summary>A conferência achou problema: volta à execução (o motivo vai no histórico).</summary>
    public OrdemDeServico Reprovar(DateTime quando, string? motivo, string? autor = null)
    {
        Exigir("reprovar", SituacaoDaOrdem.Concluida);
        return Passo(SituacaoDaOrdem.EmExecucao, quando, ComMotivo("Reprovada na conferência", motivo), autor) with { Conclusao = null };
    }

    /// <summary>A conferência aprovou o serviço.</summary>
    public OrdemDeServico Conferir(DateTime quando, string? autor = null)
    {
        Exigir("conferir", SituacaoDaOrdem.Concluida);
        return Passo(SituacaoDaOrdem.Conferida, quando, "Conferida e aprovada", autor);
    }

    /// <summary>Bloco 60: a OS conferida foi paga por inteiro.</summary>
    public OrdemDeServico MarcarComoPaga(DateTime quando, string? autor = null)
    {
        Exigir("marcar como paga", SituacaoDaOrdem.Conferida);
        return Passo(SituacaoDaOrdem.Paga, quando, "Paga", autor);
    }

    /// <summary>Bloco 60: um pagamento desfeito tira a OS de "paga".</summary>
    public OrdemDeServico VoltarAConferida(DateTime quando, string? autor = null)
    {
        Exigir("reabrir o pagamento", SituacaoDaOrdem.Paga);
        return Passo(SituacaoDaOrdem.Conferida, quando, "Pagamento reaberto", autor);
    }

    /// <summary>Cancela (antes da conferência).</summary>
    public OrdemDeServico Cancelar(DateTime quando, string? motivo, string? autor = null)
    {
        Exigir("cancelar", SituacaoDaOrdem.Rascunho, SituacaoDaOrdem.Emitida, SituacaoDaOrdem.Aceita, SituacaoDaOrdem.Recusada,
            SituacaoDaOrdem.EmExecucao, SituacaoDaOrdem.Concluida);
        return Passo(SituacaoDaOrdem.Cancelada, quando, ComMotivo("Cancelada", motivo), autor);
    }

    /// <summary>Marca (ou desmarca) um ponto da lista de verificação, durante a execução ou na conferência.</summary>
    /// <exception cref="DomainValidationException">Fora da execução, ou um ponto que não existe.</exception>
    public OrdemDeServico MarcarVerificacao(int indice, bool feito)
    {
        Exigir("marcar a verificação", SituacaoDaOrdem.Aceita, SituacaoDaOrdem.EmExecucao, SituacaoDaOrdem.Concluida);
        if (indice < 0 || indice >= Dados.Verificacao.Count)
            throw new DomainValidationException(nameof(DadosDaOrdem.Verificacao), "Esse ponto não está na lista de verificação.");
        var lista = Dados.Verificacao.ToList();
        lista[indice] = new ItemDeVerificacao(lista[indice].Texto, feito);
        return this with { Dados = Dados.ComVerificacao(lista) };
    }

    /// <summary>Se a OS pode ir para a situação (para a tela habilitar os botões).</summary>
    public bool PodeIrPara(SituacaoDaOrdem destino) => destino switch
    {
        SituacaoDaOrdem.Emitida => Situacao == SituacaoDaOrdem.Rascunho,
        SituacaoDaOrdem.Aceita or SituacaoDaOrdem.Recusada => Situacao == SituacaoDaOrdem.Emitida,
        SituacaoDaOrdem.Rascunho => Situacao is SituacaoDaOrdem.Recusada or SituacaoDaOrdem.Emitida,
        SituacaoDaOrdem.EmExecucao => Situacao is SituacaoDaOrdem.Emitida or SituacaoDaOrdem.Aceita,
        SituacaoDaOrdem.Concluida => Situacao is SituacaoDaOrdem.Aceita or SituacaoDaOrdem.EmExecucao,
        SituacaoDaOrdem.Conferida => Situacao == SituacaoDaOrdem.Concluida,
        SituacaoDaOrdem.Paga => Situacao == SituacaoDaOrdem.Conferida,
        SituacaoDaOrdem.Cancelada => Situacao is not (SituacaoDaOrdem.Conferida or SituacaoDaOrdem.Paga or SituacaoDaOrdem.Cancelada),
        _ => false,
    };

    private void Exigir(string acao, params SituacaoDaOrdem[] permitidas)
    {
        if (!permitidas.Contains(Situacao))
            throw new DomainValidationException(nameof(Situacao), $"Não dá para {acao} a {Codigo}: ela está \"{TextosDaOrdem.Texto(Situacao).ToLowerInvariant()}\".");
    }

    private OrdemDeServico Passo(SituacaoDaOrdem nova, DateTime quando, string descricao, string? autor) =>
        this with { Situacao = nova, Historico = [.. Historico, new EventoDaOrdem(quando, nova, descricao, string.IsNullOrWhiteSpace(autor) ? null : autor.Trim())] };

    private static string ComMotivo(string acao, string? motivo) => string.IsNullOrWhiteSpace(motivo) ? acao : $"{acao}: {motivo.Trim()}";
}
