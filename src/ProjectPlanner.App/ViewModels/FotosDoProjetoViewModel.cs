using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma tarefa numa lista (filtro ou importação), com a EDT; <see cref="Id"/> nulo é "todas" ou "não importar".</summary>
public sealed record OpcaoDeTarefa(Guid? Id, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>Uma miniatura da linha do tempo, com a tarefa a que pertence.</summary>
public sealed partial class FotoNoProjeto(FotoTarefa foto, byte[]? miniatura, string tarefa) : ObservableObject
{
    public FotoTarefa Foto { get; } = foto;

    public byte[]? Miniatura { get; } = miniatura;

    /// <summary>"3 Fundações".</summary>
    public string Tarefa { get; } = tarefa;

    public string Rotulo => Foto.Titulo ?? Foto.Descricao ?? Foto.Imagem.NomeArquivoOriginal;

    public string Hora => Foto.DataFoto.ToString("HH:mm", CultureInfo.InvariantCulture);

    public bool TemGps => Foto.Gps is not null;

    [ObservableProperty]
    public partial bool Selecionada { get; set; }
}

/// <summary>As fotos de um dia na linha do tempo.</summary>
public sealed record GrupoNoProjeto(string Cabecalho, IReadOnlyList<FotoNoProjeto> Fotos);

/// <summary>Uma foto lida na importação em lote: a tarefa sugerida pela data, que o usuário pode trocar.</summary>
public sealed partial class LinhaDaImportacao : ObservableObject
{
    internal LinhaDaImportacao(FotoLida lida, IReadOnlyList<OpcaoDeTarefa> tarefas, OpcaoDeTarefa escolhida, string situacao)
    {
        Lida = lida;
        Tarefas = tarefas;
        Situacao = situacao;
        TarefaEscolhida = escolhida;
    }

    internal FotoLida Lida { get; }

    public string Arquivo => Lida.Importada?.NomeArquivoOriginal ?? Path.GetFileName(Lida.Caminho);

    public byte[]? Miniatura => Lida.Importada?.Conteudo.Miniatura;

    /// <summary>"28/09/2026 07:15 (EXIF)" ou "(data do arquivo)".</summary>
    public string Data => Lida.Importada is { } i
        ? i.DataFoto.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) +
          (i.OrigemDaData == OrigemDaData.ArquivoModificado ? " (data do arquivo)" : " (EXIF)")
        : "";

    public bool Recusada => Lida.Importada is null;

    public bool PodeEscolher => !Recusada;

    /// <summary>"(não importar)" e as tarefas que podem receber fotos.</summary>
    public IReadOnlyList<OpcaoDeTarefa> Tarefas { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Importar))]
    public partial OpcaoDeTarefa TarefaEscolhida { get; set; }

    public bool Importar => !Recusada && TarefaEscolhida?.Id is not null;

    /// <summary>Por que a tarefa é essa (sugerida pela data, já está no projeto, recusada...).</summary>
    public string Situacao { get; }
}

/// <summary>
/// Exibir > Fotos do projeto… (Bloco 28): FT1, todas as fotos numa linha do tempo, com filtros por período,
/// tarefa, local, tag e texto; FT2, a importação em lote, com a tarefa sugerida pela data numa tabela para
/// conferir antes de importar; FT4 e FT5, os locais padronizados e os dias do alerta de tarefa sem foto.
/// </summary>
public sealed partial class FotosDoProjetoViewModel : ViewModelBase
{
    public const string TodasAsTarefas = "(todas as tarefas)";
    public const string TodosOsLocais = "(todos os locais)";
    public const string NaoImportar = "(não importar)";
    public const string SemFotos = "Nenhuma foto no projeto ainda. Use a aba Importar em lote, ou a galeria de uma tarefa.";
    public const string SemResultado = "Nenhuma foto com esses filtros.";

    public const int AbaLinhaDoTempo = 0;
    public const int AbaImportacao = 1;
    public const int AbaAntesEDepois = 2;
    public const int AbaSugestoesDaIa = 3;
    public const int AbaLocaisEAlerta = 4;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly OpcaoDeTarefa OpcaoTodas = new(null, TodasAsTarefas);
    private static readonly OpcaoDeTarefa OpcaoNaoImportar = new(null, NaoImportar);

