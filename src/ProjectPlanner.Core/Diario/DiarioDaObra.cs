namespace ProjectPlanner.Core.Diario;

/// <summary>Bloco 81: o tempo num período do dia (manhã ou tarde), como nos RDOs.</summary>
public enum TempoNoPeriodo
{
    Bom,
    Nublado,
    Chuva,
}

/// <summary>Bloco 81: o clima de um período do dia e se deu para trabalhar (praticável) ou não (impraticável).</summary>
public sealed record ClimaDoPeriodo(TempoNoPeriodo Tempo, bool Praticavel = true)
{
    public static ClimaDoPeriodo Bom { get; } = new(TempoNoPeriodo.Bom);

    /// <summary>"Bom, praticável", "Chuva, impraticável".</summary>
    public string Texto => $"{NomeDoTempo(Tempo)}, {(Praticavel ? "praticável" : "impraticável")}";

    public static string NomeDoTempo(TempoNoPeriodo tempo) => tempo switch
    {
        TempoNoPeriodo.Nublado => "Nublado",
        TempoNoPeriodo.Chuva => "Chuva",
        _ => "Bom",
    };
}

/// <summary>Bloco 81: uma função no efetivo do dia (pedreiro, servente…), quantos e de qual empresa (vazia: a própria).</summary>
public sealed record EfetivoNoDia(string Funcao, int Quantidade, string? Empresa = null);

/// <summary>Bloco 81: um equipamento em uso no dia (betoneira, retroescavadeira…).</summary>
public sealed record EquipamentoNoDia(string Nome, int Quantidade);

/// <summary>
/// Bloco 81: um serviço executado no dia: a tarefa do cronograma (nula num serviço que não é uma tarefa), o que foi feito e o %
/// da tarefa ao fim do dia (opcional; vai ao cronograma só quando o usuário pede).
/// </summary>
public sealed record AtividadeNoDia(Guid? TarefaId, string Descricao, int? Percentual = null);

/// <summary>Bloco 81: quando o dia foi fechado (assinado) e por quem.</summary>
public sealed record FechamentoDoDia(DateTime Quando, string? Responsavel);

/// <summary>
/// Bloco 81 (o Diário de Obra, RDO): o registro de um dia: o clima de manhã e de tarde, o efetivo, os equipamentos, os serviços
/// executados e as ocorrências (visitas, entregas, acidentes, paralisações). Fechado, não muda mais (precisa reabrir).
/// </summary>
public sealed record DiaDaObra(DateOnly Data)
{
    public ClimaDoPeriodo Manha { get; init; } = ClimaDoPeriodo.Bom;

    public ClimaDoPeriodo Tarde { get; init; } = ClimaDoPeriodo.Bom;

    public IReadOnlyList<EfetivoNoDia> Efetivo { get; init; } = [];

    public IReadOnlyList<EquipamentoNoDia> Equipamentos { get; init; } = [];

    public IReadOnlyList<AtividadeNoDia> Atividades { get; init; } = [];

    /// <summary>As ocorrências e observações do dia, em texto livre; nula sem nenhuma.</summary>
    public string? Ocorrencias { get; init; }

    /// <summary>Nulo enquanto o dia está aberto.</summary>
    public FechamentoDoDia? Fechamento { get; init; }

    public bool Fechado => Fechamento is not null;

    /// <summary>Quantas pessoas trabalharam no dia.</summary>
    public int TotalDoEfetivo => Efetivo.Sum(e => e.Quantidade);

    /// <summary>Não deu para trabalhar o dia todo (manhã e tarde impraticáveis).</summary>
    public bool Impraticavel => !Manha.Praticavel && !Tarde.Praticavel;
}

/// <summary>
/// Bloco 81 (o Diário de Obra, RDO): os dias registrados da obra, em ordem de data. O número de cada dia (RDO nº 1, 2…) é a
/// posição dele nessa ordem; para os números dos dias fechados (assinados) nunca mudarem, não se cria nem se exclui um dia antes
/// do último fechado. Volta no Desfazer, como as tarefas.
/// </summary>
public sealed class DiarioDaObra
{
    public const int LinhasMaximas = 60;

    /// <summary>Bloco 82: a quantidade máxima de uma linha do efetivo ou dos equipamentos.</summary>
    public const int QuantidadeMaxima = 999;
    public const int TamanhoMaximoDoNome = 80;
    public const int TamanhoMaximoDaDescricao = 300;
    public const int TamanhoMaximoDasOcorrencias = 4000;

    private readonly List<DiaDaObra> _dias = [];

