using System.Text.Json;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Bloco 45: a conta ProjectPlanner no programa: entrar (e-mail e senha ou Google), criar a conta, trocar a senha, ver o
/// plano e o uso, sair. Também dá o token de acesso para a IA pela conta (<see cref="ISessaoDaConta"/>).
/// </summary>
public interface IContaProjectPlanner : ISessaoDaConta
{
    /// <summary>Quem entrou; nulo sem conta.</summary>
    PerfilDaConta? Perfil { get; }

    bool Conectada { get; }

    /// <summary>O cofre recusou a sessão guardada (bloqueado, acesso negado); nulo se está tudo bem.</summary>
    string? ProblemaNoCofre { get; }

    /// <summary>Bloco 46: os modelos de cada nível (vazio até <see cref="CarregarModelosAsync"/>).</summary>
    IReadOnlyList<ModeloDaConta> Modelos { get; }

    /// <summary>Bloco 46: os créditos que restam no mês (do último pedido de IA ou de <see cref="UsoAsync"/>); nulo se ainda não se sabe.</summary>
    decimal? CreditosRestantes { get; }

    /// <summary>Entrou, saiu, ou mudou o perfil (o plano), os modelos ou o saldo.</summary>
    event EventHandler? Mudou;

    /// <summary>Bloco 46: busca os modelos de cada nível (e se o plano libera cada um).</summary>
    Task<IReadOnlyList<ModeloDaConta>> CarregarModelosAsync(CancellationToken cancelamento);

    /// <returns>A mensagem do servidor (o código foi para o e-mail).</returns>
    Task<string> CriarContaAsync(string nome, string email, string senha, CancellationToken cancelamento);

    Task ConfirmarAsync(string email, string codigo, string senha, CancellationToken cancelamento);

    Task<string> ReenviarCodigoAsync(string email, CancellationToken cancelamento);

    Task EntrarAsync(string email, string senha, CancellationToken cancelamento);

    /// <summary>Abre o login do Google no navegador e espera a pessoa terminar.</summary>
    Task EntrarComGoogleAsync(CancellationToken cancelamento);

    Task<string> EsqueciASenhaAsync(string email, CancellationToken cancelamento);

    Task RedefinirSenhaAsync(string email, string codigo, string novaSenha, CancellationToken cancelamento);

    /// <summary>O plano e o uso de IA do mês (atualiza o perfil).</summary>
    Task<UsoDaConta> UsoAsync(CancellationToken cancelamento);

    /// <summary>Sai da conta neste computador (e encerra a sessão no servidor, se der).</summary>
    Task SairAsync(CancellationToken cancelamento);

    /// <summary>Bloco 51: a triagem do nível Automático (sem conta, ou sem a rota, falha e o envio segue no Padrão).</summary>
    Task<AvaliacaoDoPedido> AvaliarAsync(PedidoDeAvaliacao pedido, CancellationToken cancelamento) =>
        Task.FromException<AvaliacaoDoPedido>(new ErroDaContaException("A triagem do nível Automático não está disponível.", 0));

    // ---- Bloco 47: pagamentos (sem conta, ou numa implementação sem eles, avisam que não estão disponíveis) ----

    Task<SituacaoDosPagamentos> PagamentosAsync(CancellationToken cancelamento) => SemPagamentos<SituacaoDosPagamentos>();

    /// <summary>Pede ao servidor o link da assinatura do Profissional e o abre no navegador.</summary>
    Task AssinarAsync(CancellationToken cancelamento) => SemPagamentos<bool>();

    /// <summary>Pede ao servidor o link de um pacote de créditos e o abre no navegador.</summary>
    Task ComprarCreditosAsync(CancellationToken cancelamento) => SemPagamentos<bool>();

    /// <returns>Até quando o Profissional ainda vale; nulo se acabou na hora.</returns>
    Task<DateTime?> CancelarAssinaturaAsync(CancellationToken cancelamento) => SemPagamentos<DateTime?>();

    private static Task<T> SemPagamentos<T>() =>
        Task.FromException<T>(new ErroDaContaException("Os pagamentos não estão disponíveis nesta versão.", 0));
}

/// <summary>
/// A conta ProjectPlanner (Bloco 45). A sessão (o token de renovação, de 30 dias, e o perfil) fica no cofre do sistema,
/// como a chave do Gemini; o token de acesso (15 min) fica só na memória e é renovado quando vence. A cada renovação o
/// token de renovação muda, e o novo substitui o velho no cofre.
/// </summary>
/// <remarks>Os erros do servidor chegam como <see cref="ErroDaContaException"/>, já com a mensagem para o usuário.</remarks>
public sealed class ContaProjectPlanner : IContaProjectPlanner
{
    /// <summary>O nome da sessão no cofre (a "conta" nas Chaves do macOS).</summary>
    public const string NomeNoCofre = "conta-projectplanner";

