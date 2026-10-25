using ProjectPlanner.App.Models;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.App.ViewModels;
using System.Security.Cryptography;
using System.Text;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

/// <summary>Repositório em memória que registra as chamadas.</summary>
public sealed class RepositorioFalso : IProjetoRepository
{
    public List<(Cronograma Cronograma, string Caminho)> Salvos { get; } = [];
    public List<string> Abertos { get; } = [];
    public Func<string, Cronograma> AoAbrir { get; set; } = caminho => throw new FileNotFoundException("Não existe.", caminho);
    public Exception? ErroAoSalvar { get; set; }

    /// <summary>As imagens passadas em cada salvamento (Bloco 22).</summary>
    public List<ImagensParaSalvar> ImagensSalvas { get; } = [];

    public void Salvar(Cronograma cronograma, string caminhoArquivo) => Salvar(cronograma, caminhoArquivo, ImagensParaSalvar.Nenhuma);

    public void Salvar(Cronograma cronograma, string caminhoArquivo, ImagensParaSalvar imagens)
    {
        if (ErroAoSalvar is not null)
            throw ErroAoSalvar;
        Salvos.Add((cronograma, caminhoArquivo));
        ImagensSalvas.Add(imagens);
    }

    public Cronograma Abrir(string caminhoArquivo)
    {
        Abertos.Add(caminhoArquivo);
        return AoAbrir(caminhoArquivo);
    }

    public IReadOnlyDictionary<Guid, byte[]> LerMiniaturas(string caminhoArquivo, IReadOnlyCollection<Guid> fotos) => new Dictionary<Guid, byte[]>();

    public byte[]? LerImagem(string caminhoArquivo, Guid foto) => null;
}

/// <summary>
/// Repositório em memória que guarda só os dados do projeto, como o arquivo faria, e reconstrói um
/// cronograma novo a cada Abrir (nunca devolve o mesmo objeto que recebeu).
/// </summary>
public sealed class RepositorioEmMemoria : IProjetoRepository
{
    private readonly Dictionary<string, Func<Cronograma>> _arquivos = [];
    private readonly Dictionary<string, Dictionary<Guid, ConteudoDeFoto>> _imagens = [];

    public IReadOnlyCollection<string> Arquivos => _arquivos.Keys;

    /// <summary>As imagens guardadas em cada "arquivo".</summary>
    public IReadOnlyDictionary<Guid, ConteudoDeFoto> ImagensDe(string caminho) => _imagens.GetValueOrDefault(caminho) ?? [];

    public void Salvar(Cronograma cronograma, string caminhoArquivo) => Salvar(cronograma, caminhoArquivo, ImagensParaSalvar.Nenhuma);

    // Como o SQLite: as imagens novas vêm da memória; as demais, copiadas do arquivo de origem; nenhuma foto sem imagem.
    public void Salvar(Cronograma cronograma, string caminhoArquivo, ImagensParaSalvar imagens)
    {
        var origem = imagens.ArquivoDeOrigem is { } o ? _imagens.GetValueOrDefault(o) : null;
        var guardadas = new Dictionary<Guid, ConteudoDeFoto>();
        foreach (var foto in cronograma.Tarefas.SelectMany(t => t.Fotos))
        {
            if (imagens.Novas.TryGetValue(foto.Id, out var nova) || origem?.TryGetValue(foto.Id, out nova) == true)
                guardadas[foto.Id] = nova!;
            else
                throw new IOException($"A foto {foto.Imagem.NomeArquivoOriginal} ficaria sem imagem. Nada foi salvo.");
        }
        _imagens[caminhoArquivo] = guardadas;

        var (id, nome, inicio) = (cronograma.Id, cronograma.Nome, cronograma.DataInicio);
        var tarefas = cronograma.Tarefas
            .Select(t => (t.Id, t.Nome, t.DataInicio, t.Duracao, t.PercentualConcluido, t.Notas, t.TarefaPaiId, t.EhResumo, Fotos: t.FotosNaOrdemDeCadastro.ToList(),
                t.NaoIniciarAntesDe, t.Prazo, t.Valor))
            .ToList();
        var vinculos = cronograma.Predecessoras.ToList();
        var (linhasDeBase, emUso) = (cronograma.LinhasDeBase.ToList(), cronograma.LinhaDeBaseEmUso);
        _arquivos[caminhoArquivo] = () =>
        {
            var reconstruido = new Cronograma(id, nome, inicio);
            foreach (var t in tarefas)
            {
                // Como no arquivo: um resumo entra com pelo menos 1 dia (é recalculado).
                var tarefa = new Tarefa(t.Id, t.Nome, t.DataInicio, t.EhResumo ? Math.Max(t.Duracao, 1) : t.Duracao);
                tarefa.AtualizarPercentualConcluido(t.PercentualConcluido);
                tarefa.AlterarNotas(t.Notas);
                if (!t.EhResumo)
                {
                    tarefa.DefinirRestricaoDeInicio(t.NaoIniciarAntesDe);
                    tarefa.DefinirValor(t.Valor);
                }
                tarefa.DefinirPrazo(t.Prazo);
                foreach (var foto in t.Fotos)
                    tarefa.AdicionarFoto(foto);
                reconstruido.AdicionarTarefa(tarefa, t.TarefaPaiId);
            }
            foreach (var vinculo in vinculos)
                reconstruido.AdicionarPredecessora(vinculo);
            foreach (var linha in linhasDeBase)
                reconstruido.AdicionarLinhaDeBase(linha, linha == emUso);
            return reconstruido;
        };
    }

    public Cronograma Abrir(string caminhoArquivo) =>
        _arquivos.TryGetValue(caminhoArquivo, out var abrir)
            ? abrir()
            : throw new FileNotFoundException("Não existe.", caminhoArquivo);

    public IReadOnlyDictionary<Guid, byte[]> LerMiniaturas(string caminhoArquivo, IReadOnlyCollection<Guid> fotos) =>
        ImagensDe(caminhoArquivo).Where(i => fotos.Contains(i.Key)).ToDictionary(i => i.Key, i => i.Value.Miniatura);

