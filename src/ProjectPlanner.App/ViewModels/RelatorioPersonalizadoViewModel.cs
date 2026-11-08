using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Relatorios;
using ProjectPlanner.Relatorios.Personalizado;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 88: um modelo na lista (os prontos vêm com o programa; os outros são do usuário).</summary>
public sealed record ModeloNaLista(string Nome, bool Pronto)
{
    public string Texto => Pronto ? $"{Nome} (pronto)" : Nome;
}

/// <summary>Bloco 88: o filtro das tarefas na lista da tela.</summary>
public sealed record OpcaoDeFiltroDeTarefas(string Nome, FiltroDeTarefas Valor);

/// <summary>Bloco 88: o papel na lista da tela.</summary>
public sealed record OpcaoDePapel(string Nome, PapelDoRelatorio Valor);

/// <summary>Bloco 88: um tipo de seção no menu "+ Adicionar seção".</summary>
public sealed record OpcaoDeSecao(string Nome, TipoDeSecao Valor);

/// <summary>Bloco 88: uma seção na tela: ligada ou não, a ordem (pelos botões) e as opções dela.</summary>
public sealed partial class SecaoNaTela : ObservableObject
{
    private readonly RelatorioPersonalizadoViewModel _tela;

    public SecaoNaTela(RelatorioPersonalizadoViewModel tela, SecaoDoRelatorio secao)
    {
        _tela = tela;
        Tipo = secao.Tipo;
        (Ativa, Titulo, Texto, Dias, FotosPorPagina, MaximoDeFotos, Nivel, ComFoto, PaginaNova) =
            (secao.Ativa, secao.Titulo ?? "", secao.Texto ?? "", secao.Dias, secao.FotosPorPagina, secao.MaximoDeFotos, secao.Nivel, secao.ComFoto, secao.PaginaNova);
        Filtro = RelatorioPersonalizadoViewModel.Filtros.First(f => f.Valor == secao.Filtro);
    }

    public TipoDeSecao Tipo { get; }

    /// <summary>"Fotos do período".</summary>
    public string Nome => SecaoDoRelatorio.TituloPadrao(Tipo);

    public string Descricao => SecaoDoRelatorio.Descricao(Tipo);

    [ObservableProperty] public partial bool Ativa { get; set; }
    [ObservableProperty] public partial string Titulo { get; set; }
    [ObservableProperty] public partial string Texto { get; set; }
    [ObservableProperty] public partial OpcaoDeFiltroDeTarefas Filtro { get; set; }
    [ObservableProperty] public partial int Dias { get; set; }
    [ObservableProperty] public partial int FotosPorPagina { get; set; }
    [ObservableProperty] public partial int MaximoDeFotos { get; set; }
    [ObservableProperty] public partial int Nivel { get; set; }
    [ObservableProperty] public partial bool ComFoto { get; set; }
    [ObservableProperty] public partial bool PaginaNova { get; set; }

    /// <summary>As opções abertas na tela (o botão de engrenagem).</summary>
    [ObservableProperty] public partial bool Aberta { get; set; }

    public bool TemTexto => Tipo is TipoDeSecao.Texto or TipoDeSecao.Capa;
    public string RotuloDoTexto => Tipo == TipoDeSecao.Capa ? "Subtítulo" : "Texto (uma linha em branco separa os parágrafos)";
    public bool TemFiltro => Tipo == TipoDeSecao.Tarefas;
    public bool TemDias => Tipo == TipoDeSecao.Proximas;
    public bool TemFotos => Tipo == TipoDeSecao.Fotos;
    public bool TemNivel => Tipo == TipoDeSecao.Gantt;
    public bool EhCapa => Tipo == TipoDeSecao.Capa;

    [RelayCommand]
    private void Subir() => _tela.Mover(this, -1);

    [RelayCommand]
    private void Descer() => _tela.Mover(this, 1);

    [RelayCommand]
    private void Remover() => _tela.Remover(this);

    [RelayCommand]
    private void AlternarOpcoes() => Aberta = !Aberta;

    public SecaoDoRelatorio ComoSecao() => new(Tipo)
    {
        Ativa = Ativa,
        Titulo = string.IsNullOrWhiteSpace(Titulo) ? null : Titulo.Trim(),
        Texto = string.IsNullOrWhiteSpace(Texto) ? null : Texto,
        Filtro = Filtro.Valor,
        Dias = Dias,
        FotosPorPagina = FotosPorPagina,
        MaximoDeFotos = MaximoDeFotos,
        Nivel = Nivel,
        ComFoto = ComFoto,
        PaginaNova = PaginaNova,
    };