    /// <summary>Para apontar o programa para outro servidor (desenvolvimento): o endereço dele.</summary>
    public const string VariavelDoServidor = "PROJECTPLANNER_SERVIDOR";

    public const string SemConta = "Entre na sua conta ProjectPlanner (menu Conta) para usar o assistente pela conta.";
    public const string SessaoAcabou = "A sessão da sua conta ProjectPlanner acabou. Entre de novo (menu Conta).";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ClienteDaConta _cliente;
    private readonly ICofreDeSegredos _cofre;
    private readonly TimeProvider _relogio;
    private readonly Func<Uri, Task> _abrirNavegador;
    private readonly SemaphoreSlim _renovando = new(1, 1);
    private bool _cofreLido;
    private Guardada? _guardada;
    private string? _acesso;
    private DateTime _acessoExpiraEm;

    public ContaProjectPlanner(ClienteDaConta cliente, ICofreDeSegredos cofre, TimeProvider relogio, Func<Uri, Task> abrirNavegador)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(cofre);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(abrirNavegador);
        (_cliente, _cofre, _relogio, _abrirNavegador) = (cliente, cofre, relogio, abrirNavegador);
    }

    /// <summary>O servidor: o da variável <see cref="VariavelDoServidor"/>, se houver um endereço válido; senão, o publicado.</summary>
    public static Uri Servidor(Func<string, string?> lerVariavel) =>
        Uri.TryCreate(lerVariavel(VariavelDoServidor)?.Trim(), UriKind.Absolute, out var endereco) && endereco.Scheme is "http" or "https"
            ? ClienteDaConta.ComBarra(endereco)
            : ClienteDaConta.ServidorPadrao;

    public event EventHandler? Mudou;

    public PerfilDaConta? Perfil => Ler()?.Perfil;

    public bool Conectada => Perfil is not null;

    public IReadOnlyList<ModeloDaConta> Modelos { get; private set; } = [];

    public decimal? CreditosRestantes { get; private set; }

    public async Task<IReadOnlyList<ModeloDaConta>> CarregarModelosAsync(CancellationToken cancelamento)
    {
        IReadOnlyList<ModeloDaConta> modelos;
        try
        {
            modelos = await _cliente.ModelosAsync(await AcessoDaContaAsync(false, cancelamento), cancelamento);
        }
        catch (ErroDaContaException ex) when (ex.SessaoRecusada)
        {
            modelos = await _cliente.ModelosAsync(await AcessoDaContaAsync(true, cancelamento), cancelamento);
        }
        if (!modelos.SequenceEqual(Modelos))
        {
            Modelos = modelos;
            Mudou?.Invoke(this, EventArgs.Empty);
        }
        return modelos;
    }

    /// <summary>O servidor mandou o saldo junto com uma resposta de IA.</summary>
    public void AtualizarSaldo(decimal creditosRestantes)
    {
        if (CreditosRestantes == creditosRestantes)
            return;
        CreditosRestantes = creditosRestantes;
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    public string? ProblemaNoCofre { get; private set; }

    public Task<string> CriarContaAsync(string nome, string email, string senha, CancellationToken cancelamento) =>
        _cliente.CadastrarAsync(email.Trim(), senha, nome.Trim(), cancelamento);

    public async Task ConfirmarAsync(string email, string codigo, string senha, CancellationToken cancelamento) =>
        Guardar(await _cliente.ConfirmarAsync(email.Trim(), codigo.Trim(), senha, cancelamento));

    public Task<string> ReenviarCodigoAsync(string email, CancellationToken cancelamento) => _cliente.ReenviarCodigoAsync(email.Trim(), cancelamento);

    public async Task EntrarAsync(string email, string senha, CancellationToken cancelamento) =>
        Guardar(await _cliente.EntrarAsync(email.Trim(), senha, cancelamento));

    public async Task EntrarComGoogleAsync(CancellationToken cancelamento)
    {
        var clientId = await _cliente.ClienteDoGoogleAsync(cancelamento)
                       ?? throw new ErroDaContaException("O login com Google ainda não está disponível. Use e-mail e senha.", 404);
        var codigo = await LoginComGoogle.ObterCodigoAsync(clientId, _abrirNavegador, cancelamento);
        Guardar(await _cliente.EntrarComGoogleAsync(codigo.Codigo, codigo.Verificador, codigo.Redirecionamento, cancelamento));
    }

    public Task<string> EsqueciASenhaAsync(string email, CancellationToken cancelamento) => _cliente.EsqueciASenhaAsync(email.Trim(), cancelamento);

    public async Task RedefinirSenhaAsync(string email, string codigo, string novaSenha, CancellationToken cancelamento) =>
        Guardar(await _cliente.RedefinirSenhaAsync(email.Trim(), codigo.Trim(), novaSenha, cancelamento));

    public async Task<UsoDaConta> UsoAsync(CancellationToken cancelamento)
    {
        UsoDaConta uso;
        try
        {
            uso = await _cliente.UsoAsync(await AcessoDaContaAsync(false, cancelamento), cancelamento);
        }
        catch (ErroDaContaException ex) when (ex.SessaoRecusada)
        {
            uso = await _cliente.UsoAsync(await AcessoDaContaAsync(true, cancelamento), cancelamento);
        }
        if (Ler() is { } guardada && guardada.Perfil != uso.Perfil)
        {
            Gravar(guardada with { Perfil = uso.Perfil });
            Modelos = []; // o plano mudou: os níveis liberados também
        }
        AtualizarSaldo(uso.CreditosRestantes);
        return uso;
    }

    public Task<AvaliacaoDoPedido> AvaliarAsync(PedidoDeAvaliacao pedido, CancellationToken cancelamento) =>
        ComAcessoAsync(a => _cliente.AvaliarAsync(a, pedido, cancelamento), cancelamento);

    public Task<SituacaoDosPagamentos> PagamentosAsync(CancellationToken cancelamento) => ComAcessoAsync(a => _cliente.PagamentosAsync(a, cancelamento), cancelamento);

    public async Task AssinarAsync(CancellationToken cancelamento) =>
        await AbrirPagamentoAsync(await ComAcessoAsync(a => _cliente.AssinarAsync(a, cancelamento), cancelamento));

    public async Task ComprarCreditosAsync(CancellationToken cancelamento) =>
        await AbrirPagamentoAsync(await ComAcessoAsync(a => _cliente.ComprarCreditosAsync(a, cancelamento), cancelamento));

    public Task<DateTime?> CancelarAssinaturaAsync(CancellationToken cancelamento) =>
        ComAcessoAsync(a => _cliente.CancelarAssinaturaAsync(a, cancelamento), cancelamento);

    /// <summary>Só abre um endereço https (o do Mercado Pago, que o servidor mandou).</summary>
    private Task AbrirPagamentoAsync(LinkDePagamento link) =>
        Uri.TryCreate(link?.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            ? _abrirNavegador(url)
            : throw new ErroDaContaException("O servidor mandou um link de pagamento inválido. Tente de novo.", 0);

    // Uma rota da conta com o token (renovado uma vez se o servidor recusar).
    private async Task<T> ComAcessoAsync<T>(Func<string, Task<T>> chamada, CancellationToken cancelamento)
    {
        try
        {
            return await chamada(await AcessoDaContaAsync(false, cancelamento));
        }
        catch (ErroDaContaException ex) when (ex.SessaoRecusada)
        {
            return await chamada(await AcessoDaContaAsync(true, cancelamento));
        }
    }

    public async Task SairAsync(CancellationToken cancelamento)
    {
        if (Ler() is not { } guardada)
            return;
        try
        {
            await _cliente.SairAsync(guardada.Renovacao, cancelamento);
        }
        catch (ErroDaContaException)
        {
            // sem internet ou sessão já vencida: sair neste computador basta (a sessão vence sozinha no servidor)
        }
        Esquecer();
    }

    /// <summary>Para a IA pela conta: os erros viram <see cref="ErroDoAssistenteException"/>, com a mensagem para o painel.</summary>
    public async Task<string> AcessoAsync(bool renovar, CancellationToken cancelamento)
    {
        try
        {
            return await AcessoDaContaAsync(renovar, cancelamento);
        }
        catch (ErroDaContaException ex)
        {
            throw new ErroDoAssistenteException(ex.SessaoRecusada ? SessaoAcabou : ex.Message, ex);
        }
    }

    // O token de acesso da memória enquanto vale (com folga de 1 minuto); senão, renova com o do cofre.
    private async Task<string> AcessoDaContaAsync(bool renovar, CancellationToken cancelamento)
    {
        await _renovando.WaitAsync(cancelamento);
        try
        {
            if (Ler() is not { } guardada)
                throw new ErroDaContaException(SemConta, 0);
            if (!renovar && _acesso is not null && _acessoExpiraEm > _relogio.GetUtcNow().UtcDateTime.AddMinutes(1))
                return _acesso;
            SessaoDaConta sessao;
            try
            {
                sessao = await _cliente.RenovarAsync(guardada.Renovacao, cancelamento);
            }
            catch (ErroDaContaException ex) when (ex.SessaoRecusada)
            {
                Esquecer(); // a sessão acabou (vencida, ou encerrada em outro lugar): é preciso entrar de novo
                throw new ErroDaContaException(SessaoAcabou, 401, ex);
            }
            Guardar(sessao);
            return sessao.Acesso;
        }
        finally
        {
            _renovando.Release();
        }
    }

    private void Guardar(SessaoDaConta sessao)
    {
        (_acesso, _acessoExpiraEm) = (sessao.Acesso, sessao.AcessoExpiraEm);
        Gravar(new Guardada(sessao.Renovacao, sessao.Usuario));
    }

    private void Gravar(Guardada guardada)
    {
        var mudou = _guardada?.Perfil != guardada.Perfil;
        (_guardada, _cofreLido) = (guardada, true);
        if (mudou)
            (Modelos, CreditosRestantes) = ([], null); // outra conta, ou outro plano: os níveis e o saldo são outros
        try
        {
            _cofre.Gravar(NomeNoCofre, JsonSerializer.Serialize(guardada, Json));
            ProblemaNoCofre = null;
        }
        catch (CofreException ex)
        {
            // A sessão vale até fechar o programa; ao abrir de novo, será preciso entrar outra vez.
            ProblemaNoCofre = ex.Message;
        }
        if (mudou)
            Mudou?.Invoke(this, EventArgs.Empty);
    }

    private void Esquecer()
    {
        var tinha = _guardada is not null;
        (_guardada, _cofreLido, _acesso, Modelos, CreditosRestantes) = (null, true, null, [], null);
        try
        {
            _cofre.Remover(NomeNoCofre);
        }
        catch (CofreException ex)
        {
            ProblemaNoCofre = ex.Message;
        }
        if (tinha)
            Mudou?.Invoke(this, EventArgs.Empty);
    }

    // A sessão do cofre, lida uma vez (um conteúdo que não dá para ler vale como sem conta).
    private Guardada? Ler()
    {
        if (_cofreLido)
            return _guardada;
        _cofreLido = true;
        try
        {
            if (_cofre.Ler(NomeNoCofre) is { Length: > 0 } texto
                && JsonSerializer.Deserialize<Guardada>(texto, Json) is { Renovacao.Length: > 0, Perfil: not null } guardada)
                _guardada = guardada;
        }
        catch (CofreException ex)
        {
            ProblemaNoCofre = ex.Message;
        }
        catch (JsonException)
        {
        }
        return _guardada;
    }

    private sealed record Guardada(string Renovacao, PerfilDaConta Perfil);
}

/// <summary>Sem conta (os testes e quem não passa a conta): nada conectado, e entrar não é possível.</summary>
public sealed class SemContaProjectPlanner : IContaProjectPlanner
{
    public PerfilDaConta? Perfil => null;
    public bool Conectada => false;
    public string? ProblemaNoCofre => null;
    public IReadOnlyList<ModeloDaConta> Modelos => [];
    public decimal? CreditosRestantes => null;
    public Task<IReadOnlyList<ModeloDaConta>> CarregarModelosAsync(CancellationToken cancelamento) => Falha<IReadOnlyList<ModeloDaConta>>();

    public event EventHandler? Mudou
    {
        add { }
        remove { }
    }

    private static Task<T> Falha<T>() => Task.FromException<T>(new ErroDaContaException("A conta ProjectPlanner não está disponível.", 0));
    public Task<string> CriarContaAsync(string nome, string email, string senha, CancellationToken cancelamento) => Falha<string>();
    public Task ConfirmarAsync(string email, string codigo, string senha, CancellationToken cancelamento) => Falha<bool>();
    public Task<string> ReenviarCodigoAsync(string email, CancellationToken cancelamento) => Falha<string>();
    public Task EntrarAsync(string email, string senha, CancellationToken cancelamento) => Falha<bool>();
    public Task EntrarComGoogleAsync(CancellationToken cancelamento) => Falha<bool>();
    public Task<string> EsqueciASenhaAsync(string email, CancellationToken cancelamento) => Falha<string>();
    public Task RedefinirSenhaAsync(string email, string codigo, string novaSenha, CancellationToken cancelamento) => Falha<bool>();
    public Task<UsoDaConta> UsoAsync(CancellationToken cancelamento) => Falha<UsoDaConta>();
    public Task SairAsync(CancellationToken cancelamento) => Task.CompletedTask;
    public Task<string> AcessoAsync(bool renovar, CancellationToken cancelamento) =>
        Task.FromException<string>(new ErroDoAssistenteException(ContaProjectPlanner.SemConta));
}