    public byte[]? LerImagem(string caminhoArquivo, Guid foto) => ImagensDe(caminhoArquivo).GetValueOrDefault(foto)?.Imagem;
}

/// <summary>Diálogos com respostas programadas.</summary>
public sealed class DialogosFalsos : IDialogosService
{
    public string? ArquivoParaAbrir { get; set; }
    public string? ArquivoParaSalvar { get; set; }
    public RespostaAlteracoesPendentes RespostaAlteracoesPendentes { get; set; } = RespostaAlteracoesPendentes.Cancelar;

    public int VezesQuePerguntouOndeSalvar { get; private set; }
    public int VezesQuePerguntouSobreAlteracoes { get; private set; }
    public string? NomeSugerido { get; private set; }

    public Task<string?> EscolherArquivoParaAbrirAsync() => Task.FromResult(ArquivoParaAbrir);

    public Task<string?> EscolherArquivoParaSalvarAsync(string nomeSugerido)
    {
        VezesQuePerguntouOndeSalvar++;
        NomeSugerido = nomeSugerido;
        return Task.FromResult(ArquivoParaSalvar);
    }

    public Task<RespostaAlteracoesPendentes> PerguntarSobreAlteracoesPendentesAsync(string nomeProjeto)
    {
        VezesQuePerguntouSobreAlteracoes++;
        return Task.FromResult(RespostaAlteracoesPendentes);
    }

    /// <summary>A resposta às confirmações (Remover resumo...).</summary>
    public bool Confirmar { get; set; } = true;

    /// <summary>As mensagens de confirmação mostradas, em ordem.</summary>
    public List<string> Confirmacoes { get; } = [];

