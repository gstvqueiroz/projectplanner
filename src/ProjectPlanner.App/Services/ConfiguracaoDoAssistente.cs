using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Services;

/// <summary>De onde vem a chave que o assistente está usando.</summary>
public enum OrigemDaChave
{
    Nenhuma,
    Cofre,
    VariavelDeAmbiente,
}

/// <summary>A configuração do assistente como a tela de configurações a vê e muda (Bloco 23).</summary>
public interface IConfiguracaoAssistente
{
    string NomeDoProvedor { get; }

    /// <summary>"Chaves do macOS", "Gerenciador de Credenciais do Windows".</summary>
    string NomeDoCofre { get; }

    bool CofreDisponivel { get; }

    string ModeloPadrao { get; }

    string Modelo { get; }

    OrigemDaChave Origem { get; }

    /// <summary>O fim da chave em uso ("…rWc"), para o usuário reconhecer qual é sem ver a chave inteira.</summary>
    string? FinalDaChave { get; }

    /// <summary>O cofre recusou a leitura (bloqueado, acesso negado); nulo se está tudo bem.</summary>
    string? ProblemaNoCofre { get; }

    /// <exception cref="ArgumentException">Chave vazia, com espaços ou longa demais.</exception>
    /// <exception cref="CofreException">O cofre não aceitou.</exception>
    void SalvarChave(string chave);

    /// <exception cref="CofreException">O cofre não deixou apagar.</exception>
    void RemoverChave();

    /// <exception cref="ArgumentException">Nome de modelo inválido.</exception>
    void SalvarModelo(string modelo);

    /// <summary>Testa a chave digitada (ou, sem ela, a que está em uso) com o modelo.</summary>
    /// <exception cref="ArgumentException">Sem chave, ou chave ou modelo inválidos.</exception>
    /// <exception cref="ErroDoAssistenteException">A chave não vale, o modelo não existe ou não há conexão.</exception>
    Task TestarAsync(string? chave, string modelo, CancellationToken cancelamento);
}

/// <summary>
/// A chave e o modelo do assistente (Bloco 23). A chave fica no cofre do sistema (nunca em arquivo do programa); sem
/// ela, vale a variável de ambiente <see cref="VariavelDeAmbiente"/>, para desenvolvimento. O modelo fica em
/// <c>assistente.json</c>. O cofre é lido uma vez, na primeira vez que a chave é pedida.
/// </summary>
public sealed class ConfiguracaoDoAssistente : IConfiguracaoAssistente, IConfiguracaoIA
{
    /// <summary>O nome do segredo no cofre (a "conta" nas Chaves do macOS).</summary>
    public const string NomeNoCofre = "gemini-api-key";

    public const string VariavelDeAmbiente = "GEMINI_API_KEY";

    public const int TamanhoMaximoDaChave = 512;

    private readonly ICofreDeSegredos _cofre;
    private readonly IPreferenciasAssistente _preferencias;
    private readonly IProvedorIA _provedor;
    private readonly Func<string, string?> _lerVariavel;
    private bool _cofreLido;
    private string? _chaveDoCofre;
    private string? _modelo;
    private string? _problemaNoCofre;

    public ConfiguracaoDoAssistente(ICofreDeSegredos cofre, IPreferenciasAssistente preferencias, IProvedorIA provedor,
        Func<string, string?> lerVariavel)
    {
        ArgumentNullException.ThrowIfNull(cofre);
        ArgumentNullException.ThrowIfNull(preferencias);
        ArgumentNullException.ThrowIfNull(provedor);
        ArgumentNullException.ThrowIfNull(lerVariavel);
        (_cofre, _preferencias, _provedor, _lerVariavel) = (cofre, preferencias, provedor, lerVariavel);
    }

    public string NomeDoProvedor => _provedor.Nome;

    public string NomeDoCofre => _cofre.Nome;

    public bool CofreDisponivel => _cofre.Disponivel;

    public string ModeloPadrao => _provedor.ModeloPadrao;

    public string? ProblemaNoCofre
    {
        get
        {
            ChaveDoCofre(); // o problema aparece na primeira leitura do cofre
            return _problemaNoCofre;
        }
    }

    public string? ChaveApi => ChaveDoCofre() ?? ChaveDaVariavel();

    public OrigemDaChave Origem => ChaveDoCofre() is not null ? OrigemDaChave.Cofre
        : ChaveDaVariavel() is not null ? OrigemDaChave.VariavelDeAmbiente
        : OrigemDaChave.Nenhuma;

    public string? FinalDaChave => ChaveApi is { Length: >= 8 } chave ? "…" + chave[^4..] : null;

    public string Modelo => _modelo ??= ModeloSalvo() ?? _provedor.ModeloPadrao;

    public void SalvarChave(string chave)
    {
        var limpa = Validar(chave);
        _cofre.Gravar(NomeNoCofre, limpa);
        (_chaveDoCofre, _cofreLido, _problemaNoCofre) = (limpa, true, null);
    }

    public void RemoverChave()
    {
        _cofre.Remover(NomeNoCofre);
        (_chaveDoCofre, _cofreLido, _problemaNoCofre) = (null, true, null);
    }

    public void SalvarModelo(string modelo)
    {
        var nome = _provedor.NormalizarModelo(modelo);
        _preferencias.Salvar(new PreferenciasAssistente(nome));
        _modelo = nome;
    }

    public Task TestarAsync(string? chave, string modelo, CancellationToken cancelamento)
    {
        var usada = string.IsNullOrWhiteSpace(chave)
            ? ChaveApi ?? throw new ArgumentException("Cole a chave de API para testar (ou salve uma antes).")
            : Validar(chave);
        return _provedor.TestarAsync(usada, _provedor.NormalizarModelo(modelo), cancelamento);
    }

    /// <summary>A chave sem os espaços das pontas (comuns ao colar), recusando espaços no meio e quebras de linha.</summary>
    public static string Validar(string chave)
    {
        var limpa = (chave ?? "").Trim();
        if (limpa.Length == 0)
            throw new ArgumentException("Cole a chave de API.");
        if (limpa.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("A chave não pode ter espaços nem quebras de linha.");
        if (limpa.Length > TamanhoMaximoDaChave)
            throw new ArgumentException($"A chave tem {limpa.Length} caracteres; o máximo é {TamanhoMaximoDaChave}.");
        return limpa;
    }

    private string? ChaveDoCofre()
    {
        if (!_cofreLido)
        {
            _cofreLido = true;
            try
            {
                _chaveDoCofre = Limpa(_cofre.Ler(NomeNoCofre));
            }
            catch (CofreException ex)
            {
                _problemaNoCofre = ex.Message;
            }
        }
        return _chaveDoCofre;
    }

    private string? ChaveDaVariavel() => Limpa(_lerVariavel(VariavelDeAmbiente));

    private static string? Limpa(string? chave) => string.IsNullOrWhiteSpace(chave) ? null : chave.Trim();

    private string? ModeloSalvo()
    {
        if (_preferencias.Carregar()?.Modelo is not { } salvo)
            return null;
        try
        {
            return _provedor.NormalizarModelo(salvo);
        }
        catch (ArgumentException)
        {
            return null; // assistente.json mexido à mão: volta ao padrão
        }
    }
}
