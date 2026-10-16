using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Recursos;

/// <summary>
/// Bloco 32: os recursos do projeto (profissionais, materiais e equipamentos) e as atribuições deles às tarefas. O custo
/// orçado de uma tarefa é a soma dos recursos (o total de cada um × o custo unitário); num resumo, a soma das tarefas
/// dentro dele. Ele é separado do Valor da tarefa (o preço do contrato): a diferença é a margem. Volta no Desfazer.
/// </summary>
public sealed class RecursosDoProjeto
{
    private readonly Cronograma _cronograma;
    private readonly List<Recurso> _recursos = [];

    internal RecursosDoProjeto(Cronograma cronograma) => _cronograma = cronograma;

    /// <summary>Os recursos na ordem em que entraram.</summary>
    public IReadOnlyList<Recurso> Lista => _recursos.AsReadOnly();

    public bool Vazio => _recursos.Count == 0;

    public Recurso? Obter(Guid id) => _recursos.Find(r => r.Id == id);

    /// <exception cref="DomainValidationException">O recurso já está na lista, ou há outro com o mesmo nome e tipo.</exception>
    public void Adicionar(Recurso recurso)
    {
        ArgumentNullException.ThrowIfNull(recurso);
        if (_recursos.Exists(r => r.Id == recurso.Id))
            throw new DomainValidationException(nameof(Lista), $"O recurso \"{recurso.Nome}\" já está no projeto.");
        RecusarNomeRepetido(recurso);
        _recursos.Add(recurso);
    }

    /// <summary>Troca o recurso de mesmo Id pela versão alterada (custo, unidade, disponível…).</summary>
    /// <exception cref="DomainValidationException">Não está na lista, nome repetido, ou muda entre por dia e material estando em uso.</exception>
    public void Substituir(Recurso recurso)
    {
        ArgumentNullException.ThrowIfNull(recurso);
        var indice = _recursos.FindIndex(r => r.Id == recurso.Id);
        if (indice < 0)
            throw new DomainValidationException(nameof(Lista), $"O recurso \"{recurso.Nome}\" não está no projeto.");
        RecusarNomeRepetido(recurso);
        if (_recursos[indice].PorDia != recurso.PorDia && TarefasCom(recurso.Id).Count > 0)
            throw new DomainValidationException(nameof(Recurso.Tipo),
                $"\"{recurso.Nome}\" está em tarefas: mudar entre material (pelo total) e profissional ou equipamento (por dia) mudaria o que foi atribuído. Tire-o das tarefas antes.");
        _recursos[indice] = recurso;
    }

    /// <exception cref="DomainValidationException">O recurso não está no projeto, ou está em alguma tarefa.</exception>
    public void Remover(Guid id)
    {
        var recurso = Obter(id) ?? throw new DomainValidationException(nameof(Lista), "O recurso não está no projeto.");
        var tarefas = TarefasCom(id);
        if (tarefas.Count > 0)
        {
            var edt = _cronograma.NumeracaoEdt();
            throw new DomainValidationException(nameof(Lista),
                $"\"{recurso.Nome}\" está em {(tarefas.Count == 1 ? "1 tarefa" : $"{tarefas.Count} tarefas")} " +
                $"({string.Join(", ", tarefas.Take(5).Select(t => $"{edt[t.Id]} {t.Nome}"))}{(tarefas.Count > 5 ? "…" : "")}): tire-o delas antes de remover.");
        }
        _recursos.Remove(recurso);
    }

    private void RecusarNomeRepetido(Recurso recurso)
    {
        if (_recursos.Exists(r => r.Id != recurso.Id && r.Tipo == recurso.Tipo && string.Equals(r.Nome, recurso.Nome, StringComparison.CurrentCultureIgnoreCase)))
            throw new DomainValidationException(nameof(Recurso.Nome), $"Já existe o recurso \"{recurso.Nome}\" ({TiposDeRecurso.Texto(recurso.Tipo).ToLowerInvariant()}).");
    }

    /// <summary>As tarefas que usam o recurso, na ordem do cronograma.</summary>
    public IReadOnlyList<Tarefa> TarefasCom(Guid recursoId) =>
        [.. _cronograma.Tarefas.Where(t => t.Atribuicoes.Any(a => a.RecursoId == recursoId))];

    // ---- Atribuições ----