    public Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoDoBotao)
    {
        Confirmacoes.Add(mensagem);
        return Task.FromResult(Confirmar);
    }

    /// <summary>O que o diálogo de anexar documentos "escolhe" (Bloco 19).</summary>
    public List<string> DocumentosParaAnexar { get; } = [];

    public Task<IReadOnlyList<string>> EscolherDocumentosAsync() => Task.FromResult<IReadOnlyList<string>>(DocumentosParaAnexar.ToList());

    /// <summary>O que o diálogo de adicionar fotos "escolhe" (Bloco 22).</summary>
    public List<string> FotosParaAdicionar { get; } = [];

    public Task<IReadOnlyList<string>> EscolherFotosAsync() => Task.FromResult<IReadOnlyList<string>>(FotosParaAdicionar.ToList());

    /// <summary>Os visualizadores ampliados abertos, em ordem.</summary>
    public List<VisualizadorDeFotosViewModel> Visualizadores { get; } = [];

    public Task MostrarFotoAmpliadaAsync(VisualizadorDeFotosViewModel visualizador)
    {
        Visualizadores.Add(visualizador);
        return Task.CompletedTask;
    }

    /// <summary>As galerias de fotos abertas, em ordem.</summary>
    public List<GaleriaFotosViewModel> Galerias { get; } = [];

    /// <summary>As telas de configuração do assistente abertas (Bloco 23); <see cref="AoConfigurar"/> faz o papel do usuário.</summary>
    public List<ConfiguracaoAssistenteViewModel> ConfiguracoesDoAssistente { get; } = [];

    public Action<ConfiguracaoAssistenteViewModel>? AoConfigurar { get; set; }

    public List<string> PastasAbertas { get; } = [];

    public Task AbrirPastaAsync(string pasta)
    {
        PastasAbertas.Add(pasta);
        return Task.CompletedTask;
    }

    /// <summary>Onde o CSV exportado vai (Bloco 25); nulo é cancelar.</summary>
    public string? ArquivoCsv { get; set; }

    public string? CsvSugerido { get; private set; }

    public Task<string?> EscolherArquivoCsvAsync(string nomeSugerido)
    {
        CsvSugerido = nomeSugerido;
        return Task.FromResult(ArquivoCsv);
    }

    /// <summary>O que o usuário "digita" quando o programa pede um texto (Bloco 25); nulo é cancelar.</summary>
    public string? TextoDigitado { get; set; }

    public List<(string Titulo, string Pergunta, string? ValorInicial)> TextosPedidos { get; } = [];

    public Task<string?> PedirTextoAsync(string titulo, string pergunta, string? valorInicial = null)
    {
        TextosPedidos.Add((titulo, pergunta, valorInicial));
        return Task.FromResult(TextoDigitado);
    }

    public List<ResumoDoProjetoViewModel> Resumos { get; } = [];

    public Task MostrarResumoDoProjetoAsync(ResumoDoProjetoViewModel resumo)
    {
        Resumos.Add(resumo);
        return Task.CompletedTask;
    }

    /// <summary>As telas Sobre abertas (Bloco 25).</summary>
    public List<SobreViewModel> Sobres { get; } = [];

    public Task MostrarSobreAsync(SobreViewModel sobre)
    {
        Sobres.Add(sobre);
        return Task.CompletedTask;
    }

    // ---- Bloco 27 ----

    public List<RelatorioFotograficoViewModel> Relatorios { get; } = [];
    public string? ArquivoDoRelatorio { get; set; }
    public List<(string Nome, string Extensao)> RelatoriosSugeridos { get; } = [];
    public string? ImagemEscolhida { get; set; }
    public List<string> ArquivosAbertos { get; } = [];

    public Task MostrarRelatorioFotograficoAsync(RelatorioFotograficoViewModel relatorio)
    {
        Relatorios.Add(relatorio);
        return Task.CompletedTask;
    }

    public Task<string?> EscolherArquivoDoRelatorioAsync(string nomeSugerido, string extensao)
    {
        RelatoriosSugeridos.Add((nomeSugerido, extensao));
        return Task.FromResult(ArquivoDoRelatorio is null ? null : Path.ChangeExtension(ArquivoDoRelatorio, extensao));
    }

    public Task<string?> EscolherImagemAsync() => Task.FromResult(ImagemEscolhida);

    public Task AbrirArquivoAsync(string caminho)
    {
        ArquivosAbertos.Add(caminho);
        return Task.CompletedTask;
    }

    // ---- Bloco 28 ----

    /// <summary>As telas Fotos do projeto abertas; <see cref="AoMostrarFotosDoProjeto"/> faz o papel do usuário.</summary>
    public List<FotosDoProjetoViewModel> FotosDoProjeto { get; } = [];

    public Func<FotosDoProjetoViewModel, Task>? AoMostrarFotosDoProjeto { get; set; }

    public List<Uri> EnderecosAbertos { get; } = [];

    public Task MostrarFotosDoProjetoAsync(FotosDoProjetoViewModel fotos)
    {
        FotosDoProjeto.Add(fotos);
        return AoMostrarFotosDoProjeto?.Invoke(fotos) ?? Task.CompletedTask;
    }

    public Task AbrirEnderecoAsync(Uri endereco)
    {
        EnderecosAbertos.Add(endereco);
        return Task.CompletedTask;
    }

    // ---- Bloco 31 ----

    public List<LinhasDeBaseViewModel> LinhasDeBase { get; } = [];

    public Action<LinhasDeBaseViewModel>? AoMostrarLinhasDeBase { get; set; }

    public Task MostrarLinhasDeBaseAsync(LinhasDeBaseViewModel linhas)
    {
        LinhasDeBase.Add(linhas);
        AoMostrarLinhasDeBase?.Invoke(linhas);
        return Task.CompletedTask;
    }

    // ---- Bloco 35 ----

    public List<FisicoFinanceiroViewModel> FisicosFinanceiros { get; } = [];

    public Func<FisicoFinanceiroViewModel, Task>? AoMostrarFisicoFinanceiro { get; set; }

    /// <summary>O caminho que o "salvar como" devolve (sem extensão: ela vem do pedido); nulo cancela.</summary>
    public string? ArquivoExportado { get; set; }

    public List<(string Titulo, string Nome, string Extensao)> Exportacoes { get; } = [];

    public string? PlanilhaEscolhida { get; set; }

    // ---- Bloco 58 ----

    public List<OrdensDeServicoViewModel> OrdensDeServico { get; } = [];

    public Func<OrdensDeServicoViewModel, Task>? AoMostrarOrdensDeServico { get; set; }

    public Task MostrarOrdensDeServicoAsync(OrdensDeServicoViewModel ordens)
    {
        OrdensDeServico.Add(ordens);
        return AoMostrarOrdensDeServico?.Invoke(ordens) ?? Task.CompletedTask;
    }

    // ---- Bloco 57 ----

    public List<GanttImpressoViewModel> ImpressoesDoGantt { get; } = [];

    public Func<GanttImpressoViewModel, Task>? AoMostrarImpressaoDoGantt { get; set; }

    public Task MostrarImpressaoDoGanttAsync(GanttImpressoViewModel impressao)
    {
        ImpressoesDoGantt.Add(impressao);
        return AoMostrarImpressaoDoGantt?.Invoke(impressao) ?? Task.CompletedTask;
    }

    public Task MostrarFisicoFinanceiroAsync(FisicoFinanceiroViewModel fisicoFinanceiro)
    {
        FisicosFinanceiros.Add(fisicoFinanceiro);
        return AoMostrarFisicoFinanceiro?.Invoke(fisicoFinanceiro) ?? Task.CompletedTask;
    }

    public Task<string?> SalvarComoAsync(string titulo, string nomeSugerido, string extensao)
    {
        Exportacoes.Add((titulo, nomeSugerido, extensao));
        return Task.FromResult(ArquivoExportado is null ? null : Path.ChangeExtension(ArquivoExportado, extensao));
    }

    public Task<string?> EscolherPlanilhaAsync() => Task.FromResult(PlanilhaEscolhida);

    /// <summary>Bloco 42: a pasta que o "escolher pasta" devolve; nula cancela.</summary>
    public string? PastaEscolhida { get; set; }

    public List<string> PedidosDePasta { get; } = [];

    public Task<string?> EscolherPastaAsync(string titulo)
    {
        PedidosDePasta.Add(titulo);
        return Task.FromResult(PastaEscolhida);
    }

    public List<RecursosViewModel> Recursos { get; } = [];

    public Task MostrarRecursosAsync(RecursosViewModel recursos)
    {
        Recursos.Add(recursos);
        return Task.CompletedTask;
    }

    public List<RecursosDaTarefaViewModel> RecursosDasTarefas { get; } = [];

    public Task MostrarRecursosDaTarefaAsync(RecursosDaTarefaViewModel recursos)
    {
        RecursosDasTarefas.Add(recursos);
        return Task.CompletedTask;
    }

    public List<OrcamentoViewModel> Orcamentos { get; } = [];

    public Task MostrarOrcamentoAsync(OrcamentoViewModel orcamento)
    {
        Orcamentos.Add(orcamento);
        return Task.CompletedTask;
    }

    public List<UsoDeRecursosViewModel> UsosDeRecursos { get; } = [];

    public Task MostrarUsoDeRecursosAsync(UsoDeRecursosViewModel uso)
    {
        UsosDeRecursos.Add(uso);
        return Task.CompletedTask;
    }

    public List<CustosViewModel> Custos { get; } = [];

    public Task MostrarCustosAsync(CustosViewModel custos)
    {
        Custos.Add(custos);
        return Task.CompletedTask;
    }

    public List<IndicadoresViewModel> Indicadores { get; } = [];

    public Task MostrarIndicadoresAsync(IndicadoresViewModel indicadores)
    {
        Indicadores.Add(indicadores);
        return Task.CompletedTask;
    }

    public List<MedicoesViewModel> Medicoes { get; } = [];

    public Func<MedicoesViewModel, Task>? AoMostrarMedicoes { get; set; }

    public Task MostrarMedicoesAsync(MedicoesViewModel medicoes)
    {
        Medicoes.Add(medicoes);
        return AoMostrarMedicoes?.Invoke(medicoes) ?? Task.CompletedTask;
    }

    /// <summary>Bloco 49: as telas Conectar o Telegram abertas.</summary>
    public List<TelegramViewModel> Telegrams { get; } = [];

    public async Task MostrarTelegramAsync(TelegramViewModel telegram)
    {
        Telegrams.Add(telegram);
        await telegram.AbrirAsync();
    }

    /// <summary>Bloco 45: as telas da conta abertas, e o que o usuário faz nelas antes de fechar.</summary>
    public List<ContaViewModel> Contas { get; } = [];

    public Func<ContaViewModel, Task>? AoMostrarConta { get; set; }

    public async Task MostrarContaAsync(ContaViewModel conta)
    {
        Contas.Add(conta);
        await conta.AbrirAsync();
        if (AoMostrarConta is not null)
            await AoMostrarConta(conta);
        conta.Fechando();
    }

    public Task MostrarConfiguracaoAssistenteAsync(ConfiguracaoAssistenteViewModel configuracao)
    {
        ConfiguracoesDoAssistente.Add(configuracao);
        AoConfigurar?.Invoke(configuracao);
        return Task.CompletedTask;
    }

    /// <summary>Bloco 28: o que o usuário faz na galeria antes de fechá-la (nulo: só fecha).</summary>
    public Func<GaleriaFotosViewModel, Task>? AoMostrarGaleria { get; set; }

    public Task MostrarFotosAsync(GaleriaFotosViewModel galeria)
    {
        Galerias.Add(galeria);
        return AoMostrarGaleria?.Invoke(galeria) ?? Task.CompletedTask;
    }
}

