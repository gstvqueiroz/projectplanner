using System.Globalization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.Core.Kanban;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Services;

/// <summary>Bloco 41: uma atualização da obra já resolvida (tarefas pelo id, datas e números conferidos), pronta para aplicar.</summary>
public abstract record OperacaoDaObra
{
    /// <summary>Aplica no cronograma (o aberto ou uma cópia).</summary>
    /// <exception cref="DomainValidationException">O domínio recusou (tarefa resumo, acima do saldo, medição aprovada…).</exception>
    public abstract void Aplicar(Cronograma cronograma, DateTime agora);

    protected static Tarefa Tarefa(Cronograma c, Guid id) =>
        c.Tarefas.FirstOrDefault(t => t.Id == id) ?? throw new DomainValidationException(nameof(Tarefa), "A tarefa não existe mais.");
}

public sealed record PercentualDaTarefa(Guid TarefaId, int Percentual) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora) => Tarefa(c, TarefaId).AtualizarPercentualConcluido(Percentual);
}

/// <summary>
/// Bloco 84: a produção de uma tarefa ("fizemos 20 m² hoje"): soma ao executado dela (com <paramref name="Total"/>, é o executado
/// acumulado) e o % concluído sai do executado ÷ a quantidade total. A conta é feita ao aplicar, sobre o executado de então: dois
/// cartões do Telegram aplicados um depois do outro somam os dois.
/// </summary>
public sealed record ProducaoDaTarefa(Guid TarefaId, decimal Quantidade, bool Total = false) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var tarefa = Tarefa(c, TarefaId);
        if (Total)
            tarefa.DefinirQuantidadeExecutada(Quantidade);
        else
            tarefa.ApontarProducao(Quantidade);
    }
}

/// <param name="Numero">A medição em rascunho; nulo, a última em rascunho ou uma nova (De a Ate).</param>
public sealed record MedirNaMedicao(int? Numero, DateOnly De, DateOnly Ate, Guid TarefaId, decimal Valor) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var numero = Numero ?? c.Contrato.Medicoes.LastOrDefault(m => !m.Travada)?.Numero ?? c.Contrato.NovaMedicao(De, Ate).Numero;
        var medicao = c.Contrato.ObterMedicao(numero);
        if (medicao.Travada)
            throw new DomainValidationException(nameof(Numero), $"A medição nº {numero:00} já foi aprovada: abra outra para medir mais.");
        c.Contrato.Medir(numero, TarefaId, Valor);
    }
}

public sealed record LancarCusto(LancamentoDeCusto Lancamento) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora) => c.Custos.Adicionar(Lancamento);
}

public sealed record RestricaoDeInicio(Guid TarefaId, DateOnly Data) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora) => Tarefa(c, TarefaId).DefinirRestricaoDeInicio(Data.ToDateTime(TimeOnly.MinValue));
}

public sealed record SalvarLinhaDeBase(string? Nome) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora) => c.SalvarLinhaDeBase(Nome, agora);
}

/// <summary>Bloco 63: o que a IA pediu para a OS (os campos que vieram; nulo é "não muda" ou "do cronograma").</summary>
public sealed record PedidoDeOrdem(string? Titulo, string? Executante, TipoDeExecutante? Tipo, DateOnly? Inicio, DateOnly? Prazo,
    FormaDePagamentoDaOrdem? Forma, decimal? Retencao, decimal? Valor, string? Observacoes)
{
    /// <summary>Os dados da OS com o que foi pedido por cima.</summary>
    /// <exception cref="DomainValidationException">O valor pedido numa OS de vários itens, ou um campo que não vale.</exception>
    public DadosDaOrdem Sobre(DadosDaOrdem d, IReadOnlyList<ItemDaOrdem>? itens = null)
    {
        var lista = (itens ?? d.Itens).ToList();
        if (Valor is { } valor)
        {
            if (lista.Count != 1)
                throw new DomainValidationException(nameof(Valor), "A OS tem vários itens: ajuste os preços de cada um na tela da OS.");
            var item = lista[0];
            lista[0] = new ItemDaOrdem(item.TarefaId, item.Descricao, item.Unidade, item.Quantidade, Math.Round(valor / item.Quantidade, 2, MidpointRounding.AwayFromZero));
        }
        return new DadosDaOrdem(Titulo ?? d.Titulo, Tipo ?? d.Tipo, Executante ?? d.Executante, d.RecursoId, d.Contato, d.Tarefas, lista,
            Inicio ?? d.InicioPrevisto, Prazo ?? d.Prazo, Forma ?? d.FormaDePagamento, Observacoes ?? d.Observacoes, d.Verificacao, Retencao ?? d.Retencao);
    }

    /// <summary>" · Construtora Silva · prazo 10/10/2026 · R$ 3.000,00": os campos pedidos, para o cartão.</summary>
    public string Texto()
    {
        var partes = new[]
        {
            Executante, Tipo is { } t ? TextosDaOrdem.Texto(t).ToLowerInvariant() : null,
            Inicio is { } i ? $"início {i:dd/MM/yyyy}" : null, Prazo is { } p ? $"prazo {p:dd/MM/yyyy}" : null,
            Forma is { } f ? TextosDaOrdem.Texto(f).ToLowerInvariant() : null, Retencao is { } r ? $"retenção {r:0.##}%" : null,
            Valor is { } v ? Moeda.ComSimbolo(v) : null,
        }.OfType<string>().ToList();
        return partes.Count == 0 ? "" : " · " + string.Join(" · ", partes);
    }
}

/// <summary>Bloco 63: a tarefa nova que a OS cria (o serviço ainda não estava no cronograma), depois da predecessora.</summary>
public sealed record NovaTarefaDaOrdem(Guid Id, string Nome, int Duracao, Guid? Apos);

/// <summary>Bloco 63: cria a OS em rascunho (e a tarefa nova, se for o caso), com os itens e as datas do cronograma.</summary>
public sealed record CriarOrdem(Guid Id, PedidoDeOrdem Pedido, IReadOnlyList<Guid> Tarefas, NovaTarefaDaOrdem? Nova) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var tarefas = Tarefas.ToList();
        if (Nova is { } nova)
        {
            var tarefa = new Tarefa(nova.Id, nova.Nome, c.DataInicio, nova.Duracao);
            if (nova.Apos is { } aposId)
            {
                var antes = Tarefa(c, aposId);
                var irmas = c.Tarefas.Where(t => t.TarefaPaiId == antes.TarefaPaiId).ToList();
                c.InserirTarefaNaPosicao(tarefa, antes.TarefaPaiId, irmas.IndexOf(antes) + 1);
                c.AdicionarPredecessora(new Predecessora(antes.Id, tarefa.Id));
            }
            else
                c.AdicionarTarefa(tarefa);
            tarefas.Add(tarefa.Id);
        }
        if (tarefas.Count == 0)
            throw new DomainValidationException(nameof(Tarefas), "A OS precisa de pelo menos uma tarefa do cronograma (ou de uma tarefa nova).");
        CalculadoraCronograma.Calcular(c); // as datas da OS vêm do cronograma calculado
        var montada = MontagemDaOrdem.Rascunho(c, tarefas, PrecoDosItens.MaoDeObra);
        c.OrdensDeServico.Adicionar(OrdemDeServico.Nova(Id, c.OrdensDeServico.ProximoNumero, Pedido.Sobre(montada), agora));
    }
}

/// <summary>Bloco 63: altera uma OS em rascunho (os campos pedidos; com tarefas, os itens vêm delas de novo).</summary>
public sealed record AlterarOrdem(int Numero, PedidoDeOrdem Pedido, IReadOnlyList<Guid>? Tarefas) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var ordem = c.OrdensDeServico.Todas.FirstOrDefault(o => o.Numero == Numero)
                    ?? throw new DomainValidationException(nameof(Numero), $"Não há a OS-{Numero:000}.");
        var dados = ordem.Dados;
        if (Tarefas is { Count: > 0 } tarefas)
        {
            var montada = MontagemDaOrdem.Rascunho(c, tarefas, PrecoDosItens.MaoDeObra);
            dados = new DadosDaOrdem(dados.Titulo, dados.Tipo, dados.Executante, dados.RecursoId, dados.Contato, montada.Tarefas, montada.Itens,
                dados.InicioPrevisto, dados.Prazo, dados.FormaDePagamento, dados.Observacoes, dados.Verificacao, dados.Retencao);
        }
        c.OrdensDeServico.Substituir(ordem.Editar(Pedido.Sobre(dados)));
    }
}