    private readonly Cronograma _cronograma;
    private readonly FotosDoProjeto _fotos;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Action<IReadOnlyCollection<Guid>, string> _fotosMudaram;
    private readonly Action _configuracaoMudou;
    private readonly Func<Guid, Task> _abrirGaleria;
    private IReadOnlyList<FotoTarefa> _filtradas = [];
    private IReadOnlyDictionary<Guid, string> _nomes = new Dictionary<Guid, string>();
    private bool _atualizando;

    /// <param name="fotosMudaram">
    /// Estas tarefas mudaram (fotos importadas; Bloco 29: legendas ou % sugeridos pela IA aplicados), com o nome da
    /// alteração para o Desfazer: a grid, o Gantt e o alerta se atualizam, e o projeto fica alterado.
    /// </param>
    /// <param name="configuracaoMudou">Os locais ou os dias do alerta mudaram.</param>
    /// <param name="abrirGaleria">Abre a galeria da tarefa e espera ser fechada.</param>
    /// <param name="analista">Bloco 29: a IA das sugestões (sem ela, a aba avisa que falta configurar).</param>
    public FotosDoProjetoViewModel(Cronograma cronograma, FotosDoProjeto fotos, IDialogosService dialogos, TimeProvider relogio,
        Action<IReadOnlyCollection<Guid>, string> fotosMudaram, Action configuracaoMudou, Func<Guid, Task> abrirGaleria,
        ProjectPlanner.IA.IAnalistaDeFotos? analista = null)
    {
        _analista = analista ?? new ProjectPlanner.IA.AnalistaIndisponivel();
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(fotos);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(fotosMudaram);
        ArgumentNullException.ThrowIfNull(configuracaoMudou);
        ArgumentNullException.ThrowIfNull(abrirGaleria);
        (_cronograma, _fotos, _dialogos, _relogio, _fotosMudaram, _configuracaoMudou, _abrirGaleria) =
            (cronograma, fotos, dialogos, relogio, fotosMudaram, configuracaoMudou, abrirGaleria);
        AtalhoEscolhido = Atalhos[0];
        TarefaFiltro = OpcaoTodas;
        LocalFiltro = TodosOsLocais;
        TagFiltro = GaleriaFotosViewModel.TodasAsTags;
        CarregarConfiguracao();
        Atualizar();
    }

    public string Titulo => $"Fotos do projeto — {_cronograma.Nome}";

    [ObservableProperty] public partial int AbaEscolhida { get; set; }

    private DateTime Hoje => _relogio.GetLocalNow().DateTime;

    private IEnumerable<FotoTarefa> TodasAsFotos => _cronograma.Tarefas.SelectMany(t => t.FotosNaOrdemDeCadastro);

    // ======== FT1: linha do tempo ========

    public IReadOnlyList<OpcaoDeAtalho> Atalhos { get; } =
    [
        new(AtalhoDePeriodo.Tudo, "Todas as datas"),
        new(AtalhoDePeriodo.Hoje, "Hoje"),
        new(AtalhoDePeriodo.Ultimos7Dias, "Últimos 7 dias"),
        new(AtalhoDePeriodo.EsteMes, "Este mês"),
    ];

    [ObservableProperty] public partial OpcaoDeAtalho AtalhoEscolhido { get; set; }
    [ObservableProperty] public partial DateTime? DeFiltro { get; set; }
    [ObservableProperty] public partial DateTime? AteFiltro { get; set; }
    [ObservableProperty] public partial OpcaoDeTarefa? TarefaFiltro { get; set; }
    [ObservableProperty] public partial string? LocalFiltro { get; set; }
    [ObservableProperty] public partial string? TagFiltro { get; set; }
    [ObservableProperty] public partial string TextoFiltro { get; set; } = "";
    [ObservableProperty] public partial bool MaisRecentesPrimeiro { get; set; } = true;

