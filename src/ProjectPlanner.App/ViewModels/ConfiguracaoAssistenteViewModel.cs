using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma escolha da preferência de modo (Bloco 26), como a lista da tela a mostra.</summary>
public sealed record OpcaoDeModo(PreferenciaDeModo Valor, string Texto);

/// <summary>
/// A tela Configurações do assistente (Blocos 23 e 46): a conta ProjectPlanner (entrar, ver quem está conectado), o nível do
/// modelo (Padrão, Avançado, Máximo, com o gasto em créditos), o modo ao pedir um cronograma e o teste da conexão. Desde o
/// Bloco 46 não há chave própria na tela: só a variável <see cref="ConfiguracaoDoAssistente.VariavelDeAmbiente"/>, para quem
/// compila o código.
/// </summary>
public sealed partial class ConfiguracaoAssistenteViewModel : ViewModelBase
{
    public const string AvisoDePrivacidade =
        "Ao usar o assistente, o texto das mensagens, o cronograma aberto e os anexos passam pelo servidor do ProjectPlanner, " +
        "que não guarda o conteúdo (só conta os créditos do seu plano), e vão ao Google (Gemini) ou à Anthropic (Claude), " +
        "conforme o nível escolhido.";

    private readonly IConfiguracaoAssistente _configuracao;
    private readonly IDialogosService _dialogos;
    private readonly IContaProjectPlanner _conta;
    private readonly bool _pronta;
    private CancellationTokenSource? _pedido;