/// <summary>Bloco 64: uma medição na OS que leva cada item até o % executado acumulado (o medido só aumenta).</summary>
public sealed record MedirAOrdem(int Numero, decimal Percentual) : OperacaoDaObra
{
    /// <summary>As quantidades desta medição: o que falta para cada item chegar ao %.</summary>
    public static IReadOnlyList<decimal> Quantidades(OrdemDeServico o, decimal percentual) =>
        [.. o.Dados.Itens.Select((item, i) => Math.Max(0, Math.Round(item.Quantidade * percentual / 100, 4) - o.MedidoDoItem(i)))];

    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var o = c.OrdensDeServico.Todas.FirstOrDefault(x => x.Numero == Numero)
                ?? throw new DomainValidationException(nameof(Numero), $"Não há a OS-{Numero:000}.");
        var nova = o.Medir(Guid.NewGuid(), DateOnly.FromDateTime(agora), Quantidades(o, Percentual), agora, MudarSituacaoDaOrdem.Autor);
        c.OrdensDeServico.Substituir(nova);
        // O medido leva o % das tarefas da OS (só para cima), como na tela.
        foreach (var (id, pct) in PagamentoDasOrdens.PercentualDasTarefas(nova))
            if (c.Tarefas.FirstOrDefault(t => t.Id == id) is { EhResumo: false } tarefa && pct > tarefa.PercentualConcluido)
                tarefa.AtualizarPercentualConcluido(pct);
    }
}

/// <summary>Bloco 63: leva a OS a outra situação (com o motivo, quando a ação pede).</summary>
public sealed record MudarSituacaoDaOrdem(int Numero, string Acao, string? Motivo) : OperacaoDaObra
{
    public const string Autor = "pelo assistente";

    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var o = c.OrdensDeServico.Todas.FirstOrDefault(x => x.Numero == Numero)
                ?? throw new DomainValidationException(nameof(Numero), $"Não há a OS-{Numero:000}.");
        c.OrdensDeServico.Substituir(Acao switch
        {
            "emitir" => o.Emitir(agora, Autor),
            "aceitar" => o.Aceitar(agora, Autor),
            "recusar" => o.Recusar(agora, Motivo, Autor),
            "iniciar" => o.Iniciar(agora, Autor),
            "concluir" => o.Concluir(agora, Autor),
            "conferir" => o.MedirOQueFalta(Guid.NewGuid(), DateOnly.FromDateTime(agora), agora, Autor).Conferir(agora, Autor),
            "reprovar" => o.Reprovar(agora, Motivo, Autor),
            "cancelar" => o.Cancelar(agora, Motivo, Autor),
            "rascunho" => o.VoltarARascunho(agora, Autor),
            "pagar" => Pagar(c, o, agora),
            "liberar" => Liberar(c, o, agora),
            "duplicar" => Duplicar(c, o, agora),
            _ => throw new DomainValidationException(nameof(Acao), $"Ação desconhecida: {Acao}."),
        });
    }

    // Bloco 67: paga as medições abertas (cada uma lança os custos dela, como na tela).
    private OrdemDeServico Pagar(Cronograma c, OrdemDeServico o, DateTime agora)
    {
        var abertas = o.Medicoes.Where(m => !m.Paga).ToList();
        if (abertas.Count == 0)
            throw new DomainValidationException(nameof(Acao), $"A {o.Codigo} não tem medição a pagar.");
        var data = DateOnly.FromDateTime(agora);
        foreach (var m in abertas)
        {
            var lancamentos = PagamentoDasOrdens.Lancamentos(o, m, data, Motivo);
            o = o.RegistrarPagamento(m.Id, new PagamentoDaMedicao(data, string.IsNullOrWhiteSpace(Motivo) ? null : Motivo.Trim(), [.. lancamentos.Select(l => l.Id)]),
                agora, Autor);
            foreach (var l in lancamentos)
                c.Custos.Adicionar(l);
        }
        return o;
    }

    private OrdemDeServico Liberar(Cronograma c, OrdemDeServico o, DateTime agora)
    {
        var data = DateOnly.FromDateTime(agora);
        var lancamentos = PagamentoDasOrdens.LancamentosDaRetencao(o, data, Motivo);
        var nova = o.LiberarRetencao(data, Motivo, [.. lancamentos.Select(l => l.Id)], agora, Autor);
        foreach (var l in lancamentos)
            c.Custos.Adicionar(l);
        return nova;
    }

    // A cópia entra como OS nova; a original fica como estava.
    private static OrdemDeServico Duplicar(Cronograma c, OrdemDeServico o, DateTime agora)
    {
        c.OrdensDeServico.Adicionar(o.Duplicar(Guid.NewGuid(), c.OrdensDeServico.ProximoNumero, agora));
        return o;
    }
}

// ---- Bloco 67: a IA no programa todo ----

/// <summary>Bloco 67: o valor, a unidade, a quantidade e o prazo de uma tarefa (só o que veio).</summary>
public sealed record DadosDaTarefa(Guid TarefaId, decimal? Valor, string? Unidade, decimal? Quantidade, DateOnly? Prazo) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var t = Tarefa(c, TarefaId);
        if (Unidade is not null)
            t.DefinirUnidade(Unidade);
        if (Quantidade is not null)
            t.DefinirQuantidade(Quantidade);
        if (Valor is not null)
            t.DefinirValor(Valor);
        if (Prazo is { } prazo)
            t.DefinirPrazo(prazo.ToDateTime(TimeOnly.MinValue));
    }
}

/// <summary>Bloco 67: os dados da obra (o que veio por cima do que havia).</summary>
public sealed record DadosDaObraOp(string? Contratante, string? Contrato, string? Endereco) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora) =>
        c.DefinirObra(new Core.Relatorios.DadosDaObra(Contratante ?? c.Obra.Contratante, Contrato ?? c.Obra.Contrato, Endereco ?? c.Obra.Endereco));
}

/// <summary>Bloco 67: cria o recurso, ou muda o de mesmo nome (os campos que vieram).</summary>
public sealed record RecursoDaIa(string Nome, Core.Recursos.TipoDeRecurso? Tipo, string? Unidade, decimal? Custo, decimal? Disponivel, string? Fornecedor)
    : OperacaoDaObra
{
    public static Core.Recursos.Recurso? PeloNome(Cronograma c, string nome) =>
        c.Recursos.Lista.FirstOrDefault(r => string.Equals(r.Nome, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public override void Aplicar(Cronograma c, DateTime agora)
    {
        if (PeloNome(c, Nome) is { } existente)
            c.Recursos.Substituir(new Core.Recursos.Recurso(existente.Id, existente.Nome, Tipo ?? existente.Tipo, Unidade ?? existente.Unidade,
                Custo ?? existente.CustoUnitario, Disponivel ?? existente.Disponivel, Fornecedor ?? existente.Fornecedor));
        else
            c.Recursos.Adicionar(new Core.Recursos.Recurso(Guid.NewGuid(), Nome, Tipo ?? Core.Recursos.TipoDeRecurso.Profissional, Unidade, Custo ?? 0,
                Disponivel, Fornecedor));
    }
}

/// <summary>Bloco 67: põe (ou tira, com zero) um recurso numa tarefa.</summary>
public sealed record AtribuirRecurso(Guid TarefaId, string Recurso, decimal Quantidade) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var tarefa = Tarefa(c, TarefaId);
        var recurso = RecursoDaIa.PeloNome(c, Recurso)
                      ?? throw new DomainValidationException(nameof(Recurso), $"O recurso \"{Recurso}\" não está no projeto: crie antes.");
        var lista = tarefa.Atribuicoes.Where(a => a.RecursoId != recurso.Id).ToList();
        if (Quantidade > 0)
            lista.Add(new Core.Recursos.AtribuicaoDeRecurso(recurso.Id, Quantidade));
        c.Recursos.Atribuir(tarefa, lista);
    }
}

