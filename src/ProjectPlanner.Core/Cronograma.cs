using ProjectPlanner.Core.Assistente;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Core;

/// <summary>
/// O projeto: suas tarefas, a estrutura delas (resumos e subtarefas) e o grafo de dependências entre elas.
/// </summary>
/// <remarks>
/// As dependências ficam numa lista única de arestas (<see cref="Predecessora"/>), e não dentro de
/// cada <see cref="Tarefa"/>, para que todo vínculo passe pelas regras do grafo:
/// <list type="bullet">
/// <item>as duas tarefas precisam pertencer ao cronograma;</item>
/// <item>só existe um vínculo por par (predecessora, sucessora); para mudar tipo ou lag, remova e adicione de novo;</item>
/// <item>nenhum vínculo pode fechar um ciclo;</item>
/// <item>remover uma tarefa remove junto os vínculos dela, sem religar as vizinhas (como no MS Project);</item>
/// <item>resumos não têm vínculos: o caminho crítico passa pelas subtarefas.</item>
/// </list>
/// <para>
/// A estrutura fica em <see cref="Tarefa.TarefaPaiId"/>, e a lista <see cref="Tarefas"/> está sempre em
/// pré-ordem: cada tarefa vem seguida de todas as suas subtarefas, em qualquer nível (como a lista de
/// tópicos do MS Project). Uma tarefa é resumo enquanto tem subtarefas; marcos não podem ter
/// subtarefas; e uma tarefa com vínculos não pode virar resumo. Não há limite de níveis.
/// </para>
/// Toda violação lança <see cref="DomainValidationException"/> e deixa o cronograma inalterado.
/// </remarks>
public class Cronograma
{
    private readonly List<Tarefa> _tarefas = [];
    private readonly List<Predecessora> _predecessoras = [];

    public Cronograma(string nome, DateTime dataInicio)
        : this(Guid.NewGuid(), nome, dataInicio)
    {
    }

    /// <summary>Reconstrói um projeto já existente, mantendo o Id (por exemplo, ao abrir um arquivo).</summary>
    public Cronograma(Guid id, string nome, DateTime dataInicio)
    {
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id do projeto é obrigatório.");

        Id = id;
        Nome = ValidarNome(nome);
        DataInicio = NormalizarInicio(dataInicio);
    }

    public Guid Id { get; }

    public string Nome { get; private set; }

    /// <summary>Quando o projeto foi salvo pela primeira vez (UTC); nulo se nunca foi salvo.</summary>
    public DateTime? DataCriacao { get; private set; }

    /// <summary>Quando o projeto foi salvo pela última vez (UTC); nulo se nunca foi salvo.</summary>
    public DateTime? DataUltimaModificacao { get; private set; }

    /// <summary>
    /// Início do projeto: no cálculo do cronograma, nenhuma tarefa começa antes dele. Como nas
    /// tarefas, o horário é descartado e um fim de semana vai para a segunda-feira seguinte.
    /// </summary>
    public DateTime DataInicio { get; private set; }

    /// <summary>
    /// Tarefas na ordem da estrutura (pré-ordem): cada tarefa seguida de todas as suas subtarefas. É a
    /// ordem das linhas da grid e o número (Nº) usado nas predecessoras.
    /// </summary>
    public IReadOnlyList<Tarefa> Tarefas => _tarefas.AsReadOnly();

    /// <summary>Todos os vínculos, na ordem em que foram adicionados.</summary>
    public IReadOnlyCollection<Predecessora> Predecessoras => _predecessoras.AsReadOnly();

    public void Renomear(string nome) => Nome = ValidarNome(nome);

    /// <summary>Muda o início do projeto. As datas planejadas das tarefas não mudam; recalcule o cronograma.</summary>
    public void AlterarDataInicio(DateTime dataInicio) => DataInicio = NormalizarInicio(dataInicio);

    /// <summary>Adiciona a tarefa no fim da lista, no primeiro nível.</summary>
    public void AdicionarTarefa(Tarefa tarefa) => AdicionarTarefa(tarefa, paiId: null);

