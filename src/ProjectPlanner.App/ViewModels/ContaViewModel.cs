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
/// código que chega por e-mail), trocar a senha esquecida e, conectado, ver o plano e o uso de IA do mês e sair. Bloco 47:
/// assinar o Profissional, comprar créditos e cancelar a assinatura (o pagamento é no Mercado Pago, pelo navegador).
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
        "Com a conta ProjectPlanner, o assistente de IA funciona sem chave de nenhuma IA: o plano Grátis tem créditos por mês " +
        "para experimentar, e o Profissional, muito mais créditos, os níveis de IA mais fortes e o Telegram.";

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
        nameof(ReenviarCodigoCommand), nameof(PedirCodigoDaSenhaCommand), nameof(RedefinirCommand), nameof(AtualizarUsoCommand), nameof(SairCommand),
        nameof(AssinarCommand), nameof(ComprarCreditosCommand), nameof(CancelarAssinaturaCommand), nameof(ConfirmarCancelamentoCommand))]
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

    // ---- Bloco 47: plano e pagamento ----

    /// <summary>A situação dos pagamentos (nula enquanto não se sabe, ou se o servidor não cobra).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostraPagamentos), nameof(PodeAssinarPlano), nameof(TemAssinatura), nameof(TextoDoAssinar),
        nameof(TextoDoPacote), nameof(TextoDoPlanoPago), nameof(EmModoDeTeste))]
    [NotifyCanExecuteChangedFor(nameof(AssinarCommand), nameof(ComprarCreditosCommand), nameof(CancelarAssinaturaCommand))]
    public partial SituacaoDosPagamentos? Pagamentos { get; private set; }

    /// <summary>O primeiro clique em "Cancelar assinatura" pede a confirmação.</summary>
    [ObservableProperty]
    public partial bool ConfirmandoCancelamento { get; private set; }

    public bool MostraPagamentos => Pagamentos is { Disponivel: true };

    public bool PodeAssinarPlano => Pagamentos is { Disponivel: true, Assinando: false } p && (p.Plano != "profissional" || p.ProfissionalAte is not null);

    public bool TemAssinatura => Pagamentos is { Assinando: true };

    public bool EmModoDeTeste => Pagamentos is { Disponivel: true, ModoDeTeste: true };

    public const string AvisoDoModoDeTeste = "Pagamentos em modo de teste: nada é cobrado de verdade.";

    public string TextoDoAssinar => Pagamentos is { } p
        ? $"Assinar o Profissional: {Reais(p.PrecoMensal)}/mês ({p.CreditosDoProfissional.ToString("N0", Brasil)} créditos por mês)"
        : "";

    public string TextoDoPacote => Pagamentos is { } p ? $"Comprar {p.CreditosDoPacote.ToString("N0", Brasil)} créditos: {Reais(p.PrecoDoPacote)}" : "";

    /// <summary>"Assinatura ativa: próxima cobrança em 18/11/2026" ou "O Profissional vale até 18/11/2026 (assinatura cancelada)".</summary>
    public string TextoDoPlanoPago => Pagamentos switch
    {
        { ProfissionalAte: { } ate } => $"O Profissional vale até {Data(ate)} (assinatura cancelada).",
        { Assinando: true, ProximaCobranca: { } proxima } => $"Assinatura ativa: próxima cobrança em {Data(proxima)}.",
        { Assinando: true } => "Assinatura ativa.",
        { Assinatura: SituacaoDosPagamentos.Pendente } => "Assinatura esperando o pagamento. Depois de pagar, clique em Atualizar.",
        _ => "",
    };

    private static string Reais(decimal valor) => valor.ToString("C", Brasil);

    private static string Data(DateTime data) => data.ToLocalTime().ToString("dd/MM/yyyy", Brasil);

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
        (Senha, Codigo, NovaSenha, Plano, TextoDoUso, PercentualDoUso, Pagamentos, ConfirmandoCancelamento) = ("", "", "", "", "", 0, null, false);
        IrPara(PaginaDaConta.Entrar);
        Mostrar("Você saiu da conta neste computador.", erro: false);
    });

    [RelayCommand(CanExecute = nameof(PodeAssinar))]
    private Task Assinar() => Executar("Abrindo o pagamento no navegador…", async c =>
    {
        await _conta.AssinarAsync(c);
        Mostrar("Conclua a assinatura no navegador (Pix ou cartão). Depois de pagar, clique em Atualizar.", erro: false);
    });

    private bool PodeAssinar() => !Ocupado && PodeAssinarPlano;

    [RelayCommand(CanExecute = nameof(PodeComprar))]
    private Task ComprarCreditos() => Executar("Abrindo o pagamento no navegador…", async c =>
    {
        await _conta.ComprarCreditosAsync(c);
        Mostrar("Conclua a compra no navegador. Assim que o pagamento for aprovado, clique em Atualizar para ver os créditos.", erro: false);
    });

    private bool PodeComprar() => !Ocupado && MostraPagamentos;

    /// <summary>O primeiro passo: pede a confirmação.</summary>
    [RelayCommand(CanExecute = nameof(PodeCancelarAssinatura))]
    private void CancelarAssinatura()
    {
        ConfirmandoCancelamento = true;
        Mostrar("Cancelar a assinatura? O Profissional continua valendo até o fim do mês já pago, e nada mais é cobrado.", erro: false);
    }

    private bool PodeCancelarAssinatura() => !Ocupado && TemAssinatura;

    [RelayCommand(CanExecute = nameof(Livre))]
    private Task ConfirmarCancelamento() => Executar("Cancelando a assinatura…", async c =>
    {
        ConfirmandoCancelamento = false;
        var ate = await _conta.CancelarAssinaturaAsync(c);
        await AtualizarUsoSemMensagem(c);
        Mostrar(ate is { } data ? $"Assinatura cancelada. O Profissional vale até {Data(data)}." : "Assinatura cancelada.", erro: false);
    });

    [RelayCommand]
    private void ManterAssinatura()
    {
        ConfirmandoCancelamento = false;
        Mostrar("", erro: false);
    }

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
        await AtualizarUsoSemMensagem(c);
        Mostrar("", erro: false);
    });

    private async Task AtualizarUsoSemMensagem(CancellationToken c)
    {
        var uso = await _conta.UsoAsync(c);
        Plano = NomeDoPlano(uso.Perfil.Plano);
        PercentualDoUso = uso.CreditosDoPlano <= 0 ? 0 : Math.Min(100, (double)(100 * uso.CreditosUsadosNoMes / uso.CreditosDoPlano));
        TextoDoUso = $"{uso.CreditosUsadosNoMes.ToString("N0", Brasil)} de {uso.CreditosDoPlano.ToString("N0", Brasil)} créditos de IA usados " +
                     $"este mês ({PercentualDoUso.ToString("0.#", Brasil)}%). Eles voltam no dia 1º." +
                     (uso.CreditosAvulsos > 0 ? $" Mais {uso.CreditosAvulsos.ToString("N0", Brasil)} créditos avulsos, que não vencem." : "");
        OnPropertyChanged(nameof(NomeDaConta));
        try
        {
            Pagamentos = await _conta.PagamentosAsync(c);
        }
        catch (ErroDaContaException ex) when (!ex.SessaoRecusada)
        {
            Pagamentos = null; // um servidor sem pagamentos (ou fora do ar só nessa rota): a tela segue sem eles
        }
    }

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