/// <summary>A calculadora real, contando as chamadas.</summary>
public sealed class CalculadoraEspia : ICalculadoraCronograma
{
    public int Chamadas { get; private set; }

    public ResultadoCronograma Calcular(Cronograma cronograma)
    {
        Chamadas++;
        return CalculadoraCronograma.Calcular(cronograma);
    }
}

/// <summary>Preferências de colunas em memória, que registram cada gravação.</summary>
public sealed class PreferenciasEmMemoria : IPreferenciasColunas
{
    public LayoutColunas? Salvo { get; set; }

    public int Gravacoes { get; private set; }

    public LayoutColunas? Carregar() => Salvo;

    public void Salvar(LayoutColunas layout)
    {
        Salvo = layout;
        Gravacoes++;
    }
}

/// <summary>Relógio parado em UTC, para que "hoje" não dependa do fuso da máquina.</summary>
public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Monta um MainWindowViewModel com dependências falsas; "hoje" é seg 28/09/2026.</summary>
public sealed class Ambiente
{
    public static readonly DateTime Segunda = new(2026, 9, 28);

    public RepositorioFalso Repositorio { get; } = new();
    public DialogosFalsos Dialogos { get; } = new();
    public CalculadoraEspia Calculadora { get; } = new();
    public RelogioFixo Relogio { get; } = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

    public PreferenciasEmMemoria Preferencias { get; } = new();

    public AssistenteFalso Assistente { get; } = new();

    public PreferenciasInterfaceEmMemoria PreferenciasInterface { get; } = new();

    public ProcessadorFalso Processador { get; } = new();

    public ConfiguracaoAssistenteFalsa ConfiguracaoAssistente { get; } = new();

    /// <summary>Bloco 29: a IA das fotos.</summary>
    public AnalistaFalso Analista { get; } = new();

    /// <summary>Bloco 32: o catálogo pessoal de recursos.</summary>
    public Services.CatalogoDeRecursosEmMemoria Catalogo { get; } = new();

    /// <summary>Bloco 33: as bases de preços importadas.</summary>
    public Services.BibliotecaDeReferenciasEmMemoria Biblioteca { get; } = new();

    /// <summary>Bloco 38: a IA das sugestões de orçamento.</summary>
    public ConsultorFalso Consultor { get; } = new();

    /// <summary>Bloco 43: a IA que analisa os indicadores.</summary>
    public AnalistaDaObraFalso AnalistaDaObra { get; } = new();

    /// <summary>Bloco 45: a conta ProjectPlanner.</summary>
    public ContaFalsa Conta { get; } = new();

    /// <summary>Bloco 49: o Telegram pela conta.</summary>
    public TelegramFalso Telegram { get; } = new();

    public MainWindowViewModel CriarViewModel() =>
        new(Repositorio, Calculadora, Dialogos, Relogio, Preferencias, Assistente, PreferenciasInterface, Processador, ConfiguracaoAssistente,
            analistaDeFotos: Analista, catalogoDeRecursos: Catalogo, bibliotecaDeReferencias: Biblioteca,
            consultorDeOrcamento: Consultor, analistaDaObra: AnalistaDaObra, conta: Conta, telegram: Telegram);

    /// <summary>Adiciona uma tarefa pela grid, como o usuário faria.</summary>
    public static TarefaViewModel AdicionarTarefa(MainWindowViewModel vm, string nome, int duracao, string predecessoras = "")
    {
        vm.AdicionarTarefaCommand.Execute(null);
        var linha = vm.Tarefas[^1];
        linha.Nome = nome;
        linha.Duracao = duracao;
        linha.Predecessoras = predecessoras;
        return linha;
    }

    /// <summary>Data do dia útil <paramref name="numero"/>, contando <see cref="Segunda"/> como dia 0.</summary>
    public static DateTime DiaUtil(int numero)
    {
        var data = Segunda;
        while (numero > 0)
        {
            data = data.AddDays(1);
            if (data.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                numero--;
        }
        return data;
    }
}

/// <summary>
/// Assistente de IA dos testes (Bloco 19): guarda os pedidos e responde na hora, com um erro, ou só quando o teste
/// mandar (<see cref="Segurar"/>), para ver o estado "aguardando".
/// </summary>
public sealed class AssistenteFalso : IAssistenteIA
{
    private TaskCompletionSource<RespostaIA>? _segurada;

    public List<PedidoIA> Pedidos { get; } = [];
    public bool EstaConfigurado { get; set; }
    public Func<PedidoIA, string> Responder { get; set; } = p => $"Resposta a: {p.Mensagem}";

    /// <summary>A resposta inteira, com alterações do cronograma (Bloco 24); sem ela, só o texto de <see cref="Responder"/>.</summary>
    public Func<PedidoIA, RespostaIA>? ResponderComAlteracoes { get; set; }

