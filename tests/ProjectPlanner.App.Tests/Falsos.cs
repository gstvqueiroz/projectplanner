using ProjectPlanner.App.Models;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.App.ViewModels;
using System.Security.Cryptography;
using System.Text;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;

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
            .Select(t => (t.Id, t.Nome, t.DataInicio, t.Duracao, t.PercentualConcluido, t.Notas, t.TarefaPaiId, t.EhResumo, Fotos: t.FotosNaOrdemDeCadastro.ToList()))
            .ToList();
        var vinculos = cronograma.Predecessoras.ToList();
        _arquivos[caminhoArquivo] = () =>
        {
            var reconstruido = new Cronograma(id, nome, inicio);
            foreach (var t in tarefas)
            {
                // Como no arquivo: um resumo entra com pelo menos 1 dia (é recalculado).
                var tarefa = new Tarefa(t.Id, t.Nome, t.DataInicio, t.EhResumo ? Math.Max(t.Duracao, 1) : t.Duracao);
                tarefa.AtualizarPercentualConcluido(t.PercentualConcluido);
                tarefa.AlterarNotas(t.Notas);
                foreach (var foto in t.Fotos)
                    tarefa.AdicionarFoto(foto);
                reconstruido.AdicionarTarefa(tarefa, t.TarefaPaiId);
            }
            foreach (var vinculo in vinculos)
                reconstruido.AdicionarPredecessora(vinculo);
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

    /// <summary>As telas Sobre abertas (Bloco 25).</summary>
    public List<SobreViewModel> Sobres { get; } = [];

    public Task MostrarSobreAsync(SobreViewModel sobre)
    {
        Sobres.Add(sobre);
        return Task.CompletedTask;
    }

    public Task MostrarConfiguracaoAssistenteAsync(ConfiguracaoAssistenteViewModel configuracao)
    {
        ConfiguracoesDoAssistente.Add(configuracao);
        AoConfigurar?.Invoke(configuracao);
        return Task.CompletedTask;
    }

    public Task MostrarFotosAsync(GaleriaFotosViewModel galeria)
    {
        Galerias.Add(galeria);
        return Task.CompletedTask;
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

    public MainWindowViewModel CriarViewModel() =>
        new(Repositorio, Calculadora, Dialogos, Relogio, Preferencias, Assistente, PreferenciasInterface, Processador, ConfiguracaoAssistente);

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
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nome))), data, OrigemDaData.ExifOriginal);
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

/// <summary>A configuração do assistente para os testes do painel (Bloco 23): só guarda o que a tela mudaria.</summary>
public sealed class ConfiguracaoAssistenteFalsa : IConfiguracaoAssistente
{
    public string NomeDoProvedor => "Gemini (Google)";
    public string NomeDoCofre => "Chaves do macOS";
    public bool CofreDisponivel => true;
    public string ModeloPadrao => "gemini-2.5-flash";
    public string Modelo { get; set; } = "gemini-2.5-flash";
    public string? Chave { get; set; }
    public OrigemDaChave Origem => Chave is null ? OrigemDaChave.Nenhuma : OrigemDaChave.Cofre;
    public string? FinalDaChave => Chave is null ? null : "…" + Chave[^4..];
    public string? ProblemaNoCofre => null;
    public void SalvarChave(string chave) => Chave = chave;
    public void RemoverChave() => Chave = null;
    public void SalvarModelo(string modelo) => Modelo = modelo;
    public Task TestarAsync(string? chave, string modelo, CancellationToken cancelamento) => Task.CompletedTask;
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

    public async Task TestarAsync(string chave, string modelo, CancellationToken cancelamento)
    {
        Testes.Add((chave, modelo));
        if (TesteSegurado is { } segurado)
            await segurado.Task.WaitAsync(cancelamento);
        if (ErroNoTeste is not null)
            throw ErroNoTeste;
    }
}
