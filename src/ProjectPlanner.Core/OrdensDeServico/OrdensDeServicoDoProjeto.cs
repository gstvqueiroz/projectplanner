namespace ProjectPlanner.Core.OrdensDeServico;

/// <summary>Bloco 58: o preço que os itens trazem do orçamento.</summary>
public enum PrecoDosItens
{
    /// <summary>Só a mão de obra e os equipamentos da composição (o material é da obra).</summary>
    MaoDeObra,

    /// <summary>O custo direto inteiro (o executante põe o material).</summary>
    ServicoCompleto,
}

/// <summary>
/// Bloco 58: as ordens de serviço da obra, na ordem do número. Voltam no Desfazer, como as tarefas (as OS são imutáveis: a
/// cópia do cronograma usa as mesmas).
/// </summary>
public sealed class OrdensDeServicoDoProjeto
{
    private readonly List<OrdemDeServico> _ordens = [];

    public IReadOnlyList<OrdemDeServico> Todas => [.. _ordens.OrderBy(o => o.Numero)];

    public bool Vazio => _ordens.Count == 0;

    /// <summary>O número da próxima OS (1 na primeira; um número de OS excluída não volta).</summary>
    public int ProximoNumero => _ordens.Count == 0 ? 1 : _ordens.Max(o => o.Numero) + 1;

    public OrdemDeServico? Obter(Guid id) => _ordens.Find(o => o.Id == id);

    /// <summary>A OS pelo número ("14", "OS-014", "os14").</summary>
    public OrdemDeServico? PeloCodigo(string? texto)
    {
        var digitos = new string((texto ?? "").Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digitos, out var numero) ? _ordens.Find(o => o.Numero == numero) : null;
    }

    /// <summary>As OS que cobrem a tarefa (pelas tarefas da OS ou pelos itens), menos as canceladas.</summary>
    public IReadOnlyList<OrdemDeServico> DaTarefa(Guid tarefaId) =>
        [.. Todas.Where(o => o.Situacao != SituacaoDaOrdem.Cancelada && (o.Dados.Tarefas.Contains(tarefaId) || o.Dados.Itens.Any(i => i.TarefaId == tarefaId)))];

    /// <exception cref="DomainValidationException">Id ou número repetido.</exception>
    public void Adicionar(OrdemDeServico ordem)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        if (_ordens.Exists(o => o.Id == ordem.Id))
            throw new DomainValidationException(nameof(Todas), $"A {ordem.Codigo} já está na lista.");
        if (_ordens.Exists(o => o.Numero == ordem.Numero))
            throw new DomainValidationException(nameof(Todas), $"Já existe a {ordem.Codigo}.");
        _ordens.Add(ordem);
    }

    /// <summary>Troca a OS de mesmo Id pela versão nova (o número não muda).</summary>
    /// <exception cref="DomainValidationException">A OS não está na lista, ou o número mudou.</exception>
    public void Substituir(OrdemDeServico ordem)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        var indice = _ordens.FindIndex(o => o.Id == ordem.Id);
        if (indice < 0)
            throw new DomainValidationException(nameof(Todas), $"A {ordem.Codigo} não está na lista.");
        if (_ordens[indice].Numero != ordem.Numero)
            throw new DomainValidationException(nameof(Todas), "O número da OS não muda.");
        _ordens[indice] = ordem;
    }

    /// <summary>Exclui um rascunho (uma OS que já saiu é cancelada, para ficar o histórico).</summary>
    /// <exception cref="DomainValidationException">A OS não está na lista ou já foi emitida.</exception>
    public void Remover(Guid id)
    {
        var ordem = Obter(id) ?? throw new DomainValidationException(nameof(Todas), "A OS não está na lista.");
        if (ordem.Situacao != SituacaoDaOrdem.Rascunho)
            throw new DomainValidationException(nameof(Todas), $"A {ordem.Codigo} já foi emitida: cancele em vez de excluir, para ficar o histórico.");
        _ordens.Remove(ordem);
    }

    internal OrdensDeServicoDoProjeto Copiar()
    {
        var copia = new OrdensDeServicoDoProjeto();
        copia._ordens.AddRange(_ordens);
        return copia;
    }

    /// <summary>As OS lidas do arquivo.</summary>
    /// <exception cref="DomainValidationException">Id ou número repetido.</exception>
    internal void Restaurar(IEnumerable<OrdemDeServico> ordens)
    {
        _ordens.Clear();
        foreach (var ordem in ordens)
            Adicionar(ordem);
    }
}

