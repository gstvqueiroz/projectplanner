namespace ProjectPlanner.Core;

/// <summary>
/// Tarefa do cronograma.
/// </summary>
/// <remarks>
/// Regras de datas (calendário padrão de segunda a sexta; feriados ficam para um bloco futuro):
/// <list type="bullet">
/// <item>As datas não têm hora: qualquer horário informado é descartado.</item>
/// <item>Um início que cai no fim de semana vai para a segunda-feira seguinte; um término
/// que cai no fim de semana volta para a sexta-feira anterior.</item>
/// <item><see cref="Duracao"/> é um número inteiro de dias úteis, zero ou mais. Duração 0 faz da
/// tarefa um marco (<see cref="TipoTarefa.Marco"/>): início e término no mesmo dia.</item>
/// <item><see cref="DataTermino"/> é inclusiva, ou seja, é o último dia útil de trabalho:
/// <see cref="DataInicio"/> + (<see cref="Duracao"/> − 1) dias úteis. Uma tarefa de 1 dia
/// termina no mesmo dia em que começa.</item>
/// <item>Alterar o início mantém a duração e desloca o término. Alterar a duração recalcula o
/// término. Alterar o término recalcula a duração, e um término anterior ao início é rejeitado.</item>
/// </list>
/// Toda violação lança <see cref="DomainValidationException"/> e deixa a tarefa inalterada.
/// <para>
/// A estrutura (quem é subtarefa de quem) é do <see cref="Cronograma"/>, que mantém
/// <see cref="TarefaPaiId"/> e <see cref="EhResumo"/>. Num resumo, duração, datas e percentual
/// concluído são calculados a partir das subtarefas e não podem ser alterados diretamente.
/// </para>
/// <para>
/// <see cref="DataInicio"/> e <see cref="DataTermino"/> são as datas planejadas, informadas pelo
/// usuário. As datas do caminho crítico (<see cref="InicioMaisCedo"/>, <see cref="TerminoMaisCedo"/>,
/// <see cref="InicioMaisTarde"/>, <see cref="TerminoMaisTarde"/>) e a <see cref="Folga"/> são o
/// resultado do último <see cref="CalculadoraCronograma.Calcular"/>. Elas ficam nulas até o primeiro
/// cálculo e não se atualizam sozinhas quando a tarefa ou o cronograma mudam: é preciso recalcular.
/// </para>
/// </remarks>
public class Tarefa
{
    private readonly List<FotoTarefa> _fotos = [];

    public Tarefa(string nome, DateTime dataInicio, int duracao)
        : this(Guid.NewGuid(), nome, dataInicio, duracao)
    {
    }

    /// <summary>Reconstrói uma tarefa já existente, mantendo o Id (por exemplo, ao abrir um arquivo).</summary>
    public Tarefa(Guid id, string nome, DateTime dataInicio, int duracao)
    {
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id da tarefa é obrigatório.");

        Id = id;
        Nome = ValidarNome(nome);
        ValidarDuracao(duracao);
        DataInicio = NormalizarInicio(dataInicio);
        Duracao = duracao;
        DataTermino = CalcularTermino(DataInicio, duracao);
    }

    public Guid Id { get; }

    public string Nome { get; private set; }

    /// <summary>Duração em dias úteis (0 num marco).</summary>
    public int Duracao { get; private set; }

    /// <summary>
    /// Resumo se tem subtarefas; senão, marco se a duração é 0; senão, tarefa comum.
    /// </summary>
    public TipoTarefa Tipo => EhResumo ? TipoTarefa.Resumo : Duracao == 0 ? TipoTarefa.Marco : TipoTarefa.Normal;

    /// <summary>Se a tarefa tem subtarefas. Mantido pelo <see cref="Cronograma"/>.</summary>
    public bool EhResumo { get; private set; }

    public bool EhMarco => Tipo == TipoTarefa.Marco;

    /// <summary>O resumo em que a tarefa está; nulo no primeiro nível. Mantido pelo <see cref="Cronograma"/>.</summary>
    public Guid? TarefaPaiId { get; private set; }

    /// <summary>Primeiro dia útil de trabalho.</summary>
    public DateTime DataInicio { get; private set; }

    /// <summary>Último dia útil de trabalho (inclusivo).</summary>
    public DateTime DataTermino { get; private set; }

    /// <summary>Percentual concluído, de 0 a 100.</summary>
    public int PercentualConcluido { get; private set; }

    /// <summary>
    /// Anotações livres sobre a tarefa, sem limite nem formato. Nulo quando não há nenhuma: texto vazio
    /// ou só com espaços vira nulo; qualquer outro texto é guardado exatamente como veio (com quebras de
    /// linha e espaços), já que é do usuário.
    /// </summary>
    public string? Notas { get; private set; }

    /// <summary>Início mais cedo (ES), do último cálculo do cronograma.</summary>
    public DateTime? InicioMaisCedo { get; private set; }

    /// <summary>Término mais cedo (EF), do último cálculo do cronograma.</summary>
    public DateTime? TerminoMaisCedo { get; private set; }