    /// <summary>Os dias, do mais antigo para o mais recente.</summary>
    public IReadOnlyList<DiaDaObra> Dias => _dias.AsReadOnly();

    public bool Vazio => _dias.Count == 0;

    public DiaDaObra? Dia(DateOnly data) => _dias.Find(d => d.Data == data);

    /// <summary>O número do RDO (1 no primeiro dia); 0 se o dia não está no diário.</summary>
    public int NumeroDe(DateOnly data) => _dias.FindIndex(d => d.Data == data) + 1;

    /// <summary>O último dia fechado; nulo sem nenhum.</summary>
    public DiaDaObra? UltimoFechado => _dias.LastOrDefault(d => d.Fechado);

    /// <summary>Um dia novo no diário (vazio ou já preenchido, por exemplo pelo cronograma).</summary>
    /// <exception cref="DomainValidationException">O dia já existe, fica antes do último fechado, ou o conteúdo não vale.</exception>
    public DiaDaObra Abrir(DiaDaObra dia)
    {
        ArgumentNullException.ThrowIfNull(dia);
        if (Dia(dia.Data) is not null)
            throw new DomainValidationException(nameof(Dias), $"O diário já tem o dia {dia.Data:dd/MM/yyyy}.");
        if (UltimoFechado is { } fechado && dia.Data < fechado.Data)
            throw new DomainValidationException(nameof(Dias),
                $"O dia {dia.Data:dd/MM/yyyy} ficaria antes do último dia fechado (RDO nº {NumeroDe(fechado.Data)}, {fechado.Data:dd/MM/yyyy}): " +
                "os números dos dias fechados não podem mudar.");
        var limpo = Validar(dia with { Fechamento = null });
        _dias.Add(limpo);
        _dias.Sort((a, b) => a.Data.CompareTo(b.Data));
        return limpo;
    }

    /// <summary>Grava o que mudou num dia aberto.</summary>
    /// <exception cref="DomainValidationException">O dia não existe, está fechado, ou o conteúdo não vale.</exception>
    public DiaDaObra Salvar(DiaDaObra dia)
    {
        ArgumentNullException.ThrowIfNull(dia);
        var atual = Obter(dia.Data);
        if (atual.Fechado)
            throw DiaFechado(atual);
        var limpo = Validar(dia with { Fechamento = null });
        _dias[_dias.IndexOf(atual)] = limpo;
        return limpo;
    }

    /// <summary>Fecha (assina) o dia: ele não muda mais, a não ser que seja reaberto.</summary>
    /// <exception cref="DomainValidationException">O dia não existe ou já está fechado.</exception>
    public void Fechar(DateOnly data, DateTime quando, string? responsavel)
    {
        var dia = Obter(data);
        if (dia.Fechado)
            throw new DomainValidationException(nameof(Dias), $"O dia {data:dd/MM/yyyy} já está fechado.");
        _dias[_dias.IndexOf(dia)] = dia with { Fechamento = new FechamentoDoDia(quando, string.IsNullOrWhiteSpace(responsavel) ? null : responsavel.Trim()) };
    }

    /// <summary>Reabre um dia fechado, para corrigir.</summary>
    /// <exception cref="DomainValidationException">O dia não existe ou está aberto.</exception>
    public void Reabrir(DateOnly data)
    {
        var dia = Obter(data);
        if (!dia.Fechado)
            throw new DomainValidationException(nameof(Dias), $"O dia {data:dd/MM/yyyy} está aberto.");
        _dias[_dias.IndexOf(dia)] = dia with { Fechamento = null };
    }

    /// <summary>Exclui um dia aberto (os dias fechados depois dele mudariam de número: não deixa).</summary>
    /// <exception cref="DomainValidationException">O dia não existe, está fechado, ou há dias fechados depois dele.</exception>
    public void Remover(DateOnly data)
    {
        var dia = Obter(data);
        if (dia.Fechado)
            throw DiaFechado(dia);
        if (_dias.Exists(d => d.Fechado && d.Data > data))
            throw new DomainValidationException(nameof(Dias),
                $"Há dias fechados depois de {data:dd/MM/yyyy}: excluir este mudaria os números deles. Reabra-os antes, se precisar.");
        _dias.Remove(dia);
    }

    /// <summary>Põe os dias guardados no arquivo.</summary>
    /// <exception cref="DomainValidationException">Datas repetidas ou conteúdo que não vale.</exception>
    public void Restaurar(IEnumerable<DiaDaObra> dias)
    {
        ArgumentNullException.ThrowIfNull(dias);
        var lista = dias.Select(d => Validar(d)).OrderBy(d => d.Data).ToList();
        if (lista.GroupBy(d => d.Data).FirstOrDefault(g => g.Count() > 1) is { } repetido)
            throw new DomainValidationException(nameof(Dias), $"O diário tem o dia {repetido.Key:dd/MM/yyyy} duas vezes.");
        _dias.Clear();
        _dias.AddRange(lista);
    }

