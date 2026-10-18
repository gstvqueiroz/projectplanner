using System.Globalization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;
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
            itens.Add(Item(cronograma, a, tarefa, nome, hoje));
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

    private static ItemDoCartao Item(Cronograma c, AtualizacaoDaObra a, Tarefa? tarefa, string nome, DateOnly hoje)
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
            case TiposDeAtualizacao.LinhaDeBase:
                return new(a.Nome is { } n2 ? $"Salvar a linha de base \"{n2}\"" : "Salvar uma linha de base (o cronograma de agora)", new SalvarLinhaDeBase(a.Nome), null);
            default:
                return new(a.Tipo, null, "tipo de atualização desconhecido");
        }
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