    /// <summary>Troca os recursos da tarefa pelos informados (vazio tira todos).</summary>
    /// <exception cref="DomainValidationException">Tarefa resumo ou de outro projeto, recurso que não é do projeto, ou recurso repetido.</exception>
    public void Atribuir(Tarefa tarefa, IEnumerable<AtribuicaoDeRecurso> atribuicoes)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        ArgumentNullException.ThrowIfNull(atribuicoes);
        if (!_cronograma.Tarefas.Contains(tarefa))
            throw new DomainValidationException(nameof(Tarefa.Atribuicoes), "A tarefa não é deste projeto.");
        if (tarefa.EhResumo)
            throw new DomainValidationException(nameof(Tarefa.Atribuicoes), $"\"{tarefa.Nome}\" é um resumo: os recursos vão nas tarefas dentro dele.");
        var lista = atribuicoes.ToList();
        foreach (var a in lista)
            if (Obter(a.RecursoId) is null)
                throw new DomainValidationException(nameof(Tarefa.Atribuicoes), "Um dos recursos não está no projeto.");
        if (lista.GroupBy(a => a.RecursoId).FirstOrDefault(g => g.Count() > 1) is { } repetido)
            throw new DomainValidationException(nameof(Tarefa.Atribuicoes), $"\"{Obter(repetido.Key)!.Nome}\" aparece duas vezes na tarefa.");
        tarefa.DefinirAtribuicoes(lista);
    }

    /// <summary>O total do recurso na tarefa: por dia × os dias úteis dela (profissional, equipamento) ou o total (material).</summary>
    public decimal Total(Tarefa tarefa, AtribuicaoDeRecurso atribuicao)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        ArgumentNullException.ThrowIfNull(atribuicao);
        return Obter(atribuicao.RecursoId) is { PorDia: true } ? atribuicao.Quantidade * tarefa.Duracao : atribuicao.Quantidade;
    }

    /// <summary>O custo do recurso na tarefa (o total × o custo unitário), em centavos.</summary>
    public decimal Custo(Tarefa tarefa, AtribuicaoDeRecurso atribuicao) =>
        Math.Round(Total(tarefa, atribuicao) * (Obter(atribuicao.RecursoId)?.CustoUnitario ?? 0), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// O custo orçado da tarefa pelos recursos; num resumo, a soma das tarefas dentro dele. Nulo sem nenhum recurso (na
    /// tarefa ou, num resumo, em nenhuma tarefa dentro dele).
    /// </summary>
    public decimal? CustoOrcado(Tarefa tarefa)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        var folhas = tarefa.EhResumo ? _cronograma.DescendentesDe(tarefa.Id).Where(t => !t.EhResumo) : [tarefa];
        var comRecursos = folhas.Where(t => t.Atribuicoes.Count > 0).ToList();
        return comRecursos.Count == 0 ? null : comRecursos.Sum(t => t.Atribuicoes.Sum(a => Custo(t, a)));
    }

    /// <summary>O custo orçado do projeto inteiro pelos recursos.</summary>
    public decimal CustoTotal => _cronograma.Tarefas.Where(t => !t.EhResumo).Sum(t => t.Atribuicoes.Sum(a => Custo(t, a)));

    /// <summary>A quantidade total e o custo do recurso no projeto.</summary>
    public (decimal Quantidade, decimal Custo) Uso(Guid recursoId)
    {
        var (quantidade, custo) = (0m, 0m);
        foreach (var tarefa in _cronograma.Tarefas)
            foreach (var a in tarefa.Atribuicoes.Where(a => a.RecursoId == recursoId))
                (quantidade, custo) = (quantidade + Total(tarefa, a), custo + Custo(tarefa, a));
        return (quantidade, custo);
    }

    /// <summary>"2 Pedreiro; 1 Servente; 120 sc Cimento" (por dia, nos profissionais e equipamentos; o total, nos materiais).</summary>
    public string Texto(Tarefa tarefa)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        return string.Join("; ", tarefa.Atribuicoes.Select(a => Obter(a.RecursoId) is { } r
            ? r.PorDia ? $"{Numero(a.Quantidade)} {r.Nome}" : $"{Numero(a.Quantidade)} {r.Unidade} {r.Nome}"
            : ""));
    }

    internal static string Numero(decimal valor) => valor == Math.Floor(valor)
        ? valor.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))
        : Moeda.FormatarQuantidade(valor);

    // ---- Cópia e arquivo ----

    internal RecursosDoProjeto CopiarPara(Cronograma cronograma)
    {
        var copia = new RecursosDoProjeto(cronograma);
        copia._recursos.AddRange(_recursos); // recursos são imutáveis
        return copia;
    }

    /// <summary>Os recursos lidos do arquivo (antes das atribuições das tarefas).</summary>
    /// <exception cref="DomainValidationException">Id ou nome repetido.</exception>
    internal void Restaurar(IEnumerable<Recurso> recursos)
    {
        _recursos.Clear();
        foreach (var recurso in recursos)
            Adicionar(recurso);
    }
}
