using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

/// <summary>As partes da tela da conta.</summary>
public enum PaginaDaConta
{
    Entrar,
    Criar,
    Confirmar,
    Esqueci,
    Redefinir,
    Perfil,
}

/// <summary>
/// Bloco 45: a tela da conta ProjectPlanner (menu Conta): entrar com e-mail e senha ou com o Google, criar a conta (com o
/// código que chega por e-mail), trocar a senha esquecida e, conectado, ver o plano e o uso de IA do mês e sair.
/// </summary>
public sealed partial class ContaViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IContaProjectPlanner _conta;
    private CancellationTokenSource? _pedido;

    public ContaViewModel(IContaProjectPlanner conta)
    {
        ArgumentNullException.ThrowIfNull(conta);
        _conta = conta;
        Pagina = conta.Conectada ? PaginaDaConta.Perfil : PaginaDaConta.Entrar;
        Email = conta.Perfil?.Email ?? "";
    }

    /// <summary>Ao abrir já conectado: busca o plano e o uso.</summary>
    public Task AbrirAsync() => Pagina == PaginaDaConta.Perfil ? AtualizarUsoAsync() : Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(EmEntrar), nameof(EmCriar), nameof(EmConfirmar), nameof(EmEsqueci), nameof(EmRedefinir),
        nameof(EmPerfil), nameof(PedeEmail), nameof(PedeSenha), nameof(NomeDaConta), nameof(EmailDaConta), nameof(EntrouComGoogle))]
    public partial PaginaDaConta Pagina { get; private set; }

    public string Titulo => Pagina switch
    {
        PaginaDaConta.Criar => "Criar conta",
        PaginaDaConta.Confirmar => "Confirmar o e-mail",
        PaginaDaConta.Esqueci => "Esqueci a senha",
        PaginaDaConta.Redefinir => "Nova senha",
        PaginaDaConta.Perfil => "Minha conta",
        _ => "Entrar",
    };

    public bool EmEntrar => Pagina == PaginaDaConta.Entrar;
    public bool EmCriar => Pagina == PaginaDaConta.Criar;
    public bool EmConfirmar => Pagina == PaginaDaConta.Confirmar;
    public bool EmEsqueci => Pagina == PaginaDaConta.Esqueci;
    public bool EmRedefinir => Pagina == PaginaDaConta.Redefinir;
    public bool EmPerfil => Pagina == PaginaDaConta.Perfil;
    public bool PedeEmail => Pagina is PaginaDaConta.Entrar or PaginaDaConta.Criar or PaginaDaConta.Esqueci;
    public bool PedeSenha => Pagina is PaginaDaConta.Entrar or PaginaDaConta.Criar;

    /// <summary>O texto que explica a conta, no alto da tela de entrar.</summary>
    public const string Explicacao =
        "Com a conta ProjectPlanner, o assistente de IA funciona sem chave do Gemini: o plano grátis tem uma cota por mês, e o " +
        "plano Profissional, uma cota maior. Quem prefere pode continuar usando a própria chave (engrenagem do assistente).";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand), nameof(CriarCommand), nameof(PedirCodigoDaSenhaCommand))]
    public partial string Email { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand), nameof(CriarCommand))]
    public partial string Senha { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CriarCommand))]
    public partial string Nome { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand), nameof(RedefinirCommand))]
    public partial string Codigo { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedefinirCommand))]
    public partial string NovaSenha { get; set; } = "";

    [ObservableProperty]
    public partial string Mensagem { get; private set; } = "";

    [ObservableProperty]
    public partial bool MensagemEhErro { get; private set; }

    /// <summary>Esperando o servidor (ou o navegador, no Google): os botões ficam travados e aparece "Cancelar".</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand), nameof(EntrarComGoogleCommand), nameof(CriarCommand), nameof(ConfirmarCommand),
        nameof(ReenviarCodigoCommand), nameof(PedirCodigoDaSenhaCommand), nameof(RedefinirCommand), nameof(AtualizarUsoCommand), nameof(SairCommand))]
    public partial bool Ocupado { get; private set; }

    // ---- Minha conta ----

    public string NomeDaConta => _conta.Perfil?.Nome ?? "";

    public string EmailDaConta => _conta.Perfil?.Email ?? "";

    public bool EntrouComGoogle => _conta.Perfil is { Google: true, TemSenha: false };

    [ObservableProperty]
    public partial string Plano { get; private set; } = "";

    [ObservableProperty]
    public partial string TextoDoUso { get; private set; } = "";

    /// <summary>O uso do mês em % da cota (0 a 100), para a barra.</summary>
    [ObservableProperty]
    public partial double PercentualDoUso { get; private set; }

    public static string NomeDoPlano(string plano) => plano switch
    {
        "profissional" => "Profissional",
        "gratis" => "Grátis",
        _ => plano,
    };

    // ---- Ações ----

    [RelayCommand(CanExecute = nameof(PodeEntrar))]
    private Task Entrar() => Executar("Entrando…", async c =>
    {
        try
        {
            await _conta.EntrarAsync(Email, Senha, c);
        }
        catch (ErroDaContaException ex) when (ex.Status == 403)
        {
            // Cadastrou e não confirmou: vai direto para o código (a senha digitada é a do cadastro).
            IrPara(PaginaDaConta.Confirmar);
            throw;
        }
        await Conectou("Você entrou na conta.");
    });

    private bool PodeEntrar() => !Ocupado && Preenchido(Email) && Senha.Length > 0;

    [RelayCommand(CanExecute = nameof(Livre))]
    private Task EntrarComGoogle() => Executar("Continue no navegador: escolha a conta Google e autorize. Esta tela espera você voltar.", async c =>
    {
        await _conta.EntrarComGoogleAsync(c);
        await Conectou("Você entrou com o Google.");
    });

    [RelayCommand(CanExecute = nameof(PodeCriar))]
    private Task Criar() => Executar("Criando a conta…", async c =>
    {
        var mensagem = await _conta.CriarContaAsync(Nome, Email, Senha, c);
        IrPara(PaginaDaConta.Confirmar);
        Mostrar(mensagem + " Olhe também a caixa de spam.", erro: false);
    });

    private bool PodeCriar() => !Ocupado && Preenchido(Email) && Senha.Length > 0 && Preenchido(Nome);

    [RelayCommand(CanExecute = nameof(PodeConfirmar))]
    private Task Confirmar() => Executar("Confirmando…", async c =>
    {
        await _conta.ConfirmarAsync(Email, Codigo, Senha, c);
        await Conectou("Conta confirmada. Bem-vindo!");
    });

    private bool PodeConfirmar() => !Ocupado && Preenchido(Codigo);

    [RelayCommand(CanExecute = nameof(Livre))]
    private Task ReenviarCodigo() => Executar("Pedindo outro código…", async c =>
        Mostrar(await _conta.ReenviarCodigoAsync(Email, c), erro: false));

    [RelayCommand(CanExecute = nameof(PodePedirCodigoDaSenha))]
    private Task PedirCodigoDaSenha() => Executar("Pedindo o código…", async c =>
    {
        var mensagem = await _conta.EsqueciASenhaAsync(Email, c);
        IrPara(PaginaDaConta.Redefinir);
        Mostrar(mensagem + " Olhe também a caixa de spam.", erro: false);
    });

    private bool PodePedirCodigoDaSenha() => !Ocupado && Preenchido(Email);

    [RelayCommand(CanExecute = nameof(PodeRedefinir))]
    private Task Redefinir() => Executar("Trocando a senha…", async c =>
    {
        await _conta.RedefinirSenhaAsync(Email, Codigo, NovaSenha, c);
        await Conectou("Senha trocada. As outras sessões da conta foram encerradas.");
    });

    private bool PodeRedefinir() => !Ocupado && Preenchido(Codigo) && NovaSenha.Length > 0;

    [RelayCommand(CanExecute = nameof(Livre))]
    private Task AtualizarUso() => AtualizarUsoAsync();

    [RelayCommand(CanExecute = nameof(Livre))]
    private Task Sair() => Executar("Saindo…", async c =>
    {
        await _conta.SairAsync(c);
        (Senha, Codigo, NovaSenha, Plano, TextoDoUso, PercentualDoUso) = ("", "", "", "", "", 0);
        IrPara(PaginaDaConta.Entrar);
        Mostrar("Você saiu da conta neste computador.", erro: false);
    });

    [RelayCommand]
    private void Cancelar() => _pedido?.Cancel();

    [RelayCommand]
    private void IrParaEntrar() => IrPara(PaginaDaConta.Entrar);

    [RelayCommand]
    private void IrParaCriar() => IrPara(PaginaDaConta.Criar);

    [RelayCommand]
    private void IrParaEsqueci() => IrPara(PaginaDaConta.Esqueci);

    /// <summary>A janela foi fechada: o que estiver em andamento (o login no navegador, por exemplo) é cancelado.</summary>
    public void Fechando() => _pedido?.Cancel();

    private Task AtualizarUsoAsync() => Executar("Buscando o uso do mês…", async c =>
    {
        var uso = await _conta.UsoAsync(c);
        Plano = NomeDoPlano(uso.Perfil.Plano);
        PercentualDoUso = uso.CreditosDoPlano <= 0 ? 0 : Math.Min(100, (double)(100 * uso.CreditosUsadosNoMes / uso.CreditosDoPlano));
        TextoDoUso = $"{uso.CreditosUsadosNoMes.ToString("N0", Brasil)} de {uso.CreditosDoPlano.ToString("N0", Brasil)} créditos de IA usados " +
                     $"este mês ({PercentualDoUso.ToString("0.#", Brasil)}%). Eles voltam no dia 1º.";
        OnPropertyChanged(nameof(NomeDaConta));
        Mostrar("", erro: false);
    });

    private async Task Conectou(string mensagem)
    {
        (Senha, Codigo, NovaSenha) = ("", "", "");
        IrPara(PaginaDaConta.Perfil);
        await AtualizarUsoAsync();
        if (!MensagemEhErro)
            Mostrar(mensagem, erro: false);
    }

    // Uma ação com o servidor: trava os botões, mostra o andamento e o erro (com a mensagem do servidor).
    private async Task Executar(string andamento, Func<CancellationToken, Task> acao)
    {
        using var pedido = _pedido = new CancellationTokenSource();
        Ocupado = true;
        Mostrar(andamento, erro: false);
        try
        {
            await acao(pedido.Token);
        }
        catch (OperationCanceledException)
        {
            Mostrar("Cancelado.", erro: true);
        }
        catch (Exception ex) when (ex is ErroDaContaException or CofreException or IA.ErroDoAssistenteException)
        {
            Mostrar(ex.Message, erro: true);
        }
        finally
        {
            _pedido = null;
            Ocupado = false;
        }
    }

    private void IrPara(PaginaDaConta pagina)
    {
        Pagina = pagina;
        Mostrar("", erro: false);
    }

    private bool Livre() => !Ocupado;

    private static bool Preenchido(string? texto) => !string.IsNullOrWhiteSpace(texto);

    private void Mostrar(string mensagem, bool erro) => (Mensagem, MensagemEhErro) = (mensagem, erro);
}