/// <summary>Bloco 67: aprova ou marca como paga uma medição do contrato.</summary>
public sealed record MedicaoDoContratoOp(int Numero, string Acao, DateOnly? Data) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        if (Acao == "aprovar")
            c.Contrato.Aprovar(Numero, agora);
        else
            c.Contrato.MarcarPaga(Numero, Data ?? DateOnly.FromDateTime(agora));
    }
}

/// <summary>
/// Bloco 71: muda o calendário de trabalho do projeto: um feriado (num dia ou todo ano), um período sem trabalho (chuva,
/// paralisação) ou com trabalho (um sábado ou feriado trabalhado), ou os dias da semana. Aplicada sobre o calendário de agora,
/// para várias no mesmo cartão se somarem.
/// </summary>
public sealed record MudarCalendario(string Acao, DateOnly De, DateOnly Ate, string? Nome, IReadOnlyList<DayOfWeek>? Dias) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var calendario = c.Calendario;
        c.DefinirCalendario(Acao switch
        {
            "feriado" => calendario.ComFeriado(new FeriadoDoCalendario(De, Nome ?? "Feriado")),
            "feriadoAnual" => calendario.ComFeriado(new FeriadoDoCalendario(De, Nome ?? "Feriado", TodoAno: true)),
            "parar" => calendario.ComExcecao(new ExcecaoDoCalendario(De, Ate, Nome ?? "Sem trabalho", Trabalha: false)),
            "trabalhar" => calendario.ComExcecao(new ExcecaoDoCalendario(De, Ate, Nome ?? "Dia trabalhado", Trabalha: true)),
            "dias" => calendario.ComDias(Dias ?? throw new DomainValidationException(nameof(Dias), "Faltam os dias da semana.")),
            _ => throw new DomainValidationException(nameof(Acao), $"Ação desconhecida: {Acao}."),
        });
    }
}

