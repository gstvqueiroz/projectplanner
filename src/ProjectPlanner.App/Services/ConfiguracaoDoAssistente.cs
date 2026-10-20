using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Services;

/// <summary>A configuração do assistente como a tela de configurações e o painel a veem e mudam.</summary>
public interface IConfiguracaoAssistente
{
    /// <summary>O modelo do nível Padrão, usado enquanto nada foi escolhido.</summary>
    string ModeloPadrao { get; }

    /// <summary>O modelo escolhido (o id do servidor, como "gemini-2.5-flash").</summary>
    string Modelo { get; }

    /// <exception cref="ArgumentException">Nome de modelo inválido.</exception>
    void SalvarModelo(string modelo);

    /// <summary>O que fazer num pedido de cronograma (Bloco 26): perguntar (padrão), montar direto ou estudar antes.</summary>
    PreferenciaDeModo PreferenciaDeModo { get; }

    /// <exception cref="ArgumentOutOfRangeException">Preferência desconhecida.</exception>
    void SalvarPreferenciaDeModo(PreferenciaDeModo preferencia);

    /// <summary>
    /// Bloco 46: a variável de ambiente <see cref="ConfiguracaoDoAssistente.VariavelDeAmbiente"/> está definida (quem compila
    /// o código): a IA vai direto ao Google com essa chave, sem a conta.
    /// </summary>
    bool ChaveDeDesenvolvimento { get; }

    /// <summary>Confere a conta (ou a chave de desenvolvimento) com o modelo, sem gastar créditos.</summary>
    /// <exception cref="ArgumentException">Sem conta, ou modelo inválido.</exception>
    /// <exception cref="ErroDoAssistenteException">O modelo não é do plano, não existe ou não há conexão.</exception>
    Task TestarAsync(string modelo, CancellationToken cancelamento);
}

/// <summary>
/// A configuração do assistente (Blocos 23, 45 e 46). Desde o Bloco 46 a IA vai pela conta ProjectPlanner: a "chave" que a IA
/// recebe é a marca <see cref="ProvedorDaConta.Credencial"/> (com a conta conectada), e o provedor do programa
/// (<see cref="ProvedorConformeCredencial"/>) manda os pedidos pelo servidor. Só quem compila o código usa uma chave do Gemini,
/// pela variável <see cref="VariavelDeAmbiente"/>. O modelo (o nível) e o modo ficam em <c>assistente.json</c>.
/// </summary>
/// <remarks>
/// A chave que as versões até a 0.44 guardavam no cofre (item <c>gemini-api-key</c>) não é mais lida: assim o macOS não pede
/// a senha das chaves a cada versão. Ela pode ser apagada no app Acesso às Chaves.
/// </remarks>
public sealed class ConfiguracaoDoAssistente : IConfiguracaoAssistente, IConfiguracaoIA
{
    /// <summary>O nome da chave que as versões até a 0.44 guardavam no cofre (só para a documentação).</summary>
    public const string NomeAntigoNoCofre = "gemini-api-key";

    public const string VariavelDeAmbiente = "GEMINI_API_KEY";

    private readonly IPreferenciasAssistente _preferencias;
    private readonly IProvedorIA _provedor;
    private readonly Func<string, string?> _lerVariavel;
    private readonly IContaProjectPlanner _conta;
    private string? _modelo;

    public ConfiguracaoDoAssistente(IPreferenciasAssistente preferencias, IProvedorIA provedor, Func<string, string?> lerVariavel,
        IContaProjectPlanner? conta = null)
    {
        ArgumentNullException.ThrowIfNull(preferencias);
        ArgumentNullException.ThrowIfNull(provedor);
        ArgumentNullException.ThrowIfNull(lerVariavel);
        (_preferencias, _provedor, _lerVariavel, _conta) = (preferencias, provedor, lerVariavel, conta ?? new SemContaProjectPlanner());
    }

    public string ModeloPadrao => _provedor.ModeloPadrao;

    public bool ChaveDeDesenvolvimento => ChaveDaVariavel() is not null;

    /// <summary>A credencial da IA: a chave de desenvolvimento, ou a marca da conta (conectada); nula sem nenhuma.</summary>
    public string? ChaveApi => ChaveDaVariavel() ?? (_conta.Conectada ? ProvedorDaConta.Credencial : null);

    public string Modelo => _modelo ??= ModeloSalvo() ?? _provedor.ModeloPadrao;

    public void SalvarModelo(string modelo)
    {
        var nome = _provedor.NormalizarModelo(modelo);
        _preferencias.Salvar((_preferencias.Carregar() ?? new PreferenciasAssistente(nome)) with { Modelo = nome });
        _modelo = nome;
    }

    public PreferenciaDeModo PreferenciaDeModo => _preferencias.Carregar()?.Modo ?? PreferenciaDeModo.Perguntar;

    public void SalvarPreferenciaDeModo(PreferenciaDeModo preferencia)
    {
        if (!Enum.IsDefined(preferencia))
            throw new ArgumentOutOfRangeException(nameof(preferencia), preferencia, "Preferência desconhecida.");
        _preferencias.Salvar((_preferencias.Carregar() ?? new PreferenciasAssistente(Modelo)) with { Modo = preferencia });
    }

    public Task TestarAsync(string modelo, CancellationToken cancelamento)
    {
        var credencial = ChaveApi ?? throw new ArgumentException("Entre na sua conta ProjectPlanner (menu Conta) para testar.");
        return _provedor.TestarAsync(credencial, _provedor.NormalizarModelo(modelo), cancelamento);
    }

    private string? ChaveDaVariavel() => _lerVariavel(VariavelDeAmbiente) is { } chave && !string.IsNullOrWhiteSpace(chave) ? chave.Trim() : null;

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
