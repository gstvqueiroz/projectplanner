using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Models;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Edicao;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// A janela principal: o projeto aberto, a grid de tarefas e os comandos da barra de ferramentas.
/// </summary>
/// <remarks>
/// Toda alteração aceita marca o projeto como alterado e recalcula o cronograma na hora, como o MS
/// Project; o comando Recalcular continua disponível para pedir o cálculo explicitamente. Alterações
/// recusadas pelo domínio aparecem como erro no campo e na barra de status, sem exceção na tela.
/// <para>
/// <see cref="Tarefas"/> tem todas as linhas, na ordem do cronograma (o Nº é a posição nela). A grid
/// mostra <see cref="LinhasVisiveis"/>: as mesmas, menos as que estão dentro de um resumo recolhido; o
/// Gantt esconde as mesmas, para que as duas listas continuem lado a lado.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : ViewModelBase, IEdicaoCronograma, ILigacoesNoGantt
{
    private const string NomePadrao = "Projeto sem título";

    private readonly IProjetoRepository _repositorio;
    private readonly ICalculadoraCronograma _calculadora;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly FotosDoProjeto _fotos;
    private Cronograma _cronograma;
    private IReadOnlyDictionary<Guid, string> _edt = new Dictionary<Guid, string>();
    private IReadOnlySet<Guid> _ocultas = new HashSet<Guid>();
    private bool _mudandoEstrutura;

    public MainWindowViewModel(IProjetoRepository repositorio, ICalculadoraCronograma calculadora,
        IDialogosService dialogos, TimeProvider relogio, IPreferenciasColunas preferenciasColunas,
        IAssistenteIA assistente, IPreferenciasInterface preferenciasInterface, IProcessadorDeImagens processadorDeImagens,
        IConfiguracaoAssistente configuracaoAssistente, IPreferenciasRelatorio? preferenciasRelatorio = null,
        IAnalistaDeFotos? analistaDeFotos = null, ICatalogoDeRecursos? catalogoDeRecursos = null, IBibliotecaDeReferencias? bibliotecaDeReferencias = null,
        IConsultorDeOrcamento? consultorDeOrcamento = null, IAnalistaDaObra? analistaDaObra = null, IContaProjectPlanner? conta = null,
        ITelegramDaConta? telegram = null, IConversorDeHeic? conversorDeHeic = null, IPreferenciasDoCalendario? preferenciasDoCalendario = null)
    {
        _preferenciasDoCalendario = preferenciasDoCalendario;
        _conta = conta ?? new SemContaProjectPlanner();
        _telegram = telegram;
        _analistaDaObra = analistaDaObra ?? new AnalistaDaObraIndisponivel();
        _consultor = consultorDeOrcamento ?? new ConsultorIndisponivel();
        _biblioteca = bibliotecaDeReferencias ?? new BibliotecaDeReferenciasEmMemoria();
        _catalogoDeRecursos = catalogoDeRecursos ?? new CatalogoDeRecursosEmMemoria();
        _preferenciasRelatorio = preferenciasRelatorio ?? new PreferenciasRelatorioEmMemoria();
        _analista = analistaDeFotos ?? new AnalistaIndisponivel();
        _fotos = new FotosDoProjeto(repositorio, processadorDeImagens, relogio);
        Colunas = new ColunasViewModel(preferenciasColunas);
        Assistente = new AssistenteViewModel(assistente, dialogos, relogio, preferenciasInterface, configuracaoAssistente, this)
        {
            Conta = _conta,
            ConversorDeHeic = conversorDeHeic,
        };
        _conta.Mudou += (_, _) => ContaMudou();
        _repositorio = repositorio;
        _calculadora = calculadora;
        _dialogos = dialogos;
        _relogio = relogio;
        _preferenciasInterface = preferenciasInterface;
        Barra = new BarraDeIconesViewModel(preferenciasInterface);
        Tema = preferenciasInterface.Carregar()?.Tema ?? TemaDaInterface.Sistema;
        _cronograma = NovoCronograma();
        Gantt.BarraClicada += ClicarNaTarefa; // Bloco 72: com Ctrl e Shift, a seleção múltipla
        Gantt.VazioClicado += () => TarefaSelecionada = null;
        Gantt.PreverMovimento = lacuna => PreverMovimento(Selecionadas, lacuna); // Bloco 74: as regras da grade
        Gantt.Mover = lacuna => MoverArrastando(Selecionadas, lacuna);
        Gantt.Ligacoes = this;
        Gantt.DicaDaTarefa = DicaDaTarefaNoGantt;
        Gantt.FotosPedidas += tarefaId => _ = MostrarFotosAsync(tarefaId);
        // Um controle só para o caminho crítico: o do Gantt também liga e desliga o fundo das linhas na grid.
        Gantt.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GanttViewModel.MostrarCaminhoCritico))
                foreach (var linha in Tarefas)
                    linha.AtualizarTela();
        };
        Carregar(_cronograma, caminho: null);
    }

    /// <summary>As imagens das fotos do projeto aberto (lidas sob demanda; as novas ficam na memória até salvar).</summary>
    public FotosDoProjeto Fotos => _fotos;

    /// <summary>O painel lateral do assistente de IA (Bloco 19).</summary>
    public AssistenteViewModel Assistente { get; }

    /// <summary>
    /// O retrato só de leitura do cronograma que vai com cada mensagem ao assistente (Bloco 23): todas as linhas (também
    /// as de resumos recolhidos), com o que a grid mostra e o resultado do último cálculo.
    /// </summary>
    public ContextoDoProjeto RetratoParaOAssistente()
    {
        var edicao = (IEdicaoCronograma)this;
        var tarefas = Tarefas.Select(linha => new TarefaNoContexto(
            linha.Numero,
            linha.Edt,
            linha.Tarefa.Nome,
            linha.Nivel,
            linha.EhResumo ? TipoNoContexto.Resumo : linha.EhMarco ? TipoNoContexto.Marco : TipoNoContexto.Tarefa,
            linha.Tarefa.Duracao,
            // As datas calculadas (as das barras do Gantt); antes do primeiro cálculo, as informadas.
            DateOnly.FromDateTime(linha.Tarefa.InicioMaisCedo ?? linha.Tarefa.DataInicio),
            DateOnly.FromDateTime(linha.Tarefa.TerminoMaisCedo ?? linha.Tarefa.DataTermino),
            linha.Tarefa.PercentualConcluido,
            edicao.TextoPredecessoras(linha.Tarefa),
            linha.Tarefa.Folga,
            linha.Tarefa.EhCritica,
            linha.Tarefa.Notas,
            linha.Tarefa.Fotos.Count,
            linha.Tarefa.Id)).ToList();
        var inicio = DateOnly.FromDateTime(_cronograma.DataInicio);
        var termino = TerminoProjeto is { } fim ? DateOnly.FromDateTime(fim) : tarefas.Select(t => t.Termino).DefaultIfEmpty(inicio).Max();
        return new ContextoDoProjeto(_cronograma.Nome, inicio, termino, DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime), tarefas,
            ProjectPlanner.Relatorios.TextosDoOrcamento.ParaOAssistente(_cronograma.Orcamento.Planilha()), SituacaoDaObra())
        {
            Calendario = CalendarioParaOAssistente(_cronograma.Calendario, inicio, termino), // Bloco 71
        };
    }

    /// <summary>Todas as linhas, na ordem do cronograma: cada resumo seguido das suas subtarefas.</summary>
    public ObservableCollection<TarefaViewModel> Tarefas { get; } = [];

    /// <summary>As linhas que a grid mostra: todas, menos as que estão dentro de um resumo recolhido.</summary>
    public ObservableCollection<TarefaViewModel> LinhasVisiveis { get; } = [];

    /// <summary>O Gantt ao lado da grid, refeito a cada recálculo.</summary>
    public GanttViewModel Gantt { get; } = new();

    /// <summary>Quais colunas da grid aparecem, em que ordem e largura (preferência do usuário, não do projeto).</summary>
    public ColunasViewModel Colunas { get; }

    /// <summary>O projeto aberto (para testes; a tela usa só as propriedades do ViewModel).</summary>
    internal Cronograma Cronograma => _cronograma;

    /// <summary>
    /// A tarefa selecionada: a única fonte da seleção. A grid se liga a ela, e o Gantt recebe dela a
    /// barra a destacar (<see cref="GanttViewModel.TarefaDestacada"/>); clicar numa barra muda esta.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoverTarefaCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdicionarSubtarefaCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebaixarCommand))]
    [NotifyCanExecuteChangedFor(nameof(PromoverCommand))]
    [NotifyCanExecuteChangedFor(nameof(GerenciarFotosCommand))]
    [NotifyCanExecuteChangedFor(nameof(InserirTarefaAcimaCommand))]
    [NotifyCanExecuteChangedFor(nameof(InserirTarefaAbaixoCommand))]
    [NotifyCanExecuteChangedFor(nameof(RolarAteTarefaCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoverParaCimaCommand), nameof(MoverParaBaixoCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopiarTarefaCommand), nameof(DuplicarTarefaCommand))]
    [NotifyPropertyChangedFor(nameof(DicaGerenciarFotos))]
    [NotifyPropertyChangedFor(nameof(FotosDaSelecionada))]
    [NotifyPropertyChangedFor(nameof(DicaFotosDaSelecionada))]
    [NotifyPropertyChangedFor(nameof(TextoSelecionadaNaBarra))]
    public partial TarefaViewModel? TarefaSelecionada { get; set; }

    /// <summary>Quantas fotos tem a tarefa selecionada: o número no botão de fotos da barra (Bloco 25, F1).</summary>
    public int FotosDaSelecionada => TarefaSelecionada?.QuantidadeDeFotos ?? 0;

    /// <summary>A dica do botão de fotos da barra (Bloco 25, F1).</summary>
    public string DicaFotosDaSelecionada => TarefaSelecionada is not { } linha
        ? "Fotos da tarefa: selecione uma tarefa para ver as fotos dela"
        : $"Fotos de \"{linha.Nome}\": " + linha.QuantidadeDeFotos switch
        {
            0 => "nenhuma ainda — abrir a galeria para adicionar",
            1 => "1 foto — abrir a galeria",
            var n => $"{n} fotos — abrir a galeria",
        };

    /// <summary>
    /// Opção de exibição (só da sessão): falso esconde o destaque da linha selecionada na grid e da barra
    /// no Gantt. A seleção continua existindo e valendo para remover e editar.
    /// </summary>
    [ObservableProperty]
    public partial bool MostrarSelecao { get; set; } = true;

    partial void OnTarefaSelecionadaChanged(TarefaViewModel? value)
    {
        // Bloco 72: escolher uma tarefa sozinha (no código, num clique simples) desfaz a seleção múltipla.
        if (!_definindoSelecao)
        {
            _selecionadas = value is null ? [] : [value];
            SelecaoAtualizada(avisarAGrade: true);
        }
        else
            AtualizarDestaqueNoGantt();
    }

    partial void OnMostrarSelecaoChanged(bool value) => AtualizarDestaqueNoGantt();

    private void AtualizarDestaqueNoGantt()
    {
        Gantt.TarefaDestacada = MostrarSelecao ? TarefaSelecionada?.Tarefa.Id : null;
        Gantt.Destacadas = MostrarSelecao && Selecionadas.Count > 1 ? Selecionadas.Select(l => l.Tarefa.Id).ToHashSet() : new HashSet<Guid>();
    }

    /// <summary>Arquivo do projeto; nulo enquanto ele nunca foi salvo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    public partial string? CaminhoArquivo { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    public partial bool PossuiAlteracoesNaoSalvas { get; private set; }

    /// <summary>Texto da barra de status: o último aviso ou erro.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MensagemNaBarra))]
    public partial string Mensagem { get; private set; } = "";

    // O arquivo citado na última mensagem de abrir ou salvar (Bloco 25, B6).
    private string? _caminhoNaMensagem;

    /// <summary>
    /// A mensagem como a barra de status mostra (Bloco 25, B6): o nome do arquivo no lugar do caminho completo, que
    /// cortava o resto da mensagem. O caminho completo continua em <see cref="Mensagem"/> (a dica da barra).
    /// </summary>
    public string MensagemNaBarra => _caminhoNaMensagem is { Length: > 0 } caminho && Mensagem.Contains(caminho, StringComparison.Ordinal)
        ? Mensagem.Replace(caminho, Path.GetFileName(caminho), StringComparison.Ordinal)
        : Mensagem;

    /// <summary>Fim do projeto no último cálculo.</summary>
    [ObservableProperty]
    public partial DateTime? TerminoProjeto { get; private set; }

    /// <summary>"*obra — ProjectPlanner": o arquivo (ou o nome do projeto, se nunca foi salvo), com * quando há alterações.</summary>
    /// <summary>
    /// Quais painéis aparecem: grid e Gantt lado a lado, só a grid ou só o Gantt. Só da sessão (o programa
    /// sempre abre em <see cref="ModoVisualizacao.Dividido"/>, como as outras opções de Exibir). A janela só
    /// muda a largura e a visibilidade dos painéis, que continuam com os dados de antes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarTabela), nameof(MostrarGantt), nameof(EhDividido), nameof(EhSomenteTabela), nameof(EhSomenteGantt))]
    public partial ModoVisualizacao ModoVisualizacao { get; set; } = ModoVisualizacao.Dividido;

    /// <summary>A grid de tarefas aparece (Dividido ou Somente Tabela).</summary>
    public bool MostrarTabela => ModoVisualizacao != ModoVisualizacao.SomenteGantt;

    /// <summary>O Gantt aparece (Dividido ou Somente Gantt).</summary>
    public bool MostrarGantt => ModoVisualizacao != ModoVisualizacao.SomenteTabela;

    public bool EhDividido => ModoVisualizacao == ModoVisualizacao.Dividido;
    public bool EhSomenteTabela => ModoVisualizacao == ModoVisualizacao.SomenteTabela;
    public bool EhSomenteGantt => ModoVisualizacao == ModoVisualizacao.SomenteGantt;

    /// <summary>Troca o modo de visualização (itens de Exibir e botões da faixa de acesso rápido).</summary>
    [RelayCommand]
    private void DefinirModoVisualizacao(ModoVisualizacao modo) => ModoVisualizacao = modo;

    /// <summary>
    /// "obra.pplan — ProjectPlanner 0.15.0": o arquivo (ou o nome do projeto, antes de salvar pela primeira
    /// vez) e a versão do programa, com * na frente quando há alterações não salvas.
    /// </summary>
    public string Titulo =>
        $"{(PossuiAlteracoesNaoSalvas ? "*" : "")}{NomeNoTitulo} — ProjectPlanner {VersaoDoProduto.Atual}";

    private string NomeNoTitulo => CaminhoArquivo is null ? _cronograma.Nome : Path.GetFileName(CaminhoArquivo);

    private string NomeParaExibir =>
        CaminhoArquivo is null ? _cronograma.Nome : Path.GetFileNameWithoutExtension(CaminhoArquivo);

    public string NomeProjeto
    {
        get => ValorExibido(nameof(NomeProjeto), _cronograma.Nome);
        set
        {
            if (AlterarDominio(nameof(NomeProjeto), value, value == _cronograma.Nome, () => _cronograma.Renomear(value)))
                MarcarAlteracao("Nome do projeto");
            else if (ErroDe(nameof(NomeProjeto)) is { } erro)
                Mensagem = erro;
        }
    }

    public DateTime? InicioProjeto
    {
        get => ValorExibido<DateTime?>(nameof(InicioProjeto), _cronograma.DataInicio);
        set
        {
            var inicio = value;
            if (AlterarDominio(nameof(InicioProjeto), inicio, inicio == _cronograma.DataInicio,
                () => _cronograma.AlterarDataInicio(inicio ?? throw new DomainValidationException(
                    nameof(InicioProjeto), "Informe a data de início do projeto."))))
                MarcarAlteracao("Início do projeto");
            else if (ErroDe(nameof(InicioProjeto)) is { } erro)
                Mensagem = erro;
        }
    }

    /// <summary>Pedido para a grid rolar até a linha (Bloco 25, P3): a janela faz a rolagem.</summary>
    public event Action<TarefaViewModel>? LinhaParaMostrar;

    /// <summary>
    /// Tarefa > Ir para a tarefa… (Bloco 25, P3): pergunta o Nº ou a EDT, seleciona a tarefa (abrindo os resumos recolhidos
    /// acima dela) e rola a grid e o Gantt até ela.
    /// </summary>
    [RelayCommand]
    private async Task IrParaTarefa()
    {
        if (Tarefas.Count == 0)
        {
            Mensagem = "O projeto ainda não tem tarefas.";
            return;
        }
        var texto = (await _dialogos.PedirTextoAsync("Ir para a tarefa", $"Nº (de 1 a {Tarefas.Count}) ou EDT da tarefa:",
            TarefaSelecionada?.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture)))?.Trim();
        if (string.IsNullOrEmpty(texto))
            return;
        var linha = int.TryParse(texto, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var numero)
            ? Tarefas.FirstOrDefault(t => t.Numero == numero)
            : Tarefas.FirstOrDefault(t => t.Edt == texto);
        if (linha is null)
        {
            Mensagem = $"Não existe a tarefa \"{texto}\". Use o Nº (de 1 a {Tarefas.Count}) ou a EDT, como 1.2.";
            return;
        }
        MostrarTarefa(linha);
    }

    /// <summary>Tarefa > Rolar até a tarefa (Bloco 25, P3): traz a selecionada para a vista na grid e no Gantt.</summary>
    [RelayCommand(CanExecute = nameof(HaTarefaSelecionada))]
    private void RolarAteTarefa() => MostrarTarefa(TarefaSelecionada!);

    // Abre os resumos recolhidos acima da linha, seleciona e pede as rolagens.
    private void MostrarTarefa(TarefaViewModel linha)
    {
        for (var pai = linha.Tarefa.TarefaPaiId; pai is { } id; pai = LinhaDe(id).Tarefa.TarefaPaiId)
            LinhaDe(id).Expandido = true;
        TarefaSelecionada = linha;
        LinhaParaMostrar?.Invoke(linha);
        Gantt.MostrarTarefa(linha.Tarefa.Id);
    }

    /// <summary>Arquivo > Abrir a pasta de cópias de segurança (Bloco 25, R2): a do projeto aberto, ou a geral.</summary>
    [RelayCommand]
    private Task AbrirBackups() => _dialogos.AbrirPastaAsync(CaminhoArquivo is { } caminho
        ? new BackupsDeProjetos(BackupsDeProjetos.PastaPadrao(), _relogio).PastaDo(caminho)
        : BackupsDeProjetos.PastaPadrao());

    /// <summary>Ajuda > Abrir a pasta de registros de erros (Bloco 25, R1).</summary>
    [RelayCommand]
    private Task AbrirRegistrosDeErros() => _dialogos.AbrirPastaAsync(RegistroDeErros.PastaPadrao());

    /// <summary>Exibir > Resumo do projeto… (Bloco 25, C7): o andamento na data de hoje, do último cálculo.</summary>
    [RelayCommand]
    private Task ResumoDoProjeto()
    {
        Recalcular();
        var hoje = _relogio.GetLocalNow().Date;
        return _dialogos.MostrarResumoDoProjetoAsync(new ResumoDoProjetoViewModel(
            Core.ResumoDoProjeto.Calcular(_cronograma, _andamento, hoje), hoje));
    }

    /// <summary>Ajuda > Sobre o ProjectPlanner (Bloco 25).</summary>
    [RelayCommand]
    private Task Sobre() => _dialogos.MostrarSobreAsync(new SobreViewModel());

    [RelayCommand]
    private void RecalcularCronograma()
    {
        if (Recalcular())
            Mensagem = "Cronograma recalculado.";
    }

    // A situação de cada tarefa no último cálculo (Bloco 25, C2).
    private IReadOnlyDictionary<Guid, AndamentoDaTarefa> _andamento = new Dictionary<Guid, AndamentoDaTarefa>();

    AndamentoDaTarefa? IEdicaoCronograma.AndamentoDe(Tarefa tarefa) => _andamento.GetValueOrDefault(tarefa.Id);

    /// <summary>A situação da tarefa na data de hoje, do último cálculo (Bloco 25, C2).</summary>
    public AndamentoDaTarefa? AndamentoDe(Guid tarefaId) => _andamento.GetValueOrDefault(tarefaId);

    string IEdicaoCronograma.TextoPredecessoras(Tarefa tarefa) =>
        NotacaoPredecessoras.Formatar(_cronograma.PredecessorasDe(tarefa.Id)
            .Select(p => new ItemPredecessora(NumeroDa(p.TarefaOrigemId), p.Tipo, p.Lag)));

    string? ILigacoesNoGantt.MotivoParaNaoLigar(Guid predecessoraId, Guid sucessoraId, TipoDependencia tipo) =>
        MotivoParaNaoLigar(predecessoraId, sucessoraId, tipo);

    bool ILigacoesNoGantt.Ligar(Guid predecessoraId, Guid sucessoraId, TipoDependencia tipo)
    {
        if (MotivoParaNaoLigar(predecessoraId, sucessoraId, tipo) is { } motivo)
        {
            Mensagem = motivo;
            return false;
        }

        _cronograma.AdicionarPredecessora(new Predecessora(predecessoraId, sucessoraId, tipo));
        MarcarAlteracao($"Vínculo {NomeDa(predecessoraId)} → {NomeDa(sucessoraId)}");
        if (Mensagem.Length == 0)
            Mensagem = $"Vínculo criado: {NomeDa(predecessoraId)} → {NomeDa(sucessoraId)} ({NotacaoPredecessoras.Sigla(tipo)}).";
        return true;
    }

    // As regras do cronograma (as mesmas da coluna Predecessoras), com uma mensagem mais útil quando
    // o par já está ligado: o vínculo existente não muda, e o tipo se troca na coluna Predecessoras.
    private string? MotivoParaNaoLigar(Guid predecessoraId, Guid sucessoraId, TipoDependencia tipo)
    {
        try
        {
            var vinculo = new Predecessora(predecessoraId, sucessoraId, tipo);
            if (_cronograma.Predecessoras.FirstOrDefault(p =>
                    p.TarefaOrigemId == predecessoraId && p.TarefaDestinoId == sucessoraId) is { } existente)
                return $"Já existe um vínculo de \"{NomeDa(predecessoraId)}\" para \"{NomeDa(sucessoraId)}\" " +
                       $"({NotacaoPredecessoras.Sigla(existente.Tipo)}). Para mudar o tipo, edite a coluna Predecessoras.";
            _cronograma.ValidarPredecessora(vinculo);
            return null;
        }
        catch (DomainValidationException ex)
        {
            return ex.Message;
        }
    }

    private string NomeDa(Guid tarefaId) => _cronograma.Tarefas.First(t => t.Id == tarefaId).Nome;

    string IEdicaoCronograma.EdtDe(Tarefa tarefa) =>
        _edt.TryGetValue(tarefa.Id, out var codigo) ? codigo : _cronograma.EdtDe(tarefa.Id);

    int IEdicaoCronograma.NivelDe(Tarefa tarefa) => _cronograma.NivelDe(tarefa.Id);

    void IEdicaoCronograma.ExpansaoMudou(TarefaViewModel linha)
    {
        if (_mudandoEstrutura)
            return;
        // A selecionada não fica escondida: se ela está dentro do resumo recolhido, a seleção vai para ele.
        if (!linha.Expandido && TarefaSelecionada is { } selecionada
            && _cronograma.DescendentesDe(linha.Tarefa.Id).Contains(selecionada.Tarefa))
            TarefaSelecionada = linha;
        AtualizarLinhasVisiveis(avisarOGantt: true);
    }

    string IEdicaoCronograma.TextoSucessoras(Tarefa tarefa) =>
        NotacaoPredecessoras.Formatar(_cronograma.Predecessoras
            .Where(p => p.TarefaOrigemId == tarefa.Id)
            .Select(p => new ItemPredecessora(NumeroDa(p.TarefaDestinoId), p.Tipo, p.Lag))
            .OrderBy(item => item.Linha));

    bool IEdicaoCronograma.MostrarCaminhoCritico => Gantt.MostrarCaminhoCritico;

    void IEdicaoCronograma.DefinirPredecessoras(Tarefa tarefa, string texto)
    {
        var novas = NotacaoPredecessoras.Interpretar(texto)
            .Select(item => new Predecessora(TarefaDaLinha(item.Linha).Id, tarefa.Id, item.Tipo, item.Lag))
            .ToList();
        var antigas = _cronograma.PredecessorasDe(tarefa.Id);

        foreach (var vinculo in antigas)
            _cronograma.RemoverPredecessora(vinculo.TarefaOrigemId, vinculo.TarefaDestinoId);
        var adicionadas = new List<Predecessora>();
        try
        {
            foreach (var vinculo in novas)
            {
                _cronograma.AdicionarPredecessora(vinculo);
                adicionadas.Add(vinculo);
            }
        }
        catch
        {
            // Desfaz tudo: a tarefa volta a ter exatamente as predecessoras de antes.
            foreach (var vinculo in adicionadas)
                _cronograma.RemoverPredecessora(vinculo.TarefaOrigemId, vinculo.TarefaDestinoId);
            foreach (var vinculo in antigas)
                _cronograma.AdicionarPredecessora(vinculo);
            throw;
        }
    }

    void IEdicaoCronograma.TarefaAlterada(string descricao) => MarcarAlteracao(descricao);

    void IEdicaoCronograma.AlteracaoRecusada(string mensagem) => Mensagem = mensagem;

    void IEdicaoCronograma.Avisar(string mensagem) => Mensagem = mensagem;

    Core.Planejamento.LinhaDeBase? IEdicaoCronograma.LinhaDeBase => _cronograma.LinhaDeBaseEmUso;

    void IEdicaoCronograma.VerificarContrato(Tarefa tarefa, bool soAUnidade) => _cronograma.Contrato.VerificarSePodeMudar(tarefa, soAUnidade);

    Core.Recursos.RecursosDoProjeto? IEdicaoCronograma.Recursos => _cronograma.Recursos;

    void IEdicaoCronograma.EdicaoCancelada() => Mensagem = "";

    // Uma alteração do cronograma (ou das fotos): entra no histórico do Desfazer, marca o projeto e recalcula.
    private PassoDoHistorico MarcarAlteracao(string descricao)
    {
        AvisoComDesfazer = null; // Bloco 72: o Desfazer do aviso valia para a alteração anterior
        var passo = RegistrarNoHistorico(descricao);
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
        Mensagem = "";
        // Duração (virar marco) e vínculos mudam se a tarefa de cima pode receber a selecionada.
        RebaixarCommand.NotifyCanExecuteChanged();
        Recalcular();
        return passo;
    }

    /// <returns>Se o cálculo deu certo.</returns>
    private bool Recalcular()
    {
        var calculou = true;
        try
        {
            var resultado = _calculadora.Calcular(_cronograma);
            TerminoProjeto = resultado.DataTermino;
            Gantt.Hoje = _relogio.GetLocalNow().Date;
            _andamento = AcompanhamentoDoCronograma.Calcular(_cronograma, Gantt.Hoje.Value);
            Gantt.TarefasAtrasadas = _andamento.Where(a => a.Value.Situacao == SituacaoDaTarefa.Atrasada).Select(a => a.Key).ToHashSet();
            Gantt.Atualizar(_cronograma, resultado, _ocultas);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = $"Não foi possível calcular o cronograma. {ex.Message}";
            Gantt.Limpar();
            calculou = false;
        }

        foreach (var linha in Tarefas)
            linha.AtualizarTela();
        AtualizarAlertasDeFotos();
        AtualizarOrdensNasLinhas();
        AtualizarBarraDeStatus();
        return calculou;
    }

    private void Carregar(Cronograma cronograma, string? caminho)
    {
        _cronograma = cronograma;
        _fotos.Recomecar(caminho);
        RecomecarHistorico();
        Assistente.RecomecarParaOutroProjeto();
        Tarefas.Clear();
        SincronizarLinhas();

        TarefaSelecionada = null;
        CaminhoArquivo = caminho;
        PossuiAlteracoesNaoSalvas = false;
        Mensagem = "";
        LimparRecusas();
        OnPropertyChanged(nameof(NomeProjeto));
        OnPropertyChanged(nameof(InicioProjeto));
        OnPropertyChanged(nameof(Titulo));
        Recalcular();
    }

    // Bloco 71: com o calendário dos projetos novos (o salvo; sem ele, o do Brasil). Sem as preferências (os testes), o padrão.
    private Cronograma NovoCronograma() => new(Guid.NewGuid(), NomePadrao, _relogio.GetLocalNow().Date,
        _preferenciasDoCalendario is null ? null : _preferenciasDoCalendario.Carregar() ?? CalendarioDoProjeto.Brasil);

    private TarefaViewModel LinhaDe(Guid tarefaId) => Tarefas.First(t => t.Tarefa.Id == tarefaId);

    // Depois de uma mudança na estrutura: refaz as linhas na ordem do cronograma, reaproveitando as
    // que já existiam (e o estado delas, como recolhido), renumera e atualiza a EDT e as visíveis. O
    // Gantt é atualizado pelo recálculo que vem em seguida, já com as tarefas novas calculadas.
    private void SincronizarLinhas()
    {
        var existentes = Tarefas.ToDictionary(l => l.Tarefa.Id);
        var linhas = _cronograma.Tarefas
            .Select(t => existentes.TryGetValue(t.Id, out var linha) ? linha : new TarefaViewModel(t, 0, this))
            .ToList();
        Reordenar(Tarefas, linhas);
        for (var i = 0; i < linhas.Count; i++)
            linhas[i].Numero = i + 1;
        _edt = _cronograma.NumeracaoEdt();
        AtualizarLinhasVisiveis(avisarOGantt: false);
        PromoverCommand.NotifyCanExecuteChanged();
        RebaixarCommand.NotifyCanExecuteChanged();
    }

    // Esconde as linhas que estão dentro de algum resumo recolhido (em qualquer nível acima).
    private void AtualizarLinhasVisiveis(bool avisarOGantt)
    {
        var porId = Tarefas.ToDictionary(l => l.Tarefa.Id);
        var ocultas = new HashSet<Guid>();
        foreach (var linha in Tarefas) // em pré-ordem, o resumo vem antes das subtarefas
            if (linha.Tarefa.TarefaPaiId is { } pai && (ocultas.Contains(pai) || !porId[pai].Expandido))
                ocultas.Add(linha.Tarefa.Id);

        _ocultas = ocultas;
        Reordenar(LinhasVisiveis, Tarefas.Where(l => !ocultas.Contains(l.Tarefa.Id)).ToList());
        if (avisarOGantt)
            Gantt.DefinirTarefasOcultas(ocultas);
    }

    // Leva a coleção ao conteúdo e à ordem desejados mexendo só no que mudou, para a grid não perder
    // a rolagem. Uma linha que muda de lugar sai e entra de novo: o DataGrid não reposiciona a linha
    // num Move (ela continuaria desenhada no lugar antigo).
    private static void Reordenar(ObservableCollection<TarefaViewModel> colecao, IReadOnlyList<TarefaViewModel> desejada)
    {
        var manter = desejada.ToHashSet();
        for (var i = colecao.Count - 1; i >= 0; i--)
            if (!manter.Contains(colecao[i]))
                colecao.RemoveAt(i);
        for (var i = 0; i < desejada.Count; i++)
        {
            if (i < colecao.Count && ReferenceEquals(colecao[i], desejada[i]))
                continue;
            var atual = colecao.IndexOf(desejada[i]);
            if (atual >= 0)
                colecao.RemoveAt(atual);
            colecao.Insert(i, desejada[i]);
        }
    }

    private int NumeroDa(Guid tarefaId) => Tarefas.First(t => t.Tarefa.Id == tarefaId).Numero;

    private Tarefa TarefaDaLinha(int linha) =>
        linha >= 1 && linha <= Tarefas.Count
            ? Tarefas[linha - 1].Tarefa
            : throw new FormatException($"Não existe a linha {linha}.");
}
