using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

/// <summary>Repositório em memória que registra as chamadas.</summary>
public sealed class RepositorioFalso : IProjetoRepository
{
    public List<(Cronograma Cronograma, string Caminho)> Salvos { get; } = [];
    public List<string> Abertos { get; } = [];
    public Func<string, Cronograma> AoAbrir { get; set; } = caminho => throw new FileNotFoundException("Não existe.", caminho);
    public Exception? ErroAoSalvar { get; set; }

    public void Salvar(Cronograma cronograma, string caminhoArquivo)
    {
        if (ErroAoSalvar is not null)
            throw ErroAoSalvar;
        Salvos.Add((cronograma, caminhoArquivo));
    }

    public Cronograma Abrir(string caminhoArquivo)
    {
        Abertos.Add(caminhoArquivo);
        return AoAbrir(caminhoArquivo);
    }
}

/// <summary>
/// Repositório em memória que guarda só os dados do projeto, como o arquivo faria, e reconstrói um
/// cronograma novo a cada Abrir (nunca devolve o mesmo objeto que recebeu).
/// </summary>
public sealed class RepositorioEmMemoria : IProjetoRepository
{
    private readonly Dictionary<string, Func<Cronograma>> _arquivos = [];

    public IReadOnlyCollection<string> Arquivos => _arquivos.Keys;

    public void Salvar(Cronograma cronograma, string caminhoArquivo)
    {
        var (id, nome, inicio) = (cronograma.Id, cronograma.Nome, cronograma.DataInicio);
        var tarefas = cronograma.Tarefas.Select(t => (t.Id, t.Nome, t.DataInicio, t.Duracao, t.PercentualConcluido)).ToList();
        var vinculos = cronograma.Predecessoras.ToList();
        _arquivos[caminhoArquivo] = () =>
        {
            var reconstruido = new Cronograma(id, nome, inicio);
            foreach (var t in tarefas)
            {
                var tarefa = new Tarefa(t.Id, t.Nome, t.DataInicio, t.Duracao);
                tarefa.AtualizarPercentualConcluido(t.PercentualConcluido);
                reconstruido.AdicionarTarefa(tarefa);
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

    public MainWindowViewModel CriarViewModel() => new(Repositorio, Calculadora, Dialogos, Relogio);

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