    /// <summary>Um diário igual a este (para a cópia do cronograma que o Desfazer guarda); os dias são registros imutáveis.</summary>
    public DiarioDaObra Copiar()
    {
        var copia = new DiarioDaObra();
        copia._dias.AddRange(_dias);
        return copia;
    }

    private DiaDaObra Obter(DateOnly data) =>
        Dia(data) ?? throw new DomainValidationException(nameof(Dias), $"O diário não tem o dia {data:dd/MM/yyyy}.");

    private static DomainValidationException DiaFechado(DiaDaObra dia) =>
        new(nameof(Dias), $"O dia {dia.Data:dd/MM/yyyy} está fechado (assinado): reabra-o para mudar.");

    // Textos sem espaços nas pontas; linhas vazias saem; nomes, quantidades e % dentro dos limites.
    private static DiaDaObra Validar(DiaDaObra dia)
    {
        if (!Enum.IsDefined(dia.Manha.Tempo) || !Enum.IsDefined(dia.Tarde.Tempo))
            throw new DomainValidationException(nameof(DiaDaObra.Manha), "Tempo desconhecido (bom, nublado ou chuva).");
        var efetivo = dia.Efetivo.Select(e => e with { Funcao = (e.Funcao ?? "").Trim(), Empresa = string.IsNullOrWhiteSpace(e.Empresa) ? null : e.Empresa.Trim() })
            .Where(e => e.Funcao.Length > 0).ToList();
        var equipamentos = dia.Equipamentos.Select(e => e with { Nome = (e.Nome ?? "").Trim() }).Where(e => e.Nome.Length > 0).ToList();
        var atividades = dia.Atividades.Select(a => a with { Descricao = (a.Descricao ?? "").Trim() }).Where(a => a.Descricao.Length > 0).ToList();
        if (efetivo.Count > LinhasMaximas || equipamentos.Count > LinhasMaximas || atividades.Count > LinhasMaximas)
            throw new DomainValidationException(nameof(DiaDaObra.Efetivo), $"Cada lista do dia pode ter até {LinhasMaximas} linhas.");
        if (efetivo.FirstOrDefault(e => e.Quantidade is < 1 or > QuantidadeMaxima) is { } pessoas)
            throw new DomainValidationException(nameof(DiaDaObra.Efetivo), $"A quantidade de \"{pessoas.Funcao}\" precisa ir de 1 a {QuantidadeMaxima}.");
        if (equipamentos.FirstOrDefault(e => e.Quantidade is < 1 or > QuantidadeMaxima) is { } equipamento)
            throw new DomainValidationException(nameof(DiaDaObra.Equipamentos), $"A quantidade de \"{equipamento.Nome}\" precisa ir de 1 a {QuantidadeMaxima}.");
        if (efetivo.Select(e => e.Funcao).Concat(efetivo.Select(e => e.Empresa ?? "")).Concat(equipamentos.Select(e => e.Nome))
                .FirstOrDefault(n => n.Length > TamanhoMaximoDoNome) is { } longo)
            throw new DomainValidationException(nameof(DiaDaObra.Efetivo), $"\"{longo[..20]}…\" passa de {TamanhoMaximoDoNome} caracteres.");
        if (atividades.FirstOrDefault(a => a.Descricao.Length > TamanhoMaximoDaDescricao) is { } comprida)
            throw new DomainValidationException(nameof(DiaDaObra.Atividades), $"\"{comprida.Descricao[..20]}…\" passa de {TamanhoMaximoDaDescricao} caracteres.");
        if (atividades.FirstOrDefault(a => a.Percentual is < 0 or > 100) is { } fora)
            throw new DomainValidationException(nameof(DiaDaObra.Atividades), $"O % de \"{fora.Descricao}\" precisa ir de 0 a 100.");
        var ocorrencias = string.IsNullOrWhiteSpace(dia.Ocorrencias) ? null : dia.Ocorrencias.Trim();
        if (ocorrencias?.Length > TamanhoMaximoDasOcorrencias)
            throw new DomainValidationException(nameof(DiaDaObra.Ocorrencias), $"As ocorrências passam de {TamanhoMaximoDasOcorrencias} caracteres.");
        return dia with { Efetivo = efetivo, Equipamentos = equipamentos, Atividades = atividades, Ocorrencias = ocorrencias };
    }
}
