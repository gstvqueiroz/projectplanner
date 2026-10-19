using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma escolha da preferência de modo (Bloco 26), como a lista da tela a mostra.</summary>
public sealed record OpcaoDeModo(PreferenciaDeModo Valor, string Texto);

/// <summary>
/// A tela Configurações do assistente (Bloco 23): colar a chave de API (que vai para o cofre do sistema), escolher o
/// modelo, testar a conexão e remover a chave. A chave digitada nunca é mostrada de volta: só o fim da que está em uso.
/// Bloco 45: escolher entre a chave própria e a conta ProjectPlanner (e abrir a tela da conta para entrar).
/// </summary>
public sealed partial class ConfiguracaoAssistenteViewModel : ViewModelBase
{
    public const string AvisoDePrivacidade =
        "Ao usar o assistente, o texto das mensagens, o cronograma aberto e os anexos são enviados ao Google (Gemini). " +
        "Na camada gratuita da API, o Google pode usar esses dados para melhorar os produtos dele: não envie o que não " +
        "pode sair da empresa.";

    /// <summary>Bloco 45: o aviso quando a IA vai pela conta.</summary>
    public const string AvisoDaConta =
        "Ao usar o assistente pela conta, o texto das mensagens, o cronograma aberto e os anexos passam pelo servidor do " +
        "ProjectPlanner, que não guarda o conteúdo (só conta os tokens do seu plano), e vão ao Google (Gemini).";

    private readonly IConfiguracaoAssistente _configuracao;
    private readonly IDialogosService _dialogos;
    private readonly IContaProjectPlanner _conta;
    private CancellationTokenSource? _teste;

