using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 45: o menu Conta (entrar, criar a conta, ver o plano, sair) e o nome da conta na barra de status.
public sealed partial class MainWindowViewModel
{
    private readonly IContaProjectPlanner _conta;
    private bool _contaConectadaNoTelegram;

    public bool ContaConectada => _conta.Conectada;

    /// <summary>O item do menu Conta: "Entrar ou criar conta…" ou, conectado, "Minha conta…".</summary>
    public string TextoDoMenuDaConta => _conta.Conectada ? "Minha conta…" : "Entrar ou criar conta…";

    /// <summary>"Conta: Ana" na barra de status; vazio sem conta.</summary>
    public string TextoDaContaNaBarra => _conta.Perfil is { } perfil ? $"Conta: {perfil.Nome}" : "";

    [RelayCommand]
    private Task Conta() => _dialogos.MostrarContaAsync(new ContaViewModel(_conta));

    [RelayCommand(CanExecute = nameof(ContaConectada))]
    private async Task SairDaConta()
    {
        if (!await _dialogos.ConfirmarAsync("Sair da conta",
                $"Sair da conta ProjectPlanner ({_conta.Perfil?.Email}) neste computador? O assistente pela conta para até você entrar de novo.",
                "Sair"))
            return;
        await _conta.SairAsync(CancellationToken.None);
        Mensagem = "Você saiu da conta ProjectPlanner.";
    }

    private void ContaMudou()
    {
        OnPropertyChanged(nameof(ContaConectada));
        OnPropertyChanged(nameof(TextoDoMenuDaConta));
        OnPropertyChanged(nameof(TextoDaContaNaBarra));
        SairDaContaCommand.NotifyCanExecuteChanged();
        Assistente.AtualizarConfiguracao();
        if (_telegram is not null && _conta.Conectada != _contaConectadaNoTelegram)
        {
            _contaConectadaNoTelegram = _conta.Conectada;
            _ = AtualizarTelegramAsync(); // Bloco 49: entrou ou saiu
        }
    }
}