    /// <summary>Início mais tarde (LS), do último cálculo do cronograma.</summary>
    public DateTime? InicioMaisTarde { get; private set; }

    /// <summary>Término mais tarde (LF), do último cálculo do cronograma.</summary>
    public DateTime? TerminoMaisTarde { get; private set; }

    /// <summary>Folga total em dias úteis: LS − ES (igual a LF − EF). Nunca é negativa.</summary>
    public int? Folga { get; private set; }

    /// <summary>Se a tarefa é crítica (folga zero). Falso antes do primeiro cálculo.</summary>
    public bool EhCritica => Folga == 0;

    /// <summary>
    /// Num marco, se ele fica no fim do dia de <see cref="InicioMaisCedo"/> (determinado pelo término de
    /// outra tarefa) em vez de no começo. No Gantt, é a diferença entre a borda direita e a esquerda do
    /// dia. Falso nas demais tarefas. Ver <see cref="CalculadoraCronograma"/>.
    /// </summary>
    public bool MarcoNoFimDoDia { get; private set; }

    /// <summary>O mesmo que <see cref="MarcoNoFimDoDia"/>, para <see cref="InicioMaisTarde"/>.</summary>
    public bool MarcoTardeNoFimDoDia { get; private set; }

    /// <summary>
    /// As fotos da tarefa, da mais antiga para a mais recente pela data da foto (empates na ordem em que foram
    /// adicionadas). Ficam na própria tarefa porque cada foto pertence a uma tarefa só.
    /// </summary>
    public IReadOnlyList<FotoTarefa> Fotos => _fotos.OrderBy(f => f.DataFoto).ToList();

    /// <summary>As fotos na ordem em que foram adicionadas (a coluna Ordem do arquivo).</summary>
    public IReadOnlyList<FotoTarefa> FotosNaOrdemDeCadastro => _fotos.AsReadOnly();

    /// <summary>A foto com este id, ou nula.</summary>
    public FotoTarefa? Foto(Guid fotoId) => _fotos.Find(f => f.Id == fotoId);

    /// <summary>Se a mesma imagem (mesmo arquivo original) já é uma foto desta tarefa.</summary>
    public bool TemFotoDaMesmaImagem(string hashOriginal) =>
        _fotos.Exists(f => string.Equals(f.Imagem.HashOriginal, hashOriginal, StringComparison.OrdinalIgnoreCase));

    /// <exception cref="DomainValidationException">A foto é de outra tarefa, já foi adicionada ou é a mesma imagem de outra.</exception>
    public void AdicionarFoto(FotoTarefa foto)
    {
        ArgumentNullException.ThrowIfNull(foto);
        if (foto.TarefaId != Id)
            throw new DomainValidationException(nameof(Fotos), $"A foto é de outra tarefa, não de \"{Nome}\".");
        if (_fotos.Exists(f => f.Id == foto.Id))
            throw new DomainValidationException(nameof(Fotos), $"A foto já pertence a \"{Nome}\".");
        if (TemFotoDaMesmaImagem(foto.Imagem.HashOriginal))
            throw new DomainValidationException(nameof(Fotos),
                $"{foto.Imagem.NomeArquivoOriginal}: esta imagem já é uma foto de \"{Nome}\".");
        _fotos.Add(foto);
    }

    /// <summary>Troca uma foto pela versão alterada (informações editadas ou imagem girada), no mesmo lugar.</summary>
    /// <exception cref="DomainValidationException">A foto não é desta tarefa.</exception>
    public void SubstituirFoto(FotoTarefa foto)
    {
        ArgumentNullException.ThrowIfNull(foto);
        var indice = _fotos.FindIndex(f => f.Id == foto.Id);
        if (indice < 0 || foto.TarefaId != Id)
            throw new DomainValidationException(nameof(Fotos), $"A foto {foto.Id} não pertence a \"{Nome}\".");
        _fotos[indice] = foto;
    }

    /// <exception cref="DomainValidationException">A foto não é desta tarefa.</exception>
    public void RemoverFoto(Guid fotoId)
    {
        if (_fotos.RemoveAll(f => f.Id == fotoId) == 0)
            throw new DomainValidationException(nameof(Fotos), $"A foto {fotoId} não pertence a \"{Nome}\".");
    }

    public void Renomear(string nome) => Nome = ValidarNome(nome);

    /// <summary>
    /// Altera a duração e recalcula o término, mantendo o início. Duração 0 transforma a tarefa em
    /// marco; uma duração maior que 0 num marco o transforma de volta em tarefa comum.
    /// </summary>
    public void AlterarDuracao(int duracao)
    {
        RecusarSeResumo(nameof(Duracao), "A duração de um resumo é calculada");
        ValidarDuracao(duracao);
        DataTermino = CalcularTermino(DataInicio, duracao);
        Duracao = duracao;
    }

