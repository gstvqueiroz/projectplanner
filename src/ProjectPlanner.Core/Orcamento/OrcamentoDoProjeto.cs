using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Orcamento;

/// <summary>Bloco 33: o BDI do orçamento (sobre o custo direto) e, se diferente, o BDI dos materiais e serviços fornecidos.</summary>
public sealed record ConfiguracaoDoOrcamento
{
    public static ConfiguracaoDoOrcamento Padrao { get; } = new();

    /// <summary>O BDI em % (ex.: 25).</summary>
    public decimal Bdi { get; init; }

    /// <summary>O BDI dos materiais e serviços (fornecimento), em %; nulo = o mesmo BDI.</summary>
    public decimal? BdiDeMateriais { get; init; }

    /// <exception cref="DomainValidationException">BDI negativo ou acima de 100%.</exception>
    public void Validar()
    {
        if (Bdi is < 0 or > 100 || BdiDeMateriais is < 0 or > 100)
            throw new DomainValidationException(nameof(Bdi), "O BDI vai de 0% a 100%.");
    }
}

/// <summary>Bloco 33: uma linha da planilha orçamentária (um serviço, uma tarefa com valor informado, ou um grupo).</summary>
/// <param name="SemComposicao">Uma tarefa sem composição: o preço é o Valor informado.</param>
public sealed record LinhaDoOrcamento(Guid? TarefaId, string Edt, string Descricao, int Nivel, bool EhGrupo, string Codigo, string Fonte, string Unidade,
    decimal? Quantidade, decimal? CustoUnitario, decimal? PrecoUnitario, decimal CustoTotal, decimal Total, bool SemComposicao = false);

/// <summary>Bloco 33: a planilha orçamentária: as linhas na ordem da EDT e os totais.</summary>
public sealed record PlanilhaOrcamentaria(IReadOnlyList<LinhaDoOrcamento> Linhas, ConfiguracaoDoOrcamento Configuracao, IReadOnlyList<string> Bases)
{
    public IEnumerable<LinhaDoOrcamento> Servicos => Linhas.Where(l => !l.EhGrupo);

    /// <summary>O custo direto dos serviços com composição.</summary>
    public decimal CustoDireto => Servicos.Where(l => !l.SemComposicao).Sum(l => l.CustoTotal);

    /// <summary>O BDI em R$ (preço − custo, nos serviços com composição).</summary>
    public decimal ValorDoBdi => Servicos.Where(l => !l.SemComposicao).Sum(l => l.Total - l.CustoTotal);

    /// <summary>O preço das tarefas sem composição (o Valor informado).</summary>
    public decimal Informado => Servicos.Where(l => l.SemComposicao).Sum(l => l.Total);

    public decimal Total => Servicos.Sum(l => l.Total);
}

/// <summary>O que aconteceu ao aplicar o orçamento às tarefas.</summary>
public sealed record ResultadoDaAplicacaoDoOrcamento(int Tarefas, int RecursosNovos, int Atribuicoes, IReadOnlyList<string> Avisos)
{
    /// <summary>"6 tarefas com o valor do orçamento; 12 recursos novos e 31 atribuições. Avisos: …".</summary>
    public string Resumo => $"{(Tarefas == 1 ? "1 tarefa" : $"{Tarefas} tarefas")} com o valor do orçamento; " +
                            $"{(RecursosNovos == 1 ? "1 recurso novo" : $"{RecursosNovos} recursos novos")} e {(Atribuicoes == 1 ? "1 atribuição" : $"{Atribuicoes} atribuições")}." +
                            (Avisos.Count == 0 ? "" : $" Avisos: {string.Join("; ", Avisos.Take(6))}{(Avisos.Count > 6 ? "…" : "")}.");
}

/// <summary>
/// Bloco 33: o orçamento do projeto: a composição (aberta, com os preços da base) de cada tarefa e o BDI. O preço unitário
/// é o custo de mão de obra e equipamentos × (1 + BDI) mais o de materiais e serviços × (1 + BDI de materiais); o total é
/// a quantidade da tarefa × o preço. Aplicar põe o total no Valor da tarefa (a unidade também, se ela não tem) e transforma
/// os insumos em recursos atribuídos (mão de obra e equipamento: as horas ÷ 8 ÷ os dias da tarefa, por dia). Volta no Desfazer.
/// </summary>
public sealed class OrcamentoDoProjeto
{
    /// <summary>Horas de trabalho num dia (para converter as horas da composição em dias de equipe).</summary>
    public const decimal HorasPorDia = 8;