    partial void OnAtalhoEscolhidoChanged(OpcaoDeAtalho value) => Atualizar();
    partial void OnDeFiltroChanged(DateTime? value) => Atualizar();
    partial void OnAteFiltroChanged(DateTime? value) => Atualizar();
    partial void OnTarefaFiltroChanged(OpcaoDeTarefa? value) => Atualizar();
    partial void OnLocalFiltroChanged(string? value) => Atualizar();
    partial void OnTagFiltroChanged(string? value) => Atualizar();
    partial void OnTextoFiltroChanged(string value) => Atualizar();
    partial void OnMaisRecentesPrimeiroChanged(bool value) => Atualizar();

    /// <summary>"(todas as tarefas)" e as tarefas que têm fotos, na ordem da EDT.</summary>
    [ObservableProperty] public partial IReadOnlyList<OpcaoDeTarefa> TarefasComFotos { get; private set; } = [];

    [ObservableProperty] public partial IReadOnlyList<string> LocaisDoFiltro { get; private set; } = [];

    [ObservableProperty] public partial IReadOnlyList<string> TagsDoFiltro { get; private set; } = [];

    [ObservableProperty] public partial string ErroDeFiltro { get; private set; } = "";

    [RelayCommand]
    private void LimparFiltros() =>
        (AtalhoEscolhido, DeFiltro, AteFiltro, TarefaFiltro, LocalFiltro, TagFiltro, TextoFiltro) =
        (Atalhos[0], null, null, OpcaoTodas, TodosOsLocais, GaleriaFotosViewModel.TodasAsTags, "");

    public ObservableCollection<GrupoNoProjeto> Grupos { get; } = [];

    /// <summary>"40 fotos em 6 tarefas" ou, com filtro, "12 de 40 fotos".</summary>
    [ObservableProperty] public partial string Contador { get; private set; } = "";

    [ObservableProperty] public partial bool Vazia { get; private set; }

    [ObservableProperty] public partial bool NadaComOsFiltros { get; private set; }

    /// <summary>As fotos visíveis, na ordem da linha do tempo (a do visualizador).</summary>
    public IReadOnlyList<FotoTarefa> FotosFiltradas => _filtradas;

    private IEnumerable<FotoNoProjeto> Itens => Grupos.SelectMany(g => g.Fotos);

    /// <summary>Refaz as listas dos filtros e a linha do tempo, mantendo os filtros e a foto selecionada.</summary>
    public void Atualizar()
    {
        // Trocar as listas faz o ComboBox desmarcar e remarcar a escolha, o que chamaria Atualizar de novo.
        if (_atualizando)
            return;
        _atualizando = true;
        try
        {
            RefazerListasERefiltrar();
        }
        finally
        {
            _atualizando = false;
        }
    }

    private void RefazerListasERefiltrar()
    {
        var edt = _cronograma.NumeracaoEdt();
        _nomes = _cronograma.Tarefas.ToDictionary(t => t.Id, t => $"{edt[t.Id]} {t.Nome}");
        var todas = TodasAsFotos.ToList();

        var (tarefa, local, tag) = (TarefaFiltro?.Id, LocalFiltro, TagFiltro);
        TarefasComFotos = [OpcaoTodas, .. _cronograma.Tarefas.Where(t => t.FotosNaOrdemDeCadastro.Count > 0).Select(t => new OpcaoDeTarefa(t.Id, _nomes[t.Id]))];
        LocaisDoFiltro = [TodosOsLocais, .. _cronograma.ConfiguracaoDasFotos.Sugestoes(todas)];
        TagsDoFiltro = [GaleriaFotosViewModel.TodasAsTags, .. FiltroDeFotos.Tags(todas)];
        // A escolha volta depois de trocar as listas; a que sumiu delas (a última foto daquela tarefa foi excluída) vira "todas".
        TarefaFiltro = TarefasComFotos.FirstOrDefault(t => t.Id == tarefa) ?? OpcaoTodas;
        LocalFiltro = LocaisDoFiltro.FirstOrDefault(l => string.Equals(l, local, StringComparison.CurrentCultureIgnoreCase)) ?? TodosOsLocais;
        TagFiltro = TagsDoFiltro.FirstOrDefault(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)) ?? GaleriaFotosViewModel.TodasAsTags;

