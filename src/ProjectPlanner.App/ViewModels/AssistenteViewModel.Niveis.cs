using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de AssistenteViewModel.cs): o nível do modelo, o saldo, a conta e as configurações, no topo do painel.
public sealed partial class AssistenteViewModel
{
    // ---- Bloco 46: o nível do modelo e o saldo, no topo do painel ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarSeletorDeNivel))]
    public partial IReadOnlyList<OpcaoDeNivel> Niveis { get; private set; } = [];

    /// <summary>A troca rápida do nível (a mesma escolha das configurações); um nível fora do plano não é aceito.</summary>
    [ObservableProperty]
    public partial OpcaoDeNivel? NivelEscolhido { get; set; }

    private bool _mostrandoNiveis;
    private bool _carregandoNiveis;

    partial void OnNivelEscolhidoChanged(OpcaoDeNivel? oldValue, OpcaoDeNivel? newValue)
    {
        if (_mostrandoNiveis || newValue is null || newValue.Id == _configuracao.Modelo)
            return;
        if (!newValue.Habilitado)
        {
            AvisoDoNivel = $"O nível {newValue.Modelo.NomeDoNivel} ({newValue.Modelo.Nome}) é {newValue.Detalhe}.";
            _mostrandoNiveis = true;
            NivelEscolhido = oldValue;
            _mostrandoNiveis = false;
            return;
        }
        _configuracao.SalvarModelo(newValue.Id);
        AvisoDoNivel = "";
        OnPropertyChanged(nameof(DescricaoDoModelo));
    }

    /// <summary>Por que a troca de nível não valeu (fora do plano).</summary>
    [ObservableProperty]
    public partial string AvisoDoNivel { get; private set; } = "";

    public bool MostrarSeletorDeNivel => Niveis.Count > 0 && Conta.Conectada && !_configuracao.ChaveDeDesenvolvimento;

    /// <summary>"5.870 créditos" (o que resta no mês), depois de uma resposta ou ao entrar.</summary>
    public string TextoDoSaldo => Conta.CreditosRestantes is { } saldo && Conta.Conectada && !_configuracao.ChaveDeDesenvolvimento
        ? $"{saldo.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} créditos"
        : "";

    // Os níveis da conta: os que ela já tem; se ainda não tem (e o painel está aberto), busca no servidor.
    private void AtualizarNiveis()
    {
        OferecerAutomatico(); // Bloco 51
        _mostrandoNiveis = true;
        Niveis = Conta.Conectada ? OpcaoDeNivel.De(Conta.Modelos) : [];
        NivelEscolhido = OpcaoDeNivel.Escolhida(Niveis, _configuracao.Modelo);
        _mostrandoNiveis = false;
        if (NivelEscolhido is { } escolhido && escolhido.Id != _configuracao.Modelo)
            _configuracao.SalvarModelo(escolhido.Id); // o modelo salvo não é do plano (ou saiu da lista)
        OnPropertyChanged(nameof(MostrarSeletorDeNivel));
        if (Visivel && Conta.Conectada && Conta.Modelos.Count == 0 && !_carregandoNiveis && !_configuracao.ChaveDeDesenvolvimento)
            _ = CarregarNiveisAsync();
    }

    private async Task CarregarNiveisAsync()
    {
        _carregandoNiveis = true;
        try
        {
            await Conta.CarregarModelosAsync(CancellationToken.None); // a conta avisa (Mudou), e o painel se atualiza
        }
        catch (Exception ex) when (ex is IA.Conta.ErroDaContaException or ErroDoAssistenteException)
        {
            // sem servidor agora: o seletor aparece na próxima vez
        }
        finally
        {
            _carregandoNiveis = false;
        }
    }

    /// <summary>Sem conta: abre a tela para entrar ou criar a conta.</summary>
    [RelayCommand]
    private async Task EntrarNaConta()
    {
        await _dialogos.MostrarContaAsync(new ContaViewModel(Conta));
        AtualizarConfiguracao();
    }

    /// <summary>Bloco 45: a conta ProjectPlanner (a tela de configurações deixa escolher entre ela e a chave própria).</summary>
    internal IContaProjectPlanner Conta
    {
        get => _conta;
        set
        {
            _conta = value;
            AtualizarConfiguracao();
        }
    }

    private IContaProjectPlanner _conta = new SemContaProjectPlanner();

    /// <summary>Abre as configurações do assistente (chave e modelo); o painel reflete a mudança na hora.</summary>
    [RelayCommand]
    private async Task Configurar()
    {
        await _dialogos.MostrarConfiguracaoAssistenteAsync(new ConfiguracaoAssistenteViewModel(_configuracao, _dialogos, Conta));
        AtualizarConfiguracao();
    }

    /// <summary>A configuração mudou (a conta, o nível, o saldo): o cabeçalho do painel acompanha.</summary>
    internal void AtualizarConfiguracao()
    {
        OnPropertyChanged(nameof(EstaConfigurado));
        AtualizarNiveis();
        OnPropertyChanged(nameof(DescricaoDoModelo));
        OnPropertyChanged(nameof(TextoDoSaldo));
    }
}
