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

    /// <summary>Bloco 65: "equipe própria" ou "empreiteiro", para ir entre parênteses depois do nome.</summary>
    public static string TipoCurto(TipoDeExecutante tipo) => tipo == TipoDeExecutante.EquipePropria ? "equipe própria" : "empreiteiro";

    public static string Texto(FormaDePagamentoDaOrdem forma) => forma switch
    {
        FormaDePagamentoDaOrdem.Empreitada => "Empreitada (preço fechado)",
        FormaDePagamentoDaOrdem.PorMedicao => "Por medição",
        _ => "Diária",
    };

    /// <summary>
    /// Bloco 59: o corpo da mensagem da OS no Telegram: o executante, as datas, o pagamento, os itens (com os valores, se
    /// <paramref name="comValores"/>), as observações e a verificação. O servidor põe o número, o título e a situação em cima.
    /// </summary>
    public static string ParaOTelegram(OrdemDeServico ordem, bool comValores)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        var brasil = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var d = ordem.Dados;
        var tipo = d.Tipo == TipoDeExecutante.EquipePropria ? "equipe própria" : "empreiteiro";
        var linhas = new List<string> { $"Executante: {d.Executante ?? "a definir"} ({tipo})" };
        var datas = string.Join(" · ", new[]
        {
            d.InicioPrevisto is { } inicio ? $"Início: {inicio.ToString("dd/MM/yyyy", brasil)}" : null,
            d.Prazo is { } prazo ? $"prazo: {prazo.ToString("dd/MM/yyyy", brasil)}" : null,
        }.OfType<string>());
        if (datas.Length > 0)
            linhas.Add(char.ToUpper(datas[0], brasil) + datas[1..]);
        if (comValores)
            linhas.Add($"Pagamento: {Texto(d.FormaDePagamento)}" + (d.Retencao > 0 ? $" · retenção de garantia de {d.Retencao.ToString("0.##", brasil)}%" : ""));
        linhas.Add("");
        linhas.Add("Itens:");
        foreach (var item in d.Itens)
        {
            var quantidade = $"{Planejamento.Moeda.FormatarQuantidade(item.Quantidade)} {item.Unidade}";
            linhas.Add(comValores
                ? $"• {item.Descricao}: {quantidade} × R$ {Planejamento.Moeda.Formatar(item.PrecoUnitario)} = R$ {Planejamento.Moeda.Formatar(item.Total)}"
                : $"• {item.Descricao}: {quantidade}");
        }
        if (comValores)
            linhas.Add($"Total: R$ {Planejamento.Moeda.Formatar(d.Total)}");
        if (d.Observacoes is { } obs)
        {
            linhas.Add("");
            linhas.Add($"Observações: {obs}");
        }
        if (d.Verificacao.Count > 0)
        {
            linhas.Add("");
            linhas.Add("Verificação: " + string.Join(" · ", d.Verificacao.Select(v => $"{(v.Feito ? "☑" : "☐")} {v.Texto}")));
        }
        return string.Join("\n", linhas);
    }

    /// <summary>
    /// Bloco 62: os dias de atraso: na concluída, da conclusão depois do prazo (0 se no prazo); na que está na rua, de hoje
    /// depois do prazo (só se venceu). Nulo sem prazo ou sem como medir.
    /// </summary>
    public static int? DiasDeAtraso(OrdemDeServico ordem, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        if (ordem.Dados.Prazo is not { } prazo)
            return null;
        if (ordem.Conclusao is { } conclusao)
            return Math.Max(0, conclusao.DayNumber - prazo.DayNumber);
        return ordem.Vencida(hoje) ? hoje.DayNumber - prazo.DayNumber : null;
    }

    /// <summary>
    /// Bloco 62: "Prazos: 3 de 4 concluídas no prazo (75%); atraso médio de 2 dias" sobre as OS concluídas com prazo; nulo sem
    /// nenhuma.
    /// </summary>
    public static string? Prazos(IEnumerable<OrdemDeServico> ordens)
    {
        ArgumentNullException.ThrowIfNull(ordens);
        var atrasos = ordens.Where(o => o.Situacao != SituacaoDaOrdem.Cancelada && o.Conclusao is not null && o.Dados.Prazo is not null)
            .Select(o => DiasDeAtraso(o, default)!.Value).ToList();
        if (atrasos.Count == 0)
            return null;
        var noPrazo = atrasos.Count(a => a == 0);
        var atrasadas = atrasos.Where(a => a > 0).ToList();
        return $"Prazos: {noPrazo} de {atrasos.Count} concluída{(atrasos.Count == 1 ? "" : "s")} no prazo ({noPrazo * 100 / atrasos.Count}%)" +
               (atrasadas.Count == 0 ? "" : $"; atraso médio de {Math.Round(atrasadas.Average(), 1).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} dia{(atrasadas.Average() == 1 ? "" : "s")}") + ".";
    }

    /// <summary>Bloco 61: "OS-003 · Em execução (vencida em 02/10)" ou "OS-005 · Rascunho", para a dica da tarefa.</summary>
    public static string Linha(OrdemDeServico ordem, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        var brasil = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var prazo = ordem.Dados.Prazo is { } p && EmAndamento(ordem.Situacao) && ordem.Situacao != SituacaoDaOrdem.Concluida
            ? ordem.Vencida(hoje) ? $" (vencida em {p.ToString("dd/MM", brasil)})" : $" (prazo {p.ToString("dd/MM", brasil)})"
            : "";
        return $"{ordem.Codigo} · {Texto(ordem.Situacao)}{prazo}" + (ordem.Dados.Executante is { } e ? $" · {e}" : "");
    }

    /// <summary>
    /// Bloco 61: o aviso ao abrir a obra: as OS vencidas e as que esperam a conferência; nulo se não há nada esperando.
    /// </summary>
    public static string? Aviso(IEnumerable<OrdemDeServico> ordens, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(ordens);
        var lista = ordens.ToList();
        var vencidas = lista.Count(o => o.Vencida(hoje));
        var conferir = lista.Count(o => o.Situacao == SituacaoDaOrdem.Concluida);
        if (vencidas == 0 && conferir == 0)
            return null;
        var partes = new[]
        {
            vencidas == 0 ? null : vencidas == 1 ? "1 vencida" : $"{vencidas} vencidas",
            conferir == 0 ? null : conferir == 1 ? "1 esperando a conferência" : $"{conferir} esperando a conferência",
        }.OfType<string>();
        return $"Ordens de serviço: {string.Join(", ", partes)} (Projeto > Ordens de serviço…).";
    }

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
        IEnumerable<ItemDeVerificacao>? verificacao, decimal retencao = 0)
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
        if (retencao is < 0 or > RetencaoMaxima)
            throw new DomainValidationException(nameof(Retencao), $"A retenção vai de 0 a {RetencaoMaxima}%.");
        Retencao = Math.Round(retencao, 2, MidpointRounding.AwayFromZero);
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
        new(Titulo, Tipo, Executante, RecursoId, Contato, Tarefas, Itens, InicioPrevisto, Prazo, FormaDePagamento, Observacoes, verificacao, Retencao);

    public const decimal RetencaoMaxima = 30;

    /// <summary>
    /// Bloco 62: a retenção de garantia, em % de cada pagamento (5% é o comum); fica retida até a liberação, depois da
    /// conferência. Zero sem retenção.
    /// </summary>
    public decimal Retencao { get; }

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
        IEnumerable<EventoDaOrdem> historico, bool noTelegram = false, IEnumerable<MedicaoDaOrdem>? medicoes = null, LiberacaoDaRetencao? liberacao = null)
    {
        ArgumentNullException.ThrowIfNull(dados);
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id da OS é obrigatório.");
        if (numero < 1)
            throw new DomainValidationException(nameof(Numero), "O número da OS começa em 1.");
        if (!Enum.IsDefined(situacao))
            throw new DomainValidationException(nameof(Situacao), "Situação de OS desconhecida.");
        var lista = (medicoes ?? []).ToList();
        if (lista.Any(m => m.Quantidades.Count != dados.Itens.Count))
            throw new DomainValidationException(nameof(Medicoes), "Uma medição não tem a quantidade de cada item.");
        return new OrdemDeServico(id, numero, dados, situacao, emissao, conclusao, [.. historico]) { NoTelegram = noTelegram, Medicoes = lista, Liberacao = liberacao };
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

    /// <summary>Bloco 59: a OS foi mandada aos grupos da obra no Telegram (as mudanças seguintes vão junto).</summary>
    public bool NoTelegram { get; private init; }

    public OrdemDeServico MandadaAoTelegram() => this with { NoTelegram = true };

    // ---- Bloco 60: medições e pagamentos ----

    /// <summary>As medições, da primeira à última.</summary>
    public IReadOnlyList<MedicaoDaOrdem> Medicoes { get; private init; } = [];

    public decimal TotalMedido => Medicoes.Sum(m => m.Valor);

    /// <summary>O que foi pago ao executante: o líquido das medições pagas e a retenção liberada.</summary>
    public decimal TotalPago => Medicoes.Where(m => m.Paga).Sum(m => m.Valor - RetencaoDe(m)) + (Liberacao?.Valor ?? 0);

    // ---- Bloco 62: retenção de garantia ----

    /// <summary>A retenção de uma medição (o % da OS sobre o valor dela).</summary>
    public decimal RetencaoDe(MedicaoDaOrdem medicao) => Math.Round(medicao.Valor * Dados.Retencao / 100, 2, MidpointRounding.AwayFromZero);

    /// <summary>A retenção das medições pagas que ainda não foi liberada.</summary>
    public decimal Retido => Medicoes.Where(m => m.Paga).Sum(RetencaoDe) - (Liberacao?.Valor ?? 0);

    /// <summary>A retenção liberada (paga) ao executante; nula antes disso.</summary>
    public LiberacaoDaRetencao? Liberacao { get; private init; }

    /// <summary>Conferida, com tudo medido e pago, falta só liberar a retenção.</summary>
    public bool PodeLiberarRetencao => Situacao == SituacaoDaOrdem.Conferida && Liberacao is null && PorMedir == 0 && Medicoes.All(m => m.Paga) && Retido > 0;

    /// <summary>Paga a retenção ao executante (a OS fica paga).</summary>
    /// <exception cref="DomainValidationException">Ainda não dá para liberar (falta conferir, medir ou pagar).</exception>
    public OrdemDeServico LiberarRetencao(DateOnly data, string? documento, IReadOnlyList<Guid> lancamentos, DateTime quando, string? autor = null)
    {
        if (!PodeLiberarRetencao)
            throw new DomainValidationException(nameof(Liberacao), $"A retenção da {Codigo} se libera depois de conferida, com tudo medido e pago.");
        var valor = Retido;
        var nova = this with
        {
            Liberacao = new LiberacaoDaRetencao(data, string.IsNullOrWhiteSpace(documento) ? null : documento.Trim(), valor, lancamentos),
            Historico = [.. Historico, new EventoDaOrdem(quando, Situacao, $"Retenção liberada: R$ {Planejamento.Moeda.Formatar(valor)}", Autor(autor))],
        };
        return nova.MarcarComoPaga(quando, autor);
    }

    /// <summary>Desfaz a liberação da retenção (a OS volta a conferida).</summary>
    public OrdemDeServico DesfazerLiberacao(DateTime quando, string? autor = null)
    {
        if (Liberacao is null)
            throw new DomainValidationException(nameof(Liberacao), $"A retenção da {Codigo} não foi liberada.");
        var nova = this with
        {
            Liberacao = null,
            Historico = [.. Historico, new EventoDaOrdem(quando, Situacao, "Liberação da retenção desfeita", Autor(autor))],
        };
        return nova.Situacao == SituacaoDaOrdem.Paga ? nova.VoltarAConferida(quando, autor) : nova;
    }

    /// <summary>Bloco 62: um rascunho novo com os mesmos dados (a verificação desmarcada), para um serviço que se repete.</summary>
    public OrdemDeServico Duplicar(Guid id, int numero, DateTime quando) =>
        Nova(id, numero, Dados.ComVerificacao([.. Dados.Verificacao.Select(v => new ItemDeVerificacao(v.Texto))]), quando) with
        {
            Historico = [new EventoDaOrdem(quando, SituacaoDaOrdem.Rascunho, $"Criada como cópia da {Codigo}")],
        };

    /// <summary>O que já foi medido e ainda não foi pago.</summary>
    public decimal APagar => TotalMedido - TotalPago;

    /// <summary>O que ainda não foi medido (o contratado menos o medido).</summary>
    public decimal PorMedir => Math.Max(0, Total - TotalMedido);

    /// <summary>A quantidade do item (pela posição) já medida, em todas as medições.</summary>
    public decimal MedidoDoItem(int indice) => Medicoes.Sum(m => m.Quantidades[indice]);

    /// <summary>Mede-se a partir do aceite até a conferência.</summary>
    public bool PodeMedir => Situacao is SituacaoDaOrdem.Aceita or SituacaoDaOrdem.EmExecucao or SituacaoDaOrdem.Concluida or SituacaoDaOrdem.Conferida;

    /// <summary>Uma medição com a quantidade de cada item nesta medição (na ordem dos itens).</summary>
    /// <exception cref="DomainValidationException">Fora da execução, quantidades erradas, nada medido ou mais que o contratado.</exception>
    public OrdemDeServico Medir(Guid id, DateOnly data, IReadOnlyList<decimal> quantidades, DateTime quando, string? autor = null)
    {
        ArgumentNullException.ThrowIfNull(quantidades);
        if (!PodeMedir)
            throw new DomainValidationException(nameof(Medicoes), $"Não dá para medir a {Codigo}: ela está \"{TextosDaOrdem.Texto(Situacao).ToLowerInvariant()}\".");
        if (quantidades.Count != Dados.Itens.Count)
            throw new DomainValidationException(nameof(Medicoes), "A medição precisa da quantidade de cada item.");
        if (quantidades.All(q => q == 0))
            throw new DomainValidationException(nameof(Medicoes), "Informe a quantidade medida de pelo menos um item.");
        for (var i = 0; i < quantidades.Count; i++)
        {
            var item = Dados.Itens[i];
            if (quantidades[i] < 0)
                throw new DomainValidationException(nameof(Medicoes), $"A quantidade medida de \"{item.Descricao}\" não pode ser negativa.");
            if (MedidoDoItem(i) + quantidades[i] > item.Quantidade)
                throw new DomainValidationException(nameof(Medicoes),
                    $"\"{item.Descricao}\" passaria do contratado: {Planejamento.Moeda.FormatarQuantidade(MedidoDoItem(i) + quantidades[i])} de " +
                    $"{Planejamento.Moeda.FormatarQuantidade(item.Quantidade)} {item.Unidade}.");
        }
        var valor = Dados.Itens.Select((item, i) => Math.Round(quantidades[i] * item.PrecoUnitario, 2, MidpointRounding.AwayFromZero)).Sum();
        var medicao = new MedicaoDaOrdem(id, Medicoes.Count == 0 ? 1 : Medicoes.Max(m => m.Numero) + 1, data, quantidades, valor);
        return this with
        {
            Medicoes = [.. Medicoes, medicao],
            Historico = [.. Historico, new EventoDaOrdem(quando, Situacao, $"Medição {medicao.Codigo}: R$ {Planejamento.Moeda.Formatar(valor)}", Autor(autor))],
        };
    }

    /// <summary>Uma medição com tudo o que falta medir; a mesma OS se não falta nada.</summary>
    public OrdemDeServico MedirOQueFalta(Guid id, DateOnly data, DateTime quando, string? autor = null)
    {
        var falta = Dados.Itens.Select((item, i) => item.Quantidade - MedidoDoItem(i)).ToList();
        return falta.All(q => q <= 0) ? this : Medir(id, data, falta, quando, autor);
    }

    /// <summary>Tira uma medição que ainda não foi paga.</summary>
    /// <exception cref="DomainValidationException">A medição não existe ou já foi paga.</exception>
    public OrdemDeServico RemoverMedicao(Guid id, DateTime quando, string? autor = null)
    {
        var medicao = Medicoes.FirstOrDefault(m => m.Id == id) ?? throw new DomainValidationException(nameof(Medicoes), "A medição não é desta OS.");
        if (medicao.Paga)
            throw new DomainValidationException(nameof(Medicoes), $"A {medicao.Codigo} já foi paga: desfaça o pagamento antes.");
        return this with
        {
            Medicoes = [.. Medicoes.Where(m => m.Id != id)],
            Historico = [.. Historico, new EventoDaOrdem(quando, Situacao, $"Medição {medicao.Codigo} retirada", Autor(autor))],
        };
    }

    /// <summary>Registra o pagamento de uma medição; conferida, toda medida e toda paga, a OS fica paga.</summary>
    /// <exception cref="DomainValidationException">A medição não existe ou já foi paga.</exception>
    public OrdemDeServico RegistrarPagamento(Guid medicaoId, PagamentoDaMedicao pagamento, DateTime quando, string? autor = null)
    {
        ArgumentNullException.ThrowIfNull(pagamento);
        var medicao = Medicoes.FirstOrDefault(m => m.Id == medicaoId) ?? throw new DomainValidationException(nameof(Medicoes), "A medição não é desta OS.");
        if (medicao.Paga)
            throw new DomainValidationException(nameof(Medicoes), $"A {medicao.Codigo} já foi paga.");
        var nova = this with
        {
            Medicoes = [.. Medicoes.Select(m => m.Id == medicaoId ? m with { Pagamento = pagamento } : m)],
            Historico = [.. Historico, new EventoDaOrdem(quando, Situacao,
                $"Pagamento da {medicao.Codigo}: R$ {Planejamento.Moeda.Formatar(medicao.Valor)}" + (pagamento.Documento is { } doc ? $" ({doc})" : ""), Autor(autor))],
        };
        return nova.Situacao == SituacaoDaOrdem.Conferida && nova.PorMedir == 0 && nova.APagar == 0 ? nova.MarcarComoPaga(quando, autor) : nova;
    }

    /// <summary>Desfaz o pagamento de uma medição (a OS paga volta a conferida).</summary>
    /// <exception cref="DomainValidationException">A medição não existe ou não foi paga.</exception>
    public OrdemDeServico DesfazerPagamento(Guid medicaoId, DateTime quando, string? autor = null)
    {
        var medicao = Medicoes.FirstOrDefault(m => m.Id == medicaoId) ?? throw new DomainValidationException(nameof(Medicoes), "A medição não é desta OS.");
        if (!medicao.Paga)
            throw new DomainValidationException(nameof(Medicoes), $"A {medicao.Codigo} não foi paga.");
        if (Liberacao is not null)
            throw new DomainValidationException(nameof(Medicoes), "A retenção já foi liberada: desfaça a liberação antes.");
        var nova = this with
        {
            Medicoes = [.. Medicoes.Select(m => m.Id == medicaoId ? m with { Pagamento = null } : m)],
            Historico = [.. Historico, new EventoDaOrdem(quando, Situacao, $"Pagamento da {medicao.Codigo} desfeito", Autor(autor))],
        };
        return nova.Situacao == SituacaoDaOrdem.Paga ? nova.VoltarAConferida(quando, autor) : nova;
    }

    private static string? Autor(string? autor) => string.IsNullOrWhiteSpace(autor) ? null : autor.Trim();

    /// <summary>
    /// Bloco 59: o passo que alguém deu pelo Telegram (aceitar, recusar, iniciar, concluir), com o nome da pessoa no histórico.
    /// </summary>
    /// <exception cref="DomainValidationException">A ação é desconhecida ou a situação de agora não deixa.</exception>
    public OrdemDeServico PeloTelegram(string acao, DateTime quando, string autor, string? motivo = null)
    {
        var quem = $"{autor}, pelo Telegram";
        return acao switch
        {
            "aceitar" => Aceitar(quando, quem),
            "recusar" => Recusar(quando, motivo, quem),
            "iniciar" => Iniciar(quando, quem),
            "concluir" => Concluir(quando, quem),
            _ => throw new DomainValidationException(nameof(acao), $"Ação desconhecida na {Codigo}: {acao}."),
        };
    }

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