    public ConfiguracaoAssistenteViewModel(IConfiguracaoAssistente configuracao, IDialogosService dialogos, IContaProjectPlanner? conta = null)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(dialogos);
        (_configuracao, _dialogos, _conta) = (configuracao, dialogos, conta ?? new SemContaProjectPlanner());
        Modelo = configuracao.Modelo;
        UsarConta = configuracao.Provedor == ProvedorDoAssistente.Conta;
        ModoEscolhido = OpcoesDeModo.FirstOrDefault(o => o.Valor == configuracao.PreferenciaDeModo) ?? OpcoesDeModo[0];
        _pronta = true;
    }

    private readonly bool _pronta;

    /// <summary>O que fazer num pedido de cronograma (Bloco 26).</summary>
    public static IReadOnlyList<OpcaoDeModo> OpcoesDeModo { get; } =
    [
        new(PreferenciaDeModo.Perguntar, "Perguntar: montar agora ou estudar antes"),
        new(PreferenciaDeModo.Direto, "Montar o cronograma direto"),
        new(PreferenciaDeModo.Estudo, "Estudar os documentos e conversar antes"),
    ];

    /// <summary>A preferência de modo; muda na hora (não precisa de Salvar).</summary>
    [ObservableProperty]
    public partial OpcaoDeModo ModoEscolhido { get; set; }

    partial void OnModoEscolhidoChanged(OpcaoDeModo value)
    {
        if (!_pronta || value is null)
            return;
        _configuracao.SalvarPreferenciaDeModo(value.Valor);
        Mostrar($"Ao pedir um cronograma: {value.Texto.ToLowerInvariant()}.", erro: false);
    }

    public string NomeDoProvedor => _configuracao.NomeDoProvedor;

    // ---- Bloco 45: a chave própria ou a conta ProjectPlanner ----

    /// <summary>Usar a conta ProjectPlanner (senão, a chave própria); muda na hora (não precisa de Salvar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsarChavePropria), nameof(TextoDoProvedor), nameof(Aviso))]
    [NotifyCanExecuteChangedFor(nameof(TestarCommand))]
    public partial bool UsarConta { get; set; }

    public bool UsarChavePropria
    {
        get => !UsarConta;
        set => UsarConta = !value;
    }

    partial void OnUsarContaChanged(bool value)
    {
        if (!_pronta)
            return;
        _configuracao.SalvarProvedor(value ? ProvedorDoAssistente.Conta : ProvedorDoAssistente.ChavePropria);
        Mostrar(value
            ? _conta.Conectada
                ? "O assistente passa a usar a sua conta ProjectPlanner."
                : "O assistente vai usar a conta ProjectPlanner: entre na conta para ele funcionar."
            : TemChave
                ? "O assistente passa a usar a sua chave do Gemini."
                : "O assistente vai usar a sua chave do Gemini: cole a chave e salve.", erro: false);
    }

    public bool ContaConectada => _conta.Conectada;

    public string TextoDoProvedor => UsarConta ? "Conta ProjectPlanner (Gemini pelo servidor)" : NomeDoProvedor;

    /// <summary>Para onde vão os dados, conforme a escolha.</summary>
    public string Aviso => UsarConta ? AvisoDaConta : AvisoDePrivacidade;

    /// <summary>Quem entrou na conta, para a área da conta.</summary>
    public string SituacaoDaConta => _conta.Perfil is { } p
        ? $"Conectado como {p.Nome} ({p.Email}), plano {ContaViewModel.NomeDoPlano(p.Plano)}."
        : "Você não entrou na conta ProjectPlanner.";

    public string TextoDoBotaoDaConta => _conta.Conectada ? "Minha conta…" : "Entrar ou criar conta…";

    /// <summary>Abre a tela da conta (entrar, criar, ver o plano); na volta, a situação é atualizada.</summary>
    [RelayCommand]
    private async Task AbrirConta()
    {
        var tela = new ContaViewModel(_conta);
        await _dialogos.MostrarContaAsync(tela);
        OnPropertyChanged(nameof(ContaConectada));
        OnPropertyChanged(nameof(SituacaoDaConta));
        OnPropertyChanged(nameof(TextoDoBotaoDaConta));
        TestarCommand.NotifyCanExecuteChanged();
    }

    public string ModeloPadrao => _configuracao.ModeloPadrao;

    /// <summary>Onde a chave fica, para a dica abaixo do campo.</summary>
    public string DicaDaChave => _configuracao.CofreDisponivel
        ? $"A chave fica no cofre do sistema ({_configuracao.NomeDoCofre}), protegida pela sua conta. Ela nunca vai no " +
          "arquivo do projeto. Crie uma em aistudio.google.com (Get API key)."
        : "Este sistema não tem um cofre de senhas que o programa saiba usar: defina a variável de ambiente " +
          $"{ConfiguracaoDoAssistente.VariavelDeAmbiente}.";

    /// <summary>A chave em uso agora, sem mostrá-la.</summary>
    public string Situacao => _configuracao.Origem switch
    {
        OrigemDaChave.Cofre => $"Chave guardada no cofre do sistema ({_configuracao.NomeDoCofre}), terminada em {_configuracao.FinalDaChave}.",
        OrigemDaChave.VariavelDeAmbiente =>
            $"Usando a chave da variável de ambiente {ConfiguracaoDoAssistente.VariavelDeAmbiente} (terminada em " +
            $"{_configuracao.FinalDaChave}), só para desenvolvimento. Salve uma chave aqui para usar no programa publicado.",
        _ => "Nenhuma chave configurada: o assistente dá respostas simuladas.",
    };

    public bool TemChave => _configuracao.Origem != OrigemDaChave.Nenhuma;

    public string? ProblemaNoCofre => _configuracao.ProblemaNoCofre;

    public bool TemProblemaNoCofre => ProblemaNoCofre is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestarCommand))]
    public partial string ChaveDigitada { get; set; } = "";

    [ObservableProperty]
    public partial string Modelo { get; set; }

    /// <summary>O resultado da última ação (salvar, testar, remover).</summary>
    [ObservableProperty]
    public partial string Mensagem { get; private set; } = "";

    [ObservableProperty]
    public partial bool MensagemEhErro { get; private set; }

    /// <summary>Testando a conexão: os botões ficam travados.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SalvarCommand), nameof(TestarCommand), nameof(RemoverChaveCommand))]
    public partial bool Ocupado { get; private set; }

    /// <summary>Grava a chave digitada (se houver) no cofre e o modelo (se mudou).</summary>
    [RelayCommand(CanExecute = nameof(Livre))]
    private void Salvar()
    {
        var salvos = new List<string>();
        try
        {
            if (!string.Equals(Modelo?.Trim(), _configuracao.Modelo, StringComparison.Ordinal))
            {
                _configuracao.SalvarModelo(Modelo ?? "");
                Modelo = _configuracao.Modelo;
                salvos.Add("modelo");
            }
            if (!string.IsNullOrWhiteSpace(ChaveDigitada))
            {
                _configuracao.SalvarChave(ChaveDigitada);
                ChaveDigitada = "";
                salvos.Add("chave");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or CofreException)
        {
            Mostrar(ex.Message, erro: true);
            AtualizarSituacao();
            return;
        }
        Mostrar(salvos switch
        {
            [] => "Nada para salvar: cole uma chave ou mude o modelo.",
            ["modelo"] => $"Modelo salvo: {Modelo}.",
            ["chave"] => "Chave salva no cofre do sistema.",
            _ => "Modelo e chave salvos.",
        }, erro: false);
        AtualizarSituacao();
    }

    /// <summary>Confere a chave digitada (ou, sem ela, a que está em uso) com o modelo, sem gastar a cota de mensagens.</summary>
    [RelayCommand(CanExecute = nameof(PodeTestar))]
    private async Task Testar()
    {
        using var teste = _teste = new CancellationTokenSource();
        Ocupado = true;
        Mostrar("Testando a conexão…", erro: false);
        try
        {
            await _configuracao.TestarAsync(ChaveDigitada, Modelo ?? "", teste.Token);
            Mostrar(UsarConta && string.IsNullOrWhiteSpace(ChaveDigitada)
                ? $"Conexão OK: a conta ProjectPlanner funciona com o modelo {(Modelo ?? "").Trim()}."
                : $"Conexão OK: a chave funciona com o modelo {(Modelo ?? "").Trim()}.", erro: false);
        }
        catch (OperationCanceledException)
        {
            Mostrar("Teste cancelado.", erro: true);
        }
        catch (Exception ex) when (ex is ArgumentException or ErroDoAssistenteException)
        {
            Mostrar(ex.Message, erro: true);
        }
        finally
        {
            _teste = null;
            Ocupado = false;
        }
    }

    private bool PodeTestar() => !Ocupado && (!string.IsNullOrWhiteSpace(ChaveDigitada) || (UsarConta ? _conta.Conectada : TemChave));

    /// <summary>Apaga a chave do cofre, depois de confirmar.</summary>
    [RelayCommand(CanExecute = nameof(PodeRemover))]
    private async Task RemoverChave()
    {
        if (!await _dialogos.ConfirmarAsync("Remover a chave",
                "Remover a chave do Gemini do cofre do sistema? O assistente volta a dar respostas simuladas até você salvar outra.",
                "Remover"))
            return;
        try
        {
            _configuracao.RemoverChave();
            Mostrar("Chave removida do cofre do sistema.", erro: false);
        }
        catch (CofreException ex)
        {
            Mostrar(ex.Message, erro: true);
        }
        AtualizarSituacao();
    }

    private bool PodeRemover() => !Ocupado && _configuracao.Origem == OrigemDaChave.Cofre;

    [RelayCommand]
    private void RestaurarModeloPadrao() => Modelo = _configuracao.ModeloPadrao;

    /// <summary>A janela foi fechada: um teste em andamento é cancelado.</summary>
    public void Fechando() => _teste?.Cancel();

    private bool Livre() => !Ocupado;

    private void Mostrar(string mensagem, bool erro) => (Mensagem, MensagemEhErro) = (mensagem, erro);

    private void AtualizarSituacao()
    {
        OnPropertyChanged(nameof(Situacao));
        OnPropertyChanged(nameof(TemChave));
        OnPropertyChanged(nameof(ProblemaNoCofre));
        OnPropertyChanged(nameof(TemProblemaNoCofre));
        TestarCommand.NotifyCanExecuteChanged();
        RemoverChaveCommand.NotifyCanExecuteChanged();
    }
}
