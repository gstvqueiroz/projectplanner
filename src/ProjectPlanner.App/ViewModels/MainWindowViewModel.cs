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
        IConsultorDeOrcamento? consultorDeOrcamento = null, IAnalistaDaObra? analistaDaObra = null)
    {
        _analistaDaObra = analistaDaObra ?? new AnalistaDaObraIndisponivel();
        _consultor = consultorDeOrcamento ?? new ConsultorIndisponivel();
        _biblioteca = bibliotecaDeReferencias ?? new BibliotecaDeReferenciasEmMemoria();
        _catalogoDeRecursos = catalogoDeRecursos ?? new CatalogoDeRecursosEmMemoria();
        _preferenciasRelatorio = preferenciasRelatorio ?? new PreferenciasRelatorioEmMemoria();
        _analista = analistaDeFotos ?? new AnalistaIndisponivel();
        _fotos = new FotosDoProjeto(repositorio, processadorDeImagens, relogio);
        Colunas = new ColunasViewModel(preferenciasColunas);
        Assistente = new AssistenteViewModel(assistente, dialogos, relogio, preferenciasInterface, configuracaoAssistente, this);
        _repositorio = repositorio;
        _calculadora = calculadora;
        _dialogos = dialogos;
        _relogio = relogio;
        _preferenciasInterface = preferenciasInterface;
        Barra = new BarraDeIconesViewModel(preferenciasInterface);
        Tema = preferenciasInterface.Carregar()?.Tema ?? TemaDaInterface.Sistema;
        _cronograma = NovoCronograma();
        Gantt.BarraClicada += tarefaId => TarefaSelecionada = LinhasVisiveis.FirstOrDefault(t => t.Tarefa.Id == tarefaId);
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
            ProjectPlanner.Relatorios.TextosDoOrcamento.ParaOAssistente(_cronograma.Orcamento.Planilha()));
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

    partial void OnTarefaSelecionadaChanged(TarefaViewModel? value) => AtualizarDestaqueNoGantt();

    partial void OnMostrarSelecaoChanged(bool value) => AtualizarDestaqueNoGantt();

    private void AtualizarDestaqueNoGantt() =>
        Gantt.TarefaDestacada = MostrarSelecao ? TarefaSelecionada?.Tarefa.Id : null;

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

    [RelayCommand]
    private async Task NovoProjeto()
    {
        if (await PodeDescartarAlteracoesAsync())
            Carregar(NovoCronograma(), caminho: null);
    }

    [RelayCommand]
    private async Task AbrirProjeto()
    {
        if (!await PodeDescartarAlteracoesAsync())
            return;
        var caminho = await _dialogos.EscolherArquivoParaAbrirAsync();
        if (caminho is not null)
            AbrirArquivo(caminho);
    }

    /// <summary>
    /// Abre um projeto pedido pelo sistema: duplo clique ou "Abrir com" num arquivo .pplan, argumentos da
    /// linha de comando, ou arquivos entregues pelo Finder no macOS (também com o programa já aberto).
    /// Vale o primeiro projeto (ver <see cref="ArquivosPedidos"/>), com a mesma pergunta sobre alterações
    /// pendentes e as mesmas mensagens de erro do "Abrir…"; os outros não são abertos (um projeto por
    /// janela), e a barra de status avisa. Sem nenhum projeto nos argumentos, não faz nada.
    /// </summary>
    public async Task AbrirPedidoDoSistemaAsync(IEnumerable<string?> arquivos)
    {
        var pedido = ArquivosPedidos.Interpretar(arquivos);
        if (pedido.Caminho is null || !await PodeDescartarAlteracoesAsync())
            return;

        AbrirArquivo(pedido.Caminho);
        if (pedido.NaoAbertos > 0)
        {
            var quais = pedido.NaoAbertos == 1
                ? "1 outro arquivo não foi aberto"
                : $"{pedido.NaoAbertos} outros arquivos não foram abertos";
            // O aviso vem antes, para não ser cortado pela barra de status depois de um caminho longo.
            Mensagem = $"O ProjectPlanner abre um projeto por janela: {quais}. {Mensagem}";
        }
    }

    private void AbrirArquivo(string caminho)
    {
        try
        {
            var aberto = _repositorio.Abrir(caminho);
            Carregar(aberto, caminho);
            _caminhoNaMensagem = caminho;
            Mensagem = $"Projeto aberto: {caminho}";
            if (aberto.ConversaDescartadaAoAbrir)
                Mensagem += " A conversa com o assistente guardada no arquivo não pôde ser lida e ficou de fora.";
            if (aberto.FotosDescartadasAoAbrir > 0)
                Mensagem += aberto.FotosDescartadasAoAbrir == 1
                    ? " 1 registro de exemplo de foto (sem imagem) foi descartado; ele some do arquivo ao salvar."
                    : $" {aberto.FotosDescartadasAoAbrir} registros de exemplo de fotos (sem imagem) foram descartados; eles somem do arquivo ao salvar.";
        }
        catch (Exception ex)
        {
            // Qualquer falha de arquivo vira mensagem; o projeto atual continua aberto.
            Mensagem = $"Não foi possível abrir o projeto. {MensagensDeErro.Descrever(ex)}";
        }
    }

    [RelayCommand]
    private Task SalvarProjeto() => SalvarAsync();

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
    private Task SalvarComoProjeto() => SalvarAsync(perguntarOndeSalvar: true);

    /// <summary>Adiciona uma tarefa no fim da lista, no primeiro nível.</summary>
    [RelayCommand]
    private void AdicionarTarefa()
    {
        var tarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        _cronograma.AdicionarTarefa(tarefa);
        AposMudarAEstrutura(selecionar: tarefa, "Adicionar tarefa");
    }

    /// <summary>
    /// Tarefa > Inserir tarefa acima e o menu de contexto da grid (Bloco 21): uma tarefa nova logo antes da
    /// tarefa da linha (ou da selecionada), entre as irmãs dela.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeInserirJuntoDe))]
    private void InserirTarefaAcima(TarefaViewModel? linha) => InserirJuntoDe(linha ?? TarefaSelecionada!, depois: false);

    /// <summary>
    /// Inserir tarefa abaixo: logo depois da tarefa da linha (ou da selecionada) e das subtarefas dela, no mesmo
    /// nível (abaixo de um resumo, depois do resumo inteiro; subtarefa nova é com Adicionar subtarefa).
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeInserirJuntoDe))]
    private void InserirTarefaAbaixo(TarefaViewModel? linha) => InserirJuntoDe(linha ?? TarefaSelecionada!, depois: true);

    private bool PodeInserirJuntoDe(TarefaViewModel? linha) => (linha ?? TarefaSelecionada) is not null;

    // A tarefa nova é como a do Adicionar tarefa; só a posição muda. O cronograma é recalculado depois, como
    // em qualquer tarefa nova (ela entra no CPM).
    private void InserirJuntoDe(TarefaViewModel referencia, bool depois)
    {
        var pai = referencia.Tarefa.TarefaPaiId;
        var posicao = _cronograma.Tarefas.Where(t => t.TarefaPaiId == pai).ToList().IndexOf(referencia.Tarefa) + (depois ? 1 : 0);
        var tarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        if (MudarEstrutura(() => _cronograma.InserirTarefaNaPosicao(tarefa, pai, posicao)))
            AposMudarAEstrutura(selecionar: tarefa, "Inserir tarefa");
    }

    /// <summary>Acrescenta um marco (tarefa de duração 0) no fim da lista, como o Adicionar tarefa.</summary>
    [RelayCommand]
    private void AdicionarMarco()
    {
        var marco = new Tarefa("Novo marco", _cronograma.DataInicio, 0);
        _cronograma.AdicionarTarefa(marco);
        AposMudarAEstrutura(selecionar: marco, "Adicionar marco");
    }

    /// <summary>
    /// Acrescenta no fim da lista um resumo com uma subtarefa dentro (um resumo sem subtarefas seria só
    /// uma tarefa), e seleciona a subtarefa, como o MS Project faz sem nada selecionado.
    /// </summary>
    [RelayCommand]
    private void AdicionarResumo()
    {
        var resumo = new Tarefa("Novo resumo", _cronograma.DataInicio, 1);
        var subtarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        _cronograma.AdicionarTarefa(resumo);
        _cronograma.AdicionarTarefa(subtarefa, resumo.Id);
        SincronizarLinhas();
        Expandir(LinhaDe(resumo.Id));
        AposMudarAEstrutura(selecionar: subtarefa, "Adicionar resumo");
    }

    /// <summary>Adiciona uma tarefa como última subtarefa da selecionada, que vira resumo (e é expandido).</summary>
    [RelayCommand(CanExecute = nameof(HaTarefaSelecionada))]
    private void AdicionarSubtarefa()
    {
        var pai = TarefaSelecionada!;
        var tarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        if (!MudarEstrutura(() => _cronograma.AdicionarTarefa(tarefa, pai.Tarefa.Id)))
            return;
        Expandir(pai);
        AposMudarAEstrutura(selecionar: tarefa, $"Adicionar subtarefa em \"{pai.Nome}\"");
    }

    /// <summary>
    /// Recua a selecionada um nível (Alt+Shift+→): ela vira a última subtarefa da tarefa de cima no
    /// mesmo nível, que vira resumo (e é expandido).
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeRebaixar))]
    private void Rebaixar()
    {
        var linha = TarefaSelecionada!;
        if (!MudarEstrutura(() => _cronograma.Rebaixar(linha.Tarefa.Id)))
            return;
        Expandir(LinhaDe(linha.Tarefa.TarefaPaiId!.Value));
        AposMudarAEstrutura(selecionar: linha.Tarefa, $"Rebaixar \"{linha.Nome}\"");
    }

    /// <summary>
    /// Sobe a selecionada um nível (Alt+Shift+←): ela sai do resumo em que está e vai para logo depois
    /// dele; as tarefas abaixo dela continuam no resumo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodePromover))]
    private void Promover()
    {
        var linha = TarefaSelecionada!;
        if (MudarEstrutura(() => _cronograma.Promover(linha.Tarefa.Id)))
            AposMudarAEstrutura(selecionar: linha.Tarefa, $"Promover \"{linha.Nome}\"");
    }

    /// <summary>Remove a selecionada; um resumo sai com todas as subtarefas, depois de confirmar.</summary>
    [RelayCommand(CanExecute = nameof(HaTarefaSelecionada))]
    private async Task RemoverTarefa()
    {
        var linha = TarefaSelecionada!;
        var descendentes = _cronograma.DescendentesDe(linha.Tarefa.Id);
        var fotos = linha.Tarefa.Fotos.Count + descendentes.Sum(t => t.Fotos.Count);
        // Bloco 22: as fotos vão junto com a tarefa (e com as subtarefas de um resumo); avisa quantas.
        var aviso = fotos switch
        {
            0 => "",
            1 => linha.EhResumo ? " A foto delas também será excluída." : " A foto dela também será excluída.",
            _ => linha.EhResumo ? $" As {fotos} fotos delas também serão excluídas." : $" As {fotos} fotos dela também serão excluídas.",
        };
        if (linha.EhResumo)
        {
            var quais = descendentes.Count == 1 ? "a subtarefa dele" : $"as {descendentes.Count} subtarefas dele";
            if (!await _dialogos.ConfirmarAsync("Remover resumo", $"Remover o resumo \"{linha.Nome}\" e {quais}?{aviso}", "Remover"))
                return;
        }
        else if (fotos > 0 && !await _dialogos.ConfirmarAsync("Remover tarefa", $"Remover a tarefa \"{linha.Nome}\"?{aviso}", "Remover"))
            return;

        var posicao = LinhasVisiveis.IndexOf(linha);
        _cronograma.RemoverTarefa(linha.Tarefa.Id);
        SincronizarLinhas();
        TarefaSelecionada = LinhasVisiveis.Count == 0 ? null : LinhasVisiveis[Math.Clamp(posicao, 0, LinhasVisiveis.Count - 1)];
        MarcarAlteracao($"Remover \"{linha.Nome}\"");
    }

    private bool HaTarefaSelecionada() => TarefaSelecionada is not null;

    /// <summary>
    /// Abre a galeria de fotos da tarefa, tenha ela fotos ou não (ícone de câmera no Gantt e na grid, menu
    /// Tarefa e menus de contexto). Uma foto adicionada na galeria entra na tarefa e marca o projeto como
    /// alterado; a câmera da grid e do Gantt aparece na hora.
    /// </summary>
    public Task MostrarFotosAsync(Guid tarefaId)
    {
        var linha = Tarefas.First(t => t.Tarefa.Id == tarefaId);
        return _dialogos.MostrarFotosAsync(new GaleriaFotosViewModel(linha, _fotos, _dialogos, _relogio, () =>
        {
            linha.FotosMudaram();
            OnPropertyChanged(nameof(FotosDaSelecionada));
            OnPropertyChanged(nameof(DicaFotosDaSelecionada));
            MarcarAlteracao($"Fotos de \"{linha.Nome}\"");
        }, SugestoesDeLocal));
    }

    /// <summary>
    /// Tarefa > Gerenciar fotos… e os menus de contexto da grid e do Gantt: a galeria da tarefa da linha
    /// (<paramref name="linha"/>) ou, sem ela, da selecionada.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeGerenciarFotos))]
    private Task GerenciarFotos(TarefaViewModel? linha) => MostrarFotosAsync((linha ?? TarefaSelecionada)!.Tarefa.Id);

    private bool PodeGerenciarFotos(TarefaViewModel? linha) => (linha ?? TarefaSelecionada) is not null;

    /// <summary>A dica do item Gerenciar fotos… (desabilitado sem tarefa selecionada, com o motivo).</summary>
    public string DicaGerenciarFotos => TarefaSelecionada is null
        ? "Selecione uma tarefa para gerenciar suas fotos"
        : "Ver as fotos da tarefa selecionada e adicionar novas";

    void IEdicaoCronograma.GerenciarFotos(TarefaViewModel linha) => _ = MostrarFotosAsync(linha.Tarefa.Id);

    /// <summary>A mensagem quando uma tarefa é solta fora das irmãs dela (Bloco 18).</summary>
    public const string MensagemReordenacaoInvalida = "Só dá para mover entre tarefas do mesmo nível (mesmo resumo).";

    /// <summary>
    /// Onde a tarefa arrastada cairia ao ser solta sobre a linha visível <paramref name="linhaVisivel"/> (nula
    /// fora das linhas: cabeçalho, linhas vazias): antes dela na metade de cima, depois dela e das subtarefas na
    /// metade de baixo. Nulo quando não vale: a linha não é irmã da arrastada (outro nível ou resumo) ou é a
    /// própria. O arraste só reordena irmãs; mudar de pai continua sendo com Promover e Rebaixar.
    /// </summary>
    public DestinoDeReordenacao? DestinoAoSoltar(TarefaViewModel arrastada, int? linhaVisivel, bool metadeDeBaixo)
    {
        ArgumentNullException.ThrowIfNull(arrastada);
        if (linhaVisivel is not { } indice || indice < 0 || indice >= LinhasVisiveis.Count)
            return null;
        var alvo = LinhasVisiveis[indice];
        var pai = arrastada.Tarefa.TarefaPaiId;
        if (alvo == arrastada || alvo.Tarefa.TarefaPaiId != pai)
            return null;

        var outrasIrmas = _cronograma.Tarefas.Where(t => t.TarefaPaiId == pai && t != arrastada.Tarefa).ToList();
        var posicao = outrasIrmas.IndexOf(alvo.Tarefa) + (metadeDeBaixo ? 1 : 0);
        var guia = indice;
        if (metadeDeBaixo)
        {
            var subtarefas = _cronograma.DescendentesDe(alvo.Tarefa.Id).Select(t => t.Id).ToHashSet();
            guia++;
            while (guia < LinhasVisiveis.Count && subtarefas.Contains(LinhasVisiveis[guia].Tarefa.Id))
                guia++;
        }
        return new DestinoDeReordenacao(posicao, guia);
    }

    /// <summary>
    /// Solta a tarefa arrastada na grid (ver <see cref="DestinoAoSoltar"/>). Num destino válido, move a tarefa
    /// (com as subtarefas) entre as irmãs, renumera a grid e a EDT e redesenha o Gantt na nova ordem, sem
    /// recalcular o cronograma: a ordem não muda datas nem caminho crítico. Num destino inválido, não muda
    /// nada e avisa na barra de status.
    /// </summary>
    /// <returns>Se a ordem mudou.</returns>
    public bool SoltarTarefa(TarefaViewModel arrastada, int? linhaVisivel, bool metadeDeBaixo)
    {
        if (DestinoAoSoltar(arrastada, linhaVisivel, metadeDeBaixo) is not { } destino)
        {
            Mensagem = MensagemReordenacaoInvalida;
            return false;
        }
        if (!_cronograma.MoverEntreIrmas(arrastada.Tarefa.Id, destino.PosicaoEntreIrmas))
            return false;

        SincronizarLinhas();
        foreach (var linha in Tarefas)
            linha.AtualizarTela(); // EDT e predecessoras/sucessoras (que usam o Nº das linhas)
        TarefaSelecionada = arrastada;
        Gantt.RedesenharNaNovaOrdem();
        RegistrarNoHistorico($"Mover \"{arrastada.Nome}\"");
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
        Mensagem = $"\"{arrastada.Nome}\" agora é a {arrastada.Edt}.";
        return true;
    }

    private bool PodePromover() => TarefaSelecionada?.Tarefa.TarefaPaiId is not null;

    // A mesma regra do domínio: há tarefa acima no mesmo nível, e ela pode virar resumo (não é marco nem tem vínculos).
    private bool PodeRebaixar() => TarefaSelecionada is { } linha && _cronograma.PodeRebaixar(linha.Tarefa.Id);

    /// <summary>
    /// Num resumo, duração, datas, % e predecessoras vêm das subtarefas: a grid não entra em edição
    /// nessas células e avisa por quê.
    /// </summary>
    public void AvisarValorCalculadoNoResumo() =>
        Mensagem = "Num resumo, esse valor é calculado a partir das subtarefas.";

    /// <summary>Bloco 36: tentou editar a unidade ou a quantidade de um resumo.</summary>
    public void AvisarSemUnidadeNoResumo() => Mensagem = Tarefa.SemUnidadeNoResumo;

    // Aplica uma mudança de estrutura no cronograma; se o domínio recusar, só mostra o motivo.
    private bool MudarEstrutura(Action mudanca)
    {
        try
        {
            mudanca();
            return true;
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
    }

    // Expande o resumo que acabou de receber uma subtarefa. As linhas visíveis e o Gantt são refeitos
    // logo em seguida (AposMudarAEstrutura), já com a tarefa nova calculada.
    private void Expandir(TarefaViewModel resumo)
    {
        _mudandoEstrutura = true;
        try
        {
            resumo.Expandido = true;
        }
        finally
        {
            _mudandoEstrutura = false;
        }
    }

    private void AposMudarAEstrutura(Tarefa selecionar, string descricao)
    {
        SincronizarLinhas();
        TarefaSelecionada = LinhaDe(selecionar.Id);
        MarcarAlteracao(descricao);
    }

    [RelayCommand]
    private void RecalcularCronograma()
    {
        if (Recalcular())
            Mensagem = "Cronograma recalculado.";
    }

    /// <summary>
    /// Salva no arquivo atual. Pergunta onde salvar quando o projeto nunca foi salvo ou quando
    /// <paramref name="perguntarOndeSalvar"/> é verdadeiro ("Salvar como"); o arquivo escolhido
    /// passa a ser o do projeto.
    /// </summary>
    /// <returns>Se o projeto foi salvo.</returns>
    public async Task<bool> SalvarAsync(bool perguntarOndeSalvar = false)
    {
        var caminho = perguntarOndeSalvar || CaminhoArquivo is null
            ? await _dialogos.EscolherArquivoParaSalvarAsync(NomeParaExibir)
            : CaminhoArquivo;
        if (caminho is null)
            return false;

        // Bloco 30: as imagens que o Desfazer pode trazer de volta e que só estão no arquivo de origem vão para a memória
        // antes de ele ser substituído.
        var fotosAbertas = _cronograma.Tarefas.SelectMany(t => t.FotosNaOrdemDeCadastro).Select(f => f.Id).ToHashSet();
        _fotos.Preservar(_historico.FotosNoHistorico().Where(id => !fotosAbertas.Contains(id)));
        try
        {
            _repositorio.Salvar(_cronograma, caminho, _fotos.ParaSalvar());
        }
        catch (Exception ex)
        {
            // Sem permissão, disco cheio, arquivo em uso...: vira mensagem, e as alterações continuam pendentes.
            Mensagem = $"Não foi possível salvar o projeto. {MensagensDeErro.Descrever(ex)}";
            return false;
        }

        _fotos.Salvou(caminho, fotosAbertas);
        _historico.MarcarSalvo();
        _alteracoesForaDoHistorico = false;
        CaminhoArquivo = caminho;
        PossuiAlteracoesNaoSalvas = false;
        _caminhoNaMensagem = caminho;
        Mensagem = $"Projeto salvo em {caminho}";
        return true;
    }

    /// <summary>
    /// Se houver alterações não salvas, pergunta o que fazer com elas (Salvar, Não salvar ou Cancelar).
    /// Usado antes de Novo, Abrir e ao fechar a janela.
    /// </summary>
    /// <returns>Se pode seguir e descartar o projeto atual.</returns>
    public async Task<bool> PodeDescartarAlteracoesAsync()
    {
        if (!PossuiAlteracoesNaoSalvas)
            return true;

        return await _dialogos.PerguntarSobreAlteracoesPendentesAsync(NomeParaExibir) switch
        {
            RespostaAlteracoesPendentes.Salvar => await SalvarAsync(),
            RespostaAlteracoesPendentes.NaoSalvar => true,
            _ => false,
        };
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

    private Cronograma NovoCronograma() => new(NomePadrao, _relogio.GetLocalNow().Date);

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
