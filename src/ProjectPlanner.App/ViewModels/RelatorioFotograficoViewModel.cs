using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Relatorios;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma escolha das listas da tela do relatório.</summary>
public sealed record Escolha<T>(T Valor, string Texto)
{
    public override string ToString() => Texto; // o que as listas mostram
}

/// <summary>
/// Arquivo > Relatório fotográfico… (Bloco 27): o que entra (período, tarefas, tags), como organizar (agrupamento,
/// fotos por página, resumo, índice, assinaturas) e o cabeçalho (empresa nas preferências; obra no projeto), com a prévia
/// do tamanho e o aviso de fotos parecidas. Gera em PDF e em Word; o primeiro arquivo gerado registra o relatório no
/// histórico do projeto, e os seguintes (o outro formato, outra tentativa) usam o mesmo número.
/// </summary>
public sealed partial class RelatorioFotograficoViewModel : ViewModelBase
{
    private readonly Cronograma _cronograma;
    private readonly FotosDoProjeto _fotos;
    private readonly IPreferenciasRelatorio _preferencias;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Action _projetoMudou;
    private readonly bool _pronta;
    private int? _numeroGerado;
    private byte[]? _logotipo;

    /// <param name="cronograma">O cronograma já calculado.</param>
    /// <param name="projetoMudou">Os dados da obra ou o histórico mudaram: o projeto tem alterações não salvas.</param>
    /// <param name="analista">Bloco 29: a IA que sugere o texto do andamento (sem ela, o botão avisa que falta configurar).</param>
    public RelatorioFotograficoViewModel(Cronograma cronograma, FotosDoProjeto fotos, IPreferenciasRelatorio preferencias,
        IDialogosService dialogos, TimeProvider relogio, Action projetoMudou, ProjectPlanner.IA.IAnalistaDeFotos? analista = null)
    {
        _analista = analista ?? new ProjectPlanner.IA.AnalistaIndisponivel();
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(fotos);
        ArgumentNullException.ThrowIfNull(preferencias);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(projetoMudou);
        (_cronograma, _fotos, _preferencias, _dialogos, _relogio, _projetoMudou) = (cronograma, fotos, preferencias, dialogos, relogio, projetoMudou);

        var edt = cronograma.NumeracaoEdt();
        Ramos = [.. cronograma.Tarefas.Where(t => t.EhResumo).Select(t => new Escolha<Guid>(t.Id, $"{edt[t.Id]} {t.Nome}"))];
        Ordens = [.. cronograma.OrdensDeServico.Todas.Where(o => o.Situacao != Core.OrdensDeServico.SituacaoDaOrdem.Rascunho)
            .Select(o => new Escolha<Guid>(o.Id, $"{o.Codigo} · {o.Dados.Titulo}"))];
        var salvas = preferencias.Carregar();
        var (de, ate) = MontadorDoRelatorio.DesdeOUltimo(cronograma, Hoje);
        (De, Ate) = (de.ToDateTime(TimeOnly.MinValue), ate.ToDateTime(TimeOnly.MinValue));
        TarefasEscolhidas = OpcoesDeTarefas[0];
        RamoEscolhido = Ramos.FirstOrDefault();
        OrdemEscolhida = Ordens.LastOrDefault();
        AgrupamentoEscolhido = OpcoesDeAgrupamento.First(o => o.Valor == salvas.Agrupamento);
        FotosPorPaginaEscolhida = OpcoesDeFotosPorPagina.First(o => o.Valor == salvas.FotosPorPagina);
        (Responsavel, Registro, Logotipo) = (salvas.Responsavel ?? "", salvas.Registro ?? "", salvas.Logotipo);
        _logotipo = LerLogotipo(salvas.Logotipo);
        (Contratante, Contrato, Endereco) = (cronograma.Obra.Contratante ?? "", cronograma.Obra.Contrato ?? "", cronograma.Obra.Endereco ?? "");
        _pronta = true;
        PropertyChanged += Mudou;
        Atualizar();
    }