/// <summary>
/// Bloco 58: o rascunho de uma OS a partir de tarefas do cronograma: um resumo traz as tarefas dentro dele; cada tarefa vira um
/// item com a unidade e a quantidade dela (ou a estimada no orçamento) e o preço da composição (só a mão de obra, ou o custo
/// direto inteiro); sem composição, o preço unitário da tarefa. As datas vêm do cronograma calculado.
/// </summary>
public static class MontagemDaOrdem
{
    public const string UnidadeSemQuantidade = "vb";

    /// <exception cref="DomainValidationException">Nenhuma tarefa, ou uma que não é do projeto.</exception>
    public static DadosDaOrdem Rascunho(Cronograma cronograma, IEnumerable<Guid> tarefas, PrecoDosItens preco)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefas);
        var escolhidas = Folhas(cronograma, tarefas);
        if (escolhidas.Count == 0)
            throw new DomainValidationException(nameof(tarefas), "Escolha pelo menos uma tarefa para a OS.");
        var itens = escolhidas.Select(t => Item(cronograma, t, preco)).ToList();
        var titulo = escolhidas.Count == 1 ? escolhidas[0].Nome : $"{escolhidas[0].Nome} e mais {escolhidas.Count - 1}";
        if (tarefas.Distinct().Count() == 1 && cronograma.Tarefas.FirstOrDefault(t => t.Id == tarefas.First()) is { EhResumo: true } resumo)
            titulo = resumo.Nome; // a etapa inteira
        if (titulo.Length > DadosDaOrdem.TamanhoMaximoDoTitulo)
            titulo = titulo[..(DadosDaOrdem.TamanhoMaximoDoTitulo - 1)] + "…";
        var inicio = escolhidas.Min(t => t.InicioMaisCedo ?? t.DataInicio);
        var fim = escolhidas.Max(t => t.TerminoMaisCedo ?? t.DataTermino);
        return new DadosDaOrdem(titulo, TipoDeExecutante.Terceiro, null, null, null, escolhidas.Select(t => t.Id), itens,
            DateOnly.FromDateTime(inicio), DateOnly.FromDateTime(fim), FormaDePagamentoDaOrdem.Empreitada, null, null);
    }

    /// <summary>As tarefas (não resumos) escolhidas, com as de dentro de cada resumo, na ordem do cronograma.</summary>
    public static IReadOnlyList<Tarefa> Folhas(Cronograma cronograma, IEnumerable<Guid> tarefas)
    {
        var ids = new HashSet<Guid>();
        foreach (var id in tarefas)
        {
            var tarefa = cronograma.Tarefas.FirstOrDefault(t => t.Id == id)
                         ?? throw new DomainValidationException(nameof(tarefas), "Uma das tarefas não é deste projeto.");
            ids.Add(tarefa.Id);
            if (tarefa.EhResumo)
                foreach (var filha in cronograma.DescendentesDe(tarefa.Id))
                    ids.Add(filha.Id);
        }
        return [.. cronograma.Tarefas.Where(t => ids.Contains(t.Id) && !t.EhResumo)];
    }

    private static ItemDaOrdem Item(Cronograma cronograma, Tarefa tarefa, PrecoDosItens preco)
    {
        var composicao = cronograma.Orcamento.ComposicaoDe(tarefa.Id);
        var quantidade = tarefa.Quantidade is > 0 ? tarefa.Quantidade.Value
            : cronograma.Orcamento.QuantidadesEstimadas.TryGetValue(tarefa.Id, out var estimada) && estimada > 0 ? estimada : 1;
        var unidade = tarefa.Unidade ?? composicao?.Unidade;
        if (string.IsNullOrWhiteSpace(unidade) || quantidade == 1 && tarefa.Quantidade is null && composicao is null)
            unidade = UnidadeSemQuantidade;
        var unitario = composicao is not null
            ? preco == PrecoDosItens.MaoDeObra ? composicao.CustoDeMaoDeObraEEquipamentos : composicao.CustoUnitario
            : tarefa.PrecoUnitario ?? tarefa.Valor ?? 0;
        if (unidade!.Length > ItemDaOrdem.TamanhoMaximoDaUnidade)
            unidade = unidade[..ItemDaOrdem.TamanhoMaximoDaUnidade];
        var descricao = tarefa.Nome.Length > ItemDaOrdem.TamanhoMaximoDaDescricao ? tarefa.Nome[..ItemDaOrdem.TamanhoMaximoDaDescricao] : tarefa.Nome;
        return new ItemDaOrdem(tarefa.Id, descricao, unidade, quantidade, unitario);
    }
}
