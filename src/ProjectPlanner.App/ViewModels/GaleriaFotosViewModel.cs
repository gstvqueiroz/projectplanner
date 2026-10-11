using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Um atalho de período no filtro, com o nome que aparece na lista.</summary>
public sealed record OpcaoDeAtalho(AtalhoDePeriodo Valor, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>Uma miniatura da galeria.</summary>
public sealed partial class FotoNaGaleria(FotoTarefa foto, byte[]? miniatura) : ObservableObject
{
    public FotoTarefa Foto { get; private set; } = foto;

    /// <summary>Bloco 27: tirada do relatório fotográfico (a miniatura fica esmaecida).</summary>
    public bool ForaDoRelatorio => !Foto.NoRelatorio;

    /// <summary>Bloco 27: destaque no relatório (a miniatura ganha uma estrela).</summary>
    public bool EhDestaque => Foto.Destaque;

    /// <summary>A mesma foto com a curadoria nova, sem refazer a galeria (e sem perder uma edição em andamento).</summary>
    internal void Trocar(FotoTarefa nova)
    {
        Foto = nova;
        OnPropertyChanged(nameof(Foto));
        OnPropertyChanged(nameof(ForaDoRelatorio));
        OnPropertyChanged(nameof(EhDestaque));
    }

    public byte[]? Miniatura { get; } = miniatura;

    public bool TemMiniatura => Miniatura is not null;

    /// <summary>O título, ou a legenda, ou o nome do arquivo.</summary>
    public string Rotulo => Foto.Titulo ?? Foto.Descricao ?? Foto.Imagem.NomeArquivoOriginal;

    public string Hora => Foto.DataFoto.ToString("HH:mm", CultureInfo.InvariantCulture);

    [ObservableProperty]
    public partial bool Selecionada { get; set; }
}

/// <summary>As fotos de um dia, com o cabeçalho de data.</summary>
public sealed record GrupoNaGaleria(string Cabecalho, IReadOnlyList<FotoNaGaleria> Fotos);

/// <summary>
/// A tela de fotos de uma tarefa (Bloco 22): adicionar (diálogo ou arrastar), filtrar por data, intervalo, atalhos,
/// texto e tag, ver agrupadas por dia, selecionar várias, editar as informações da selecionada, excluir, girar e
/// ampliar. Toda alteração chama <c>aoMudar</c> (projeto não salvo, câmera da grid e do Gantt atualizadas).
/// </summary>
public sealed partial class GaleriaFotosViewModel : ViewModelBase
{
    public const string TodasAsTags = "(todas as tags)";
    public const string SemFotos = "Nenhuma foto nesta tarefa. Clique em Adicionar fotos (ou arraste fotos para cá).";
    public const string SemResultado = "Nenhuma foto com esses filtros.";

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    private readonly TarefaViewModel _linha;
    private readonly FotosDoProjeto _fotos;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Action _aoMudar;
    private readonly Func<IReadOnlyList<string>> _locais;
    private IReadOnlyList<FotoTarefa> _filtradas = [];
    private bool _carregandoEdicao;

    /// <param name="locais">Bloco 28: os locais para sugerir no campo Local (os padronizados do projeto e os já usados).</param>
    public GaleriaFotosViewModel(TarefaViewModel linha, FotosDoProjeto fotos, IDialogosService dialogos, TimeProvider relogio, Action aoMudar,
        Func<IReadOnlyList<string>>? locais = null)
    {
        ArgumentNullException.ThrowIfNull(linha);
        ArgumentNullException.ThrowIfNull(fotos);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(aoMudar);
        (_linha, _fotos, _dialogos, _relogio, _aoMudar) = (linha, fotos, dialogos, relogio, aoMudar);
        _locais = locais ?? (() => []);
        AtalhoEscolhido = Atalhos[0];
        TagEscolhida = TodasAsTags;
        Atualizar();
    }

    private Tarefa Tarefa => _linha.Tarefa;

    public string Titulo => $"Fotos — {_linha.Edt} {_linha.Nome}";

    /// <summary>EDT, nome, datas previstas, duração e % concluído da tarefa.</summary>
    public string InfoDaTarefa =>
        $"EDT {_linha.Edt} · {_linha.Nome} · Início {_linha.DataInicio:dd/MM/yyyy} · Término {_linha.DataTermino:dd/MM/yyyy} · " +
        $"{(_linha.Duracao == 1 ? "1 dia" : $"{_linha.Duracao} dias")} · {_linha.PercentualConcluido}% concluído";

    // ---- Filtros (D3) ----

    public IReadOnlyList<OpcaoDeAtalho> Atalhos { get; } =
    [
        new(AtalhoDePeriodo.Tudo, "Todas as datas"),
        new(AtalhoDePeriodo.Hoje, "Hoje"),
        new(AtalhoDePeriodo.Ultimos7Dias, "Últimos 7 dias"),
        new(AtalhoDePeriodo.EsteMes, "Este mês"),
    ];

    [ObservableProperty] public partial OpcaoDeAtalho AtalhoEscolhido { get; set; }
    [ObservableProperty] public partial DateTime? DiaFiltro { get; set; }
    [ObservableProperty] public partial DateTime? DeFiltro { get; set; }
    [ObservableProperty] public partial DateTime? AteFiltro { get; set; }
    [ObservableProperty] public partial string TextoFiltro { get; set; } = "";
    [ObservableProperty] public partial string? TagEscolhida { get; set; }
    [ObservableProperty] public partial bool MaisRecentesPrimeiro { get; set; }

    partial void OnAtalhoEscolhidoChanged(OpcaoDeAtalho value) => Atualizar();
    partial void OnDiaFiltroChanged(DateTime? value) => Atualizar();
    partial void OnDeFiltroChanged(DateTime? value) => Atualizar();
    partial void OnAteFiltroChanged(DateTime? value) => Atualizar();
    partial void OnTextoFiltroChanged(string value) => Atualizar();
    partial void OnTagEscolhidaChanged(string? value) => Atualizar();
    partial void OnMaisRecentesPrimeiroChanged(bool value) => Atualizar();

    [ObservableProperty] public partial IReadOnlyList<string> TagsDisponiveis { get; private set; } = [TodasAsTags];

    /// <summary>Problema nos filtros (intervalo invertido), mostrado em vez das fotos.</summary>
    [ObservableProperty] public partial string ErroDeFiltro { get; private set; } = "";

    [RelayCommand]
    private void LimparFiltros()
    {
        (AtalhoEscolhido, DiaFiltro, DeFiltro, AteFiltro, TextoFiltro, TagEscolhida) = (Atalhos[0], null, null, null, "", TodasAsTags);
    }

    // ---- Fotos agrupadas por dia ----

    public ObservableCollection<GrupoNaGaleria> Grupos { get; } = [];

    /// <summary>"40 fotos" ou, com filtro, "12 de 40 fotos".</summary>
    [ObservableProperty] public partial string Contador { get; private set; } = "";

    /// <summary>A tarefa não tem fotos: a tela mostra <see cref="SemFotos"/>.</summary>
    [ObservableProperty] public partial bool Vazia { get; private set; }

    /// <summary>Há fotos, mas nenhuma passa pelos filtros.</summary>
    [ObservableProperty] public partial bool NadaComOsFiltros { get; private set; }

    /// <summary>Resultado da última operação (adicionar, excluir, girar) ou erro amigável.</summary>
    [ObservableProperty] public partial string Aviso { get; private set; } = "";

    /// <summary>Importando fotos: a tela mostra o progresso e trava as outras ações.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdicionarFotosCommand), nameof(ExcluirCommand), nameof(GirarEsquerdaCommand), nameof(GirarDireitaCommand))]
    public partial bool Ocupado { get; private set; }

    [ObservableProperty] public partial string Progresso { get; private set; } = "";

    /// <summary>As fotos visíveis, na ordem da galeria (a do visualizador).</summary>
    public IReadOnlyList<FotoTarefa> FotosFiltradas => _filtradas;

    private IEnumerable<FotoNaGaleria> Itens => Grupos.SelectMany(g => g.Fotos);

    /// <summary>Refaz a galeria a partir das fotos da tarefa, mantendo a seleção.</summary>
    public void Atualizar()
    {
        var selecionadas = Itens.Where(i => i.Selecionada).Select(i => i.Foto.Id).ToHashSet();
        var atual = FotoAtual?.Foto.Id;
        var todas = Tarefa.FotosNaOrdemDeCadastro;
        TagsDisponiveis = [TodasAsTags, .. FiltroDeFotos.Tags(todas)];

        IReadOnlyList<GrupoDeFotosDoDia> grupos;
        try
        {
            grupos = FiltroDeFotos.Agrupar(todas, Criterios(), DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime));
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
            Grupos.Add(new GrupoNaGaleria(Cabecalho(grupo),
                grupo.Fotos.Select(f => new FotoNaGaleria(f, miniaturas.GetValueOrDefault(f.Id)) { Selecionada = selecionadas.Contains(f.Id) }).ToList()));
        _filtradas = grupos.SelectMany(g => g.Fotos).ToList();

        var total = todas.Count;
        Contador = total == _filtradas.Count
            ? (total == 1 ? "1 foto" : $"{total} fotos")
            : $"{_filtradas.Count} de {total} fotos";
        Vazia = total == 0;
        NadaComOsFiltros = total > 0 && _filtradas.Count == 0 && ErroDeFiltro.Length == 0;
        FotoAtual = Itens.FirstOrDefault(i => i.Foto.Id == atual) ?? Itens.FirstOrDefault(i => i.Selecionada);
        NotificarSelecao();
    }

    private CriteriosDeFotos Criterios() => new()
    {
        Atalho = AtalhoEscolhido?.Valor ?? AtalhoDePeriodo.Tudo,
        Dia = DiaFiltro is { } dia ? DateOnly.FromDateTime(dia) : null,
        De = DeFiltro is { } de ? DateOnly.FromDateTime(de) : null,
        Ate = AteFiltro is { } ate ? DateOnly.FromDateTime(ate) : null,
        Texto = TextoFiltro,
        Tag = TagEscolhida is null or TodasAsTags ? null : TagEscolhida,
        MaisRecentesPrimeiro = MaisRecentesPrimeiro,
    };

    private static string Cabecalho(GrupoDeFotosDoDia grupo)
    {
        var dia = grupo.Dia.ToString("dddd, dd/MM/yyyy", Cultura);
        var quantidade = grupo.Fotos.Count == 1 ? "1 foto" : $"{grupo.Fotos.Count} fotos";
        return $"{char.ToUpper(dia[0], Cultura)}{dia[1..]} · {quantidade}";
    }

    // ---- Seleção ----

    /// <summary>A foto dos detalhes (a última clicada).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GirarEsquerdaCommand), nameof(GirarDireitaCommand), nameof(AmpliarCommand))]
    public partial FotoNaGaleria? FotoAtual { get; private set; }

    public int QuantidadeSelecionada => Itens.Count(i => i.Selecionada);

    /// <summary>Clique numa miniatura: sem <paramref name="adicionar"/>, só ela fica selecionada; com, ela entra ou sai da seleção.</summary>
    public void Selecionar(FotoNaGaleria item, bool adicionar)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (adicionar)
            item.Selecionada = !item.Selecionada;
        else
            foreach (var outro in Itens)
                outro.Selecionada = outro == item;
        FotoAtual = item.Selecionada ? item : Itens.LastOrDefault(i => i.Selecionada);
        NotificarSelecao();
    }

    [RelayCommand]
    private void SelecionarTodas()
    {
        foreach (var item in Itens)
            item.Selecionada = true;
        FotoAtual ??= Itens.FirstOrDefault();
        NotificarSelecao();
    }

    private void NotificarSelecao()
    {
        OnPropertyChanged(nameof(QuantidadeSelecionada));
        ExcluirCommand.NotifyCanExecuteChanged();
    }

    partial void OnFotoAtualChanged(FotoNaGaleria? value)
    {
        CarregarEdicao();
        ImagemAtual = value is null ? null : _fotos.Imagem(value.Foto.Id) ?? value.Miniatura;
        OnPropertyChanged(nameof(NoRelatorio));
        OnPropertyChanged(nameof(DestaqueNoRelatorio));
    }

    // ---- Bloco 27: curadoria para o relatório fotográfico (vale na hora, sem Aplicar) ----

    /// <summary>A foto atual entra no relatório fotográfico.</summary>
    public bool NoRelatorio
    {
        get => FotoAtual?.Foto.NoRelatorio ?? false;
        set => Curar(value, value && DestaqueNoRelatorio);
    }

    /// <summary>A foto atual é destaque no relatório (marcar o destaque também a põe no relatório).</summary>
    public bool DestaqueNoRelatorio
    {
        get => FotoAtual?.Foto.Destaque ?? false;
        set => Curar(value || NoRelatorio, value);
    }

    private void Curar(bool noRelatorio, bool destaque)
    {
        if (FotoAtual is not { } item || (item.Foto.NoRelatorio == noRelatorio && item.Foto.Destaque == destaque))
            return;
        item.Trocar(_fotos.DefinirCuradoria(Tarefa, item.Foto.Id, noRelatorio, destaque));
        _aoMudar();
        OnPropertyChanged(nameof(NoRelatorio));
        OnPropertyChanged(nameof(DestaqueNoRelatorio));
    }

    /// <summary>A imagem da foto atual para o painel de detalhes (a completa, ou a miniatura se não der para ler).</summary>
    [ObservableProperty] public partial byte[]? ImagemAtual { get; private set; }

    // ---- Adicionar (diálogo ou arrastar) ----

    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task AdicionarFotos()
    {
        var caminhos = await _dialogos.EscolherFotosAsync();
        if (caminhos.Count > 0)
            await AdicionarArquivosAsync(caminhos);
    }

    /// <summary>Adiciona as fotos dos arquivos (do diálogo ou soltos na janela).</summary>
    public async Task AdicionarArquivosAsync(IEnumerable<string> caminhos)
    {
        var lista = caminhos.ToList();
        if (lista.Count == 0 || Ocupado)
            return;
        Ocupado = true;
        try
        {
            var progresso = new Progress<int>(i => Progresso = $"Importando {Math.Min(i + 1, lista.Count)} de {lista.Count}…");
            var resultado = await _fotos.ImportarAsync(Tarefa, lista, progresso);
            Aviso = resultado.Resumo;
            if (resultado.Adicionadas.Count > 0)
            {
                _aoMudar();
                Atualizar();
                if (Itens.FirstOrDefault(i => i.Foto.Id == resultado.Adicionadas[^1].Id) is { } ultima)
                    Selecionar(ultima, adicionar: false);
            }
        }
        finally
        {
            Ocupado = false;
            Progresso = "";
        }
    }

    private bool Livre() => !Ocupado;

    // ---- Excluir, girar, ampliar ----

    [RelayCommand(CanExecute = nameof(PodeExcluir))]
    private async Task Excluir()
    {
        var selecionadas = Itens.Where(i => i.Selecionada).Select(i => i.Foto).ToList();
        var pergunta = selecionadas.Count == 1
            ? $"Excluir a foto \"{selecionadas[0].Imagem.NomeArquivoOriginal}\"? Não dá para desfazer."
            : $"Excluir as {selecionadas.Count} fotos selecionadas? Não dá para desfazer.";
        if (!await _dialogos.ConfirmarAsync("Excluir fotos", pergunta, "Excluir"))
            return;
        _fotos.Excluir(Tarefa, selecionadas.Select(f => f.Id));
        Aviso = selecionadas.Count == 1 ? "1 foto excluída." : $"{selecionadas.Count} fotos excluídas.";
        FotoAtual = null;
        _aoMudar();
        Atualizar();
    }

    private bool PodeExcluir() => !Ocupado && QuantidadeSelecionada > 0;

    [RelayCommand(CanExecute = nameof(PodeMexerNaAtual))]
    private Task GirarEsquerda() => Girar(sentidoHorario: false);

    [RelayCommand(CanExecute = nameof(PodeMexerNaAtual))]
    private Task GirarDireita() => Girar(sentidoHorario: true);

    private bool PodeMexerNaAtual() => !Ocupado && FotoAtual is not null;

    private async Task Girar(bool sentidoHorario)
    {
        var id = FotoAtual!.Foto.Id;
        try
        {
            id = (await _fotos.GirarAsync(Tarefa, id, sentidoHorario)).Id; // a versão girada tem outro Id (Bloco 30)
        }
        catch (ImagemRecusadaException ex)
        {
            Aviso = ex.Message;
            return;
        }
        Aviso = "";
        _aoMudar();
        FotoAtual = null; // recarrega a imagem dos detalhes
        Atualizar();
        if (Itens.FirstOrDefault(i => i.Foto.Id == id) is { } girada)
            Selecionar(girada, adicionar: false);
    }

    [RelayCommand(CanExecute = nameof(PodeAmpliar))]
    private Task Ampliar(FotoNaGaleria? item)
    {
        var foto = (item ?? FotoAtual)!.Foto;
        var indice = Math.Max(0, _filtradas.ToList().FindIndex(f => f.Id == foto.Id));
        return _dialogos.MostrarFotoAmpliadaAsync(new VisualizadorDeFotosViewModel(_filtradas, indice, _fotos, Titulo));
    }

    private bool PodeAmpliar(FotoNaGaleria? item) => (item ?? FotoAtual) is not null;

    // ---- Detalhes e edição da foto atual ----

    [ObservableProperty] public partial DateTime? EdicaoData { get; set; }
    [ObservableProperty] public partial TimeSpan? EdicaoHora { get; set; }
    [ObservableProperty] public partial string? EdicaoTitulo { get; set; }
    [ObservableProperty] public partial string? EdicaoDescricao { get; set; }
    [ObservableProperty] public partial string? EdicaoAutor { get; set; }
    [ObservableProperty] public partial string? EdicaoLocal { get; set; }
    [ObservableProperty] public partial decimal? EdicaoPercentual { get; set; }
    [ObservableProperty] public partial string? EdicaoTags { get; set; }

    [ObservableProperty] public partial string ErroDeEdicao { get; private set; } = "";

    /// <summary>"IMG_0042.jpg · 1600 × 1200 · 342 KB".</summary>
    [ObservableProperty] public partial string DetalheDoArquivo { get; private set; } = "";

    /// <summary>"Cadastrada em 01/10/2026 09:00".</summary>
    [ObservableProperty] public partial string DetalheDoCadastro { get; private set; } = "";

    /// <summary>Bloco 28: "GPS -22.905810, -47.062630", vazio sem GPS.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerNoMapaCommand))]
    public partial string DetalheDoGps { get; private set; } = "";

    /// <summary>Bloco 28: os locais sugeridos no campo Local.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> LocaisSugeridos { get; private set; } = [];

    [RelayCommand(CanExecute = nameof(TemGps))]
    private Task VerNoMapa() => _dialogos.AbrirEnderecoAsync(FotoAtual!.Foto.Gps!.Mapa());

    private bool TemGps() => FotoAtual?.Foto.Gps is not null;

    partial void OnEdicaoDataChanged(DateTime? value) => EdicaoMudou();
    partial void OnEdicaoHoraChanged(TimeSpan? value) => EdicaoMudou();
    partial void OnEdicaoTituloChanged(string? value) => EdicaoMudou();
    partial void OnEdicaoDescricaoChanged(string? value) => EdicaoMudou();
    partial void OnEdicaoAutorChanged(string? value) => EdicaoMudou();
    partial void OnEdicaoLocalChanged(string? value) => EdicaoMudou();
    partial void OnEdicaoPercentualChanged(decimal? value) => EdicaoMudou();
    partial void OnEdicaoTagsChanged(string? value) => EdicaoMudou();

    /// <summary>Os campos do painel diferem da foto atual (Aplicar e Descartar ficam habilitados).</summary>
    public bool EdicaoAlterada { get; private set; }

    private void EdicaoMudou()
    {
        if (_carregandoEdicao)
            return;
        EdicaoAlterada = FotoAtual is not null;
        OnPropertyChanged(nameof(EdicaoAlterada));
        AplicarEdicaoCommand.NotifyCanExecuteChanged();
        DescartarEdicaoCommand.NotifyCanExecuteChanged();
    }

    private void CarregarEdicao()
    {
        _carregandoEdicao = true;
        var foto = FotoAtual?.Foto;
        EdicaoData = foto?.DataFoto.Date;
        EdicaoHora = foto?.DataFoto.TimeOfDay;
        EdicaoTitulo = foto?.Titulo;
        EdicaoDescricao = foto?.Descricao;
        EdicaoAutor = foto?.Autor;
        EdicaoLocal = foto?.Local;
        EdicaoPercentual = foto?.PercentualConcluidoNaData;
        EdicaoTags = foto?.Metadados.TextoDasTags;
        DetalheDoArquivo = foto is null ? ""
            : $"{foto.Imagem.NomeArquivoOriginal} · {foto.Imagem.Largura} × {foto.Imagem.Altura} · {Math.Max(1, foto.Imagem.TamanhoBytes / 1024)} KB";
        DetalheDoCadastro = foto is null ? "" : $"Cadastrada em {foto.DataCadastro:dd/MM/yyyy HH:mm}";
        DetalheDoGps = foto?.Gps is { } gps ? $"GPS {gps}" : "";
        LocaisSugeridos = _locais();
        ErroDeEdicao = "";
        _carregandoEdicao = false;
        EdicaoAlterada = false;
        OnPropertyChanged(nameof(EdicaoAlterada));
        AplicarEdicaoCommand.NotifyCanExecuteChanged();
        DescartarEdicaoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Grava as informações do painel na foto atual (com as validações do domínio).</summary>
    [RelayCommand(CanExecute = nameof(EdicaoAlterada))]
    private void AplicarEdicao()
    {
        var foto = FotoAtual!.Foto;
        if (EdicaoData is not { } data)
        {
            ErroDeEdicao = "A data da foto é obrigatória.";
            return;
        }
        MetadadosFoto metadados;
        try
        {
            metadados = new MetadadosFoto(data.Date + (EdicaoHora ?? TimeSpan.Zero), EdicaoTitulo, EdicaoDescricao, EdicaoAutor,
                EdicaoLocal, EdicaoPercentual is { } p ? (int)Math.Round(p) : null, MetadadosFoto.LerTags(EdicaoTags));
        }
        catch (DomainValidationException ex)
        {
            ErroDeEdicao = ex.Message;
            return;
        }
        if (metadados != foto.Metadados)
        {
            _fotos.Editar(Tarefa, foto.Id, metadados);
            _aoMudar();
        }
        Aviso = "Informações da foto atualizadas.";
        Atualizar();
        CarregarEdicao();
    }

    [RelayCommand(CanExecute = nameof(EdicaoAlterada))]
    private void DescartarEdicao() => CarregarEdicao();
}

/// <summary>O visualizador ampliado (Bloco 22): uma foto por vez, com anterior/próxima e as informações dela.</summary>
public sealed partial class VisualizadorDeFotosViewModel : ViewModelBase
{
    private readonly IReadOnlyList<FotoTarefa> _fotos;
    private readonly FotosDoProjeto _armazem;
    private readonly Dictionary<Guid, byte[]?> _carregadas = [];

    public VisualizadorDeFotosViewModel(IReadOnlyList<FotoTarefa> fotos, int indice, FotosDoProjeto armazem, string titulo)
    {
        if (fotos.Count == 0)
            throw new ArgumentException("Não há fotos para mostrar.", nameof(fotos));
        (_fotos, _armazem, Titulo) = (fotos, armazem, titulo);
        Indice = Math.Clamp(indice, 0, fotos.Count - 1);
        Mostrar();
    }

    public string Titulo { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnteriorCommand), nameof(ProximaCommand))]
    public partial int Indice { get; private set; }

    public FotoTarefa Atual => _fotos[Indice];

    [ObservableProperty] public partial byte[]? Imagem { get; private set; }

    public string Posicao => $"{Indice + 1} de {_fotos.Count}";
    public string Data => Atual.DataFoto.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    public string? TituloDaFoto => Atual.Titulo;
    public string? Legenda => Atual.Descricao;
    public string? Autor => Atual.Autor;
    public string? Local => Atual.Local;
    public string? Percentual => Atual.PercentualConcluidoNaData is { } p ? $"{p}% concluído na data" : null;
    public string? Tags => Atual.Tags.Count == 0 ? null : Atual.Metadados.TextoDasTags;
    public string Arquivo => $"{Atual.Imagem.NomeArquivoOriginal} · {Atual.Imagem.Largura} × {Atual.Imagem.Altura}";
    public bool ImagemIndisponivel => Imagem is null;

    [RelayCommand(CanExecute = nameof(TemAnterior))]
    private void Anterior()
    {
        Indice--;
        Mostrar();
    }

    [RelayCommand(CanExecute = nameof(TemProxima))]
    private void Proxima()
    {
        Indice++;
        Mostrar();
    }

    private bool TemAnterior() => Indice > 0;
    private bool TemProxima() => Indice < _fotos.Count - 1;

    // A atual, e a anterior e a próxima já carregadas, para a navegação não esperar.
    private void Mostrar()
    {
        Imagem = Carregar(Indice);
        if (TemAnterior())
            Carregar(Indice - 1);
        if (TemProxima())
            Carregar(Indice + 1);
        foreach (var propriedade in new[] { nameof(Atual), nameof(Posicao), nameof(Data), nameof(TituloDaFoto), nameof(Legenda),
                     nameof(Autor), nameof(Local), nameof(Percentual), nameof(Tags), nameof(Arquivo), nameof(ImagemIndisponivel) })
            OnPropertyChanged(propriedade);
    }

    private byte[]? Carregar(int indice)
    {
        var id = _fotos[indice].Id;
        if (!_carregadas.TryGetValue(id, out var imagem))
            _carregadas[id] = imagem = _armazem.Imagem(id);
        return imagem;
    }
}