    private readonly Cronograma _cronograma;
    private readonly Dictionary<Guid, ComposicaoAberta> _servicos = [];

    internal OrcamentoDoProjeto(Cronograma cronograma) => _cronograma = cronograma;

    public ConfiguracaoDoOrcamento Configuracao { get; private set; } = ConfiguracaoDoOrcamento.Padrao;

    /// <summary>A composição de cada tarefa que tem uma.</summary>
    public IReadOnlyDictionary<Guid, ComposicaoAberta> Servicos => _servicos;

    public bool Vazio => _servicos.Count == 0 && Configuracao.Bdi == 0 && Configuracao.BdiDeMateriais is null;

    /// <exception cref="DomainValidationException">BDI fora de 0 a 100%.</exception>
    public void DefinirConfiguracao(ConfiguracaoDoOrcamento configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        configuracao.Validar();
        Configuracao = configuracao;
    }

    /// <summary>Põe (ou troca) a composição da tarefa.</summary>
    /// <exception cref="DomainValidationException">Tarefa resumo ou de outro projeto.</exception>
    public void Associar(Tarefa tarefa, ComposicaoAberta composicao)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        ArgumentNullException.ThrowIfNull(composicao);
        if (!_cronograma.Tarefas.Contains(tarefa))
            throw new DomainValidationException(nameof(Servicos), "A tarefa não é deste projeto.");
        if (tarefa.EhResumo)
            throw new DomainValidationException(nameof(Servicos), $"\"{tarefa.Nome}\" é um resumo: a composição vai nas tarefas dentro dele.");
        _servicos[tarefa.Id] = composicao;
    }

    public void Desassociar(Guid tarefaId) => _servicos.Remove(tarefaId);

    public ComposicaoAberta? ComposicaoDe(Guid tarefaId)
    {
        // Uma tarefa que virou resumo ou saiu do projeto não conta mais.
        return _servicos.TryGetValue(tarefaId, out var c) && _cronograma.Tarefas.FirstOrDefault(t => t.Id == tarefaId) is { EhResumo: false } ? c : null;
    }

    /// <summary>O preço unitário com BDI (em centavos).</summary>
    public decimal PrecoUnitario(ComposicaoAberta composicao)
    {
        ArgumentNullException.ThrowIfNull(composicao);
        var bdi = Configuracao.Bdi / 100;
        var bdiMateriais = (Configuracao.BdiDeMateriais ?? Configuracao.Bdi) / 100;
        return Math.Round(composicao.CustoDeMaoDeObraEEquipamentos * (1 + bdi) + composicao.CustoDeMateriais * (1 + bdiMateriais), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>A planilha orçamentária (sintética), na ordem da EDT, com os grupos somando.</summary>
    public PlanilhaOrcamentaria Planilha()
    {
        var edt = _cronograma.NumeracaoEdt();
        var niveis = new Dictionary<Guid, int>();
        foreach (var t in _cronograma.Tarefas)
            niveis[t.Id] = t.TarefaPaiId is { } pai ? niveis[pai] + 1 : 0;
        var porId = _cronograma.Tarefas.ToDictionary(t => t.Id);
        var doServico = new Dictionary<Guid, LinhaDoOrcamento>();
        foreach (var tarefa in _cronograma.Tarefas.Where(t => !t.EhResumo))
        {
            if (ComposicaoDe(tarefa.Id) is { } c)
            {
                var q = tarefa.Quantidade ?? 0;
                var preco = PrecoUnitario(c);
                doServico[tarefa.Id] = new LinhaDoOrcamento(tarefa.Id, edt[tarefa.Id], tarefa.Nome, niveis[tarefa.Id], false, c.Codigo, c.Base.Fonte,
                    c.Unidade, tarefa.Quantidade, c.CustoUnitario, preco, Math.Round(q * c.CustoUnitario, 2, MidpointRounding.AwayFromZero),
                    Math.Round(q * preco, 2, MidpointRounding.AwayFromZero));
            }
            else if (tarefa.Valor is > 0 and var valor)
                doServico[tarefa.Id] = new LinhaDoOrcamento(tarefa.Id, edt[tarefa.Id], tarefa.Nome, niveis[tarefa.Id], false, "", "", tarefa.Unidade ?? "",
                    tarefa.Quantidade, null, tarefa.PrecoUnitario is { } p ? Math.Round(p, 2) : null, valor, valor, SemComposicao: true);
        }
        var somas = new Dictionary<Guid, (decimal Custo, decimal Total)>();
        foreach (var linha in doServico.Values)
            for (var pai = porId[linha.TarefaId!.Value].TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
            {
                var (c, t) = somas.GetValueOrDefault(id);
                somas[id] = (c + linha.CustoTotal, t + linha.Total);
            }
        var linhas = new List<LinhaDoOrcamento>();
        foreach (var tarefa in _cronograma.Tarefas)
            if (doServico.TryGetValue(tarefa.Id, out var linha))
                linhas.Add(linha);
            else if (somas.TryGetValue(tarefa.Id, out var soma))
                linhas.Add(new LinhaDoOrcamento(tarefa.Id, edt[tarefa.Id], tarefa.Nome, niveis[tarefa.Id], true, "", "", "", null, null, null, soma.Custo, soma.Total));
        return new PlanilhaOrcamentaria(linhas, Configuracao,
            [.. _servicos.Where(s => ComposicaoDe(s.Key) is not null).Select(s => s.Value.Base.Descricao).Distinct().Order()]);
    }

    /// <summary>
    /// Aplica o orçamento às tarefas com composição: o Valor (quantidade × preço com BDI), a unidade (se a tarefa não tem) e os
    /// insumos como recursos atribuídos (os recursos de mesmo nome e tipo são reaproveitados, com o custo da base; as outras
    /// atribuições da tarefa ficam).
    /// </summary>
    /// <exception cref="DomainValidationException">O contrato está travado (há medição aprovada): os valores mudam por aditivo.</exception>
    public ResultadoDaAplicacaoDoOrcamento Aplicar()
    {
        if (_cronograma.Contrato.Travado)
            throw new DomainValidationException(nameof(Servicos),
                "O contrato já tem medição aprovada: o orçamento não muda mais o Valor das tarefas (as mudanças vão por aditivo).");
        var edt = _cronograma.NumeracaoEdt();
        var avisos = new List<string>();
        var (tarefas, novos, atribuicoes) = (0, 0, 0);
        foreach (var tarefa in _cronograma.Tarefas.Where(t => !t.EhResumo))
        {
            if (ComposicaoDe(tarefa.Id) is not { } c)
                continue;
            var nome = $"{edt[tarefa.Id]} {tarefa.Nome}";
            if (tarefa.Quantidade is not { } quantidade)
            {
                avisos.Add($"{nome}: sem quantidade (preencha a coluna Quantidade)");
                continue;
            }
            if (tarefa.Unidade is null)
                tarefa.DefinirUnidade(c.Unidade.Length > Tarefa.TamanhoMaximoDaUnidade ? c.Unidade[..Tarefa.TamanhoMaximoDaUnidade] : c.Unidade);
            else if (MesmaUnidade(tarefa.Unidade, c.Unidade) is false)
                avisos.Add($"{nome}: a unidade da tarefa ({tarefa.Unidade}) é diferente da composição ({c.Unidade})");
            tarefa.DefinirValor(Math.Round(quantidade * PrecoUnitario(c), 2, MidpointRounding.AwayFromZero));
            tarefas++;

            var lista = tarefa.Atribuicoes.ToList();
            foreach (var insumo in c.Insumos)
            {
                var (recurso, novo) = Recurso(insumo, c.Base);
                novos += novo ? 1 : 0;
                var total = insumo.Coeficiente * quantidade;
                decimal porTarefa;
                if (recurso.PorDia)
                {
                    if (tarefa.Duracao == 0)
                    {
                        avisos.Add($"{nome}: um marco não tem dias para {recurso.Nome}");
                        continue;
                    }
                    porTarefa = total / HorasPorDia / tarefa.Duracao;
                }
                else
                    porTarefa = total;
                porTarefa = Math.Round(porTarefa, 4, MidpointRounding.AwayFromZero);
                if (porTarefa <= 0)
                    continue;
                lista.RemoveAll(a => a.RecursoId == recurso.Id);
                lista.Add(new AtribuicaoDeRecurso(recurso.Id, porTarefa));
                atribuicoes++;
            }
            _cronograma.Recursos.Atribuir(tarefa, lista);
        }
        return new ResultadoDaAplicacaoDoOrcamento(tarefas, novos, atribuicoes, avisos);
    }

    // O recurso do insumo: o de mesmo nome e tipo no projeto (com o custo atualizado) ou um novo. Mão de obra e
    // equipamento custam por dia (o preço da hora × 8).
    private (Recurso Recurso, bool Novo) Recurso(InsumoNaComposicao insumo, BaseDeReferencia @base)
    {
        var tipo = TiposDeInsumo.Recurso(insumo.Tipo);
        var nome = insumo.Descricao.Length == 0 ? $"{@base.Fonte} {insumo.Codigo}" : insumo.Descricao.Trim();
        if (nome.Length > Recursos.Recurso.TamanhoMaximoDoNome)
            nome = nome[..(Recursos.Recurso.TamanhoMaximoDoNome - 1)].TrimEnd() + "…";
        var custo = TiposDeRecurso.PorDia(tipo) ? insumo.Preco * HorasPorDia : insumo.Preco;
        var unidade = insumo.Unidade.Length > Recursos.Recurso.TamanhoMaximoDaUnidade ? insumo.Unidade[..Recursos.Recurso.TamanhoMaximoDaUnidade] : insumo.Unidade;
        var existente = _cronograma.Recursos.Lista.FirstOrDefault(r => r.Tipo == tipo && string.Equals(r.Nome, nome, StringComparison.CurrentCultureIgnoreCase));
        var recurso = new Recurso(existente?.Id ?? Guid.NewGuid(), nome, tipo, unidade, custo, existente?.Disponivel, $"{@base.Fonte} {insumo.Codigo}");
        if (existente is null)
            _cronograma.Recursos.Adicionar(recurso);
        else
            _cronograma.Recursos.Substituir(recurso);
        return (recurso, existente is null);
    }

    // "m²" e "M2" são a mesma unidade; nulo se uma das duas está vazia.
    private static bool? MesmaUnidade(string a, string b)
    {
        static string Limpa(string u) => TiposDeInsumo.Normalizar(u).Replace("²", "2").Replace("³", "3").Replace(" ", "").Replace(".", "");
        return a.Length == 0 || b.Length == 0 ? null : Limpa(a) == Limpa(b);
    }

    /// <summary>Atualiza as composições do orçamento pelos preços (e coeficientes) de outra base com os mesmos códigos.</summary>
    /// <returns>Quantas foram atualizadas e os códigos que a base nova não tem (esses ficam como estavam).</returns>
    public (int Atualizadas, IReadOnlyList<string> NaoEncontradas) AtualizarPrecos(TabelaDeReferencia tabela)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        var (atualizadas, faltando) = (0, new List<string>());
        foreach (var (tarefaId, composicao) in _servicos.ToList())
        {
            if (tabela.Composicao(composicao.Codigo) is null)
            {
                faltando.Add(composicao.Codigo);
                continue;
            }
            try
            {
                _servicos[tarefaId] = tabela.Abrir(composicao.Codigo);
                atualizadas++;
            }
            catch (DomainValidationException)
            {
                faltando.Add(composicao.Codigo);
            }
        }
        return (atualizadas, faltando);
    }

    // ---- Cópia e arquivo ----

    internal OrcamentoDoProjeto CopiarPara(Cronograma cronograma)
    {
        var copia = new OrcamentoDoProjeto(cronograma) { Configuracao = Configuracao };
        foreach (var (id, c) in _servicos)
            copia._servicos[id] = c; // composições abertas são imutáveis
        return copia;
    }

    /// <summary>O orçamento lido do arquivo (as tarefas que não existem mais ficam de fora).</summary>
    internal void Restaurar(ConfiguracaoDoOrcamento configuracao, IEnumerable<(Guid TarefaId, ComposicaoAberta Composicao)> servicos)
    {
        DefinirConfiguracao(configuracao);
        _servicos.Clear();
        var ids = _cronograma.Tarefas.Select(t => t.Id).ToHashSet();
        foreach (var (tarefaId, composicao) in servicos)
            if (ids.Contains(tarefaId))
                _servicos[tarefaId] = composicao;
    }
}