    partial void OnAtivaChanged(bool value) => _tela.Mudou();
    partial void OnTituloChanged(string value) => _tela.Mudou();
    partial void OnTextoChanged(string value) => _tela.Mudou();
    partial void OnFiltroChanged(OpcaoDeFiltroDeTarefas value) => _tela.Mudou();
    partial void OnDiasChanged(int value) => _tela.Mudou();
    partial void OnFotosPorPaginaChanged(int value) => _tela.Mudou();
    partial void OnMaximoDeFotosChanged(int value) => _tela.Mudou();
    partial void OnNivelChanged(int value) => _tela.Mudou();
    partial void OnComFotoChanged(bool value) => _tela.Mudou();
    partial void OnPaginaNovaChanged(bool value) => _tela.Mudou();
}

/// <summary>
/// Bloco 88: Arquivo > Relatório personalizado…: o relatório montado com as seções que o usuário escolhe (capa, números, etapas,
/// Gantt, tarefas, fotos, diário, medições, custos, OS, texto, assinaturas), na ordem que ele quiser, com a cor, o papel e o rodapé
/// dele, e a prévia ao vivo. Os modelos (os prontos e os salvos) ficam nas preferências e servem para qualquer obra.
/// </summary>
public sealed partial class RelatorioPersonalizadoViewModel : ViewModelBase, IDisposable
{
    public const int LarguraDaPrevia = 820;

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly IModelosDeRelatorio _modelos;
    private readonly Func<CabecalhoDoRelatorio> _cabecalho;
    private readonly Func<Guid, byte[]?> _imagem;
    private RelatorioPersonalizado? _montado;
    private PeriodoPadrao _periodoPadrao;
    private bool _carregando;
    private bool _soEscolher; // escolher o modelo na lista sem carregar (depois de salvar, a tela já está como ele)
    private int _versao;

    public RelatorioPersonalizadoViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, IModelosDeRelatorio modelos,
        Func<CabecalhoDoRelatorio> cabecalho, Func<Guid, byte[]?> imagem)
    {
        (_cronograma, _dialogos, _relogio, _modelos, _cabecalho, _imagem) = (cronograma, dialogos, relogio, modelos, cabecalho, imagem);
        var salvos = modelos.Carregar();
        AtualizarLista(salvos);
        ModeloEscolhido = Modelos.FirstOrDefault(m => m.Nome == salvos.Ultimo) ?? Modelos[0];
    }

    public static IReadOnlyList<OpcaoDeFiltroDeTarefas> Filtros { get; } =
    [
        new("Todas", FiltroDeTarefas.Todas), new("Em andamento", FiltroDeTarefas.EmAndamento), new("Atrasadas", FiltroDeTarefas.Atrasadas),
        new("Concluídas", FiltroDeTarefas.Concluidas),
    ];

    public static IReadOnlyList<OpcaoDePapel> Papeis { get; } = [new("A4", PapelDoRelatorio.A4), new("Carta", PapelDoRelatorio.Carta)];

    public static IReadOnlyList<OpcaoDeSecao> TiposDeSecao { get; } =
        [.. Enum.GetValues<TipoDeSecao>().Select(t => new OpcaoDeSecao(SecaoDoRelatorio.TituloPadrao(t), t))];

    public static IReadOnlyList<string> Cores => TemaDoRelatorio.Cores;
    public static IReadOnlyList<int> OpcoesDeDias { get; } = [7, 15, 30, 60, 90];
    public static IReadOnlyList<int> OpcoesDeFotosPorPagina => SecaoDoRelatorio.FotosPorPaginaAceitas;
    public static IReadOnlyList<int> OpcoesDeMaximoDeFotos { get; } = [6, 12, 24, 48, 100, 200];
    public static IReadOnlyList<int> OpcoesDeNivel { get; } = [1, 2, 3, 4, 5, 6];

    public string TituloDaJanela => $"Relatório personalizado — {_cronograma.Nome}";

    public ObservableCollection<ModeloNaLista> Modelos { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExcluirModeloCommand))]
    public partial ModeloNaLista? ModeloEscolhido { get; set; }

    [ObservableProperty] public partial string Titulo { get; set; } = "";
    [ObservableProperty] public partial string Cor { get; set; } = TemaDoRelatorio.Cores[0];
    [ObservableProperty] public partial OpcaoDePapel Papel { get; set; } = Papeis[0];
    [ObservableProperty] public partial bool Paisagem { get; set; }
    [ObservableProperty] public partial bool ComLogotipo { get; set; } = true;
    [ObservableProperty] public partial string Rodape { get; set; } = "";
    [ObservableProperty] public partial DateTime? De { get; set; }
    [ObservableProperty] public partial DateTime? Ate { get; set; }

    public ObservableCollection<SecaoNaTela> Secoes { get; } = [];

    // ---- A prévia ----

    /// <summary>A página da prévia (PNG); nula sem relatório (um erro).</summary>
    [ObservableProperty] public partial byte[]? Previa { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaPagina))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(ProximaPaginaCommand))]
    public partial int PaginaAtual { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaPagina))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(ProximaPaginaCommand), nameof(GerarPdfCommand))]
    public partial int TotalDePaginas { get; private set; }

    public string TextoDaPagina => TotalDePaginas == 0 ? "Sem páginas" : $"Página {PaginaAtual + 1} de {TotalDePaginas}";

    /// <summary>O que impede o relatório (uma opção que não vale); vazio quando está tudo certo.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GerarPdfCommand))]
    public partial string Erro { get; private set; } = "";

    /// <summary>As seções que ficaram sem dados no período.</summary>
    [ObservableProperty] public partial string Avisos { get; private set; } = "";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GerarPdfCommand))]
    public partial bool Gerando { get; private set; }

    /// <summary>A montagem em curso (os testes esperam por ela).</summary>
    internal Task Atualizacao { get; private set; } = Task.CompletedTask;

    /// <summary>O intervalo entre a última mudança e a nova prévia (várias mudanças seguidas montam uma vez só).</summary>
    internal TimeSpan Espera { get; set; } = TimeSpan.FromMilliseconds(180);

    // ---- O modelo ----

    partial void OnModeloEscolhidoChanged(ModeloNaLista? value)
    {
        if (!_soEscolher && value is not null && Achar(value.Nome) is { } modelo)
            Carregar(modelo);
    }

    private ModeloDeRelatorio? Achar(string nome) =>
        _modelos.Carregar().Modelos.FirstOrDefault(m => m.Nome == nome) ?? ModelosProntos.PeloNome(nome);

    private void Carregar(ModeloDeRelatorio modelo)
    {
        _carregando = true;
        try
        {
            Titulo = modelo.Titulo;
            Cor = modelo.Tema.Cor;
            Papel = Papeis.First(p => p.Valor == modelo.Tema.Papel);
            Paisagem = modelo.Tema.Paisagem;
            ComLogotipo = modelo.Tema.ComLogotipo;
            Rodape = modelo.Tema.Rodape ?? "";
            Secoes.Clear();
            foreach (var secao in modelo.Secoes)
                Secoes.Add(new SecaoNaTela(this, secao));
            Periodo(modelo.Periodo);
        }
        finally
        {
            _carregando = false;
        }
        Mudou();
    }

    /// <summary>O modelo como está na tela (com o nome do escolhido).</summary>
    public ModeloDeRelatorio ModeloAtual() => new()
    {
        Nome = ModeloEscolhido?.Nome ?? "Meu relatório",
        Titulo = Titulo.Trim(),
        Periodo = _periodoPadrao,
        Tema = new TemaDoRelatorio
        {
            Cor = Cor.Trim(),
            Papel = Papel.Valor,
            Paisagem = Paisagem,
            ComLogotipo = ComLogotipo,
            Rodape = string.IsNullOrWhiteSpace(Rodape) ? null : Rodape.Trim(),
        },
        Secoes = [.. Secoes.Select(s => s.ComoSecao())],
    };

    partial void OnTituloChanged(string value) => Mudou();
    partial void OnCorChanged(string value) => Mudou();
    partial void OnPapelChanged(OpcaoDePapel value) => Mudou();
    partial void OnPaisagemChanged(bool value) => Mudou();
    partial void OnComLogotipoChanged(bool value) => Mudou();
    partial void OnRodapeChanged(string value) => Mudou();
    partial void OnDeChanged(DateTime? value) => Mudou();
    partial void OnAteChanged(DateTime? value) => Mudou();

    [RelayCommand]
    private void EscolherCor(string cor) => Cor = cor;

    // ---- As seções ----

    [RelayCommand]
    private void AdicionarSecao(TipoDeSecao tipo)
    {
        var secao = new SecaoNaTela(this, new SecaoDoRelatorio(tipo)) { Aberta = tipo is TipoDeSecao.Texto };
        if (tipo == TipoDeSecao.Capa)
            Secoes.Insert(0, secao);
        else
            Secoes.Add(secao);
        Mudou();
    }

    internal void Mover(SecaoNaTela secao, int passo)
    {
        var de = Secoes.IndexOf(secao);
        var para = de + passo;
        if (de < 0 || para < 0 || para >= Secoes.Count)
            return;
        Secoes.Move(de, para);
        Mudou();
    }

    internal void Remover(SecaoNaTela secao)
    {
        if (Secoes.Remove(secao))
            Mudou();
    }

    // ---- O período ----

    [RelayCommand]
    private void EstaSemana() => Periodo(PeriodoPadrao.EstaSemana);

    [RelayCommand]
    private void EsteMes() => Periodo(PeriodoPadrao.EsteMes);

    [RelayCommand]
    private void MesPassado() => Periodo(PeriodoPadrao.MesPassado);

    [RelayCommand]
    private void DesdeOInicio() => Periodo(PeriodoPadrao.DesdeOInicio);

    private DateOnly Hoje => DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);

    private void Periodo(PeriodoPadrao padrao)
    {
        _periodoPadrao = padrao;
        var periodo = PeriodoDoRelatorio.Do(padrao, DateOnly.FromDateTime(_cronograma.DataInicio), Hoje);
        var carregando = _carregando;
        _carregando = true;
        (De, Ate) = (periodo.De.ToDateTime(TimeOnly.MinValue), periodo.Ate.ToDateTime(TimeOnly.MinValue));
        _carregando = carregando;
        Mudou();
    }

    // ---- A montagem e a prévia ----

    /// <summary>Algo mudou na tela: a prévia é montada de novo (depois de um instante, uma vez para várias mudanças).</summary>
    internal void Mudou()
    {
        if (_carregando)
            return;
        Atualizacao = AtualizarAsync(++_versao, PaginaAtual);
    }

    private async Task AtualizarAsync(int versao, int pagina)
    {
        if (Espera > TimeSpan.Zero)
            await Task.Delay(Espera);
        if (versao != _versao)
            return;
        ModeloDeRelatorio modelo;
        PeriodoDoRelatorio periodo;
        try
        {
            modelo = ModeloAtual();
            modelo.Validar();
            if (De is null || Ate is null)
                throw new DomainValidationException(nameof(De), "Escolha o período.");
            periodo = new PeriodoDoRelatorio(DateOnly.FromDateTime(De.Value), DateOnly.FromDateTime(Ate.Value));
        }
        catch (DomainValidationException ex)
        {
            Trocar(null);
            (Erro, Avisos, Previa, TotalDePaginas, PaginaAtual) = (ex.Message, "", null, 0, 0);
            return;
        }
        var cabecalho = _cabecalho();
        var agora = _relogio.GetLocalNow().DateTime;
        RelatorioPersonalizado montado;
        byte[] previa;
        try
        {
            (montado, previa) = await Task.Run(() =>
            {
                var relatorio = GeradorRelatorioPersonalizado.Montar(modelo, _cronograma, cabecalho, periodo, _imagem, agora);
                try
                {
                    return (relatorio, relatorio.Previa(Math.Clamp(pagina, 0, relatorio.Paginas - 1), LarguraDaPrevia));
                }
                catch
                {
                    relatorio.Dispose();
                    throw;
                }
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Um dado que o relatório não soube desenhar: a tela avisa em vez de ficar com a prévia parada.
            if (versao == _versao)
            {
                Trocar(null);
                (Erro, Avisos, Previa, TotalDePaginas, PaginaAtual) = ($"Não foi possível montar o relatório: {ex.Message}", "", null, 0, 0);
            }
            return;
        }
        if (versao != _versao)
        {
            montado.Dispose();
            return;
        }
        Trocar(montado);
        Erro = "";
        Avisos = string.Join("\n", montado.Avisos);
        TotalDePaginas = montado.Paginas;
        PaginaAtual = Math.Clamp(pagina, 0, montado.Paginas - 1);
        Previa = previa;
    }

    private void Trocar(RelatorioPersonalizado? novo)
    {
        var antigo = _montado;
        _montado = novo;
        antigo?.Dispose();
    }

    private bool PodeVoltar() => PaginaAtual > 0;

    private bool PodeAvancar() => PaginaAtual < TotalDePaginas - 1;

    [RelayCommand(CanExecute = nameof(PodeVoltar))]
    private Task PaginaAnterior() => IrPara(PaginaAtual - 1);

    [RelayCommand(CanExecute = nameof(PodeAvancar))]
    private Task ProximaPagina() => IrPara(PaginaAtual + 1);

    private async Task IrPara(int pagina)
    {
        if (_montado is not { } relatorio || pagina < 0 || pagina >= relatorio.Paginas)
            return;
        PaginaAtual = pagina;
        try
        {
            var previa = await Task.Run(() => relatorio.Previa(pagina, LarguraDaPrevia));
            if (relatorio == _montado && pagina == PaginaAtual)
                Previa = previa;
        }
        catch (ObjectDisposedException)
        {
            // A tela montou outro relatório nesse meio-tempo: a prévia dele já vem.
        }
    }

    // ---- Salvar, excluir e gerar ----

    [RelayCommand]
    private async Task SalvarModelo()
    {
        if (ModeloEscolhido is not { Pronto: false } escolhido)
        {
            await SalvarComo();
            return;
        }
        Guardar(ModeloAtual() with { Nome = escolhido.Nome });
    }

    [RelayCommand]
    private async Task SalvarComo()
    {
        var sugestao = ModeloEscolhido is { Pronto: false } escolhido ? escolhido.Nome : $"{ModeloEscolhido?.Nome ?? "Meu relatório"} (meu)";
        if (await _dialogos.PedirTextoAsync("Salvar o modelo", "Nome do modelo (ele fica para as outras obras também):", sugestao) is not { } nome
            || string.IsNullOrWhiteSpace(nome))
            return;
        nome = nome.Trim();
        if (ModelosProntos.PeloNome(nome) is not null)
        {
            Mensagem = $"\"{nome}\" é o nome de um modelo pronto: escolha outro nome.";
            return;
        }
        Guardar(ModeloAtual() with { Nome = nome });
    }

    private void Guardar(ModeloDeRelatorio modelo)
    {
        try
        {
            modelo.Validar();
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        var salvos = _modelos.Carregar();
        var lista = salvos.Modelos.Where(m => m.Nome != modelo.Nome).Append(modelo).OrderBy(m => m.Nome, StringComparer.CurrentCulture).ToList();
        var novos = new ModelosSalvos(lista, modelo.Nome);
        _modelos.Salvar(novos);
        _soEscolher = true;
        AtualizarLista(novos);
        ModeloEscolhido = Modelos.First(m => m.Nome == modelo.Nome);
        _soEscolher = false;
        Mensagem = $"Modelo \"{modelo.Nome}\" salvo: ele aparece na lista para qualquer obra.";
    }

    private bool PodeExcluir() => ModeloEscolhido is { Pronto: false };

    [RelayCommand(CanExecute = nameof(PodeExcluir))]
    private async Task ExcluirModelo()
    {
        if (ModeloEscolhido is not { Pronto: false } escolhido
            || !await _dialogos.ConfirmarAsync("Excluir o modelo", $"Excluir o modelo \"{escolhido.Nome}\"? Os relatórios já gerados não mudam.", "Excluir"))
            return;
        var salvos = _modelos.Carregar();
        var novos = new ModelosSalvos([.. salvos.Modelos.Where(m => m.Nome != escolhido.Nome)], salvos.Ultimo == escolhido.Nome ? null : salvos.Ultimo);
        _modelos.Salvar(novos);
        AtualizarLista(novos);
        ModeloEscolhido = Modelos[0];
        Mensagem = $"Modelo \"{escolhido.Nome}\" excluído.";
    }

    private void AtualizarLista(ModelosSalvos salvos)
    {
        Modelos.Clear();
        foreach (var modelo in ModelosProntos.Todos)
            Modelos.Add(new ModeloNaLista(modelo.Nome, Pronto: true));
        foreach (var modelo in salvos.Modelos)
            Modelos.Add(new ModeloNaLista(modelo.Nome, Pronto: false));
    }

    private bool PodeGerar() => !Gerando && TotalDePaginas > 0 && Erro.Length == 0;

    [RelayCommand(CanExecute = nameof(PodeGerar))]
    private async Task GerarPdf()
    {
        await Atualizacao;
        if (_montado is not { } relatorio)
            return;
        if (await _dialogos.EscolherArquivoDoRelatorioAsync(relatorio.NomeSugerido, "pdf") is not { } caminho)
            return;
        Gerando = true;
        Mensagem = "Gerando o PDF…";
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    relatorio.GerarPdf(destino);
                File.Move(temporario, caminho, overwrite: true);
            });
            Mensagem = $"Relatório salvo: {caminho} ({(relatorio.Paginas == 1 ? "1 página" : $"{relatorio.Paginas} páginas")}).";
            var salvos = _modelos.Carregar();
            _modelos.Salvar(salvos with { Ultimo = ModeloEscolhido?.Nome });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = ex is ObjectDisposedException ? "O relatório mudou antes de o PDF começar: gere de novo." : $"Não foi possível salvar o relatório: {ex.Message}";
        }
        finally
        {
            Gerando = false;
        }
    }

    public void Dispose() => Trocar(null);
}