    public Exception? Erro { get; set; }

    private RespostaIA Resposta(PedidoIA pedido) => ResponderComAlteracoes?.Invoke(pedido) ?? new RespostaIA(Responder(pedido));

    /// <summary>A próxima resposta só sai com <see cref="Soltar"/> (ou com o cancelamento).</summary>
    public void Segurar() => _segurada = new TaskCompletionSource<RespostaIA>(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Soltar() => _segurada?.TrySetResult(Resposta(Pedidos[^1]));

    public async Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento)
    {
        Pedidos.Add(pedido);
        if (Erro is not null)
            throw Erro;
        if (_segurada is { } segurada)
        {
            using var registro = cancelamento.Register(() => segurada.TrySetCanceled(cancelamento));
            try
            {
                return await segurada.Task;
            }
            finally
            {
                _segurada = null;
            }
        }
        return Resposta(pedido);
    }
}

public sealed class PreferenciasInterfaceEmMemoria : IPreferenciasInterface
{
    public PreferenciasInterface? Salvo { get; set; }

    public int Gravacoes { get; private set; }

    public PreferenciasInterface? Carregar() => Salvo;

    public void Salvar(PreferenciasInterface preferencias)
    {
        Salvo = preferencias;
        Gravacoes++;
    }
}

/// <summary>
/// Processador de imagens dos testes do App (Bloco 22): não lê arquivo nenhum. A "imagem" de um caminho é o texto
/// "nome|largura|altura"; o hash vem do nome (mesmo nome = mesma imagem). Nomes com "corrompida" são recusados,
/// e HEIC e formatos não aceitos seguem as regras de verdade.
/// </summary>
public sealed class ProcessadorFalso : IProcessadorDeImagens
{
    /// <summary>Data da foto por nome de arquivo; sem entrada, 29/09/2026 10:00.</summary>
    public Dictionary<string, DateTime> Datas { get; } = [];

    /// <summary>Bloco 28: o GPS do EXIF por nome de arquivo; sem entrada, sem GPS.</summary>
    public Dictionary<string, Coordenadas> Gps { get; } = [];

    public int Importacoes { get; private set; }

    public ImagemImportada Importar(string caminhoArquivo)
    {
        Importacoes++;
        var nome = Path.GetFileName(caminhoArquivo);
        RegrasDeFoto.ValidarArquivo(nome, 1000);
        if (nome.Contains("corrompida", StringComparison.OrdinalIgnoreCase))
            throw new ImagemRecusadaException($"{nome}: o arquivo não é uma imagem válida (está corrompido ou não é uma foto).");
        var data = Datas.GetValueOrDefault(nome, new DateTime(2026, 9, 29, 10, 0, 0));
        return new ImagemImportada(Conteudo(nome, 1600, 1200), nome,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nome))), data,
            nome.StartsWith("semexif", StringComparison.Ordinal) ? OrigemDaData.ArquivoModificado : OrigemDaData.ExifOriginal, Gps.GetValueOrDefault(nome));
    }

    public ConteudoDeFoto Girar(byte[] imagem, bool sentidoHorario)
    {
        var partes = Encoding.UTF8.GetString(imagem).Split('|');
        return Conteudo(partes[0] + (sentidoHorario ? "↻" : "↺"), int.Parse(partes[2]), int.Parse(partes[1]));
    }

    public static ConteudoDeFoto Conteudo(string nome, int largura, int altura) =>
        new(Encoding.UTF8.GetBytes($"{nome}|{largura}|{altura}"), Encoding.UTF8.GetBytes($"mini:{nome}"), largura, altura);

    /// <summary>O nome que a imagem guardada descreve (para os testes conferirem a imagem certa).</summary>
    public static string Descrever(byte[]? bytes) => bytes is null ? "(nenhuma)" : Encoding.UTF8.GetString(bytes);
}

/// <summary>A configuração do assistente para os testes do painel (Blocos 23 e 46): só guarda o que a tela mudaria.</summary>
public sealed class ConfiguracaoAssistenteFalsa : IConfiguracaoAssistente
{
    public string ModeloPadrao => "gemini-2.5-flash";
    public string Modelo { get; set; } = "gemini-2.5-flash";
    public void SalvarModelo(string modelo) => Modelo = modelo;
    public bool ChaveDeDesenvolvimento { get; set; }
    public List<string> Testes { get; } = [];
    public Exception? ErroNoTeste { get; set; }

    public Task TestarAsync(string modelo, CancellationToken cancelamento)
    {
        Testes.Add(modelo);
        return ErroNoTeste is null ? Task.CompletedTask : Task.FromException(ErroNoTeste);
    }

    // Bloco 26
    public PreferenciaDeModo PreferenciaDeModo { get; set; } = PreferenciaDeModo.Perguntar;
    public void SalvarPreferenciaDeModo(PreferenciaDeModo preferencia) => PreferenciaDeModo = preferencia;

    // Bloco 51
    public bool AutomaticoOferecido { get; set; }

    public void OferecerAutomatico(bool trocar)
    {
        if (trocar)
            Modelo = NiveisDaConta.Automatico;
        AutomaticoOferecido = true;
    }
}

/// <summary>Um cofre em memória (Bloco 23); <see cref="Falha"/> faz toda operação falhar como um cofre bloqueado.</summary>
public sealed class CofreEmMemoria : ICofreDeSegredos
{
    public Dictionary<string, string> Segredos { get; } = [];
    public CofreException? Falha { get; set; }
    public int Leituras { get; private set; }
    public bool Disponivel { get; set; } = true;
    public string Nome => "Chaves do macOS";

    public string? Ler(string nome)
    {
        Leituras++;
        if (Falha is not null)
            throw Falha;
        return Segredos.GetValueOrDefault(nome);
    }

    public void Gravar(string nome, string segredo)
    {
        if (Falha is not null)
            throw Falha;
        Segredos[nome] = segredo;
    }

    public void Remover(string nome)
    {
        if (Falha is not null)
            throw Falha;
        Segredos.Remove(nome);
    }
}

