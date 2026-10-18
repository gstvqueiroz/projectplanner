using System.Globalization;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Medicoes;

/// <summary>
/// Bloco 36: um item do contrato: uma tarefa (não resumo) com valor. Com quantidade, é medido nela; sem quantidade, em %
/// (a unidade é "%" e a quantidade contratada, 100).
/// </summary>
public sealed record ItemDoContrato(Guid TarefaId, string Edt, string Nome, int Nivel, string Unidade, bool PorPercentual,
    decimal QuantidadeContratada, decimal ValorContratado)
{
    /// <summary>O valor de uma unidade (num item em %, o valor de 1%).</summary>
    public decimal PrecoUnitario => QuantidadeContratada == 0 ? 0 : ValorContratado / QuantidadeContratada;
}

/// <summary>
/// Bloco 36: o contrato do projeto: as regras (<see cref="ConfiguracaoDoContrato"/>), os aditivos e as medições. Os itens do
/// contrato são as tarefas com valor; o contratado de cada um é o valor (e a quantidade) da tarefa. Depois da primeira
/// medição aprovada, o contrato fica travado: o valor, a quantidade e a unidade dos itens só mudam por um aditivo.
/// </summary>
/// <remarks>
/// As medições vão em ordem (nº 1, 2, 3…), sem períodos sobrepostos, e só se abre uma nova depois de aprovar a anterior.
/// Uma medição aprovada guarda o boletim inteiro (as linhas e os números) como estava; o "anterior" das seguintes é a soma
/// do que as aprovadas mediram. Só a última pode voltar a rascunho (se não estiver paga), e só a última, em rascunho, pode
/// ser removida. Toda violação lança <see cref="DomainValidationException"/> e não muda nada.
/// </remarks>
public sealed class ContratoDoProjeto
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly List<Aditivo> _aditivos = [];
    private readonly List<Medicao> _medicoes = [];

    internal ContratoDoProjeto(Cronograma cronograma) => _cronograma = cronograma;

    public ConfiguracaoDoContrato Configuracao { get; private set; } = ConfiguracaoDoContrato.Padrao;

    public IReadOnlyList<Aditivo> Aditivos => _aditivos.AsReadOnly();

    public IReadOnlyList<Medicao> Medicoes => _medicoes.AsReadOnly();

    /// <summary>Sem regras, aditivos nem medições (nada a gravar).</summary>
    public bool Vazio => EhPadrao(Configuracao) && _aditivos.Count == 0 && _medicoes.Count == 0;

    /// <summary>Há medição aprovada: o contratado só muda por aditivo.</summary>
    public bool Travado => _medicoes.Exists(m => m.Travada);

    /// <summary>A soma dos aditivos (o valor original do contrato é o atual menos ela).</summary>
    public decimal ValorDosAditivos => _aditivos.Sum(a => a.Valor);

    /// <summary>O valor do contrato agora: a soma dos itens.</summary>
    public decimal ValorContratado => Itens().Sum(i => i.ValorContratado);

    /// <summary>O total das medições aprovadas.</summary>
    public decimal MedidoAprovado => _medicoes.Where(m => m.Travada).Sum(m => m.Resumo!.MedidoNoPeriodo);

    /// <summary>Há tarefas com Valor (itens do contrato) para medir.</summary>
    public bool TemItens => Itens().Count > 0;

    /// <summary>Por que uma tarefa sem Valor não se mede, e como resolver.</summary>
    public static string SemValor(string? tarefa) =>
        $"{(tarefa is null ? "A tarefa" : $"\"{tarefa}\"")} não tem Valor, então não é item do contrato: preencha a coluna Valor " +
        "(ou aplique o orçamento em Projeto > Orçamento…) para medi-la.";

    public const string MotivoDoTravamento =
        "O contrato já tem medição aprovada: o valor, a quantidade e a unidade dos itens mudam por um aditivo (Projeto > Medições…, aba Aditivos).";

    /// <exception cref="DomainValidationException">Uma medição com este número não existe.</exception>
    public Medicao ObterMedicao(int numero) =>
        _medicoes.Find(m => m.Numero == numero) ?? throw new DomainValidationException(nameof(Medicoes), $"Não existe a medição nº {numero:00}.");

    /// <summary>Recusa mudar o valor, a quantidade ou a unidade da tarefa com o contrato travado.</summary>
    /// <param name="soAUnidade">Só a unidade: vale numa tarefa sem valor (que ainda não é item do contrato).</param>
    /// <exception cref="DomainValidationException">O contrato está travado.</exception>
    public void VerificarSePodeMudar(Tarefa tarefa, bool soAUnidade = false)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        if (!Travado || (soAUnidade && (tarefa.Valor is null or 0) && !Medidas().ContainsKey(tarefa.Id)))
            return;
        throw new DomainValidationException(nameof(Travado), MotivoDoTravamento);
    }

    /// <exception cref="DomainValidationException">Retenção, índice ou nome do índice inválidos.</exception>
    public void DefinirConfiguracao(ConfiguracaoDoContrato configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        var limpa = configuracao with
        {
            NomeDoIndice = string.IsNullOrWhiteSpace(configuracao.NomeDoIndice) ? null : configuracao.NomeDoIndice.Trim(),
            Cortes = [.. configuracao.Cortes.Distinct().Order()],
        };
        limpa.Validar();
        Configuracao = limpa;
    }

    // ---- Itens ----

    /// <summary>
    /// Os itens do contrato, na ordem do cronograma: as tarefas (não resumo) com valor, e as que já foram medidas (mesmo que
    /// o valor tenha ido a zero).
    /// </summary>
    public IReadOnlyList<ItemDoContrato> Itens()
    {
        var medidas = Medidas();
        var edt = _cronograma.NumeracaoEdt();
        var niveis = Niveis();
        return [.. _cronograma.Tarefas
            .Where(t => !t.EhResumo && (t.Valor > 0 || medidas.ContainsKey(t.Id)))
            .Select(t => new ItemDoContrato(t.Id, edt[t.Id], t.Nome, niveis[t.Id], t.Quantidade is null ? "%" : t.Unidade ?? "",
                t.Quantidade is null, t.Quantidade ?? 100, t.Valor ?? 0))];
    }

    // O medido de cada tarefa nas aprovadas (de número menor que o pedido): a quantidade (ou o %) e o valor.
    private Dictionary<Guid, (decimal Quantidade, decimal Valor)> Anterior(int numero)
    {
        var acumulado = new Dictionary<Guid, (decimal Quantidade, decimal Valor)>();
        foreach (var medicao in _medicoes.Where(m => m.Travada && m.Numero < numero))
            foreach (var linha in medicao.Linhas!.Where(l => !l.EhGrupo && l.TarefaId is not null))
            {
                var (q, v) = acumulado.GetValueOrDefault(linha.TarefaId!.Value);
                acumulado[linha.TarefaId.Value] = (q + linha.QuantidadeNoPeriodo, v + linha.ValorNoPeriodo);
            }
        return acumulado;
    }

    private Dictionary<Guid, (decimal Quantidade, decimal Valor)> Medidas() =>
        Anterior(int.MaxValue).Where(p => p.Value.Valor != 0 || p.Value.Quantidade != 0).ToDictionary(p => p.Key, p => p.Value);

    private Dictionary<Guid, int> Niveis()
    {
        var niveis = new Dictionary<Guid, int>();
        foreach (var tarefa in _cronograma.Tarefas) // em pré-ordem, o pai vem antes
            niveis[tarefa.Id] = tarefa.TarefaPaiId is { } pai ? niveis[pai] + 1 : 0;
        return niveis;
    }

    // Num item em %, o anterior em % vem do valor já medido (o valor do item pode ter mudado por aditivo).
    private static decimal QuantidadeAnterior(ItemDoContrato item, (decimal Quantidade, decimal Valor) anterior) =>
        item.PorPercentual && item.ValorContratado > 0
            ? Math.Round(anterior.Valor / item.ValorContratado * 100, 2, MidpointRounding.AwayFromZero)
            : anterior.Quantidade;

    private static int Casas(ItemDoContrato item) => item.PorPercentual ? 2 : 4;

    // ---- Medições ----

    /// <summary>
    /// O período sugerido para a próxima medição: começa no dia seguinte ao fim da última (ou no início do projeto) e vai até
    /// o fim do período do plano (semana, quinzena, mês ou a próxima data de corte).
    /// </summary>
    public (DateOnly De, DateOnly Ate) ProximoPeriodo()
    {
        var de = _medicoes.Count == 0 ? DateOnly.FromDateTime(_cronograma.DataInicio) : _medicoes[^1].Ate.AddDays(1);
        var fim = de.AddMonths(2);
        var periodos = PeriodosFinanceiros.Gerar(de, fim, Configuracao.Periodicidade, Configuracao.Cortes);
        return (de, periodos[0].Fim);
    }

    /// <summary>Abre a próxima medição, em rascunho e sem nada medido.</summary>
    /// <exception cref="DomainValidationException">A anterior não foi aprovada, ou o período é inválido ou sobrepõe o da anterior.</exception>
    public Medicao NovaMedicao(DateOnly de, DateOnly ate)
    {
        if (_medicoes.Count > 0 && !_medicoes[^1].Travada)
            throw new DomainValidationException(nameof(Medicoes), $"Aprove a {_medicoes[^1].Rotulo.ToLower(Brasil)} antes de abrir outra.");
        var numero = _medicoes.Count == 0 ? 1 : _medicoes[^1].Numero + 1;
        ValidarPeriodo(numero, de, ate);
        var medicao = new Medicao { Numero = numero, De = de, Ate = ate, Situacao = SituacaoDaMedicao.Rascunho };
        _medicoes.Add(medicao);
        return medicao;
    }

    /// <exception cref="DomainValidationException">Medição travada, período invertido ou sobreposto ao da anterior.</exception>
    public void DefinirPeriodo(int numero, DateOnly de, DateOnly ate)
    {
        var medicao = Rascunho(numero);
        ValidarPeriodo(numero, de, ate);
        Trocar(medicao with { De = de, Ate = ate });
    }

    private void ValidarPeriodo(int numero, DateOnly de, DateOnly ate)
    {
        if (ate < de)
            throw new DomainValidationException(nameof(Medicao.Ate), "O período da medição termina antes de começar.");
        if (_medicoes.LastOrDefault(m => m.Numero < numero) is { } anterior && de <= anterior.Ate)
            throw new DomainValidationException(nameof(Medicao.De),
                $"A medição nº {numero:00} precisa começar depois do fim da nº {anterior.Numero:00} ({anterior.Ate:dd/MM/yyyy}).");
    }

    /// <summary>
    /// O medido de um item no período, na unidade dele (ou em %, num item sem quantidade); zero ou nulo tira o item da
    /// medição. Até 4 casas decimais (2 em %).
    /// </summary>
    /// <exception cref="DomainValidationException">Medição travada, tarefa que não é item do contrato, quantidade negativa ou acima do saldo.</exception>
    public void Medir(int numero, Guid tarefaId, decimal? quantidade)
    {
        var medicao = Rascunho(numero);
        var item = Itens().FirstOrDefault(i => i.TarefaId == tarefaId)
                   ?? throw new DomainValidationException(nameof(Medicao.Itens), SemValor(_cronograma.Tarefas.FirstOrDefault(t => t.Id == tarefaId)?.Nome));
        var q = Math.Round(quantidade ?? 0, Casas(item), MidpointRounding.AwayFromZero);
        if (q < 0)
            throw new DomainValidationException(nameof(Medicao.Itens), "O medido não pode ser negativo.");
        var saldo = item.QuantidadeContratada - QuantidadeAnterior(item, Anterior(numero).GetValueOrDefault(tarefaId));
        if (q > saldo)
            throw new DomainValidationException(nameof(Medicao.Itens),
                $"\"{item.Nome}\": {Quantidade(q, item)} passa do saldo do contrato ({Quantidade(Math.Max(saldo, 0), item)}). " +
                "Para medir mais, registre um aditivo.");
        var itens = medicao.Itens.Where(i => i.TarefaId != tarefaId).ToList();
        if (q > 0)
            itens.Add(new ItemMedido(tarefaId, q));
        Trocar(medicao with { Itens = itens });
    }

    /// <summary>
    /// Sugere o medido de cada item pelo % concluído da tarefa (que pode ter vindo das fotos): o que falta para chegar a ele,
    /// descontado o que as medições anteriores já mediram. Troca o que estava na medição.
    /// </summary>
    /// <returns>Quantos itens ficaram com medido.</returns>
    public int SugerirPeloPercentualConcluido(int numero)
    {
        var medicao = Rascunho(numero);
        var anterior = Anterior(numero);
        var porId = _cronograma.Tarefas.ToDictionary(t => t.Id);
        var itens = new List<ItemMedido>();
        foreach (var item in Itens())
        {
            var jaMedido = QuantidadeAnterior(item, anterior.GetValueOrDefault(item.TarefaId));
            var alvo = item.QuantidadeContratada * porId[item.TarefaId].PercentualConcluido / 100m;
            var q = Math.Min(Math.Round(Math.Max(0, alvo - jaMedido), Casas(item), MidpointRounding.AwayFromZero), item.QuantidadeContratada - jaMedido);
            if (q > 0)
                itens.Add(new ItemMedido(item.TarefaId, q));
        }
        Trocar(medicao with { Itens = itens });
        return itens.Count;
    }

    /// <exception cref="DomainValidationException">Medição travada.</exception>
    public void DefinirGlosas(int numero, IEnumerable<GlosaDaMedicao> glosas)
    {
        ArgumentNullException.ThrowIfNull(glosas);
        Trocar(Rascunho(numero) with { Glosas = [.. glosas] });
    }

    /// <summary>O índice de reajuste do período (I1); nulo sem reajuste nesta medição.</summary>
    /// <exception cref="DomainValidationException">Medição travada, ou índice zero ou negativo.</exception>
    public void DefinirIndice(int numero, decimal? indice)
    {
        var medicao = Rascunho(numero);
        if (indice is <= 0)
            throw new DomainValidationException(nameof(Medicao.Indice), "O índice do período precisa ser maior que zero.");
        Trocar(medicao with { Indice = indice });
    }

    /// <summary>Se esta medição devolve a retenção acumulada (normalmente a última).</summary>
    /// <exception cref="DomainValidationException">Medição travada.</exception>
    public void DefinirDevolucaoDaRetencao(int numero, bool devolver) => Trocar(Rascunho(numero) with { DevolverRetencao = devolver });

    /// <exception cref="DomainValidationException">Medição travada, ou texto longo demais.</exception>
    public void DefinirObservacoes(int numero, string? observacoes) =>
        Trocar(Rascunho(numero) with { Observacoes = Medicao.LimparObservacoes(observacoes) });

    /// <summary>O boletim da medição: guardado (aprovada ou paga) ou calculado agora (rascunho).</summary>
    public BoletimDeMedicao Boletim(int numero)
    {
        var medicao = ObterMedicao(numero);
        if (medicao.Travada)
            return new BoletimDeMedicao(medicao, medicao.Linhas!, medicao.Resumo!);
        var (linhas, resumo) = Calcular(medicao);
        return new BoletimDeMedicao(medicao, linhas, resumo);
    }

    /// <summary>
    /// Aprova a medição: o boletim (linhas e números) fica guardado como está, e o contrato passa a mudar só por aditivo.
    /// </summary>
    /// <exception cref="DomainValidationException">Já aprovada, nada medido (sem devolver a retenção), medido acima do contratado, ou glosas acima do valor.</exception>
    public BoletimDeMedicao Aprovar(int numero, DateTime agora)
    {
        var medicao = Rascunho(numero);
        var (linhas, resumo) = Calcular(medicao);
        var passou = linhas.Where(l => !l.EhGrupo && l.QuantidadeNoPeriodo > 0 && l.QuantidadeAcumulada > l.QuantidadeContratada).ToList();
        if (passou.Count > 0)
            throw new DomainValidationException(nameof(Medicao.Itens),
                $"O medido passa do contratado em {string.Join(", ", passou.Take(3).Select(l => $"{l.Edt} {l.Descricao}"))}. " +
                "Ajuste as quantidades ou registre um aditivo.");
        if (resumo.MedidoNoPeriodo == 0 && !medicao.DevolverRetencao)
            throw new DomainValidationException(nameof(Medicao.Itens),
                "A medição não tem nada medido: informe o medido dos itens (ou use Sugerir pelo % concluído).");
        if (resumo.Bruto < 0)
            throw new DomainValidationException(nameof(Medicao.Glosas),
                $"As glosas ({Moeda.ComSimbolo(resumo.Glosas)}) passam do valor medido com o reajuste ({Moeda.ComSimbolo(resumo.MedidoNoPeriodo + resumo.Reajuste)}).");
        var aprovada = medicao with { Situacao = SituacaoDaMedicao.Aprovada, AprovadaEm = agora, Linhas = linhas, Resumo = resumo };
        Trocar(aprovada);
        return new BoletimDeMedicao(aprovada, linhas, resumo);
    }

    /// <summary>Volta a última medição aprovada (não paga) a rascunho: o boletim guardado sai, e o medido fica.</summary>
    /// <exception cref="DomainValidationException">Não é a última, está paga ou já é rascunho.</exception>
    public void Reabrir(int numero)
    {
        var medicao = ObterMedicao(numero);
        if (medicao.Situacao == SituacaoDaMedicao.Rascunho)
            throw new DomainValidationException(nameof(Medicao.Situacao), $"A {medicao.Rotulo.ToLower(Brasil)} já está em rascunho.");
        if (medicao.Situacao == SituacaoDaMedicao.Paga)
            throw new DomainValidationException(nameof(Medicao.Situacao), $"A {medicao.Rotulo.ToLower(Brasil)} está paga: desmarque o pagamento antes de reabrir.");
        if (medicao.Numero != _medicoes[^1].Numero)
            throw new DomainValidationException(nameof(Medicao.Situacao), "Só a última medição pode voltar a rascunho.");
        Trocar(medicao with { Situacao = SituacaoDaMedicao.Rascunho, AprovadaEm = null, Linhas = null, Resumo = null });
    }

    /// <exception cref="DomainValidationException">A medição não está aprovada.</exception>
    public void MarcarPaga(int numero, DateOnly data)
    {
        var medicao = ObterMedicao(numero);
        if (medicao.Situacao != SituacaoDaMedicao.Aprovada)
            throw new DomainValidationException(nameof(Medicao.Situacao),
                medicao.Situacao == SituacaoDaMedicao.Paga ? $"A {medicao.Rotulo.ToLower(Brasil)} já está paga." : "Aprove a medição antes de marcar o pagamento.");
        Trocar(medicao with { Situacao = SituacaoDaMedicao.Paga, PagaEm = data });
    }

    /// <exception cref="DomainValidationException">A medição não está paga.</exception>
    public void DesmarcarPaga(int numero)
    {
        var medicao = ObterMedicao(numero);
        if (medicao.Situacao != SituacaoDaMedicao.Paga)
            throw new DomainValidationException(nameof(Medicao.Situacao), $"A {medicao.Rotulo.ToLower(Brasil)} não está paga.");
        Trocar(medicao with { Situacao = SituacaoDaMedicao.Aprovada, PagaEm = null });
    }

    /// <summary>Remove a última medição, se ainda é rascunho.</summary>
    /// <exception cref="DomainValidationException">Não é a última, ou está aprovada.</exception>
    public void RemoverMedicao(int numero)
    {
        var medicao = ObterMedicao(numero);
        if (medicao.Travada)
            throw new DomainValidationException(nameof(Medicao.Situacao), "Uma medição aprovada não pode ser removida (reabra-a antes).");
        if (medicao.Numero != _medicoes[^1].Numero)
            throw new DomainValidationException(nameof(Medicoes), "Só a última medição pode ser removida.");
        _medicoes.Remove(medicao);
    }

    private Medicao Rascunho(int numero)
    {
        var medicao = ObterMedicao(numero);
        return medicao.Travada
            ? throw new DomainValidationException(nameof(Medicao.Situacao),
                $"A {medicao.Rotulo.ToLower(Brasil)} está {(medicao.Situacao == SituacaoDaMedicao.Paga ? "paga" : "aprovada")} e não muda mais. Para corrigir, reabra-a.")
            : medicao;
    }

    private void Trocar(Medicao medicao)
    {
        medicao.Validar();
        _medicoes[_medicoes.FindIndex(m => m.Numero == medicao.Numero)] = medicao;
    }

    // ---- O boletim ----

    private (List<LinhaDoBoletim> Linhas, ResumoDoBoletim Resumo) Calcular(Medicao medicao)
    {
        var anterior = Anterior(medicao.Numero);
        var medido = medicao.Itens.ToDictionary(i => i.TarefaId, i => i.Quantidade);
        var porId = _cronograma.Tarefas.ToDictionary(t => t.Id);
        var edt = _cronograma.NumeracaoEdt();
        var niveis = Niveis();

        var doItem = new Dictionary<Guid, LinhaDoBoletim>();
        foreach (var item in Itens())
        {
            var ant = anterior.GetValueOrDefault(item.TarefaId);
            var qAnt = QuantidadeAnterior(item, ant);
            var q = medido.GetValueOrDefault(item.TarefaId);
            doItem[item.TarefaId] = new LinhaDoBoletim(item.TarefaId, item.Edt, item.Nome, item.Nivel, false, item.PorPercentual, item.Unidade,
                item.QuantidadeContratada, Math.Round(item.PrecoUnitario, 2, MidpointRounding.AwayFromZero), item.ValorContratado,
                qAnt, q, ant.Valor, ValorDoPeriodo(item, qAnt, ant.Valor, q));
        }

        // Os grupos (resumos com algum item dentro) somam os valores dos itens.
        var somas = new Dictionary<Guid, (decimal Contratado, decimal Anterior, decimal Periodo)>();
        foreach (var linha in doItem.Values)
            for (var pai = porId[linha.TarefaId!.Value].TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
            {
                var (c, a, p) = somas.GetValueOrDefault(id);
                somas[id] = (c + linha.ValorContratado, a + linha.ValorAnterior, p + linha.ValorNoPeriodo);
            }
        var linhas = new List<LinhaDoBoletim>();
        foreach (var tarefa in _cronograma.Tarefas)
            if (doItem.TryGetValue(tarefa.Id, out var linha))
                linhas.Add(linha);
            else if (somas.TryGetValue(tarefa.Id, out var soma))
                linhas.Add(new LinhaDoBoletim(tarefa.Id, edt[tarefa.Id], tarefa.Nome, niveis[tarefa.Id], true, false, "", 0, 0, soma.Contratado, 0, 0,
                    soma.Anterior, soma.Periodo));

        // Itens medidos antes que saíram do cronograma: continuam, com o contratado zero.
        var removidas = anterior.Where(p => !porId.ContainsKey(p.Key) && (p.Value.Valor != 0 || p.Value.Quantidade != 0)).ToList();
        if (removidas.Count > 0)
        {
            linhas.Add(new LinhaDoBoletim(null, "—", "Itens que saíram do cronograma", 0, true, false, "", 0, 0, 0, 0, 0,
                removidas.Sum(r => r.Value.Valor), 0, Removida: true));
            foreach (var (id, (q, v)) in removidas)
            {
                var ultima = _medicoes.Where(m => m.Travada).SelectMany(m => m.Linhas!).Last(l => l.TarefaId == id && !l.EhGrupo);
                linhas.Add(ultima with
                {
                    Nivel = 1, QuantidadeContratada = 0, ValorContratado = 0, QuantidadeAnterior = ultima.PorPercentual ? 0 : q,
                    QuantidadeNoPeriodo = 0, ValorAnterior = v, ValorNoPeriodo = 0, Removida = true,
                });
            }
        }

        var itens = linhas.Where(l => !l.EhGrupo).ToList();
        var noPeriodo = itens.Sum(l => l.ValorNoPeriodo);
        var (reajuste, descricao) = Reajuste(medicao, noPeriodo);
        var glosas = medicao.Glosas.Sum(g => g.Valor);
        var bruto = noPeriodo + reajuste - glosas;
        var retidoAntes = _medicoes.Where(m => m.Travada && m.Numero < medicao.Numero).Sum(m => m.Resumo!.Retencao - m.Resumo.Devolucao);
        var retencao = medicao.DevolverRetencao || bruto <= 0
            ? 0
            : Math.Round(bruto * Configuracao.PercentualDeRetencao / 100, 2, MidpointRounding.AwayFromZero);
        var fisico = _cronograma.Tarefas.Any(t => !t.EhResumo)
            ? CalculadoraFisicoFinanceira.Realizado(_cronograma, medicao.Ate).Percentual
            : (double?)null;
        var resumo = new ResumoDoBoletim(ValorDosAditivos, _aditivos.Count, itens.Sum(l => l.ValorContratado), itens.Sum(l => l.ValorAnterior), noPeriodo,
            reajuste, descricao, glosas, Configuracao.PercentualDeRetencao, retencao, medicao.DevolverRetencao ? retidoAntes : 0, retidoAntes, fisico);
        return (linhas, resumo);
    }

    // Fecha o item sem sobra de centavos: a medição que chega ao contratado leva o que falta do valor.
    private static decimal ValorDoPeriodo(ItemDoContrato item, decimal quantidadeAnterior, decimal valorAnterior, decimal medido)
    {
        if (medido == 0)
            return 0;
        if (quantidadeAnterior + medido == item.QuantidadeContratada)
            return item.ValorContratado - valorAnterior;
        return Math.Round(medido * item.PrecoUnitario, 2, MidpointRounding.AwayFromZero);
    }

    private (decimal Valor, string? Descricao) Reajuste(Medicao medicao, decimal medido)
    {
        if (medicao.Indice is not { } indice || Configuracao.IndiceBase is not { } baseDoIndice)
            return (0, null);
        var fator = indice / baseDoIndice;
        var valor = Math.Round(medido * (fator - 1), 2, MidpointRounding.AwayFromZero);
        var nome = Configuracao.NomeDoIndice ?? "Índice";
        return (valor, $"{nome}: {Indice(indice)} ÷ {Indice(baseDoIndice)} = {fator.ToString("0.0000", Brasil)}");
    }

    private static string Indice(decimal valor) => valor.ToString("#,##0.####", Brasil);

    private static string Quantidade(decimal valor, ItemDoContrato item) =>
        item.PorPercentual ? $"{valor.ToString("0.##", Brasil)}%" : $"{Moeda.FormatarQuantidade(valor)} {item.Unidade}".TrimEnd();

    // ---- Aditivos ----

    /// <summary>
    /// Registra o próximo aditivo e aplica as mudanças nas tarefas (a quantidade e o valor de cada item; um item novo é uma
    /// tarefa sem valor que passa a ter).
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// Aditivo sem motivo ou sem mudança, tarefa que não está no cronograma ou é resumo, supressão abaixo do já medido,
    /// quantidade num item medido em %, ou valor fora do aceito.
    /// </exception>
    public Aditivo RegistrarAditivo(DateOnly data, string? descricao, IEnumerable<AlteracaoDoAditivo> itens)
    {
        var aditivo = new Aditivo(_aditivos.Count == 0 ? 1 : _aditivos[^1].Numero + 1, data, descricao, itens);
        var mudancas = Mudancas(aditivo.Itens, sinal: 1, oQue: "o aditivo");
        foreach (var (tarefa, quantidade, valor) in mudancas)
            Aplicar(tarefa, quantidade, valor);
        _aditivos.Add(aditivo);
        return aditivo;
    }

    /// <summary>Remove o último aditivo e desfaz as mudanças dele nas tarefas que ainda existem.</summary>
    /// <exception cref="DomainValidationException">Não é o último, ou desfazer deixaria um item abaixo do já medido.</exception>
    public void RemoverAditivo(int numero)
    {
        var aditivo = _aditivos.Find(a => a.Numero == numero)
                      ?? throw new DomainValidationException(nameof(Aditivos), $"Não existe o aditivo nº {numero}.");
        if (aditivo != _aditivos[^1])
            throw new DomainValidationException(nameof(Aditivos), "Só o último aditivo pode ser removido.");
        var existentes = aditivo.Itens.Where(i => _cronograma.Tarefas.Any(t => t.Id == i.TarefaId)).ToList();
        var mudancas = Mudancas(existentes, sinal: -1, oQue: "remover o aditivo");
        foreach (var (tarefa, quantidade, valor) in mudancas)
            Aplicar(tarefa, quantidade, valor);
        _aditivos.Remove(aditivo);
    }

    private static void Aplicar(Tarefa tarefa, decimal? quantidade, decimal valor)
    {
        tarefa.DefinirQuantidade(quantidade);
        tarefa.DefinirValor(valor);
    }

    // Confere tudo antes de mudar qualquer tarefa: a quantidade e o valor que cada uma vai ter.
    private List<(Tarefa Tarefa, decimal? Quantidade, decimal Valor)> Mudancas(IEnumerable<AlteracaoDoAditivo> itens, int sinal, string oQue)
    {
        var porId = _cronograma.Tarefas.ToDictionary(t => t.Id);
        var medidas = Medidas();
        var mudancas = new List<(Tarefa, decimal?, decimal)>();
        foreach (var item in itens)
        {
            var tarefa = porId.GetValueOrDefault(item.TarefaId)
                         ?? throw new DomainValidationException(nameof(Aditivo.Itens), "Uma tarefa do aditivo não está no cronograma.");
            if (tarefa.EhResumo)
                throw new DomainValidationException(nameof(Aditivo.Itens), $"\"{tarefa.Nome}\" é um resumo: o aditivo muda as tarefas dentro dele.");
            var medido = medidas.GetValueOrDefault(tarefa.Id);
            var foiMedida = medido.Valor != 0 || medido.Quantidade != 0;
            var quantidade = tarefa.Quantidade;
            if (item.Quantidade != 0)
            {
                if (tarefa.Quantidade is null && foiMedida)
                    throw new DomainValidationException(nameof(Aditivo.Itens),
                        $"\"{tarefa.Nome}\" é medida em % (sem quantidade): {oQue} só muda o valor dela.");
                var nova = (tarefa.Quantidade ?? 0) + sinal * item.Quantidade;
                if (nova < 0 || (foiMedida && nova < medido.Quantidade))
                    throw new DomainValidationException(nameof(Aditivo.Itens),
                        $"\"{tarefa.Nome}\": {oQue} deixa a quantidade em {Moeda.FormatarQuantidade(nova)}, menos do que já foi medido ({Moeda.FormatarQuantidade(medido.Quantidade)}).");
                if (nova == 0 && foiMedida)
                    throw new DomainValidationException(nameof(Aditivo.Itens), $"\"{tarefa.Nome}\" já foi medida: a quantidade não pode ir a zero.");
                quantidade = nova == 0 ? null : nova;
            }
            var valor = (tarefa.Valor ?? 0) + sinal * item.Valor;
            if (valor < medido.Valor || valor < 0)
                throw new DomainValidationException(nameof(Aditivo.Itens),
                    $"\"{tarefa.Nome}\": {oQue} deixa o valor em {Moeda.ComSimbolo(valor)}, menos do que já foi medido ({Moeda.ComSimbolo(medido.Valor)}).");
            if (valor > Tarefa.ValorMaximo || quantidade > Tarefa.QuantidadeMaxima)
                throw new DomainValidationException(nameof(Aditivo.Itens), $"\"{tarefa.Nome}\": o valor ou a quantidade passa do máximo aceito.");
            mudancas.Add((tarefa, quantidade, valor));
        }
        return mudancas;
    }

    // ---- Cópia e arquivo ----

    internal ContratoDoProjeto CopiarPara(Cronograma cronograma)
    {
        var copia = new ContratoDoProjeto(cronograma) { Configuracao = Configuracao };
        copia._aditivos.AddRange(_aditivos); // aditivos e medições são imutáveis
        copia._medicoes.AddRange(_medicoes);
        return copia;
    }

    /// <summary>O contrato lido do arquivo.</summary>
    /// <exception cref="DomainValidationException">Configuração inválida, números repetidos ou fora de ordem, períodos sobrepostos, ou rascunho antes de uma aprovada.</exception>
    internal void Restaurar(ConfiguracaoDoContrato configuracao, IEnumerable<Aditivo> aditivos, IEnumerable<Medicao> medicoes)
    {
        DefinirConfiguracao(configuracao);
        var listaDeAditivos = aditivos.OrderBy(a => a.Numero).ToList();
        if (listaDeAditivos.GroupBy(a => a.Numero).Any(g => g.Count() > 1))
            throw new DomainValidationException(nameof(Aditivos), "Há aditivos com o mesmo número.");
        var listaDeMedicoes = medicoes.OrderBy(m => m.Numero).ToList();
        for (var i = 0; i < listaDeMedicoes.Count; i++)
        {
            var medicao = listaDeMedicoes[i];
            medicao.Validar();
            if (i > 0 && (medicao.Numero == listaDeMedicoes[i - 1].Numero || medicao.De <= listaDeMedicoes[i - 1].Ate))
                throw new DomainValidationException(nameof(Medicoes), $"A {medicao.Rotulo.ToLower(Brasil)} repete o número ou sobrepõe o período da anterior.");
            if (!medicao.Travada && i < listaDeMedicoes.Count - 1)
                throw new DomainValidationException(nameof(Medicoes), $"A {medicao.Rotulo.ToLower(Brasil)} está em rascunho, mas não é a última.");
        }
        _aditivos.Clear();
        _aditivos.AddRange(listaDeAditivos);
        _medicoes.Clear();
        _medicoes.AddRange(listaDeMedicoes);
    }

    private static bool EhPadrao(ConfiguracaoDoContrato c) =>
        c.PercentualDeRetencao == 0 && c.NomeDoIndice is null && c.IndiceBase is null && c.Periodicidade == Periodicidade.Mensal && c.Cortes.Count == 0;
}