    public ConfiguracaoAssistenteViewModel(IConfiguracaoAssistente configuracao, IDialogosService dialogos, IContaProjectPlanner? conta = null)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(dialogos);
        (_configuracao, _dialogos, _conta) = (configuracao, dialogos, conta ?? new SemContaProjectPlanner());
        ModoEscolhido = OpcoesDeModo.FirstOrDefault(o => o.Valor == configuracao.PreferenciaDeModo) ?? OpcoesDeModo[0];
        MostrarNiveis(_conta.Modelos);
        _pronta = true;
    }

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

    // ---- A conta ----

    public bool ContaConectada => _conta.Conectada;

    public string SituacaoDaConta => _conta.Perfil is { } p
        ? $"Conectado como {p.Nome} ({p.Email}), plano {ContaViewModel.NomeDoPlano(p.Plano)}."
        : "Entre na sua conta ProjectPlanner para usar o assistente (o plano Grátis tem 300 créditos por mês).";

    public string TextoDoBotaoDaConta => _conta.Conectada ? "Minha conta…" : "Entrar ou criar conta…";

    /// <summary>Quem compila o código e definiu GEMINI_API_KEY: a IA vai direto ao Google, sem a conta.</summary>
    public bool ChaveDeDesenvolvimento => _configuracao.ChaveDeDesenvolvimento;

    public string TextoDaChaveDeDesenvolvimento =>
        $"Usando a chave da variável de ambiente {ConfiguracaoDoAssistente.VariavelDeAmbiente} (desenvolvimento): a IA vai direto ao " +
        $"Google, sem a conta, com o modelo {_configuracao.Modelo}.";

    /// <summary>Abre a tela da conta; na volta, a situação e os níveis são atualizados.</summary>
    [RelayCommand]
    private async Task AbrirConta()
    {
        await _dialogos.MostrarContaAsync(new ContaViewModel(_conta));
        OnPropertyChanged(nameof(ContaConectada));
        OnPropertyChanged(nameof(SituacaoDaConta));
        OnPropertyChanged(nameof(TextoDoBotaoDaConta));
        TestarCommand.NotifyCanExecuteChanged();
        await CarregarAsync();
    }

    // ---- O nível do modelo ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemNiveis))]
    public partial IReadOnlyList<OpcaoDeNivel> Niveis { get; private set; } = [];

    public bool TemNiveis => Niveis.Count > 0;

    /// <summary>O nível escolhido; muda na hora (não precisa de Salvar). Um nível fora do plano não é aceito.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestarCommand))]
    public partial OpcaoDeNivel? NivelEscolhido { get; set; }

    partial void OnNivelEscolhidoChanged(OpcaoDeNivel? oldValue, OpcaoDeNivel? newValue)
    {
        if (!_pronta || newValue is null || _mostrando)
            return;
        if (!newValue.Habilitado)
        {
            Mostrar($"O nível {newValue.Modelo.NomeDoNivel} ({newValue.Modelo.Nome}) é {newValue.Detalhe}.", erro: true);
            _mostrando = true;
            NivelEscolhido = oldValue; // fica o que estava
            _mostrando = false;
            return;
        }
        _configuracao.SalvarModelo(newValue.Id);
        Mostrar($"O assistente passa a usar o nível {newValue.Modelo.NomeDoNivel} ({newValue.Modelo.Nome}), {newValue.Detalhe}.", erro: false);
    }

    private bool _mostrando;

    /// <summary>Ao abrir a tela (e depois de entrar): busca os níveis da conta no servidor.</summary>
    public async Task CarregarAsync()
    {
        if (!_conta.Conectada)
        {
            MostrarNiveis([]);
            return;
        }
        using var pedido = _pedido = new CancellationTokenSource();
        try
        {
            MostrarNiveis(await _conta.CarregarModelosAsync(pedido.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is ErroDaContaException or ErroDoAssistenteException)
        {
            Mostrar(ex.Message, erro: true);
        }
        finally
        {
            _pedido = null;
        }
    }

    private void MostrarNiveis(IReadOnlyList<ModeloDaConta> modelos)
    {
        _mostrando = true;
        Niveis = OpcaoDeNivel.De(modelos);
        NivelEscolhido = OpcaoDeNivel.Escolhida(Niveis, _configuracao.Modelo);
        _mostrando = false;
        if (NivelEscolhido is { } escolhido && escolhido.Id != _configuracao.Modelo)
            _configuracao.SalvarModelo(escolhido.Id); // o modelo salvo saiu da lista ou não é do plano: volta ao que vale
    }

    // ---- Testar ----

    [ObservableProperty]
    public partial string Mensagem { get; private set; } = "";

    [ObservableProperty]
    public partial bool MensagemEhErro { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestarCommand))]
    public partial bool Ocupado { get; private set; }

    /// <summary>Confere a conta com o nível escolhido, sem gastar créditos.</summary>
    [RelayCommand(CanExecute = nameof(PodeTestar))]
    private async Task Testar()
    {
        var modelo = NivelEscolhido?.Id ?? _configuracao.Modelo;
        using var teste = _pedido = new CancellationTokenSource();
        Ocupado = true;
        Mostrar("Testando a conexão…", erro: false);
        try
        {
            await _configuracao.TestarAsync(modelo, teste.Token);
            Mostrar(NivelEscolhido is { } nivel
                ? $"Conexão OK: a conta funciona com o nível {nivel.Modelo.NomeDoNivel} ({nivel.Modelo.Nome})."
                : $"Conexão OK com o modelo {modelo}.", erro: false);
        }
        catch (OperationCanceledException)
        {
            Mostrar("Teste cancelado.", erro: true);
        }
        catch (Exception ex) when (ex is ArgumentException or ErroDoAssistenteException or ErroDaContaException)
        {
            Mostrar(ex.Message, erro: true);
        }
        finally
        {
            _pedido = null;
            Ocupado = false;
        }
    }

    private bool PodeTestar() => !Ocupado && (_conta.Conectada || _configuracao.ChaveDeDesenvolvimento);

    /// <summary>A janela foi fechada: um teste ou uma busca em andamento é cancelado.</summary>
    public void Fechando() => _pedido?.Cancel();

    private void Mostrar(string mensagem, bool erro) => (Mensagem, MensagemEhErro) = (mensagem, erro);
}