public sealed class PreferenciasAssistenteEmMemoria : IPreferenciasAssistente
{
    public PreferenciasAssistente? Salvas { get; set; }
    public PreferenciasAssistente? Carregar() => Salvas;
    public void Salvar(PreferenciasAssistente preferencias) => Salvas = preferencias;
}

/// <summary>Um provedor de IA sem rede (Bloco 23): guarda os testes e os assistentes criados.</summary>
public sealed class ProvedorFalso : IProvedorIA
{
    private readonly ProjectPlanner.IA.Gemini.ProvedorGemini _regras = new(new HttpClient());

    public List<(string Chave, string Modelo)> Testes { get; } = [];
    public List<(string Chave, string Modelo)> Criados { get; } = [];
    public Exception? ErroNoTeste { get; set; }
    public TaskCompletionSource? TesteSegurado { get; set; }

    public string Nome => "Gemini (Google)";
    public string ModeloPadrao => "gemini-2.5-flash";
    public string NormalizarModelo(string modelo) => _regras.NormalizarModelo(modelo);

    public IAssistenteIA CriarAssistente(string chave, string modelo)
    {
        Criados.Add((chave, modelo));
        return new AssistenteFalso { EstaConfigurado = true, Responder = p => $"[{modelo}] {p.Mensagem}" };
    }

    public IAnalistaDeFotos CriarAnalista(string chave, string modelo) => new AnalistaFalso();

    public IConsultorDeOrcamento CriarConsultor(string chave, string modelo) => new ConsultorFalso();

    public IAnalistaDaObra CriarAnalistaDaObra(string chave, string modelo) => new AnalistaDaObraFalso();

    public async Task TestarAsync(string chave, string modelo, CancellationToken cancelamento)
    {
        Testes.Add((chave, modelo));
        if (TesteSegurado is { } segurado)
            await segurado.Task.WaitAsync(cancelamento);
        if (ErroNoTeste is not null)
            throw ErroNoTeste;
    }
}

/// <summary>Bloco 43: a IA que analisa os indicadores, nos testes: guarda os pedidos e responde o que o teste mandar.</summary>
public sealed class AnalistaDaObraFalso : IAnalistaDaObra
{
    public bool EstaConfigurado { get; set; } = true;

    public List<PedidoDeAnaliseDaObra> Pedidos { get; } = [];

    public Exception? Erro { get; set; }

    public TaskCompletionSource? Segurar { get; set; }

    public Func<PedidoDeAnaliseDaObra, AnaliseDaObraIA> Responder { get; set; } =
        _ => new AnaliseDaObraIA("A obra está no prazo e dentro do custo.", ["Atenção à estrutura."], ["Lançar os custos da semana."]);

    public async Task<AnaliseDaObraIA> AnalisarAsync(PedidoDeAnaliseDaObra pedido, CancellationToken cancelamento)
    {
        Pedidos.Add(pedido);
        if (Segurar is { } segurar)
            await segurar.Task.WaitAsync(cancelamento);
        cancelamento.ThrowIfCancellationRequested();
        if (Erro is not null)
            throw Erro;
        return Responder(pedido);
    }
}

/// <summary>Bloco 38: o consultor de orçamento nos testes: guarda os pedidos e responde o que o teste mandar (sem rede).</summary>
public sealed class ConsultorFalso : IConsultorDeOrcamento
{
    public bool EstaConfigurado { get; set; } = true;

    public List<PedidoDeOrcamento> Pedidos { get; } = [];

    public Exception? Erro { get; set; }

    /// <summary>Segura a resposta até o teste liberar (para ver a tela "trabalhando" e o cancelar).</summary>
    public TaskCompletionSource? Segurar { get; set; }

    public Func<PedidoDeOrcamento, RespostaDoConsultor> Responder { get; set; } = _ => new RespostaDoConsultor("Nada a sugerir.", []);

    public async Task<RespostaDoConsultor> SugerirAsync(PedidoDeOrcamento pedido, CancellationToken cancelamento)
    {
        Pedidos.Add(pedido);
        if (Segurar is { } segurar)
            await segurar.Task.WaitAsync(cancelamento);
        cancelamento.ThrowIfCancellationRequested();
        if (Erro is not null)
            throw Erro;
        return Responder(pedido);
    }
}

/// <summary>Bloco 29: a IA das fotos nos testes: guarda os pedidos e responde o que o teste mandar (sem rede).</summary>
public sealed class AnalistaFalso : IAnalistaDeFotos
{
    public bool EstaConfigurado { get; set; } = true;

    public List<PedidoDeLegendas> PedidosDeLegendas { get; } = [];
    public List<PedidoDeAvanco> PedidosDeAvanco { get; } = [];
    public List<PedidoDeTextoDoRelatorio> PedidosDeTexto { get; } = [];

    /// <summary>Uma falha para devolver em vez da resposta.</summary>
    public Exception? Erro { get; set; }

    /// <summary>Segura a resposta até o teste liberar (para ver a tela "trabalhando").</summary>
    public TaskCompletionSource? Segurar { get; set; }

    public Func<PedidoDeLegendas, IReadOnlyList<LegendaSugerida>> Legendas { get; set; } =
        p => p.Fotos.Select(f => new LegendaSugerida(f.Id, $"Legenda da IA para {f.Tarefa}")).ToList();

    public Func<PedidoDeAvanco, IReadOnlyList<AvancoSugerido>> Avanco { get; set; } =
        p => p.Tarefas.Select(t => new AvancoSugerido(t.Id, Math.Min(100, t.Percentual + 20), "As fotos mostram mais avanço.", true)).ToList();

    public Func<PedidoDeTextoDoRelatorio, TextoSugerido> Texto { get; set; } =
        p => new TextoSugerido($"No período de {p.De:dd/MM} a {p.Ate:dd/MM}, a obra avançou.\nSegundo parágrafo.", ["Atenção à laje."]);