    /// <summary>Move o início mantendo a duração; o término é deslocado junto (num marco, é o mesmo dia).</summary>
    public void AlterarDataInicio(DateTime dataInicio)
    {
        RecusarSeResumo(nameof(DataInicio), "O início de um resumo é calculado");
        var inicio = NormalizarInicio(dataInicio);
        DataTermino = CalcularTermino(inicio, Duracao);
        DataInicio = inicio;
    }

    /// <summary>
    /// Altera o término e recalcula a duração, mantendo o início. Num marco, move o marco: início e
    /// término vão juntos para o novo dia.
    /// </summary>
    public void AlterarDataTermino(DateTime dataTermino)
    {
        RecusarSeResumo(nameof(DataTermino), "O término de um resumo é calculado");
        var termino = DiasUteis.AnteriorOuMesmo(dataTermino.Date);
        if (EhMarco)
        {
            DataInicio = termino;
            DataTermino = termino;
            return;
        }
        if (termino < DataInicio)
            throw new DomainValidationException(nameof(DataTermino),
                $"A data de término ({dataTermino:d}) não pode ser anterior à data de início ({DataInicio:d}).");

        Duracao = DiasUteis.ContarInclusivo(DataInicio, termino);
        DataTermino = termino;
    }

    public void AlterarNotas(string? notas) => Notas = string.IsNullOrWhiteSpace(notas) ? null : notas;

    public void AtualizarPercentualConcluido(int percentual)
    {
        RecusarSeResumo(nameof(PercentualConcluido), "O percentual concluído de um resumo é calculado");
        if (percentual is < 0 or > 100)
            throw new DomainValidationException(nameof(PercentualConcluido),
                $"O percentual concluído deve estar entre 0 e 100 (informado: {percentual}).");

        PercentualConcluido = percentual;
    }

    internal void DefinirDatasCalculadas(DateTime inicioMaisCedo, DateTime terminoMaisCedo,
        DateTime inicioMaisTarde, DateTime terminoMaisTarde, int folga,
        bool marcoNoFimDoDia = false, bool marcoTardeNoFimDoDia = false)
    {
        InicioMaisCedo = inicioMaisCedo;
        TerminoMaisCedo = terminoMaisCedo;
        InicioMaisTarde = inicioMaisTarde;
        TerminoMaisTarde = terminoMaisTarde;
        Folga = folga;
        MarcoNoFimDoDia = marcoNoFimDoDia;
        MarcoTardeNoFimDoDia = marcoTardeNoFimDoDia;
    }

    /// <summary>Valores do resumo calculados a partir das subtarefas (ver <see cref="CalculadoraResumos"/>).</summary>
    internal void DefinirValoresDoResumo(DateTime inicio, DateTime termino, int duracao, int percentualConcluido)
    {
        DataInicio = inicio;
        DataTermino = termino;
        Duracao = duracao;
        PercentualConcluido = percentualConcluido;
    }

    internal void DefinirPai(Guid? tarefaPaiId) => TarefaPaiId = tarefaPaiId;

    /// <summary>Uma tarefa igual a esta, com o mesmo Id e as mesmas fotos (para <see cref="Cronograma.Copiar"/>).</summary>
    internal Tarefa Copiar()
    {
        var copia = new Tarefa(Id, Nome, DataInicio, Duracao)
        {
            EhResumo = EhResumo,
            TarefaPaiId = TarefaPaiId,
            DataTermino = DataTermino,
            PercentualConcluido = PercentualConcluido,
            Notas = Notas,
            InicioMaisCedo = InicioMaisCedo,
            TerminoMaisCedo = TerminoMaisCedo,
            InicioMaisTarde = InicioMaisTarde,
            TerminoMaisTarde = TerminoMaisTarde,
            Folga = Folga,
            MarcoNoFimDoDia = MarcoNoFimDoDia,
            MarcoTardeNoFimDoDia = MarcoTardeNoFimDoDia,
        };
        copia._fotos.AddRange(_fotos);
        return copia;
    }

    internal void DefinirSeEhResumo(bool ehResumo) => EhResumo = ehResumo;

    private void RecusarSeResumo(string propriedade, string oQueECalculado)
    {
        if (EhResumo)
            throw new DomainValidationException(propriedade, $"{oQueECalculado} a partir das subtarefas.");
    }

    private static string ValidarNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new DomainValidationException(nameof(Nome), "O nome da tarefa é obrigatório.");

        return nome.Trim();
    }

    private static void ValidarDuracao(int duracao)
    {
        if (duracao < 0)
            throw new DomainValidationException(nameof(Duracao),
                $"A duração não pode ser negativa (informado: {duracao}).");
    }

    private static DateTime NormalizarInicio(DateTime dataInicio) =>
        DiasUteis.ProximoOuMesmo(dataInicio.Date);

    // Um marco (duração 0) termina no próprio dia em que começa.
    private static DateTime CalcularTermino(DateTime inicio, int duracao) =>
        DiasUteis.Adicionar(inicio, Math.Max(duracao - 1, 0))
        ?? throw new DomainValidationException(nameof(DataTermino),
            "A data de término calculada ultrapassa a maior data suportada (31/12/9999).");
}
