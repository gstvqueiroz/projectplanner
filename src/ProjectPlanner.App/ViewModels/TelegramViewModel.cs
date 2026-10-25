using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// Bloco 49: a tela Conta > Conectar o Telegram…: mostra se a conta está ligada a um chat, gera o código (com o link que abre
/// o bot já com ele) e desliga.
/// </summary>
public sealed partial class TelegramViewModel(ITelegramDaConta telegram, IDialogosService dialogos) : ViewModelBase
{
    public const string ComoUsar =
        "No Telegram, pergunte sobre a obra (\"quanto já medi?\"), mande atualizações (\"a alvenaria está em 60%\") e fotos. As " +
        "atualizações e as fotos ficam esperando no servidor: no programa, Conta > Buscar do Telegram mostra o cartão para " +
        "conferir e aplicar. O bot só enxerga as obras marcadas em Conta > Usar esta obra no Telegram.\n\n" +
        "Com a equipe: crie um grupo no Telegram para a obra, ponha o bot nele e mande /obra lá (você, que ligou o chat " +
        "privado). A equipe não precisa de conta: as fotos e as mensagens com #atualizacao vão para a caixa da obra, o bot " +
        "só responde quando é marcado, e os créditos são os seus. /papel muda o que cada um pode fazer.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaSituacao), nameof(PodeDesligar))]
    public partial SituacaoDoTelegram? Situacao { get; private set; }

    public string TextoDaSituacao => Situacao switch
    {
        null => "",
        { Disponivel: false } => "O Telegram ainda não está configurado no servidor do ProjectPlanner.",
        { Ligado: true, Nome: { } nome } => $"Ligado ao Telegram de {nome}.",
        { Ligado: true } => "Ligado ao Telegram.",
        _ => "Ainda não ligado. Gere o código e abra o Telegram.",
    };

    public bool PodeDesligar => Situacao?.Ligado == true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemCodigo))]
    public partial CodigoParaOTelegram? Codigo { get; private set; }

    public bool TemCodigo => Codigo is not null;

    public string InstrucaoDoCodigo => Codigo is null ? ""
        : Codigo.Link is not null
            ? $"Toque em \"Abrir o Telegram\" e depois em Iniciar. Se preferir, mande ao bot @{Codigo.Bot}: /start {Codigo.Codigo}. O código vale {Codigo.Minutos} minutos."
            : $"No Telegram, mande ao bot: /start {Codigo.Codigo}. O código vale {Codigo.Minutos} minutos.";

    [ObservableProperty]
    public partial string Mensagem { get; private set; } = "";

    [ObservableProperty]
    public partial bool MensagemEhErro { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GerarCodigoCommand), nameof(VerificarCommand), nameof(DesligarCommand), nameof(AbrirTelegramCommand))]
    public partial bool Ocupado { get; private set; }

    public Task AbrirAsync() => Executar(async c => Situacao = await telegram.SituacaoAsync(c));

    [RelayCommand(CanExecute = nameof(Livre))]
    private Task GerarCodigo() => Executar(async c =>
    {
        Codigo = await telegram.CodigoAsync(c);
        OnPropertyChanged(nameof(InstrucaoDoCodigo));
        AbrirTelegramCommand.NotifyCanExecuteChanged();
    });

    [RelayCommand(CanExecute = nameof(PodeAbrirTelegram))]
    private Task AbrirTelegram() => Codigo?.Link is { } link ? dialogos.AbrirEnderecoAsync(new Uri(link)) : Task.CompletedTask;

    private bool PodeAbrirTelegram() => !Ocupado && Codigo?.Link is not null;

    /// <summary>Depois de iniciar o bot no Telegram: confere se a conta ficou ligada.</summary>
    [RelayCommand(CanExecute = nameof(Livre))]
    private Task Verificar() => Executar(async c =>
    {
        Situacao = await telegram.SituacaoAsync(c);
        if (Situacao.Ligado)
            (Codigo, Mensagem, MensagemEhErro) = (null, "Pronto: o Telegram está ligado à sua conta.", false);
        else
            (Mensagem, MensagemEhErro) = ("Ainda não ligado: no Telegram, toque em Iniciar (ou mande /start e o código) e verifique de novo.", true);
    });

    [RelayCommand(CanExecute = nameof(Livre))]
    private Task Desligar() => Executar(async c =>
    {
        await telegram.DesligarAsync(c);
        Situacao = await telegram.SituacaoAsync(c);
        (Mensagem, MensagemEhErro) = ("O Telegram não está mais ligado à sua conta.", false);
    });

    private bool Livre() => !Ocupado;

    private async Task Executar(Func<CancellationToken, Task> acao)
    {
        Ocupado = true;
        (Mensagem, MensagemEhErro) = ("", false);
        try
        {
            await acao(CancellationToken.None);
        }
        catch (ErroDaContaException ex)
        {
            (Mensagem, MensagemEhErro) = (ex.Message, true);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
