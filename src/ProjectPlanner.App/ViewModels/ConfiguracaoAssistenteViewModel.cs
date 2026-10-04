using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// A tela Configurações do assistente (Bloco 23): colar a chave de API (que vai para o cofre do sistema), escolher o
/// modelo, testar a conexão e remover a chave. A chave digitada nunca é mostrada de volta: só o fim da que está em uso.
/// </summary>
public sealed partial class ConfiguracaoAssistenteViewModel : ViewModelBase
{
    public const string AvisoDePrivacidade =
        "Ao usar o assistente, o texto das mensagens, o cronograma aberto e os anexos são enviados ao Google (Gemini). " +
        "Na camada gratuita da API, o Google pode usar esses dados para melhorar os produtos dele: não envie o que não " +
        "pode sair da empresa.";

    private readonly IConfiguracaoAssistente _configuracao;
    private readonly IDialogosService _dialogos;
    private CancellationTokenSource? _teste;

    public ConfiguracaoAssistenteViewModel(IConfiguracaoAssistente configuracao, IDialogosService dialogos)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(dialogos);
        (_configuracao, _dialogos) = (configuracao, dialogos);
        Modelo = configuracao.Modelo;
    }

    public string NomeDoProvedor => _configuracao.NomeDoProvedor;

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
            Mostrar($"Conexão OK: a chave funciona com o modelo {(Modelo ?? "").Trim()}.", erro: false);
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

    private bool PodeTestar() => !Ocupado && (!string.IsNullOrWhiteSpace(ChaveDigitada) || TemChave);

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