    /// <summary>
    /// Adiciona a tarefa no fim da lista (sem pai) ou como última subtarefa de <paramref name="paiId"/>,
    /// que passa a ser um resumo.
    /// </summary>
    public void AdicionarTarefa(Tarefa tarefa, Guid? paiId)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        if (_tarefas.Exists(t => t.Id == tarefa.Id))
            throw new DomainValidationException(nameof(Tarefas),
                $"A tarefa \"{tarefa.Nome}\" já pertence ao cronograma.");
        if (paiId == tarefa.Id)
            throw SubtarefaDeSiMesma();

        var pai = paiId is { } id ? ObterTarefa(id, nameof(Tarefa.TarefaPaiId)) : null;
        if (pai is not null)
            ValidarPodeTerSubtarefas(pai);

        _tarefas.Insert(pai is null ? _tarefas.Count : FimDaSubarvore(pai), tarefa);
        tarefa.DefinirPai(pai?.Id);
        tarefa.DefinirSeEhResumo(false);
        pai?.DefinirSeEhResumo(true);
    }

    /// <summary>
    /// Remove a tarefa, as subtarefas dela (em todos os níveis) e todos os vínculos dessas tarefas.
    /// Se o resumo em que ela estava ficar sem subtarefas, ele volta a ser tarefa comum (ou marco).
    /// </summary>
    public void RemoverTarefa(Guid tarefaId)
    {
        var tarefa = ObterTarefa(tarefaId, nameof(Tarefas));
        var removidas = RetirarSubarvore(tarefa).Select(t => t.Id).ToHashSet();
        _predecessoras.RemoveAll(p => removidas.Contains(p.TarefaOrigemId) || removidas.Contains(p.TarefaDestinoId));
        AtualizarSeEhResumo(tarefa.TarefaPaiId);
    }

    /// <summary>
    /// Recua a tarefa um nível: ela passa a ser a última subtarefa da tarefa de cima que está no mesmo
    /// nível (que vira resumo), levando junto as próprias subtarefas. A ordem das linhas não muda.
    /// </summary>
    public void Rebaixar(Guid tarefaId)
    {
        var tarefa = ObterTarefa(tarefaId, nameof(Tarefas));
        var irma = ResumoAoRebaixar(tarefa);

        tarefa.DefinirPai(irma.Id);
        irma.DefinirSeEhResumo(true);
    }

    /// <summary>
    /// Acrescenta a tarefa como irmã das tarefas de <paramref name="paiId"/> (nulo = primeiro nível), na posição
    /// <paramref name="posicao"/> entre elas, contando a partir de 0: 0 é antes da primeira e a quantidade de
    /// irmãs é depois da última (Bloco 21). As irmãs dessa posição em diante andam uma posição, com as subtarefas;
    /// a ordem é a da lista (a coluna Ordem do arquivo sai dela), e a EDT acompanha.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// A posição não existe entre as irmãs, ou a tarefa não pode entrar ali (as mesmas regras de
    /// <see cref="AdicionarTarefa(Tarefa, Guid?)"/>).
    /// </exception>
    public void InserirTarefaNaPosicao(Tarefa tarefa, Guid? paiId, int posicao)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        var irmas = _tarefas.Count(t => t.TarefaPaiId == paiId);
        if (posicao < 0 || posicao > irmas)
            throw new DomainValidationException(nameof(Tarefas),
                $"A posição {posicao} não existe: o grupo tem {irmas} tarefa(s), então vai de 0 a {irmas}.");

        AdicionarTarefa(tarefa, paiId);
        MoverEntreIrmas(tarefa.Id, posicao);
    }

    /// <summary>
    /// Muda a posição da tarefa entre as irmãs (as tarefas com o mesmo pai), levando junto as subtarefas
    /// dela; o pai não muda (Bloco 18). <paramref name="posicao"/> é o lugar que ela passa a ocupar entre
    /// as irmãs, contando a partir de 0. Só a ordem muda: datas, vínculos e caminho crítico não dependem
    /// dela (a EDT, sim, é a da nova ordem).
    /// </summary>
    /// <returns>Se a ordem mudou (falso quando a tarefa já está nessa posição).</returns>
    /// <exception cref="DomainValidationException">A tarefa não pertence ao cronograma.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A posição não existe entre as irmãs.</exception>
    public bool MoverEntreIrmas(Guid tarefaId, int posicao)
    {
        var tarefa = ObterTarefa(tarefaId, nameof(Tarefas));
        var irmas = _tarefas.Where(t => t.TarefaPaiId == tarefa.TarefaPaiId).ToList();
        ArgumentOutOfRangeException.ThrowIfNegative(posicao);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(posicao, irmas.Count);
        if (irmas.IndexOf(tarefa) == posicao)
            return false;

        var subarvore = RetirarSubarvore(tarefa);
        var restantes = irmas.Where(t => t != tarefa).ToList();
        var onde = posicao < restantes.Count ? _tarefas.IndexOf(restantes[posicao]) : FimDaSubarvore(restantes[^1]);
        _tarefas.InsertRange(onde, subarvore);
        return true;
    }

    /// <summary>
    /// Se <see cref="Rebaixar"/> aceitaria a tarefa agora (há tarefa acima no mesmo nível, e ela pode ter
    /// subtarefas), sem mudar nada. Falso também para uma tarefa que não está no cronograma.
    /// </summary>
    public bool PodeRebaixar(Guid tarefaId)
    {
        var tarefa = _tarefas.Find(t => t.Id == tarefaId);
        if (tarefa is null)
            return false;
        try
        {
            ResumoAoRebaixar(tarefa);
            return true;
        }
        catch (DomainValidationException)
        {
            return false;
        }
    }

    // A regra do rebaixar, usada por Rebaixar e PodeRebaixar: a tarefa de cima no mesmo nível, que vai
    // ser o resumo, ou a exceção com o motivo de não dar.
    private Tarefa ResumoAoRebaixar(Tarefa tarefa)
    {
        var indice = _tarefas.IndexOf(tarefa);
        // Em pré-ordem, a irmã de cima é a última tarefa antes desta com o mesmo pai.
        var irma = _tarefas.Take(indice).LastOrDefault(t => t.TarefaPaiId == tarefa.TarefaPaiId)
            ?? throw new DomainValidationException(nameof(Tarefa.TarefaPaiId),
                $"Não há tarefa acima de \"{tarefa.Nome}\" no mesmo nível para ser o resumo dela.");
        ValidarPodeTerSubtarefas(irma);
        return irma;
    }

    /// <summary>
    /// Sobe a tarefa um nível: ela sai do resumo em que está, com as próprias subtarefas, e vai para
    /// logo depois dele, no nível dele. As tarefas abaixo dela continuam no resumo. Se o resumo ficar
    /// sem subtarefas, ele volta a ser tarefa comum (ou marco).
    /// </summary>
    public void Promover(Guid tarefaId)
    {
        var tarefa = ObterTarefa(tarefaId, nameof(Tarefas));
        var pai = tarefa.TarefaPaiId is { } paiId
            ? ObterTarefa(paiId, nameof(Tarefa.TarefaPaiId))
            : throw new DomainValidationException(nameof(Tarefa.TarefaPaiId),
                $"\"{tarefa.Nome}\" já está no primeiro nível.");

        var subarvore = RetirarSubarvore(tarefa);
        _tarefas.InsertRange(FimDaSubarvore(pai), subarvore);
        tarefa.DefinirPai(pai.TarefaPaiId);
        AtualizarSeEhResumo(pai.Id);
    }

    /// <summary>
    /// Move a tarefa, com as próprias subtarefas, para ser a última subtarefa de
    /// <paramref name="novoPaiId"/> (que vira resumo), ou para o fim da lista, no primeiro nível, quando
    /// ele é nulo. Se o resumo de onde ela saiu ficar sem subtarefas, ele volta a ser tarefa comum.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// O novo pai é a própria tarefa ou uma subtarefa dela (a estrutura teria um ciclo), é um marco, ou
    /// tem vínculos e viraria resumo.
    /// </exception>
    public void DefinirPai(Guid tarefaId, Guid? novoPaiId)
    {
        var tarefa = ObterTarefa(tarefaId, nameof(Tarefas));
        Tarefa? novoPai = null;
        if (novoPaiId is { } id)
        {
            if (id == tarefaId)
                throw SubtarefaDeSiMesma();
            novoPai = ObterTarefa(id, nameof(Tarefa.TarefaPaiId));
            if (EhDescendente(novoPai, tarefa))
                throw new DomainValidationException(nameof(Tarefa.TarefaPaiId),
                    $"\"{novoPai.Nome}\" é subtarefa de \"{tarefa.Nome}\"; colocar \"{tarefa.Nome}\" dentro dela " +
                    "criaria um ciclo na estrutura.");
            ValidarPodeTerSubtarefas(novoPai);
        }

        var paiAnterior = tarefa.TarefaPaiId;
        var subarvore = RetirarSubarvore(tarefa);
        _tarefas.InsertRange(novoPai is null ? _tarefas.Count : FimDaSubarvore(novoPai), subarvore);
        tarefa.DefinirPai(novoPai?.Id);
        novoPai?.DefinirSeEhResumo(true);
        AtualizarSeEhResumo(paiAnterior);
    }

    /// <summary>Subtarefas diretas da tarefa, na ordem da lista.</summary>
    public IReadOnlyList<Tarefa> FilhasDe(Guid tarefaId)
    {
        ObterTarefa(tarefaId, nameof(Tarefas));
        return _tarefas.Where(t => t.TarefaPaiId == tarefaId).ToList();
    }

    /// <summary>Todas as subtarefas da tarefa, em qualquer nível, na ordem da lista.</summary>
    public IReadOnlyList<Tarefa> DescendentesDe(Guid tarefaId)
    {
        var tarefa = ObterTarefa(tarefaId, nameof(Tarefas));
        var inicio = _tarefas.IndexOf(tarefa) + 1;
        return _tarefas.GetRange(inicio, FimDaSubarvore(tarefa) - inicio);
    }

    /// <summary>Profundidade da tarefa na estrutura: 0 no primeiro nível, 1 numa subtarefa, e assim por diante.</summary>
    public int NivelDe(Guid tarefaId)
    {
        var tarefa = ObterTarefa(tarefaId, nameof(Tarefas));
        var nivel = 0;
        while (tarefa.TarefaPaiId is { } paiId)
        {
            nivel++;
            tarefa = _tarefas.Find(t => t.Id == paiId)!;
        }
        return nivel;
    }

    /// <summary>Código da tarefa na EDT (estrutura analítica do projeto): 1, 1.1, 1.2, 1.2.1, 2...</summary>
    public string EdtDe(Guid tarefaId)
    {
        ObterTarefa(tarefaId, nameof(Tarefas));
        return NumeracaoEdt()[tarefaId];
    }

    /// <summary>
    /// Código de todas as tarefas na EDT: as do primeiro nível são numeradas 1, 2, 3... na ordem da
    /// lista, e as subtarefas de uma tarefa de código c são c.1, c.2... Depende só da estrutura.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> NumeracaoEdt()
    {
        var codigos = new Dictionary<Guid, string>(_tarefas.Count);
        var ultimaNumeracao = new Dictionary<Guid, int>(); // por pai; Guid.Empty = primeiro nível
        foreach (var tarefa in _tarefas)
        {
            var pai = tarefa.TarefaPaiId ?? Guid.Empty;
            var numero = ultimaNumeracao.GetValueOrDefault(pai) + 1;
            ultimaNumeracao[pai] = numero;
            codigos[tarefa.Id] = tarefa.TarefaPaiId is { } paiId
                ? $"{codigos[paiId]}.{numero}"
                : numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return codigos;
    }

    /// <exception cref="CircularDependencyException">O vínculo fecharia um ciclo.</exception>
    /// <exception cref="DomainValidationException">
    /// Alguma das tarefas não pertence ao cronograma, ou já existe vínculo entre elas.
    /// </exception>
    public void AdicionarPredecessora(Predecessora predecessora)
    {
        ValidarPredecessora(predecessora);
        _predecessoras.Add(predecessora);
    }

    /// <summary>
    /// Verifica se o vínculo pode ser adicionado, com as mesmas regras e mensagens de
    /// <see cref="AdicionarPredecessora"/>, sem mudar nada (por exemplo, para avisar durante um arraste).
    /// </summary>
    /// <exception cref="CircularDependencyException">O vínculo fecharia um ciclo.</exception>
    /// <exception cref="DomainValidationException">
    /// Alguma das tarefas não pertence ao cronograma, é um resumo, ou já existe vínculo entre elas.
    /// </exception>
    public void ValidarPredecessora(Predecessora predecessora)
    {
        ArgumentNullException.ThrowIfNull(predecessora);
        var origem = ObterTarefa(predecessora.TarefaOrigemId, nameof(Predecessora.TarefaOrigemId));
        var destino = ObterTarefa(predecessora.TarefaDestinoId, nameof(Predecessora.TarefaDestinoId));
        foreach (var ponta in new[] { origem, destino })
            if (ponta.EhResumo)
                throw new DomainValidationException(nameof(Predecessoras),
                    $"\"{ponta.Nome}\" é um resumo, e resumos não têm predecessoras nem sucessoras. " +
                    "Vincule as subtarefas dele.");

        if (Encontrar(origem.Id, destino.Id) is not null)
            throw new DomainValidationException(nameof(Predecessoras),
                $"Já existe um vínculo de \"{origem.Nome}\" para \"{destino.Nome}\".");

        // O vínculo origem → destino fecha um ciclo se a origem já é alcançável a partir do destino.
        if (Caminho(destino.Id, origem.Id) is { } caminho)
        {
            var ciclo = caminho.Prepend(origem.Id).ToList();
            var nomes = string.Join(" → ", ciclo.Select(id => _tarefas.Find(t => t.Id == id)!.Nome));
            throw new CircularDependencyException(ciclo,
                $"\"{origem.Nome}\" não pode ser predecessora de \"{destino.Nome}\": " +
                $"isso criaria a dependência circular {nomes}.");
        }
    }

    public void RemoverPredecessora(Guid tarefaOrigemId, Guid tarefaDestinoId)
    {
        var predecessora = Encontrar(tarefaOrigemId, tarefaDestinoId)
            ?? throw new DomainValidationException(nameof(Predecessoras),
                "Não existe vínculo entre as tarefas informadas.");

        _predecessoras.Remove(predecessora);
    }

    /// <summary>
    /// Vínculos em que a tarefa é a sucessora, na ordem em que foram adicionados.
    /// </summary>
    public IReadOnlyList<Predecessora> PredecessorasDe(Guid tarefaId)
    {
        ObterTarefa(tarefaId, nameof(Tarefas));
        return _predecessoras.Where(p => p.TarefaDestinoId == tarefaId).ToList();
    }

    /// <summary>Usado pela persistência ao salvar e ao abrir um arquivo.</summary>
    /// <summary>
    /// Registros de foto sem imagem (os de exemplo das versões 0.14 a 0.21) que foram descartados ao abrir o
    /// arquivo; somem dele no próximo salvamento (Bloco 22, D6).
    /// </summary>
    public int FotosDescartadasAoAbrir { get; private set; }

    internal void RegistrarFotosDescartadas(int quantidade) => FotosDescartadasAoAbrir = quantidade;

    /// <summary>
    /// A conversa com o assistente e a ficha do projeto (Bloco 26). É do projeto: as cópias do cronograma usam a mesma.
    /// </summary>
    public ConversaDoProjeto Assistente { get; private set; } = new();

    /// <summary>A conversa guardada no arquivo não pôde ser lida (formato de outra versão ou com defeito) e ficou de fora.</summary>
    public bool ConversaDescartadaAoAbrir { get; private set; }

    internal void DefinirAssistente(ConversaDoProjeto conversa) => Assistente = conversa ?? throw new ArgumentNullException(nameof(conversa));

    internal void RegistrarConversaDescartada() => ConversaDescartadaAoAbrir = true;

    /// <summary>Bloco 27: contratante, contrato e endereço da obra, para a capa do relatório fotográfico.</summary>
    public DadosDaObra Obra { get; private set; } = DadosDaObra.Vazios;

    public void DefinirObra(DadosDaObra obra) => Obra = obra ?? throw new ArgumentNullException(nameof(obra));

    /// <summary>Bloco 28: os locais padronizados das fotos e o alerta de tarefa sem foto recente.</summary>
    public Fotos.ConfiguracaoDasFotos ConfiguracaoDasFotos { get; private set; } = Fotos.ConfiguracaoDasFotos.Padrao;

    public void DefinirConfiguracaoDasFotos(Fotos.ConfiguracaoDasFotos configuracao) =>
        ConfiguracaoDasFotos = configuracao ?? throw new ArgumentNullException(nameof(configuracao));

    private readonly List<RelatorioGerado> _relatorios = [];

    // ---- Bloco 31: linhas de base ----

    /// <summary>Quantas linhas de base um projeto guarda (como no MS Project, que guarda 11).</summary>
    public const int LinhasDeBaseMaximas = 20;

    private readonly List<Planejamento.LinhaDeBase> _linhasDeBase = [];

    /// <summary>As linhas de base salvas, da mais antiga (LB0) para a mais recente.</summary>
    public IReadOnlyList<Planejamento.LinhaDeBase> LinhasDeBase => _linhasDeBase.AsReadOnly();

    /// <summary>A linha de base com que a grid, o Gantt e a Curva S comparam; nula sem nenhuma.</summary>
    public Planejamento.LinhaDeBase? LinhaDeBaseEmUso { get; private set; }

    /// <summary>
    /// Salva o cronograma calculado como uma linha de base nova (LB0, LB1…), que passa a ser a em uso.
    /// </summary>
    /// <exception cref="DomainValidationException">Sem tarefas, não calculado, ou com o máximo de linhas de base.</exception>
    public Planejamento.LinhaDeBase SalvarLinhaDeBase(string? nome, DateTime agora)
    {
        if (_tarefas.Count == 0)
            throw new DomainValidationException(nameof(LinhasDeBase), "O projeto ainda não tem tarefas para guardar na linha de base.");
        if (_tarefas.Exists(t => t.InicioMaisCedo is null))
            throw new DomainValidationException(nameof(LinhasDeBase), "Calcule o cronograma antes de salvar a linha de base.");
        if (_linhasDeBase.Count >= LinhasDeBaseMaximas)
            throw new DomainValidationException(nameof(LinhasDeBase),
                $"O projeto já tem {LinhasDeBaseMaximas} linhas de base; remova uma antiga antes de salvar outra.");
        var numero = _linhasDeBase.Count == 0 ? 0 : _linhasDeBase.Max(l => l.Numero) + 1;
        var linha = new Planejamento.LinhaDeBase(numero, nome, agora, _tarefas.ToDictionary(t => t.Id,
            t => new Planejamento.TarefaNaLinhaDeBase(t.InicioMaisCedo!.Value, t.TerminoMaisCedo!.Value, t.Duracao, t.Valor)));
        _linhasDeBase.Add(linha);
        LinhaDeBaseEmUso = linha;
        return linha;
    }

    /// <summary>Põe uma linha de base guardada (ao abrir o arquivo) e diz se ela é a em uso.</summary>
    /// <exception cref="DomainValidationException">Número repetido.</exception>
    public void AdicionarLinhaDeBase(Planejamento.LinhaDeBase linha, bool emUso)
    {
        ArgumentNullException.ThrowIfNull(linha);
        if (_linhasDeBase.Exists(l => l.Numero == linha.Numero))
            throw new DomainValidationException(nameof(LinhasDeBase), $"Já existe a linha de base LB{linha.Numero}.");
        _linhasDeBase.Add(linha);
        _linhasDeBase.Sort((a, b) => a.Numero.CompareTo(b.Numero));
        if (emUso)
            LinhaDeBaseEmUso = linha;
    }

    /// <summary>Escolhe a linha de base em uso (nula: comparar com nenhuma).</summary>
    /// <exception cref="DomainValidationException">A linha de base não é deste projeto.</exception>
    public void UsarLinhaDeBase(int? numero)
    {
        LinhaDeBaseEmUso = numero is { } n
            ? _linhasDeBase.Find(l => l.Numero == n) ?? throw new DomainValidationException(nameof(LinhasDeBase), $"Não existe a linha de base LB{n}.")
            : null;
    }

    /// <summary>Remove uma linha de base; se era a em uso, passa a ser a mais recente que sobrou.</summary>
    /// <exception cref="DomainValidationException">A linha de base não é deste projeto.</exception>
    public void RemoverLinhaDeBase(int numero)
    {
        var linha = _linhasDeBase.Find(l => l.Numero == numero)
                    ?? throw new DomainValidationException(nameof(LinhasDeBase), $"Não existe a linha de base LB{numero}.");
        _linhasDeBase.Remove(linha);
        if (LinhaDeBaseEmUso == linha)
            LinhaDeBaseEmUso = _linhasDeBase.LastOrDefault();
    }

    /// <summary>Bloco 27: os relatórios fotográficos já gerados, do primeiro ao último.</summary>
    public IReadOnlyList<RelatorioGerado> Relatorios => _relatorios.AsReadOnly();

    /// <summary>O número do próximo relatório (1 no primeiro).</summary>
    public int ProximoNumeroDeRelatorio => _relatorios.Count == 0 ? 1 : _relatorios.Max(r => r.Numero) + 1;

    /// <exception cref="DomainValidationException">Número repetido.</exception>
    public void RegistrarRelatorio(RelatorioGerado relatorio)
    {
        ArgumentNullException.ThrowIfNull(relatorio);
        if (_relatorios.Exists(r => r.Numero == relatorio.Numero))
            throw new DomainValidationException(nameof(Relatorios), $"Já existe o relatório nº {relatorio.Numero}.");
        _relatorios.Add(relatorio);
    }

    /// <summary>
    /// Um cronograma igual a este, com os mesmos Ids, a mesma estrutura e ordem, os mesmos vínculos e as mesmas fotos,
    /// mas independente: mudar a cópia não muda o original (Bloco 24: as alterações do assistente são feitas numa
    /// cópia, e o original fica guardado para o Desfazer).
    /// </summary>
    public Cronograma Copiar()
    {
        var copia = new Cronograma(Id, Nome, DataInicio)
        {
            DataCriacao = DataCriacao,
            DataUltimaModificacao = DataUltimaModificacao,
            FotosDescartadasAoAbrir = FotosDescartadasAoAbrir,
            Assistente = Assistente, // a mesma conversa: ela é do projeto, não desta versão do cronograma
            ConversaDescartadaAoAbrir = ConversaDescartadaAoAbrir,
            Obra = Obra,
            ConfiguracaoDasFotos = ConfiguracaoDasFotos,
            LinhaDeBaseEmUso = LinhaDeBaseEmUso, // as linhas de base são imutáveis: a cópia usa as mesmas
        };
        copia._linhasDeBase.AddRange(_linhasDeBase);
        copia._relatorios.AddRange(_relatorios);
        copia._tarefas.AddRange(_tarefas.Select(t => t.Copiar()));
        copia._predecessoras.AddRange(_predecessoras); // vínculos são registros imutáveis
        return copia;
    }

    /// <summary>
    /// Bloco 30: o que não volta no Desfazer (a conversa, os dados da obra, os locais e o alerta das fotos e o
    /// histórico dos relatórios) passa a ser o de <paramref name="atual"/>; o resto continua o deste cronograma.
    /// </summary>
    internal void ManterDadosDoProjetoDe(Cronograma atual)
    {
        Assistente = atual.Assistente;
        ConversaDescartadaAoAbrir = atual.ConversaDescartadaAoAbrir;
        FotosDescartadasAoAbrir = atual.FotosDescartadasAoAbrir;
        Obra = atual.Obra;
        ConfiguracaoDasFotos = atual.ConfiguracaoDasFotos;
        _relatorios.Clear();
        _relatorios.AddRange(atual._relatorios);
    }

    internal void DefinirDatasDoArquivo(DateTime criacao, DateTime ultimaModificacao)
    {
        DataCriacao = criacao;
        DataUltimaModificacao = ultimaModificacao;
    }

    private static string ValidarNome(string nome) =>
        string.IsNullOrWhiteSpace(nome)
            ? throw new DomainValidationException(nameof(Nome), "O nome do projeto é obrigatório.")
            : nome.Trim();

    private static DateTime NormalizarInicio(DateTime dataInicio) => DiasUteis.ProximoOuMesmo(dataInicio.Date);

    private static DomainValidationException SubtarefaDeSiMesma() =>
        new(nameof(Tarefa.TarefaPaiId), "Uma tarefa não pode ser subtarefa de si mesma.");

    // Uma tarefa só vira resumo se não for marco nem tiver vínculos (resumos não têm vínculos).
    private void ValidarPodeTerSubtarefas(Tarefa pai)
    {
        if (pai.EhResumo)
            return;
        if (pai.EhMarco)
            throw new DomainValidationException(nameof(Tarefa.TarefaPaiId),
                $"\"{pai.Nome}\" é um marco, e marcos não têm subtarefas.");
        if (_predecessoras.Exists(p => p.TarefaOrigemId == pai.Id || p.TarefaDestinoId == pai.Id))
            throw new DomainValidationException(nameof(Tarefa.TarefaPaiId),
                $"\"{pai.Nome}\" tem vínculos (predecessoras ou sucessoras) e não pode virar resumo, porque " +
                "resumos não têm vínculos. Remova os vínculos dela primeiro.");
    }

    private void AtualizarSeEhResumo(Guid? tarefaId)
    {
        if (tarefaId is { } id)
            _tarefas.Find(t => t.Id == id)!.DefinirSeEhResumo(_tarefas.Exists(t => t.TarefaPaiId == id));
    }

    // Posição logo depois da última subtarefa (em qualquer nível) da tarefa: em pré-ordem, as
    // subtarefas vêm todas em seguida a ela.
    private int FimDaSubarvore(Tarefa tarefa)
    {
        var fim = _tarefas.IndexOf(tarefa) + 1;
        while (fim < _tarefas.Count && EhDescendente(_tarefas[fim], tarefa))
            fim++;
        return fim;
    }

    // Tira da lista a tarefa e todas as subtarefas dela, na ordem, e as devolve.
    private List<Tarefa> RetirarSubarvore(Tarefa tarefa)
    {
        var inicio = _tarefas.IndexOf(tarefa);
        var quantidade = FimDaSubarvore(tarefa) - inicio;
        var subarvore = _tarefas.GetRange(inicio, quantidade);
        _tarefas.RemoveRange(inicio, quantidade);
        return subarvore;
    }

    private bool EhDescendente(Tarefa tarefa, Tarefa ancestral)
    {
        for (var pai = tarefa.TarefaPaiId; pai is { } id; pai = _tarefas.Find(t => t.Id == id)!.TarefaPaiId)
            if (id == ancestral.Id)
                return true;
        return false;
    }

    private Tarefa ObterTarefa(Guid id, string propriedade) =>
        _tarefas.Find(t => t.Id == id)
        ?? throw new DomainValidationException(propriedade, $"A tarefa {id} não pertence ao cronograma.");

    private Predecessora? Encontrar(Guid tarefaOrigemId, Guid tarefaDestinoId) =>
        _predecessoras.Find(p => p.TarefaOrigemId == tarefaOrigemId && p.TarefaDestinoId == tarefaDestinoId);

    /// <summary>
    /// Menor caminho de <paramref name="de"/> até <paramref name="ate"/> seguindo os vínculos
    /// (de predecessora para sucessora), incluindo as duas pontas; <c>null</c> se não houver.
    /// </summary>
    private List<Guid>? Caminho(Guid de, Guid ate)
    {
        var sucessoras = _predecessoras.ToLookup(p => p.TarefaOrigemId, p => p.TarefaDestinoId);
        var veioDe = new Dictionary<Guid, Guid> { [de] = de };
        var fila = new Queue<Guid>([de]);

        while (fila.TryDequeue(out var atual))
        {
            if (atual == ate)
            {
                var caminho = new List<Guid> { ate };
                while (caminho[^1] != de)
                    caminho.Add(veioDe[caminho[^1]]);
                caminho.Reverse();
                return caminho;
            }

            foreach (var proxima in sucessoras[atual])
                if (veioDe.TryAdd(proxima, atual))
                    fila.Enqueue(proxima);
        }

        return null;
    }
}