        var atual = FotoAtual?.Foto.Id;
        IReadOnlyList<GrupoDeFotosDoDia> grupos;
        try
        {
            grupos = FiltroDeFotos.Agrupar(todas, Criterios(), DateOnly.FromDateTime(Hoje));
            ErroDeFiltro = "";
        }
        catch (DomainValidationException ex)
        {
            grupos = [];
            ErroDeFiltro = ex.Message;
        }

        var miniaturas = _fotos.Miniaturas(grupos.SelectMany(g => g.Fotos).Select(f => f.Id));
        Grupos.Clear();
        foreach (var grupo in grupos)
            Grupos.Add(new GrupoNoProjeto(Cabecalho(grupo),
                grupo.Fotos.Select(f => new FotoNoProjeto(f, miniaturas.GetValueOrDefault(f.Id), _nomes[f.TarefaId])).ToList()));
        _filtradas = grupos.SelectMany(g => g.Fotos).ToList();

        var total = todas.Count;
        var tarefas = todas.Select(f => f.TarefaId).Distinct().Count();
        Contador = total == _filtradas.Count
            ? $"{(total == 1 ? "1 foto" : $"{total} fotos")} em {(tarefas == 1 ? "1 tarefa" : $"{tarefas} tarefas")}"
            : $"{_filtradas.Count} de {total} fotos";
        Vazia = total == 0;
        NadaComOsFiltros = total > 0 && _filtradas.Count == 0 && ErroDeFiltro.Length == 0;
        Selecionar(Itens.FirstOrDefault(i => i.Foto.Id == atual));
        AtualizarAlerta();
        AtualizarAntesEDepois();
        AtualizarTextosDaIa();
    }

    private CriteriosDeFotos Criterios() => new()
    {
        Atalho = AtalhoEscolhido?.Valor ?? AtalhoDePeriodo.Tudo,
        De = DeFiltro is { } de ? DateOnly.FromDateTime(de) : null,
        Ate = AteFiltro is { } ate ? DateOnly.FromDateTime(ate) : null,
        TarefaId = TarefaFiltro?.Id,
        Local = LocalFiltro is null or TodosOsLocais ? null : LocalFiltro,
        Tag = TagFiltro is null or GaleriaFotosViewModel.TodasAsTags ? null : TagFiltro,
        Texto = TextoFiltro,
        MaisRecentesPrimeiro = MaisRecentesPrimeiro,
    };

    private static string Cabecalho(GrupoDeFotosDoDia grupo)
    {
        var dia = grupo.Dia.ToString("dddd, dd/MM/yyyy", Cultura);
        var quantidade = grupo.Fotos.Count == 1 ? "1 foto" : $"{grupo.Fotos.Count} fotos";
        var tarefas = grupo.Fotos.Select(f => f.TarefaId).Distinct().Count();
        return $"{char.ToUpper(dia[0], Cultura)}{dia[1..]} · {quantidade}" + (tarefas > 1 ? $" em {tarefas} tarefas" : "");
    }

    // ---- Foto selecionada ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirGaleriaDaFotoCommand), nameof(VerNoMapaCommand), nameof(AmpliarCommand))]
    public partial FotoNoProjeto? FotoAtual { get; private set; }

    /// <summary>"3 Fundações · 28/09/2026 07:15 · Bloco A · GPS -22.905810, -47.062630".</summary>
    public string DetalheDaAtual => FotoAtual is not { } item ? ""
        : string.Join(" · ", new[]
        {
            item.Tarefa, item.Foto.DataFoto.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), item.Foto.Local,
            item.Foto.Gps is { } gps ? $"GPS {gps}" : null,
        }.OfType<string>());

    partial void OnFotoAtualChanged(FotoNoProjeto? value) => OnPropertyChanged(nameof(DetalheDaAtual));

    /// <summary>Clique numa miniatura: ela fica sendo a atual.</summary>
    public void Selecionar(FotoNoProjeto? item)
    {
        foreach (var outro in Itens)
            outro.Selecionada = outro == item;
        FotoAtual = item;
    }

    [RelayCommand(CanExecute = nameof(PodeAmpliar))]
    private Task Ampliar(FotoNoProjeto? item)
    {
        var foto = (item ?? FotoAtual)!.Foto;
        var indice = Math.Max(0, _filtradas.ToList().FindIndex(f => f.Id == foto.Id));
        return _dialogos.MostrarFotoAmpliadaAsync(new VisualizadorDeFotosViewModel(_filtradas, indice, _fotos, Titulo));
    }

    private bool PodeAmpliar(FotoNoProjeto? item) => (item ?? FotoAtual) is not null;

    /// <summary>A galeria da tarefa da foto (para editar, girar, excluir); ao voltar, a linha do tempo se refaz.</summary>
    [RelayCommand(CanExecute = nameof(TemAtual))]
    private async Task AbrirGaleriaDaFoto()
    {
        await _abrirGaleria(FotoAtual!.Foto.TarefaId);
        Atualizar();
    }

    private bool TemAtual() => FotoAtual is not null;

    [RelayCommand(CanExecute = nameof(AtualTemGps))]
    private Task VerNoMapa() => _dialogos.AbrirEnderecoAsync(FotoAtual!.Foto.Gps!.Mapa());

    private bool AtualTemGps() => FotoAtual?.Foto.Gps is not null;

    // ======== FT2: importação em lote ========

    public ObservableCollection<LinhaDaImportacao> Lote { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EscolherFotosCommand), nameof(ImportarLoteCommand), nameof(DescartarLoteCommand))]
    public partial bool Ocupado { get; private set; }

    [ObservableProperty] public partial string Progresso { get; private set; } = "";

    /// <summary>O local posto em todas as fotos importadas (vazio: sem local).</summary>
    [ObservableProperty] public partial string? LocalDoLote { get; set; }

    /// <summary>Resultado da última importação, ou o erro.</summary>
    [ObservableProperty] public partial string AvisoDoLote { get; private set; } = "";

    /// <summary>"8 de 10 fotos vão ser importadas".</summary>
    public string ResumoDoLote
    {
        get
        {
            var importar = Lote.Count(l => l.Importar);
            return Lote.Count == 0 ? "" : $"{importar} de {Lote.Count} {(Lote.Count == 1 ? "foto vai" : "fotos vão")} ser importada{(Lote.Count == 1 ? "" : "s")}.";
        }
    }

    public bool TemLote => Lote.Count > 0;

    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task EscolherFotos()
    {
        var caminhos = await _dialogos.EscolherFotosAsync();
        if (caminhos.Count > 0)
            await LerLoteAsync(caminhos);
    }

    /// <summary>Lê os arquivos (do diálogo ou soltos na janela) e monta a tabela, sem importar ainda.</summary>
    public async Task LerLoteAsync(IEnumerable<string> caminhos)
    {
        var lista = caminhos.ToList();
        if (lista.Count == 0 || Ocupado)
            return;
        Ocupado = true;
        AbaEscolhida = AbaImportacao;
        try
        {
            var progresso = new Progress<int>(i => Progresso = $"Lendo {Math.Min(i + 1, lista.Count)} de {lista.Count}…");
            var lidas = await _fotos.LerAsync(lista, progresso);
            MontarLote(lidas);
            AvisoDoLote = "";
        }
        finally
        {
            Ocupado = false;
            Progresso = "";
        }
    }

    private void MontarLote(IReadOnlyList<FotoLida> lidas)
    {
        var edt = _cronograma.NumeracaoEdt();
        var opcoes = (IReadOnlyList<OpcaoDeTarefa>)[OpcaoNaoImportar,
            .. _cronograma.Tarefas.Where(t => !t.EhResumo).Select(t => new OpcaoDeTarefa(t.Id, $"{edt[t.Id]} {t.Nome}"))];
        var hashesDoLote = Lote.Select(l => l.Lida.Importada?.HashOriginal).OfType<string>().ToHashSet();
        foreach (var lida in lidas)
        {
            if (lida.Importada is not { } importada)
            {
                Lote.Add(new LinhaDaImportacao(lida, [OpcaoNaoImportar], OpcaoNaoImportar, "Recusada: " + lida.Erro));
                continue;
            }
            if (!hashesDoLote.Add(importada.HashOriginal))
            {
                Lote.Add(new LinhaDaImportacao(lida, opcoes, OpcaoNaoImportar, "Repetida: a mesma foto já está nesta lista."));
                continue;
            }
            if (_cronograma.Tarefas.FirstOrDefault(t => t.TemFotoDaMesmaImagem(importada.HashOriginal)) is { } dona)
            {
                Lote.Add(new LinhaDaImportacao(lida, opcoes, OpcaoNaoImportar, $"Já está no projeto, em \"{edt[dona.Id]} {dona.Nome}\"."));
                continue;
            }
            var sugerida = SugestaoDeTarefa.Sugerir(_cronograma, importada.DataFoto);
            Lote.Add(sugerida is null
                ? new LinhaDaImportacao(lida, opcoes, OpcaoNaoImportar, "Nenhuma tarefa prevista nesse dia: escolha uma.")
                : new LinhaDaImportacao(lida, opcoes, opcoes.First(o => o.Id == sugerida.Id), "Sugerida pela data da foto."));
        }
        foreach (var linha in Lote)
        {
            linha.PropertyChanged -= LinhaMudou;
            linha.PropertyChanged += LinhaMudou;
        }
        LoteMudou();
    }

    private void LinhaMudou(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => LoteMudou();

    private void LoteMudou()
    {
        OnPropertyChanged(nameof(ResumoDoLote));
        OnPropertyChanged(nameof(TemLote));
        ImportarLoteCommand.NotifyCanExecuteChanged();
        DescartarLoteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(PodeImportar))]
    private void ImportarLote()
    {
        string? local = string.IsNullOrWhiteSpace(LocalDoLote) ? null : LocalDoLote.Trim();
        if (local is { Length: > MetadadosFoto.TamanhoMaximoDoLocal })
        {
            AvisoDoLote = $"O local tem {local.Length} caracteres; o máximo é {MetadadosFoto.TamanhoMaximoDoLocal}.";
            return;
        }
        var tarefas = _cronograma.Tarefas.ToDictionary(t => t.Id);
        var (importadas, jaEstavam, mudaram) = (0, 0, new HashSet<Guid>());
        foreach (var linha in Lote.Where(l => l.Importar))
        {
            var tarefa = tarefas[linha.TarefaEscolhida.Id!.Value];
            if (_fotos.Adicionar(tarefa, linha.Lida.Importada!, local) is null)
                jaEstavam++;
            else
            {
                importadas++;
                mudaram.Add(tarefa.Id);
            }
        }
        if (mudaram.Count > 0)
            _fotosMudaram(mudaram, mudaram.Count == 1 ? "Importar fotos" : $"Importar fotos em {mudaram.Count} tarefas");
        Lote.Clear();
        LoteMudou();
        AvisoDoLote = (importadas == 1 ? "1 foto importada" : $"{importadas} fotos importadas") +
                      (mudaram.Count == 1 ? " em 1 tarefa." : $" em {mudaram.Count} tarefas.") +
                      (jaEstavam == 0 ? "" : jaEstavam == 1 ? " 1 já estava na tarefa escolhida." : $" {jaEstavam} já estavam nas tarefas escolhidas.");
        Atualizar();
    }

    private bool PodeImportar() => !Ocupado && Lote.Any(l => l.Importar);

    [RelayCommand(CanExecute = nameof(PodeDescartar))]
    private void DescartarLote()
    {
        Lote.Clear();
        AvisoDoLote = "";
        LoteMudou();
    }

    private bool PodeDescartar() => !Ocupado && Lote.Count > 0;

    private bool Livre() => !Ocupado;

    // ======== FT4 e FT5: locais padronizados e alerta ========

    /// <summary>Um local por linha.</summary>
    [ObservableProperty] public partial string TextoDosLocais { get; set; } = "";

    /// <summary>Dias sem foto para o alerta (0 desliga).</summary>
    [ObservableProperty] public partial decimal? DiasSemFoto { get; set; }

    [ObservableProperty] public partial string ErroDaConfiguracao { get; private set; } = "";

    [ObservableProperty] public partial string AvisoDaConfiguracao { get; private set; } = "";

    /// <summary>Os locais para sugerir (padronizados e usados), no lote e nos filtros.</summary>
    public IReadOnlyList<string> SugestoesDeLocal => _cronograma.ConfiguracaoDasFotos.Sugestoes(TodasAsFotos);

    private void CarregarConfiguracao()
    {
        var configuracao = _cronograma.ConfiguracaoDasFotos;
        TextoDosLocais = string.Join(Environment.NewLine, configuracao.Locais);
        DiasSemFoto = configuracao.DiasSemFotoParaAlerta;
    }

    [RelayCommand]
    private void AplicarConfiguracao()
    {
        ConfiguracaoDasFotos nova;
        try
        {
            nova = new ConfiguracaoDasFotos(TextoDosLocais.Split('\n').Select(l => l.TrimEnd('\r')), (int)Math.Round(DiasSemFoto ?? 0));
        }
        catch (DomainValidationException ex)
        {
            ErroDaConfiguracao = ex.Message;
            AvisoDaConfiguracao = "";
            return;
        }
        ErroDaConfiguracao = "";
        if (nova != _cronograma.ConfiguracaoDasFotos)
        {
            _cronograma.DefinirConfiguracaoDasFotos(nova);
            _configuracaoMudou();
        }
        CarregarConfiguracao(); // sem os vazios e repetidos
        AvisoDaConfiguracao = nova.AlertaLigado
            ? $"Salvo no projeto. Alerta para tarefas em andamento sem foto há {Dias(nova.DiasSemFotoParaAlerta)} ou mais."
            : "Salvo no projeto. Alerta de tarefa sem foto desligado.";
        OnPropertyChanged(nameof(SugestoesDeLocal));
        Atualizar();
    }

    /// <summary>Acrescenta à lista os locais já usados nas fotos que ainda não estão nela.</summary>
    [RelayCommand]
    private void TrazerLocaisUsados()
    {
        var atuais = TextoDosLocais.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var novos = TodasAsFotos.Select(f => f.Local).OfType<string>()
            .Where(l => !atuais.Contains(l, StringComparer.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.Create(Cultura, ignoreCase: true)).ToList();
        TextoDosLocais = string.Join(Environment.NewLine, atuais.Concat(novos));
        AvisoDaConfiguracao = novos.Count switch
        {
            0 => "Todos os locais usados nas fotos já estão na lista.",
            1 => "1 local trazido das fotos. Clique em Aplicar para salvar.",
            var n => $"{n} locais trazidos das fotos. Clique em Aplicar para salvar.",
        };
    }

    /// <summary>As tarefas em andamento sem foto recente, na data de hoje.</summary>
    [ObservableProperty] public partial IReadOnlyList<TarefaSemFotoRecente> SemFotoRecente { get; private set; } = [];

    public string TituloDoAlerta => !_cronograma.ConfiguracaoDasFotos.AlertaLigado
        ? "O alerta de tarefa sem foto está desligado."
        : SemFotoRecente.Count == 0
            ? $"Nenhuma tarefa em andamento está sem foto há {Dias(_cronograma.ConfiguracaoDasFotos.DiasSemFotoParaAlerta)} ou mais."
            : $"Tarefas em andamento sem foto há {Dias(_cronograma.ConfiguracaoDasFotos.DiasSemFotoParaAlerta)} ou mais:";

    private void AtualizarAlerta()
    {
        SemFotoRecente = AlertaDeFotos.Calcular(_cronograma, Hoje);
        OnPropertyChanged(nameof(TituloDoAlerta));
    }

    /// <summary>A galeria de uma tarefa da lista do alerta.</summary>
    [RelayCommand]
    private async Task AbrirGaleriaDaTarefa(TarefaSemFotoRecente? item)
    {
        if (item is null)
            return;
        await _abrirGaleria(item.Tarefa.Id);
        Atualizar();
    }

    private static string Dias(int dias) => dias == 1 ? "1 dia" : $"{dias} dias";
}