    private async Task<T> Responder<T>(Func<T> resposta, CancellationToken cancelamento)
    {
        if (Segurar is { } segurar)
            await segurar.Task.WaitAsync(cancelamento);
        cancelamento.ThrowIfCancellationRequested();
        if (Erro is not null)
            throw Erro;
        return resposta();
    }

    public Task<IReadOnlyList<LegendaSugerida>> SugerirLegendasAsync(PedidoDeLegendas pedido, CancellationToken cancelamento)
    {
        PedidosDeLegendas.Add(pedido);
        return Responder(() => Legendas(pedido), cancelamento);
    }

    public Task<IReadOnlyList<AvancoSugerido>> SugerirAvancoAsync(PedidoDeAvanco pedido, CancellationToken cancelamento)
    {
        PedidosDeAvanco.Add(pedido);
        return Responder(() => Avanco(pedido), cancelamento);
    }

    public Task<TextoSugerido> RedigirRelatorioAsync(PedidoDeTextoDoRelatorio pedido, CancellationToken cancelamento)
    {
        PedidosDeTexto.Add(pedido);
        return Responder(() => Texto(pedido), cancelamento);
    }

    // ---- Bloco 42 ----

    public List<PedidoDeClassificacao> PedidosDeClassificacao { get; } = [];

    /// <summary>Por padrão, cada foto vai para a primeira tarefa em execução, com certeza alta.</summary>
    public Func<PedidoDeClassificacao, IReadOnlyList<ClassificacaoDaFoto>> Classificar { get; set; } =
        p => p.Fotos.Select(f => new ClassificacaoDaFoto(f.Id, p.Tarefas.FirstOrDefault(t => t.EmExecucao)?.Id, ConfiancaDaIA.Alta,
            $"Legenda da IA para {f.Id}", null, 50, "A foto mostra o serviço.")).ToList();

    public Task<IReadOnlyList<ClassificacaoDaFoto>> ClassificarFotosAsync(PedidoDeClassificacao pedido, CancellationToken cancelamento)
    {
        PedidosDeClassificacao.Add(pedido);
        return Responder(() => Classificar(pedido), cancelamento);
    }
}

/// <summary>
/// Bloco 45: a conta nos testes das telas: guarda o que foi pedido e responde como o servidor, sem rede. Senha certa:
/// "senha-boa-123"; código certo: "123456". <see cref="Erro"/> faz o próximo pedido falhar com essa mensagem.
/// </summary>
public sealed class ContaFalsa : IContaProjectPlanner
{
    public const string SenhaCerta = "senha-boa-123";
    public const string CodigoCerto = "123456";

    public PerfilDaConta? Perfil { get; set; }
    public bool Conectada => Perfil is not null;
    public string? ProblemaNoCofre => null;

    // Bloco 46
    public static readonly IReadOnlyList<ModeloDaConta> ModelosDoGratis =
    [
        new("gemini-2.5-flash", "padrao", "Gemini 2.5 Flash", true, true, 14),
        new("gemini-2.5-pro", "avancado", "Gemini 2.5 Pro", false, true, 55),
        new("claude-opus-5-5", "maximo", "Claude Opus 5.5", false, true, 140),
    ];

    public IReadOnlyList<ModeloDaConta> Modelos { get; private set; } = [];
    public IReadOnlyList<ModeloDaConta> ModelosDoServidor { get; set; } = ModelosDoGratis;
    public decimal? CreditosRestantes { get; private set; }

    public async Task<IReadOnlyList<ModeloDaConta>> CarregarModelosAsync(CancellationToken cancelamento)
    {
        await Passo("modelos", cancelamento);
        Modelos = ModelosDoServidor;
        Mudou?.Invoke(this, EventArgs.Empty);
        return Modelos;
    }

    public void AtualizarSaldo(decimal creditosRestantes)
    {
        CreditosRestantes = creditosRestantes;
        Mudou?.Invoke(this, EventArgs.Empty);
    }
    public event EventHandler? Mudou;
    public List<string> Pedidos { get; } = [];
    public ErroDaContaException? Erro { get; set; }
    public TaskCompletionSource? Segurar { get; set; }
    public UsoDaConta? Uso { get; set; }
    public bool PendenteDeConfirmacao { get; set; }