    private DateOnly Hoje => DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);

    // ---- O que entra ----

    [ObservableProperty] public partial DateTime? De { get; set; }
    [ObservableProperty] public partial DateTime? Ate { get; set; }

    /// <summary>Do dia seguinte ao último relatório (ou do início do projeto) até hoje.</summary>
    [RelayCommand]
    private void DesdeOUltimo()
    {
        var (de, ate) = MontadorDoRelatorio.DesdeOUltimo(_cronograma, Hoje);
        (De, Ate) = (de.ToDateTime(TimeOnly.MinValue), ate.ToDateTime(TimeOnly.MinValue));
    }

    public IReadOnlyList<Escolha<TarefasDoRelatorio>> OpcoesDeTarefas { get; } =
    [
        new(TarefasDoRelatorio.Todas, "Todas as tarefas"),
        new(TarefasDoRelatorio.EmExecucaoNoPeriodo, "Em execução no período"),
        new(TarefasDoRelatorio.SoCriticas, "Só as críticas"),
        new(TarefasDoRelatorio.SoAtrasadas, "Só as atrasadas"),
        new(TarefasDoRelatorio.DeUmRamo, "Um resumo (etapa)"),
        new(TarefasDoRelatorio.DeUmaOrdem, "Uma ordem de serviço"),
    ];

    /// <summary>Bloco 66: as OS que já saíram do rascunho, para o relatório de uma OS.</summary>
    public IReadOnlyList<Escolha<Guid>> Ordens { get; }

    [ObservableProperty] public partial Escolha<Guid>? OrdemEscolhida { get; set; }

    public bool EscolheOrdem => TarefasEscolhidas.Valor == TarefasDoRelatorio.DeUmaOrdem;

    /// <summary>Bloco 66: o relatório das fotos de uma OS: as tarefas dela, no período dela (da emissão à conferência, ou hoje).</summary>
    public RelatorioFotograficoViewModel ParaAOrdem(Guid ordem)
    {
        if (Ordens.FirstOrDefault(o => o.Valor == ordem) is { } escolhida)
        {
            OrdemEscolhida = escolhida;
            TarefasEscolhidas = OpcoesDeTarefas.First(o => o.Valor == TarefasDoRelatorio.DeUmaOrdem);
            UsarPeriodoDaOrdem();
        }
        return this;
    }

    /// <summary>O período da OS escolhida.</summary>
    [RelayCommand]
    private void UsarPeriodoDaOrdem()
    {
        if (OrdemEscolhida is { } o && _cronograma.OrdensDeServico.Obter(o.Valor) is { } ordem)
        {
            var (de, ate) = MontadorDoRelatorio.PeriodoDaOrdem(ordem, Hoje);
            (De, Ate) = (de.ToDateTime(TimeOnly.MinValue), ate.ToDateTime(TimeOnly.MinValue));
        }
    }

    [ObservableProperty] public partial Escolha<TarefasDoRelatorio> TarefasEscolhidas { get; set; }

    public IReadOnlyList<Escolha<Guid>> Ramos { get; }

    [ObservableProperty] public partial Escolha<Guid>? RamoEscolhido { get; set; }

    public bool EscolheRamo => TarefasEscolhidas.Valor == TarefasDoRelatorio.DeUmRamo;

    /// <summary>Só fotos com alguma destas tags, separadas por vírgula (vazio: todas).</summary>
    [ObservableProperty] public partial string Tags { get; set; } = "";

    // ---- Como organizar ----

    public IReadOnlyList<Escolha<AgrupamentoDoRelatorio>> OpcoesDeAgrupamento { get; } =
    [
        new(AgrupamentoDoRelatorio.PorTarefa, "Por tarefa (na ordem da EDT)"),
        new(AgrupamentoDoRelatorio.PorData, "Por data"),
        new(AgrupamentoDoRelatorio.PorLocal, "Por local"),
    ];

    [ObservableProperty] public partial Escolha<AgrupamentoDoRelatorio> AgrupamentoEscolhido { get; set; }

    public IReadOnlyList<Escolha<int>> OpcoesDeFotosPorPagina { get; } =
        [new(1, "1 por página"), new(2, "2 por página"), new(4, "4 por página"), new(6, "6 por página")];

    [ObservableProperty] public partial Escolha<int> FotosPorPaginaEscolhida { get; set; }

    [ObservableProperty] public partial bool ComResumo { get; set; } = true;

    /// <summary>Bloco 37: uma página de indicadores (valor agregado, Curva S, etapas e atrasos) no fim do período.</summary>
    [ObservableProperty] public partial bool ComIndicadores { get; set; }
    [ObservableProperty] public partial bool ComIndice { get; set; } = true;
    [ObservableProperty] public partial bool ComAssinaturas { get; set; } = true;

    // ---- Cabeçalho ----

    [ObservableProperty] public partial string Responsavel { get; set; }
    [ObservableProperty] public partial string Registro { get; set; }
    [ObservableProperty] public partial string? Logotipo { get; private set; }

    public byte[]? ImagemDoLogotipo => _logotipo;
    public bool TemLogotipo => _logotipo is not null;

    [ObservableProperty] public partial string Contratante { get; set; }
    [ObservableProperty] public partial string Contrato { get; set; }
    [ObservableProperty] public partial string Endereco { get; set; }

    [RelayCommand]
    private async Task EscolherLogotipo()
    {
        if (await _dialogos.EscolherImagemAsync() is not { } arquivo)
            return;
        try
        {
            var copia = _preferencias.GuardarLogotipo(arquivo);
            _logotipo = LerLogotipo(copia) ?? throw new IOException("o arquivo não é uma imagem que dê para ler");
            Logotipo = copia;
            _preferencias.Salvar(_preferencias.Carregar() with { Logotipo = copia });
            Mensagem = "Logotipo guardado: vale para os próximos relatórios.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível usar o logotipo: {ex.Message}.";
        }
        AvisarLogotipo();
    }

    [RelayCommand]
    private void RemoverLogotipo()
    {
        (_logotipo, Logotipo) = (null, null);
        _preferencias.Salvar(_preferencias.Carregar() with { Logotipo = null });
        AvisarLogotipo();
    }

    private void AvisarLogotipo()
    {
        OnPropertyChanged(nameof(ImagemDoLogotipo));
        OnPropertyChanged(nameof(TemLogotipo));
        Atualizar();
    }

    private static byte[]? LerLogotipo(string? arquivo)
    {
        try
        {
            return arquivo is not null && File.Exists(arquivo) ? File.ReadAllBytes(arquivo) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- Prévia ----

    /// <summary>"Relatório nº 3 · 24 fotos · 14 páginas".</summary>
    [ObservableProperty] public partial string Previa { get; private set; } = "";

    /// <summary>Fotos da mesma tarefa com poucos minutos de diferença (talvez baste uma).</summary>
    [ObservableProperty] public partial string AvisoDeParecidas { get; private set; } = "";

    /// <summary>Uma escolha que não vale (período invertido, sem resumo, dado longo demais) ou nenhuma foto.</summary>
    [ObservableProperty] public partial string Erro { get; private set; } = "";

    /// <summary>O resultado da última geração.</summary>
    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GerarPdfCommand), nameof(GerarWordCommand))]
    public partial bool Gerando { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirUltimoCommand))]
    public partial string? UltimoArquivo { get; private set; }

    private RelatorioFotografico? _relatorio;

    private static readonly HashSet<string> NaoMudamAPrevia =
        [nameof(Previa), nameof(AvisoDeParecidas), nameof(Erro), nameof(Mensagem), nameof(Gerando), nameof(UltimoArquivo),
         nameof(EscolheRamo), nameof(EscolheOrdem), nameof(ImagemDoLogotipo), nameof(TemLogotipo), nameof(Logotipo),
         nameof(IaOcupada), nameof(AvisoDaIa), nameof(TemFotosForaDoPeriodo)];

    private void Mudou(object? sender, PropertyChangedEventArgs e)
    {
        if (!_pronta || NaoMudamAPrevia.Contains(e.PropertyName ?? ""))
            return;
        if (e.PropertyName == nameof(TarefasEscolhidas))
        {
            OnPropertyChanged(nameof(EscolheRamo));
            OnPropertyChanged(nameof(EscolheOrdem));
        }
        if (e.PropertyName == nameof(OrdemEscolhida) || e.PropertyName == nameof(TarefasEscolhidas) && EscolheOrdem)
            UsarPeriodoDaOrdem(); // Bloco 66: as fotos da OS são as do período dela
        if (e.PropertyName is nameof(Contratante) or nameof(Contrato) or nameof(Endereco))
            GuardarObra();
        Atualizar();
    }

    // Os dados da obra vão para o projeto na hora (são dele, não do relatório).
    private void GuardarObra()
    {
        try
        {
            var obra = new DadosDaObra(Contratante, Contrato, Endereco);
            if (obra == _cronograma.Obra)
                return;
            _cronograma.DefinirObra(obra);
            _projetoMudou();
        }
        catch (DomainValidationException)
        {
            // O erro aparece na prévia (Atualizar).
        }
    }

    private void Atualizar()
    {
        _relatorio = null;
        AvisoDeParecidas = "";
        try
        {
            _relatorio = Montar();
            if (_relatorio.TotalDeFotos > 0 && _fotosForaDoPeriodo is not null)
            {
                _fotosForaDoPeriodo = null;
                OnPropertyChanged(nameof(TemFotosForaDoPeriodo));
                IncluirTodasAsFotosCommand.NotifyCanExecuteChanged();
            }
            Erro = _relatorio.TotalDeFotos == 0 ? SemFotosNoPeriodo() : "";
            Previa = $"Relatório nº {_relatorio.Numero} · {Quantas(_relatorio.TotalDeFotos, "foto", "fotos")} · " +
                     Quantas(_relatorio.TotalDePaginas, "página", "páginas");
            AvisoDeParecidas = _relatorio.Parecidas.Count switch
            {
                0 => "",
                1 => $"As fotos {_relatorio.Parecidas[0].Primeira.Numero} e {_relatorio.Parecidas[0].Segunda.Numero} são da mesma tarefa, " +
                     "com poucos minutos de diferença: talvez baste uma (tire a outra do relatório na galeria).",
                var n => $"{n} pares de fotos da mesma tarefa com poucos minutos de diferença (por exemplo, as fotos " +
                         $"{_relatorio.Parecidas[0].Primeira.Numero} e {_relatorio.Parecidas[0].Segunda.Numero}): talvez baste uma de cada " +
                         "(tire as outras do relatório na galeria).",
            };
        }
        catch (DomainValidationException ex)
        {
            (Erro, Previa) = (ex.Message, "");
        }
        GerarPdfCommand.NotifyCanExecuteChanged();
        GerarWordCommand.NotifyCanExecuteChanged();
    }

    private static string Quantas(int n, string um, string varios) => n == 1 ? $"1 {um}" : $"{n} {varios}";

    // ---- Bloco 42: onde estão as fotos quando o período não pega nenhuma ----

    private (DateOnly De, DateOnly Ate, int Quantas)? _fotosForaDoPeriodo;

    /// <summary>Há fotos (que podem ir ao relatório) fora do período escolhido: o botão as inclui.</summary>
    public bool TemFotosForaDoPeriodo => _fotosForaDoPeriodo is not null;

    // "Nenhuma foto no período com essas escolhas." e, se há fotos fora do período, quantas e de quando (a data da câmera).
    private string SemFotosNoPeriodo()
    {
        const string semFotos = "Nenhuma foto no período com essas escolhas.";
        var datas = _cronograma.Tarefas.SelectMany(t => t.Fotos).Where(f => f.NoRelatorio).Select(f => DateOnly.FromDateTime(f.DataFoto)).ToList();
        var (de, ate) = (De is { } d ? DateOnly.FromDateTime(d) : DateOnly.MinValue, Ate is { } a ? DateOnly.FromDateTime(a) : DateOnly.MaxValue);
        var fora = datas.Where(d => d < de || d > ate).ToList();
        _fotosForaDoPeriodo = fora.Count == 0 ? null : (fora.Min(), fora.Max(), fora.Count);
        OnPropertyChanged(nameof(TemFotosForaDoPeriodo));
        IncluirTodasAsFotosCommand.NotifyCanExecuteChanged();
        if (_fotosForaDoPeriodo is not { } f)
            return semFotos;
        var quando = f.De == f.Ate ? $"em {f.De:dd/MM/yyyy}" : $"de {f.De:dd/MM/yyyy} a {f.Ate:dd/MM/yyyy}";
        return $"{semFotos} {(f.Quantas == 1 ? "1 foto do projeto foi tirada" : $"{f.Quantas} fotos do projeto foram tiradas")} fora dele, {quando} " +
               "(a data da câmera): use Incluir todas as fotos.";
    }

    /// <summary>Estende o período para pegar as fotos que estão fora dele.</summary>
    [RelayCommand(CanExecute = nameof(TemFotosForaDoPeriodo))]
    private void IncluirTodasAsFotos()
    {
        if (_fotosForaDoPeriodo is not { } f)
            return;
        var de = De is { } d && DateOnly.FromDateTime(d) < f.De ? DateOnly.FromDateTime(d) : f.De;
        var ate = Ate is { } a && DateOnly.FromDateTime(a) > f.Ate ? DateOnly.FromDateTime(a) : f.Ate;
        (De, Ate) = (de.ToDateTime(TimeOnly.MinValue), ate.ToDateTime(TimeOnly.MinValue));
    }

    private RelatorioFotografico Montar()
    {
        if (De is not { } de || Ate is not { } ate)
            throw new DomainValidationException(nameof(De), "Escolha o período.");
        var opcoes = new OpcoesDoRelatorio(DateOnly.FromDateTime(de), DateOnly.FromDateTime(ate))
        {
            Tarefas = TarefasEscolhidas.Valor,
            Ramo = RamoEscolhido?.Valor,
            Ordem = OrdemEscolhida?.Valor,
            Tags = MetadadosFoto.LerTags(Tags),
            Agrupamento = AgrupamentoEscolhido.Valor,
            FotosPorPagina = FotosPorPaginaEscolhida.Valor,
            ComResumo = ComResumo,
            ComIndice = ComIndice,
            ComAssinaturas = ComAssinaturas,
            ComAntesEDepois = ComAntesEDepois,
            ComIndicadores = ComIndicadores,
            Texto = new TextoDoRelatorio(TextoDoAndamento, TextoDoRelatorio.LerPontos(TextoDosPontos)),
        };
        var cabecalho = new CabecalhoDoRelatorio(_cronograma.Nome, new DadosDaObra(Contratante, Contrato, Endereco),
            string.IsNullOrWhiteSpace(Responsavel) ? null : Responsavel.Trim(),
            string.IsNullOrWhiteSpace(Registro) ? null : Registro.Trim(), _logotipo);
        return MontadorDoRelatorio.Montar(_cronograma, opcoes, cabecalho, _relogio.GetLocalNow().DateTime, _numeroGerado);
    }

    // ---- Gerar ----

    private bool PodeGerar() => !Gerando && _relatorio is { TotalDeFotos: > 0 } && Erro.Length == 0;

    [RelayCommand(CanExecute = nameof(PodeGerar))]
    private Task GerarPdf() => GerarAsync(new GeradorPdf());

    [RelayCommand(CanExecute = nameof(PodeGerar))]
    private Task GerarWord() => GerarAsync(new GeradorWord());

    private async Task GerarAsync(IGeradorDeRelatorio gerador)
    {
        var relatorio = _relatorio!;
        if (await _dialogos.EscolherArquivoDoRelatorioAsync(relatorio.NomeSugerido, gerador.Extensao) is not { } caminho)
            return;
        Gerando = true;
        Mensagem = "Gerando o relatório…";
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    gerador.Gerar(relatorio, _fotos.Imagem, destino);
                File.Move(temporario, caminho, overwrite: true);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar o relatório: {ex.Message}";
            return;
        }
        finally
        {
            Gerando = false;
        }

        if (_numeroGerado is null)
        {
            _cronograma.RegistrarRelatorio(new RelatorioGerado(relatorio.Numero, relatorio.GeradoEm, relatorio.Opcoes.De, relatorio.Opcoes.Ate,
                relatorio.TotalDeFotos));
            _numeroGerado = relatorio.Numero;
            _projetoMudou();
        }
        _preferencias.Salvar(_preferencias.Carregar() with
        {
            Responsavel = relatorio.Cabecalho.Responsavel,
            Registro = relatorio.Cabecalho.Registro,
            FotosPorPagina = relatorio.Opcoes.FotosPorPagina,
            Agrupamento = relatorio.Opcoes.Agrupamento,
        });
        UltimoArquivo = caminho;
        Mensagem = $"Relatório nº {relatorio.Numero} salvo em {Path.GetFileName(caminho)}.";
    }

    private bool TemUltimo() => UltimoArquivo is not null;

    [RelayCommand(CanExecute = nameof(TemUltimo))]
    private Task AbrirUltimo() => _dialogos.AbrirArquivoAsync(UltimoArquivo!);
}