/// <summary>
/// Bloco 80: uma mudança no quadro Kanban pedida pela IA (no programa ou pelo Telegram), pelas mesmas regras do quadro: mover o
/// card de coluna (o % segue a coluna), itens do checklist, etiquetas, arquivar, uma coluna nova e as raias.
/// </summary>
public sealed record MudarOQuadro(string Acao, Guid? TarefaId, string? Texto) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var quadro = c.Quadro;
        quadro.Sincronizar();
        switch (Acao)
        {
            case "coluna":
                RegrasDasColunas.Adicionar(c, Texto ?? "");
                return;
            case "raias":
                RegrasDasRaias.DefinirModo(c, ModoDasRaias(Texto));
                return;
        }
        var id = TarefaId ?? throw new DomainValidationException(nameof(TarefaId), "Falta a tarefa do card.");
        var card = quadro.CardDe(id) is { } k && !Tarefa(c, id).EhResumo ? k
            : throw new DomainValidationException(nameof(TarefaId), $"\"{Tarefa(c, id).Nome}\" é um resumo: os cards são das tarefas sem subtarefas.");
        switch (Acao)
        {
            case "mover":
                var coluna = ColunaPeloNome(c, Texto);
                if (card.Arquivado)
                    RegrasDosCards.Desarquivar(c, [id]);
                var plano = RegrasDoQuadro.Planejar(c, [id], coluna.Id, int.MaxValue);
                if (plano is { Valido: true, MudaAlgo: true })
                    RegrasDoQuadro.Aplicar(c, plano);
                return;
            case "item":
                var novos = Itens(Texto).Where(t => !card.Checklist.Any(i => Iguais(i.Texto, t))).Select(t => new ItemDoChecklist(t, false));
                RegrasDosCards.DefinirChecklist(c, id, [.. card.Checklist, .. novos]);
                return;
            case "marcarItem":
                var alvo = (Texto ?? "").Trim();
                var item = card.Checklist.FirstOrDefault(i => Iguais(i.Texto, alvo)) ?? card.Checklist.FirstOrDefault(i => Contem(i.Texto, alvo))
                           ?? throw new DomainValidationException(nameof(Texto), $"O checklist de \"{Tarefa(c, id).Nome}\" não tem o item \"{alvo}\".");
                RegrasDosCards.DefinirChecklist(c, id, [.. card.Checklist.Select(i => i == item ? i with { Feito = true } : i)]);
                return;
            case "etiqueta":
                var etiqueta = quadro.Etiquetas.FirstOrDefault(e => Iguais(e.Nome, Texto)) ?? RegrasDosCards.CriarEtiqueta(c, Texto ?? "");
                RegrasDosCards.MarcarEtiqueta(c, id, etiqueta.Id, true);
                return;
            case "tirarEtiqueta":
                var existente = quadro.Etiquetas.FirstOrDefault(e => Iguais(e.Nome, Texto))
                                ?? throw new DomainValidationException(nameof(Texto), $"Não há a etiqueta \"{Texto}\" no projeto.");
                RegrasDosCards.MarcarEtiqueta(c, id, existente.Id, false);
                return;
            case "arquivar":
                RegrasDosCards.Arquivar(c, [id]);
                return;
            case "desarquivar":
                RegrasDosCards.Desarquivar(c, [id]);
                return;
            default:
                throw new DomainValidationException(nameof(Acao), $"Ação desconhecida no quadro: {Acao}.");
        }
    }

    /// <summary>Os itens de um texto: separados por ";" (ou por linhas).</summary>
    public static IReadOnlyList<string> Itens(string? texto) =>
        [.. (texto ?? "").Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>A coluna do quadro com o nome (sem diferença de maiúsculas e acentos).</summary>
    public static ColunaDoQuadro ColunaPeloNome(Cronograma c, string? nome) =>
        c.Quadro.Colunas.FirstOrDefault(k => Iguais(k.Nome, nome)) ?? c.Quadro.Colunas.FirstOrDefault(k => Contem(k.Nome, nome))
        ?? throw new DomainValidationException(nameof(Texto),
            $"O quadro não tem a coluna \"{nome}\" (tem {string.Join(", ", c.Quadro.Colunas.Select(k => k.Nome))}).");

    public static ModoDasRaias ModoDasRaias(string? texto) => Normal(texto) switch
    {
        "resumo" or "por resumo" => Core.Kanban.ModoDasRaias.PorResumo,
        "responsavel" or "por responsavel" => Core.Kanban.ModoDasRaias.PorResponsavel,
        "nenhuma" or "sem raias" or "sem" => Core.Kanban.ModoDasRaias.Nenhuma,
        _ => throw new DomainValidationException(nameof(Texto), "Raias: resumo, responsavel ou nenhuma."),
    };

    private static bool Iguais(string? a, string? b) => string.Equals(Normal(a), Normal(b), StringComparison.Ordinal);

    private static bool Contem(string? a, string? b) => Normal(b) is { Length: > 0 } parte && Normal(a).Contains(parte, StringComparison.Ordinal);

    // Sem maiúsculas, acentos e espaços nas pontas.
    private static string Normal(string? texto) =>
        new string((texto ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
}

/// <summary>
/// Bloco 82: uma mudança no diário de obra (RDO) pedida pela IA, no programa ou pelo Telegram: o clima de um período, uma função
/// do efetivo, um equipamento, um serviço executado ou uma ocorrência. O dia que não existe é criado só com o clima do
/// calendário: as listas vêm do que o canteiro contou ("Preencher pelo cronograma" fica na tela). Um dia fechado não muda.
/// </summary>
/// <param name="Manha">clima: muda a manhã.</param>
/// <param name="Tarde">clima: muda a tarde.</param>
/// <param name="Quantidade">efetivo e equipamento: quantos no dia; 0 tira a linha.</param>
/// <param name="Percentual">servico: o % da tarefa ao fim do dia (não muda o cronograma).</param>
/// <param name="Executado">Bloco 84: servico: a quantidade executada no dia (a produção), na <paramref name="Unidade"/>.</param>
/// <param name="Unidade">Bloco 84: servico: a unidade da quantidade executada; vazia, a da tarefa.</param>
public sealed record MudarODiario(DateOnly Data, string Acao, bool Manha = false, bool Tarde = false, TempoNoPeriodo? Tempo = null,
    bool? Praticavel = null, string? Nome = null, int? Quantidade = null, string? Empresa = null, Guid? TarefaId = null,
    string? Descricao = null, int? Percentual = null, decimal? Executado = null, string? Unidade = null) : OperacaoDaObra
{
    public override void Aplicar(Cronograma c, DateTime agora)
    {
        var diario = c.Diario;
        var dia = diario.Dia(Data) ?? diario.Abrir(RegrasDoDiario.Sugerir(c, Data) with { Atividades = [], Efetivo = [], Equipamentos = [] });
        diario.Salvar(Acao switch
        {
            "clima" => dia with { Manha = Manha ? Clima(dia.Manha) : dia.Manha, Tarde = Tarde ? Clima(dia.Tarde) : dia.Tarde },
            "efetivo" => dia with { Efetivo = Efetivo(dia) },
            "equipamento" => dia with { Equipamentos = Equipamentos(dia) },
            "servico" => dia with { Atividades = Atividades(c, dia) },
            "ocorrencia" => dia with { Ocorrencias = Ocorrencia(dia) },
            _ => throw new DomainValidationException(nameof(Acao), $"Ação desconhecida no diário: {Acao}."),
        });
    }

    private ClimaDoPeriodo Clima(ClimaDoPeriodo atual) => new(Tempo ?? atual.Tempo, Praticavel ?? atual.Praticavel);

    private List<EfetivoNoDia> Efetivo(DiaDaObra dia)
    {
        var lista = dia.Efetivo.ToList();
        var i = lista.FindIndex(e => MesmoNome(e.Funcao, Nome) && (Empresa is null || MesmoNome(e.Empresa, Empresa)));
        if (Quantidade is 0)
        {
            if (i < 0)
                throw new DomainValidationException(nameof(Nome), $"O efetivo de {Data:dd/MM/yyyy} não tem \"{Nome}\".");
            lista.RemoveAt(i);
        }
        else if (i >= 0)
            lista[i] = lista[i] with { Quantidade = Quantidade ?? lista[i].Quantidade, Empresa = Empresa ?? lista[i].Empresa };
        else
            lista.Add(new EfetivoNoDia(Nome ?? "", Quantidade ?? 1, Empresa));
        return lista;
    }

    private List<EquipamentoNoDia> Equipamentos(DiaDaObra dia)
    {
        var lista = dia.Equipamentos.ToList();
        var i = lista.FindIndex(e => MesmoNome(e.Nome, Nome));
        if (Quantidade is 0)
        {
            if (i < 0)
                throw new DomainValidationException(nameof(Nome), $"Os equipamentos de {Data:dd/MM/yyyy} não têm \"{Nome}\".");
            lista.RemoveAt(i);
        }
        else if (i >= 0)
            lista[i] = lista[i] with { Quantidade = Quantidade ?? lista[i].Quantidade };
        else
            lista.Add(new EquipamentoNoDia(Nome ?? "", Quantidade ?? 1));
        return lista;
    }

    private List<AtividadeNoDia> Atividades(Cronograma c, DiaDaObra dia)
    {
        var lista = dia.Atividades.ToList();
        var i = TarefaId is { } id ? lista.FindIndex(a => a.TarefaId == id) : lista.FindIndex(a => MesmoNome(a.Descricao, Descricao));
        var unidade = Executado is null ? null : Unidade ?? (TarefaId is { } daTarefa ? Tarefa(c, daTarefa).Unidade : null);
        if (i >= 0)
            lista[i] = lista[i] with
            {
                Descricao = Descricao ?? lista[i].Descricao, Percentual = Percentual ?? lista[i].Percentual,
                Quantidade = Executado ?? lista[i].Quantidade, Unidade = Executado is null ? lista[i].Unidade : unidade,
            };
        else if (TarefaId is { } tarefa)
            lista.Add(new AtividadeNoDia(tarefa, Descricao ?? $"{c.NumeracaoEdt()[tarefa]} {Tarefa(c, tarefa).Nome}", Percentual, Executado, unidade));
        else
            lista.Add(new AtividadeNoDia(null, Descricao ?? "", Percentual, Executado, unidade));
        return lista;
    }

    private string Ocorrencia(DiaDaObra dia)
    {
        var texto = (Descricao ?? "").Trim();
        return dia.Ocorrencias is not { Length: > 0 } atuais ? texto
            : atuais.Contains(texto, StringComparison.CurrentCultureIgnoreCase) ? atuais
            : atuais + "\n" + texto;
    }

    /// <summary>O mesmo nome, sem diferença de maiúsculas, acentos e plural ("Serventes" é "Servente").</summary>
    public static bool MesmoNome(string? a, string? b)
    {
        var (x, y) = (Normal(a), Normal(b));
        return x.Length > 0 && (x == y || x + "s" == y || y + "s" == x || x + "es" == y || y + "es" == x);
    }

    private static string Normal(string? texto) =>
        new string((texto ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
}

/// <summary>Um item do cartão: o texto para o usuário e a operação, ou o motivo de não dar para aplicar.</summary>
public sealed record ItemDoCartao(string Texto, OperacaoDaObra? Operacao, string? Problema)
{
    public bool Valido => Operacao is not null && Problema is null;
}

/// <summary>
/// Bloco 41: confere as atualizações que o assistente propôs (as tarefas pelo Nº do retrato mandado com o pedido), escreve
/// cada uma para o cartão ("2.1 Alvenaria: 40% → 60% concluído") e descobre o problema de cada uma aplicando-a sozinha numa
/// cópia do cronograma. Aplicar mesmo é com o cronograma aberto (ver MainWindowViewModel.AplicarAtualizacoes).
/// </summary>
public static class AtualizacoesDoAssistente
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<ItemDoCartao> Conferir(Cronograma cronograma, IReadOnlyList<AtualizacaoDaObra> atualizacoes, ContextoDoProjeto retrato,
        DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(atualizacoes);
        ArgumentNullException.ThrowIfNull(retrato);
        var porNumero = retrato.Tarefas.ToDictionary(t => t.Numero.ToString(CultureInfo.InvariantCulture), t => t.Id);
        var edt = cronograma.NumeracaoEdt();
        var hoje = DateOnly.FromDateTime(agora);
        var itens = new List<ItemDoCartao>();
        foreach (var a in atualizacoes)
        {
            Tarefa? tarefa = null;
            if (a.Tarefa?.Trim().TrimStart('#') is { Length: > 0 } referencia && porNumero.TryGetValue(referencia, out var id))
                tarefa = cronograma.Tarefas.FirstOrDefault(t => t.Id == id);
            var nome = tarefa is null ? a.Tarefa is { } r ? $"Tarefa {r}" : "" : $"{edt[tarefa.Id]} {tarefa.Nome}";
            itens.Add(Item(cronograma, a, tarefa, nome, hoje, porNumero));
        }

        // O problema de cada uma, aplicando-as em ordem numa cópia (o domínio diz o porquê). Bloco 67: as que valem ficam na
        // cópia, para a seguinte poder depender delas (criar o recurso e depois atribuí-lo), como na aplicação de verdade.
        var acumulado = cronograma.Copiar();
        return [.. itens.Select(i =>
        {
            if (!i.Valido)
                return i;
            try
            {
                i.Operacao!.Aplicar(acumulado.Copiar(), agora);
                i.Operacao.Aplicar(acumulado, agora);
                return i;
            }
            catch (DomainValidationException ex)
            {
                return i with { Problema = ex.Message };
            }
        })];
    }

    private static ItemDoCartao Item(Cronograma c, AtualizacaoDaObra a, Tarefa? tarefa, string nome, DateOnly hoje, IReadOnlyDictionary<string, Guid> porNumero)
    {
        string N(decimal v) => v.ToString("#,##0.####", Brasil);
        ItemDoCartao SemTarefa(string texto) => new(texto, null, a.Tarefa is null ? "falta a tarefa" : $"não há a tarefa nº {a.Tarefa} no cronograma");
        switch (a.Tipo)
        {
            case TiposDeAtualizacao.Percentual:
            {
                if (tarefa is null)
                    return SemTarefa($"{nome}: % concluído");
                if (a.Percentual is not { } p || p < 0 || p > 100)
                    return new($"{nome}: % concluído", null, "o % precisa ir de 0 a 100");
                var novo = (int)Math.Round(p);
                return new($"{nome}: {tarefa.PercentualConcluido}% → {novo}% concluído", new PercentualDaTarefa(tarefa.Id, novo),
                    tarefa.EhResumo ? "o % de um resumo é calculado (atualize as tarefas dentro dele)" : null);
            }
            case TiposDeAtualizacao.Producao: // Bloco 84
                return tarefa is null ? SemTarefa($"{nome}: produção") : Producao(a, tarefa, nome);
            case TiposDeAtualizacao.Medicao:
            {
                if (tarefa is null)
                    return SemTarefa($"{nome}: medir");
                var rascunho = a.Medicao is { } n ? c.Contrato.Medicoes.FirstOrDefault(m => m.Numero == n) : c.Contrato.Medicoes.LastOrDefault(m => !m.Travada);
                var ultima = c.Contrato.Medicoes.LastOrDefault();
                var de = Data(a.De) ?? (ultima is null ? DateOnly.FromDateTime(c.DataInicio) : ultima.Ate.AddDays(1));
                var ate = Data(a.Ate) ?? (hoje < de ? de : hoje);
                var qual = rascunho is not null ? $"Medição {rascunho.Numero:00}"
                    : a.Medicao is { } numero ? $"Medição {numero:00}"
                    : $"Medição {(ultima?.Numero ?? 0) + 1:00} (nova, {de:dd/MM} a {ate:dd/MM/yyyy})";
                // Com quantidade, mede-se a quantidade; sem, o %. O % de um item com quantidade vira quantidade.
                decimal? valor;
                string texto;
                if (tarefa.Quantidade is { } total)
                {
                    valor = a.Quantidade ?? (a.Percentual is { } pct ? Math.Round(total * pct / 100, 4) : null);
                    texto = $"{qual}: {nome}, {(valor is { } v ? N(v) : "?")} {tarefa.Unidade}".TrimEnd();
                }
                else
                {
                    valor = a.Percentual;
                    texto = $"{qual}: {nome}, {(valor is { } v ? N(v) + "%" : "?")}";
                }
                if (a.Medicao is { } m && rascunho is null)
                    return new(texto, null, $"não há a medição nº {m:00}");
                if (valor is not > 0)
                    return new(texto, null, tarefa.Quantidade is null && a.Quantidade is not null
                        ? "a tarefa não tem quantidade: informe o % medido"
                        : "informe a quantidade (ou o %) medida no período");
                return new(texto, new MedirNaMedicao(rascunho?.Numero ?? a.Medicao, de, ate, tarefa.Id, valor.Value), null);
            }
            case TiposDeAtualizacao.Custo:
            {
                var data = Data(a.Data) ?? hoje;
                var categoria = Categorias.Ler(a.Categoria) ?? CategoriaDeCusto.Outros;
                var texto = $"Custo de {data:dd/MM/yyyy}: {a.Descricao ?? "(sem descrição)"} ({Categorias.Texto(categoria)}) · " +
                            $"{(a.Valor is { } v ? Moeda.ComSimbolo(v) : "?")}" + (tarefa is null ? " · custo geral" : $" · {nome}");
                if (a.Tarefa is not null && tarefa is null)
                    return SemTarefa(texto);
                try
                {
                    return new(texto, new LancarCusto(new LancamentoDeCusto(Guid.NewGuid(), data, tarefa?.Id, categoria, a.Descricao, a.Documento, a.Valor ?? 0)), null);
                }
                catch (DomainValidationException ex)
                {
                    return new(texto, null, ex.Message);
                }
            }
            case TiposDeAtualizacao.Inicio:
            {
                if (tarefa is null)
                    return SemTarefa($"{nome}: não iniciar antes de");
                if (Data(a.Data) is not { } data)
                    return new($"{nome}: não iniciar antes de ?", null, "informe a data");
                var atual = tarefa.InicioMaisCedo is { } i ? $" (hoje começa em {i:dd/MM/yyyy})" : "";
                return new($"{nome}: não iniciar antes de {data:dd/MM/yyyy}{atual}", new RestricaoDeInicio(tarefa.Id, data), null);
            }
            case TiposDeAtualizacao.Os:
                return Ordem(c, a, porNumero);
            case TiposDeAtualizacao.SituacaoOs:
            {
                var acao = a.Acao?.Trim().ToLowerInvariant();
                var texto = $"OS-{a.Os ?? 0:000}: {acao switch
                {
                    "emitir" => "emitir",
                    "aceitar" => "aceita pelo executante",
                    "recusar" => "recusada",
                    "iniciar" => "iniciar a execução",
                    "concluir" => "concluída",
                    "conferir" => "conferir e aprovar",
                    "reprovar" => "reprovada na conferência",
                    "cancelar" => "cancelar",
                    "rascunho" => "voltar a rascunho",
                    "medir" => $"medir até {(a.Percentual is { } pm ? N(pm) : "?")}% executado",
                    "pagar" => "pagar as medições abertas" + (a.Documento is { } doc ? $" ({doc})" : ""),
                    "liberar" => "liberar a retenção" + (a.Documento is { } docl ? $" ({docl})" : ""),
                    "duplicar" => "duplicar (uma OS nova em rascunho)",
                    _ => acao ?? "?",
                }}" + (string.IsNullOrWhiteSpace(a.Descricao) ? "" : $" ({a.Descricao.Trim()})");
                if (a.Os is not { } numero || c.OrdensDeServico.Todas.All(o => o.Numero != numero))
                    return new(texto, null, a.Os is null ? "falta o número da OS" : $"não há a OS-{a.Os:000}");
                if (acao is null || !TiposDeAtualizacao.AcoesDaOs.Contains(acao))
                    return new(texto, null, "ação desconhecida");
                if (acao == "medir")
                {
                    if (a.Percentual is not { } pct || pct <= 0 || pct > 100)
                        return new(texto, null, "informe o % executado (de 1 a 100)");
                    var os = c.OrdensDeServico.Todas.First(o => o.Numero == numero);
                    var valor = os.Dados.Itens.Select((item, i) => Math.Round(MedirAOrdem.Quantidades(os, pct)[i] * item.PrecoUnitario, 2, MidpointRounding.AwayFromZero)).Sum();
                    return new($"{texto} · {Moeda.ComSimbolo(valor)} a medir", new MedirAOrdem(numero, pct),
                        valor == 0 ? $"a {os.Codigo} já está medida até esse %" : null);
                }
                return new(texto, new MudarSituacaoDaOrdem(numero, acao, acao is "pagar" or "liberar" ? a.Documento : a.Descricao), null);
            }
            case TiposDeAtualizacao.Tarefa: // Bloco 67
            {
                var partes = new[]
                {
                    a.Valor is { } v ? $"valor {Moeda.ComSimbolo(v)}" : null,
                    a.Quantidade is { } q ? $"{N(q)} {a.Unidade ?? tarefa?.Unidade}".TrimEnd() : a.Unidade is { } u ? $"unidade {u}" : null,
                    Data(a.Prazo ?? a.Data) is { } pz ? $"prazo {pz:dd/MM/yyyy}" : null,
                }.OfType<string>().ToList();
                var texto = $"{nome}: {(partes.Count == 0 ? "?" : string.Join(" · ", partes))}";
                if (tarefa is null)
                    return SemTarefa(texto);
                if (partes.Count == 0)
                    return new(texto, null, "informe o valor, a quantidade, a unidade ou o prazo");
                return new(texto, new DadosDaTarefa(tarefa.Id, a.Valor, a.Unidade, a.Quantidade, Data(a.Prazo ?? a.Data)),
                    tarefa.EhResumo ? "um resumo não tem valor nem quantidade (use as tarefas dentro dele)" : null);
            }
            case TiposDeAtualizacao.Obra:
            {
                var partes = new[] { a.Contratante is { } ct ? $"contratante {ct}" : null, a.Contrato is { } ctr ? $"contrato {ctr}" : null,
                    a.Endereco is { } end ? $"endereço {end}" : null }.OfType<string>().ToList();
                var texto = $"Dados da obra: {(partes.Count == 0 ? "?" : string.Join(" · ", partes))}";
                return partes.Count == 0 ? new(texto, null, "informe o contratante, o contrato ou o endereço")
                    : new(texto, new DadosDaObraOp(a.Contratante, a.Contrato, a.Endereco), null);
            }
            case TiposDeAtualizacao.Recurso:
            {
                var tipo = Core.Recursos.TiposDeRecurso.Ler(a.TipoDoRecurso);
                var existe = a.Nome is { } n && RecursoDaIa.PeloNome(c, n) is not null;
                var detalhes = new[]
                {
                    tipo is { } t ? Core.Recursos.TiposDeRecurso.Texto(t).ToLowerInvariant() : null,
                    a.Valor is { } custo ? $"{Moeda.ComSimbolo(custo)} por {a.Unidade ?? (tipo is null or Core.Recursos.TipoDeRecurso.Material ? "un" : "dia")}" : null,
                    a.Quantidade is { } disp ? $"{N(disp)} por dia" : null, a.Fornecedor,
                }.OfType<string>();
                var texto = $"{(existe ? "Alterar o recurso" : "Novo recurso")} \"{a.Nome ?? "?"}\" · {string.Join(" · ", detalhes)}".TrimEnd(' ', '·');
                if (string.IsNullOrWhiteSpace(a.Nome))
                    return new(texto, null, "falta o nome do recurso");
                if (a.TipoDoRecurso is not null && tipo is null)
                    return new(texto, null, "tipo de recurso desconhecido (profissional, material ou equipamento)");
                return new(texto, new RecursoDaIa(a.Nome, tipo, a.Unidade, a.Valor, a.Quantidade, a.Fornecedor), null);
            }
            case TiposDeAtualizacao.Atribuir:
            {
                var texto = $"{nome}: {(a.Quantidade is 0 ? "tirar" : $"{N(a.Quantidade ?? 0)} ×")} {a.Recurso ?? "?"}";
                if (tarefa is null)
                    return SemTarefa(texto);
                if (string.IsNullOrWhiteSpace(a.Recurso) || a.Quantidade is not { } quantidade || quantidade < 0)
                    return new(texto, null, "informe o recurso e a quantidade");
                return new(texto, new AtribuirRecurso(tarefa.Id, a.Recurso, quantidade), null);
            }
            case TiposDeAtualizacao.MedicaoDoContrato:
            {
                var acao = a.Acao?.Trim().ToLowerInvariant();
                var texto = $"Medição {a.Medicao ?? 0:00} do contrato: {(acao == "pagar" ? $"paga em {Data(a.Data) ?? hoje:dd/MM/yyyy}" : "aprovar")}";
                if (a.Medicao is not { } numero || c.Contrato.Medicoes.All(m => m.Numero != numero))
                    return new(texto, null, a.Medicao is null ? "falta o número da medição" : $"não há a medição nº {a.Medicao:00}");
                if (acao is null || !TiposDeAtualizacao.AcoesDaMedicao.Contains(acao))
                    return new(texto, null, "ação desconhecida (aprovar ou pagar)");
                return new(texto, new MedicaoDoContratoOp(numero, acao, Data(a.Data)), null);
            }
            case TiposDeAtualizacao.Calendario: // Bloco 71
                return Calendario(a, hoje);
            case TiposDeAtualizacao.Quadro: // Bloco 80
                return Quadro(c, a, tarefa, nome);
            case TiposDeAtualizacao.Diario: // Bloco 82
                return Diario(c, a, tarefa, nome, hoje);
            case TiposDeAtualizacao.LinhaDeBase:
                return new(a.Nome is { } n2 ? $"Salvar a linha de base \"{n2}\"" : "Salvar uma linha de base (o cronograma de agora)", new SalvarLinhaDeBase(a.Nome), null);
            default:
                return new(a.Tipo, null, "tipo de atualização desconhecido");
        }
    }

    // Bloco 84: "1.2 Reboco: +20 m² executados (10% de 200 m²): 30% → 40% concluído". A prévia é pelo executado de agora; ao
    // aplicar, a conta é refeita sobre o executado de então.
    private static ItemDoCartao Producao(AtualizacaoDaObra a, Tarefa tarefa, string nome)
    {
        var acao = Normal(a.Acao);
        var total = acao == "total";
        var unidade = string.IsNullOrWhiteSpace(a.Unidade) ? tarefa.Unidade : a.Unidade.Trim();
        string Quanto(decimal q, string? u) => $"{q.ToString("#,##0.####", Brasil)} {u}".TrimEnd();
        if (a.Quantidade is not { } quantidade)
            return new($"{nome}: produção ?", null, "informe a quantidade executada");
        var texto = total ? $"{nome}: {Quanto(quantidade, unidade)} executados no total"
            : $"{nome}: {(quantidade > 0 ? "+" : "")}{Quanto(quantidade, unidade)} executados";
        if (acao is not ("" or "somar" or "total"))
            return new(texto, null, "a ação é somar (o executado no período) ou total (o acumulado até agora)");
        if (quantidade == 0 || (total && quantidade < 0))
            return new(texto, null, "informe a quantidade executada");
        if (tarefa.EhResumo)
            return new(texto, null, "o andamento de um resumo vem das tarefas dentro dele (informe uma delas)");
        if (Core.Planejamento.Unidades.Mesma(a.Unidade, tarefa.Unidade) is false)
            return new(texto, null, $"a tarefa é medida em {tarefa.Unidade}: confira a unidade ({a.Unidade!.Trim()})");
        if (tarefa.Quantidade is { } totalDaTarefa && tarefa.ExecutadoAtual is { } antes)
        {
            var depois = total ? quantidade : antes + quantidade;
            texto += $" ({(quantidade / totalDaTarefa * 100).ToString("0.#", Brasil)}% de {Quanto(totalDaTarefa, tarefa.Unidade)}): " +
                     $"{tarefa.PercentualConcluido}% → {Tarefa.PercentualPeloExecutado(depois, totalDaTarefa)}% concluído" +
                     (depois > totalDaTarefa ? $"; passa do total em {Quanto(depois - totalDaTarefa, tarefa.Unidade)}" : "");
        }
        return new(texto, new ProducaoDaTarefa(tarefa.Id, quantidade, total), null);
    }

    // Bloco 80: o quadro Kanban. O card pelo Nº da tarefa; a coluna pelo nome; os itens em descricao; a etiqueta em nome.
    private static ItemDoCartao Quadro(Cronograma c, AtualizacaoDaObra a, Tarefa? tarefa, string nome)
    {
        var acao = TiposDeAtualizacao.AcoesDoQuadro.FirstOrDefault(x => string.Equals(x, a.Acao?.Trim(), StringComparison.OrdinalIgnoreCase));
        var etiqueta = (a.Nome ?? a.Descricao)?.Trim();
        var texto = acao switch
        {
            "mover" => $"{nome}: card para \"{a.Coluna ?? "?"}\"" + (a.Coluna is { } k && QuadroSemErro(() => MudarOQuadro.ColunaPeloNome(c, k)) is { } coluna
                ? coluna.Tipo switch { TipoDeColuna.Concluida => " (100%)", TipoDeColuna.AFazer => " (o % volta a 0)", _ => "" } : ""),
            "item" => $"{nome}: checklist + {string.Join("; ", MudarOQuadro.Itens(a.Descricao))}",
            "marcarItem" => $"{nome}: marcar \"{a.Descricao?.Trim()}\" no checklist",
            "etiqueta" => $"{nome}: etiqueta \"{etiqueta}\"",
            "tirarEtiqueta" => $"{nome}: tirar a etiqueta \"{etiqueta}\"",
            "arquivar" => $"{nome}: arquivar o card (a tarefa continua no cronograma)",
            "desarquivar" => $"{nome}: card de volta ao quadro",
            "coluna" => $"Quadro: coluna nova \"{a.Nome?.Trim()}\" (antes de Concluída)",
            "raias" => $"Quadro: raias {a.Nome?.Trim() ?? "?"}",
            _ => $"Quadro: {a.Acao ?? "?"}",
        };
        if (acao is null)
            return new(texto, null, "ação desconhecida (" + string.Join(", ", TiposDeAtualizacao.AcoesDoQuadro) + ")");
        if (acao is "coluna" or "raias")
            return string.IsNullOrWhiteSpace(a.Nome) ? new(texto, null, "informe o nome") : new(texto, new MudarOQuadro(acao, null, a.Nome.Trim()), null);
        if (tarefa is null)
            return new(texto, null, a.Tarefa is null ? "falta a tarefa" : $"não há a tarefa nº {a.Tarefa} no cronograma");
        var valor = acao switch
        {
            "mover" => a.Coluna,
            "item" or "marcarItem" => a.Descricao,
            "etiqueta" or "tirarEtiqueta" => etiqueta,
            _ => null,
        };
        if (acao is not ("arquivar" or "desarquivar") && string.IsNullOrWhiteSpace(valor))
            return new(texto, null, acao switch { "mover" => "informe a coluna", "etiqueta" or "tirarEtiqueta" => "informe a etiqueta", _ => "informe o item" });
        return new(texto, new MudarOQuadro(acao, tarefa.Id, valor?.Trim()), null);
    }

    private static T? QuadroSemErro<T>(Func<T> f) where T : class
    {
        try
        {
            return f();
        }
        catch (DomainValidationException)
        {
            return null;
        }
    }

    // Bloco 82: o diário de obra (RDO): o dia em data (vazia é hoje), um dado por atualização. O dia que não existe é criado.
    private static ItemDoCartao Diario(Cronograma c, AtualizacaoDaObra a, Tarefa? tarefa, string nome, DateOnly hoje)
    {
        var acao = TiposDeAtualizacao.AcoesDoDiario.FirstOrDefault(x => string.Equals(x, a.Acao?.Trim(), StringComparison.OrdinalIgnoreCase));
        var data = Data(a.Data) ?? hoje;
        var dia = $"RDO de {data:dd/MM/yyyy}" + (c.Diario.Dia(data) is null ? " (dia novo)" : "");
        var oQue = (a.Nome ?? "").Trim();
        int? quantidade = a.Quantidade is { } q && q == Math.Round(q) && q is >= 0 and <= DiarioDaObra.QuantidadeMaxima ? (int)q : null;
        var empresa = string.IsNullOrWhiteSpace(a.Fornecedor) ? null : a.Fornecedor.Trim();
        var descricao = string.IsNullOrWhiteSpace(a.Descricao) ? null : a.Descricao.Trim();
        ItemDoCartao Item(string texto, OperacaoDaObra? operacao, string? problema = null) =>
            new($"{dia}: {texto}", problema is null ? operacao : null, problema);
        if (!string.IsNullOrWhiteSpace(a.Data) && Data(a.Data) is null)
            return Item(acao ?? a.Acao ?? "?", null, "não entendi a data (AAAA-MM-DD)");
        switch (acao)
        {
            case "clima":
            {
                var periodo = Normal(a.Periodo);
                var (manha, tarde) = periodo switch { "manha" => (true, false), "tarde" => (false, true), _ => (true, true) };
                TempoNoPeriodo? tempo = Normal(a.Nome) switch
                {
                    "" => null,
                    "bom" or "sol" or "ensolarado" or "limpo" => TempoNoPeriodo.Bom,
                    "nublado" => TempoNoPeriodo.Nublado,
                    "chuva" or "chuvoso" => TempoNoPeriodo.Chuva,
                    _ => (TempoNoPeriodo)(-1),
                };
                bool? praticavel = Normal(a.Praticavel) switch { "sim" => true, "nao" => false, _ => null };
                var partes = new[] { tempo is { } t && Enum.IsDefined(t) ? ClimaDoPeriodo.NomeDoTempo(t).ToLowerInvariant() : null,
                    praticavel is { } p ? p ? "praticável" : "impraticável" : null }.OfType<string>();
                var texto = $"{(manha && tarde ? "manhã e tarde" : manha ? "manhã" : "tarde")}: {string.Join(", ", partes)}";
                if (periodo is not ("" or "manha" or "tarde" or "dia"))
                    return Item(texto, null, "o período é manha, tarde ou dia");
                if (tempo is { } desconhecido && !Enum.IsDefined(desconhecido))
                    return Item($"clima \"{oQue}\"", null, "o tempo é bom, nublado ou chuva");
                if (tempo is null && praticavel is null)
                    return Item("clima", null, "informe o tempo (bom, nublado ou chuva) ou se foi praticável");
                return Item(texto, new MudarODiario(data, acao, manha, tarde, tempo, praticavel));
            }
            case "efetivo" or "equipamento":
            {
                var lista = acao == "efetivo" ? "efetivo" : "equipamento";
                var texto = quantidade is 0 ? $"tirar {oQue} {(acao == "efetivo" ? "do efetivo" : "dos equipamentos")}"
                    : $"{lista} {oQue} = {quantidade?.ToString(Brasil) ?? "?"}{(acao == "efetivo" && empresa is not null ? $" ({empresa})" : "")}";
                if (oQue.Length == 0)
                    return Item(lista, null, acao == "efetivo" ? "informe a função (nome)" : "informe o equipamento (nome)");
                if (a.Quantidade is null)
                    return Item(texto, null, "informe a quantidade");
                if (quantidade is null)
                    return Item(texto, null, $"a quantidade vai de 0 a {DiarioDaObra.QuantidadeMaxima} (0 tira)");
                return Item(texto, new MudarODiario(data, acao, Nome: oQue, Quantidade: quantidade, Empresa: acao == "efetivo" ? empresa : null));
            }
            case "servico":
            {
                int? percentual = a.Percentual is { } pc ? (int)Math.Round(pc) : null;
                // Bloco 84: a quantidade executada no dia (a produção), na unidade dada ou na da tarefa.
                var unidadeDoServico = string.IsNullOrWhiteSpace(a.Unidade) ? tarefa?.Unidade : a.Unidade.Trim();
                var texto = $"serviço {(tarefa is null ? descricao ?? nome : descricao is null ? nome : $"{nome}: {descricao}")}" +
                            (a.Quantidade is { } executado ? $", {$"{executado.ToString("#,##0.####", Brasil)} {unidadeDoServico}".TrimEnd()} executados" : "") +
                            (percentual is { } x ? $" ({x}% no fim do dia)" : "");
                if (a.Quantidade is < 0)
                    return Item(texto, null, "a quantidade executada não pode ser negativa");
                if (tarefa is not null && Core.Planejamento.Unidades.Mesma(a.Unidade, tarefa.Unidade) is false)
                    return Item(texto, null, $"a tarefa é medida em {tarefa.Unidade}: confira a unidade");
                if (a.Tarefa is not null && tarefa is null)
                    return Item(texto, null, $"não há a tarefa nº {a.Tarefa} no cronograma");
                if (tarefa is null && descricao is null)
                    return Item("serviço", null, "informe a tarefa ou a descrição do serviço");
                if (tarefa is { EhResumo: true })
                    return Item(texto, null, $"\"{tarefa.Nome}\" é um resumo: informe uma tarefa sem subtarefas");
                if (a.Percentual is { } fora && (fora < 0 || fora > 100))
                    return Item(texto, null, "o % precisa ir de 0 a 100");
                return Item(texto, new MudarODiario(data, acao, TarefaId: tarefa?.Id, Descricao: descricao, Percentual: percentual,
                    Executado: a.Quantidade, Unidade: a.Quantidade is null ? null : unidadeDoServico));
            }
            case "ocorrencia":
                return descricao is null
                    ? Item("ocorrência", null, "informe a ocorrência (descricao)")
                    : Item($"ocorrência \"{descricao}\"", new MudarODiario(data, acao, Descricao: descricao));
            default:
                return Item(a.Acao ?? "?", null, "ação desconhecida (" + string.Join(", ", TiposDeAtualizacao.AcoesDoDiario) + ")");
        }
    }

    // Sem maiúsculas, acentos e espaços nas pontas.
    private static string Normal(string? texto) =>
        new string((texto ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());

    // Bloco 71: o calendário de trabalho (o dia: data ou de; o fim: ate; o nome: nome ou descricao).
    private static ItemDoCartao Calendario(AtualizacaoDaObra a, DateOnly hoje)
    {
        var acao = a.Acao?.Trim();
        acao = TiposDeAtualizacao.AcoesDoCalendario.FirstOrDefault(x => string.Equals(x, acao, StringComparison.OrdinalIgnoreCase)) ?? acao;
        var de = Data(a.De) ?? Data(a.Data);
        var ate = Data(a.Ate) ?? de;
        var nome = string.IsNullOrWhiteSpace(a.Nome) ? string.IsNullOrWhiteSpace(a.Descricao) ? null : a.Descricao.Trim() : a.Nome.Trim();
        string Periodo(DateOnly d, DateOnly f) => d == f ? $"em {d:dd/MM/yyyy} ({CalendarioDoProjeto.NomeDoDia(d.DayOfWeek)})" : $"de {d:dd/MM/yyyy} a {f:dd/MM/yyyy}";
        var motivo = nome is null ? "" : $" ({nome})";
        if (acao == "dias")
        {
            var dias = CalendarioDoProjeto.LerDias(a.Dias ?? a.Descricao);
            var textoDosDias = dias is null ? a.Dias ?? "?" : CalendarioDoProjeto.Padrao.ComDias(dias).TextoDosDias;
            return dias is null
                ? new($"Calendário: trabalhar {textoDosDias}", null, "não entendi os dias da semana (por exemplo, \"segunda a sábado\")")
                : new($"Calendário: trabalhar {textoDosDias}", new MudarCalendario(acao, hoje, hoje, null, dias), null);
        }
        var texto = acao switch
        {
            "feriado" => $"Calendário: feriado {(de is { } d1 ? Periodo(d1, d1) : "em ?")}{motivo}",
            "feriadoAnual" => $"Calendário: feriado todo ano em {(de is { } d2 ? d2.ToString("dd/MM") : "?")}{motivo}",
            "parar" => $"Calendário: sem trabalho {(de is { } d3 ? Periodo(d3, ate!.Value) : "?")}{motivo}",
            "trabalhar" => $"Calendário: com trabalho {(de is { } d4 ? Periodo(d4, ate!.Value) : "?")}{motivo}",
            _ => $"Calendário: {acao ?? "?"}",
        };
        if (acao is null || !TiposDeAtualizacao.AcoesDoCalendario.Contains(acao))
            return new(texto, null, "ação desconhecida (feriado, feriadoAnual, parar, trabalhar ou dias)");
        if (de is not { } inicio)
            return new(texto, null, "informe a data");
        return new(texto, new MudarCalendario(acao, inicio, ate!.Value, nome, null), null);
    }

    // Bloco 63: criar ou alterar uma OS.
    private static ItemDoCartao Ordem(Cronograma c, AtualizacaoDaObra a, IReadOnlyDictionary<string, Guid> porNumero)
    {
        var edt = c.NumeracaoEdt();
        var tipo = a.TipoDeExecutante?.Trim().ToLowerInvariant() switch
        {
            "equipe" or "equipe propria" or "equipe própria" or "propria" or "própria" => TipoDeExecutante.EquipePropria,
            "terceiro" or "empreiteiro" => TipoDeExecutante.Terceiro,
            _ => (TipoDeExecutante?)null,
        };
        var forma = a.Pagamento?.Trim().ToLowerInvariant() switch
        {
            "empreitada" => FormaDePagamentoDaOrdem.Empreitada,
            "medicao" or "medição" or "por medição" or "por medicao" => FormaDePagamentoDaOrdem.PorMedicao,
            "diaria" or "diária" => FormaDePagamentoDaOrdem.Diaria,
            _ => (FormaDePagamentoDaOrdem?)null,
        };
        var pedido = new PedidoDeOrdem(a.Titulo, a.Executante, tipo, Data(a.De), Data(a.Prazo), forma, a.Retencao, a.Valor, a.Descricao);
        // As tarefas pelo Nº do retrato.
        var referencias = (a.Tarefas ?? a.Tarefa ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim().TrimStart('#')).ToList();
        var faltando = referencias.Where(r => !porNumero.ContainsKey(r)).ToList();
        var tarefas = referencias.Where(porNumero.ContainsKey).Select(r => porNumero[r]).Where(id => c.Tarefas.Any(t => t.Id == id)).ToList();
        string NomesDas(IEnumerable<Guid> ids) => string.Join(", ", ids.Select(id => c.Tarefas.First(t => t.Id == id)).Select(t => $"{edt[t.Id]} {t.Nome}"));

        if (a.Os is { } numero)
        {
            var existente = c.OrdensDeServico.Todas.FirstOrDefault(o => o.Numero == numero);
            var texto = $"OS-{numero:000}: alterar" + (a.Titulo is { } t ? $" \"{t}\"" : "") + pedido.Texto() + (tarefas.Count > 0 ? $" · tarefas {NomesDas(tarefas)}" : "");
            if (existente is null)
                return new(texto, null, $"não há a OS-{numero:000}");
            if (faltando.Count > 0)
                return new(texto, null, $"não há a tarefa nº {faltando[0]} no cronograma");
            return new(texto, new AlterarOrdem(numero, pedido, tarefas.Count > 0 ? tarefas : null),
                existente.PodeEditar ? null : $"a {existente.Codigo} já foi emitida: volte a rascunho antes");
        }

        NovaTarefaDaOrdem? nova = null;
        var textoDaNova = "";
        if (!string.IsNullOrWhiteSpace(a.NovaTarefa))
        {
            Guid? apos = a.Apos?.Trim().TrimStart('#') is { Length: > 0 } r && porNumero.TryGetValue(r, out var idApos) ? idApos : null;
            if (a.Apos is not null && apos is null)
                return new($"Nova OS: {a.Titulo ?? a.NovaTarefa}", null, $"não há a tarefa nº {a.Apos} no cronograma");
            var duracao = a.Duracao is > 0 ? a.Duracao.Value : 1;
            nova = new NovaTarefaDaOrdem(Guid.NewGuid(), a.NovaTarefa.Trim(), duracao, apos);
            textoDaNova = $" · cria a tarefa \"{nova.Nome}\" ({(duracao == 1 ? "1 dia" : $"{duracao} dias")}" +
                          (apos is { } p ? $", depois de {NomesDas([p])})" : ", no fim do cronograma)");
        }
        var textoNova = $"Nova OS: {a.Titulo ?? a.NovaTarefa ?? "(sem título)"}{pedido.Texto()}" +
                        (tarefas.Count > 0 ? $" · tarefas {NomesDas(tarefas)}" : "") + textoDaNova;
        if (faltando.Count > 0)
            return new(textoNova, null, $"não há a tarefa nº {faltando[0]} no cronograma");
        if (tarefas.Count == 0 && nova is null)
            return new(textoNova, null, "a OS precisa de tarefas do cronograma (ou de uma tarefa nova)");
        return new(textoNova, new CriarOrdem(Guid.NewGuid(), pedido, tarefas, nova), null);
    }

    private static DateOnly? Data(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var t = texto.Trim();
        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
            : DateOnly.TryParseExact(t, ["dd/MM/yyyy", "d/M/yyyy"], Brasil, DateTimeStyles.None, out d) ? d
            : null;
    }
}
