using System.Globalization;
using ProjectPlanner.Core;
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
            _ => throw new DomainValidationException(nameof(Acao), $"Ação desconhecida: {Acao}."),
        });
    }
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

        // O problema de cada uma, aplicando-a sozinha numa cópia (o domínio diz o porquê).
        return [.. itens.Select(i =>
        {
            if (!i.Valido)
                return i;
            try
            {
                i.Operacao!.Aplicar(cronograma.Copiar(), agora);
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
                    _ => acao ?? "?",
                }}" + (string.IsNullOrWhiteSpace(a.Descricao) ? "" : $" ({a.Descricao.Trim()})");
                if (a.Os is not { } numero || c.OrdensDeServico.Todas.All(o => o.Numero != numero))
                    return new(texto, null, a.Os is null ? "falta o número da OS" : $"não há a OS-{a.Os:000}");
                if (acao is null || !TiposDeAtualizacao.AcoesDaOs.Contains(acao))
                    return new(texto, null, "ação desconhecida");
                return new(texto, new MudarSituacaoDaOrdem(numero, acao, a.Descricao), null);
            }
            case TiposDeAtualizacao.LinhaDeBase:
                return new(a.Nome is { } n2 ? $"Salvar a linha de base \"{n2}\"" : "Salvar uma linha de base (o cronograma de agora)", new SalvarLinhaDeBase(a.Nome), null);
            default:
                return new(a.Tipo, null, "tipo de atualização desconhecido");
        }
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