    public void Conectar(string nome = "Ana", string email = "ana@exemplo.com", string plano = "gratis")
    {
        Perfil = new PerfilDaConta(email, nome, plano, true, false);
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    private async Task Passo(string pedido, CancellationToken cancelamento)
    {
        Pedidos.Add(pedido);
        if (Segurar is { } segurar)
            await segurar.Task.WaitAsync(cancelamento);
        if (Erro is { } erro)
        {
            Erro = null;
            throw erro;
        }
    }

    public async Task<string> CriarContaAsync(string nome, string email, string senha, CancellationToken cancelamento)
    {
        await Passo($"criar {nome} {email}", cancelamento);
        return "Se o e-mail puder ser usado, mandamos um código para ele.";
    }

    public async Task ConfirmarAsync(string email, string codigo, string senha, CancellationToken cancelamento)
    {
        await Passo($"confirmar {email} {codigo}", cancelamento);
        if (codigo != CodigoCerto || senha != SenhaCerta)
            throw new ErroDaContaException("Código inválido ou vencido. Peça outro.", 400);
        Conectar(email: email);
    }

    public async Task<string> ReenviarCodigoAsync(string email, CancellationToken cancelamento)
    {
        await Passo($"reenviar {email}", cancelamento);
        return "Se o e-mail puder ser usado, mandamos um código para ele.";
    }

    public async Task EntrarAsync(string email, string senha, CancellationToken cancelamento)
    {
        await Passo($"entrar {email}", cancelamento);
        if (senha != SenhaCerta)
            throw new ErroDaContaException("E-mail ou senha incorretos.", 401);
        if (PendenteDeConfirmacao)
            throw new ErroDaContaException("Confirme o e-mail antes de entrar: digite o código que mandamos (ou peça outro).", 403);
        Conectar(email: email);
    }

    public async Task EntrarComGoogleAsync(CancellationToken cancelamento)
    {
        await Passo("google", cancelamento);
        Perfil = new PerfilDaConta("bia@gmail.com", "Bia", "gratis", false, true);
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> EsqueciASenhaAsync(string email, CancellationToken cancelamento)
    {
        await Passo($"esqueci {email}", cancelamento);
        return "Se houver uma conta com esse e-mail, mandamos um código para trocar a senha.";
    }

    public async Task RedefinirSenhaAsync(string email, string codigo, string novaSenha, CancellationToken cancelamento)
    {
        await Passo($"redefinir {email} {codigo}", cancelamento);
        if (codigo != CodigoCerto)
            throw new ErroDaContaException("Código inválido ou vencido. Peça outro.", 400);
        Conectar(email: email);
    }

    public async Task<UsoDaConta> UsoAsync(CancellationToken cancelamento)
    {
        await Passo("uso", cancelamento);
        var uso = Uso ?? new UsoDaConta(Perfil!, 12_000, 25.5m, 300);
        CreditosRestantes = uso.CreditosRestantes;
        return uso;
    }

    public async Task SairAsync(CancellationToken cancelamento)
    {
        await Passo("sair", cancelamento);
        (Perfil, Modelos, CreditosRestantes) = (null, [], null);
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    public Task<string> AcessoAsync(bool renovar, CancellationToken cancelamento) =>
        Perfil is null ? Task.FromException<string>(new ErroDoAssistenteException("sem conta")) : Task.FromResult("acesso-falso");

    // ---- Bloco 51: a triagem do Automático (sem Avaliacao, falha como um servidor sem a rota) ----

    public AvaliacaoDoPedido? Avaliacao { get; set; }
    public List<PedidoDeAvaliacao> Avaliados { get; } = [];

    public async Task<AvaliacaoDoPedido> AvaliarAsync(PedidoDeAvaliacao pedido, CancellationToken cancelamento)
    {
        Avaliados.Add(pedido);
        await Passo("avaliar", cancelamento);
        return Avaliacao ?? throw new ErroDaContaException("O servidor do ProjectPlanner não reconheceu o pedido. Atualize o programa.", 404);
    }

    // ---- Bloco 47: sem Pagamentos, como um servidor que não cobra (a rota não existe, e o pedido nem conta) ----

    public SituacaoDosPagamentos? Pagamentos { get; set; }

    public async Task<SituacaoDosPagamentos> PagamentosAsync(CancellationToken cancelamento)
    {
        if (Pagamentos is null)
            throw new ErroDaContaException("O servidor do ProjectPlanner não reconheceu o pedido. Atualize o programa.", 404);
        await Passo("pagamentos", cancelamento);
        return Pagamentos;
    }

    public Task AssinarAsync(CancellationToken cancelamento) => Passo("assinar", cancelamento);

    public Task ComprarCreditosAsync(CancellationToken cancelamento) => Passo("comprar créditos", cancelamento);

    public async Task<DateTime?> CancelarAssinaturaAsync(CancellationToken cancelamento)
    {
        await Passo("cancelar assinatura", cancelamento);
        var ate = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
        Pagamentos = Pagamentos! with { Assinatura = "cancelled", ProfissionalAte = ate };
        return ate;
    }
}

/// <summary>Bloco 49: o Telegram da conta nos testes: as obras marcadas, a caixa de entrada e o que foi pedido.</summary>
public sealed class TelegramFalso : ITelegramDaConta
{
    public SituacaoDoTelegram Situacao { get; set; } = new(true, false, null, "ProjectPlannerBot");
    public Dictionary<Guid, (string Nome, ContextoDoProjeto Retrato)> Marcadas { get; } = [];
    public Dictionary<Guid, List<ItemDoTelegram>> Caixas { get; } = [];
    public Dictionary<Guid, byte[]> Fotos { get; } = [];
    public List<string> Pedidos { get; } = [];
    public ErroDaContaException? Erro { get; set; }

    private Task Passo(string pedido)
    {
        Pedidos.Add(pedido);
        if (Erro is { } erro)
        {
            Erro = null;
            return Task.FromException(erro);
        }
        return Task.CompletedTask;
    }

    public async Task<SituacaoDoTelegram> SituacaoAsync(CancellationToken c)
    {
        await Passo("situacao");
        return Situacao;
    }

    public async Task<CodigoParaOTelegram> CodigoAsync(CancellationToken c)
    {
        await Passo("codigo");
        return new CodigoParaOTelegram("ABCD2345", "https://t.me/ProjectPlannerBot?start=ABCD2345", "ProjectPlannerBot", 15);
    }

    public async Task DesligarAsync(CancellationToken c)
    {
        await Passo("desligar");
        Situacao = Situacao with { Ligado = false, Nome = null };
    }

    public async Task<IReadOnlyList<ObraNoTelegram>> ObrasAsync(CancellationToken c)
    {
        await Passo("obras");
        return [.. Marcadas.Select(m => new ObraNoTelegram(m.Key, m.Value.Nome, DateTime.UtcNow, Caixas.GetValueOrDefault(m.Key)?.Count ?? 0))];
    }

    public async Task MarcarAsync(Guid projeto, string nome, ContextoDoProjeto retrato, CancellationToken c)
    {
        await Passo($"marcar {nome}");
        Marcadas[projeto] = (nome, retrato);
    }

    public async Task DesmarcarAsync(Guid projeto, CancellationToken c)
    {
        await Passo("desmarcar");
        Marcadas.Remove(projeto);
        Caixas.Remove(projeto);
    }

    public async Task<IReadOnlyList<ItemDoTelegram>> CaixaAsync(Guid projeto, CancellationToken c)
    {
        await Passo("caixa");
        return [.. Caixas.GetValueOrDefault(projeto) ?? []];
    }

    public async Task<byte[]> FotoAsync(Guid item, CancellationToken c)
    {
        await Passo("foto");
        return Fotos.TryGetValue(item, out var bytes) ? bytes : throw new ErroDaContaException("Essa foto não está mais na caixa.", 404);
    }

    public async Task LevarAsync(Guid item, CancellationToken c)
    {
        await Passo("levar");
        foreach (var caixa in Caixas.Values)
            caixa.RemoveAll(i => i.Id == item);
    }
}
