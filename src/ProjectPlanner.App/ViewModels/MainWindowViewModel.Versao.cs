using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 69: o aviso de versão nova (ou de versão que o servidor não aceita mais), comparando com o que o servidor anuncia.</summary>
public sealed record AvisoDeVersao(string Texto, bool Urgente, Uri? Download)
{
    /// <summary>O aviso para a versão <paramref name="atual"/>; nulo quando ela está em dia (ou o servidor não diz nada).</summary>
    public static AvisoDeVersao? Montar(string atual, VersaoNoServidor? servidor)
    {
        static Version? Ler(string? texto) => Version.TryParse(texto?.Trim(), out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
        if (servidor is null || Ler(atual) is not { } minha)
            return null;
        var download = Uri.TryCreate(servidor.Download?.Trim(), UriKind.Absolute, out var u) && u.Scheme is "https" or "http" ? u : null;
        var ultima = Ler(servidor.Ultima);
        if (Ler(servidor.Minima) is { } minima && minha < minima)
            return new AvisoDeVersao($"Esta versão ({atual}) não funciona mais com o servidor: atualize para a {(ultima ?? minima).ToString(3)}", true, download);
        if (ultima is not null && minha < ultima)
            return new AvisoDeVersao($"Versão nova: {ultima.ToString(3)}", false, download);
        return null;
    }
}

// Bloco 69: ao abrir, o programa pergunta ao servidor a última versão; mais velho, aparece o aviso na barra de status (com o
// botão para baixar, se o servidor diz onde).
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAvisoDeVersao), nameof(TextoDoAvisoDeVersao))]
    [NotifyCanExecuteChangedFor(nameof(BaixarVersaoNovaCommand))]
    public partial AvisoDeVersao? AvisoDeVersao { get; private set; }

    public bool TemAvisoDeVersao => AvisoDeVersao is not null;

    /// <summary>"Versão nova: 0.70.0 · Baixar"; o texto do botão na barra de status.</summary>
    public string TextoDoAvisoDeVersao => AvisoDeVersao is { } aviso ? aviso.Texto + (aviso.Download is null ? "" : " · Baixar") : "";

    /// <summary>Pergunta ao servidor (sem conta) e mostra o aviso; sem resposta, nada muda.</summary>
    public async Task VerificarVersaoAsync()
    {
        try
        {
            AvisoDeVersao = AvisoDeVersao.Montar(VersaoDoProduto.Atual, await _conta.VersaoNoServidorAsync(CancellationToken.None));
            if (AvisoDeVersao is { Urgente: true } urgente)
                Mensagem = urgente.Texto + ".";
        }
        catch (Exception)
        {
            // O aviso de versão nunca atrapalha a abertura.
        }
    }

    private bool PodeBaixarVersaoNova() => AvisoDeVersao?.Download is not null;

    [RelayCommand(CanExecute = nameof(PodeBaixarVersaoNova))]
    private Task BaixarVersaoNova() => AvisoDeVersao?.Download is { } endereco ? _dialogos.AbrirEnderecoAsync(endereco) : Task.CompletedTask;
}
