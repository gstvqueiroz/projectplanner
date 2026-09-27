using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma mensagem da conversa como o painel a mostra.</summary>
public sealed record MensagemNaTela(MensagemChat Mensagem)
{
    public bool DoUsuario => Mensagem.Autor == AutorMensagem.Usuario;
    public bool EhErro => Mensagem.EhErro;
    public string Texto => Mensagem.Texto;
    public bool TemTexto => !string.IsNullOrWhiteSpace(Mensagem.Texto);
    public string Hora => Mensagem.DataHora.ToString("HH:mm");
    public IReadOnlyList<AnexoIA> Anexos => Mensagem.Anexos;
    public bool TemAnexos => Mensagem.Anexos.Count > 0;
}

/// <summary>
/// O painel lateral do assistente de IA (Bloco 19): a conversa (só em memória, da sessão), os anexos que vão
/// com a próxima mensagem, o envio e a espera pela resposta. Só conhece <see cref="IAssistenteIA"/>; hoje
/// quem responde é <see cref="AssistenteIAIndisponivel"/>, sem nenhuma chamada de rede.
/// </summary>
public sealed partial class AssistenteViewModel : ViewModelBase
{
    private readonly IAssistenteIA _assistente;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly IPreferenciasInterface _preferencias;
    private readonly bool _carregado;
    private CancellationTokenSource? _cancelamento;
    private int _conversa; // muda a cada limpeza: uma resposta atrasada de uma conversa limpa é descartada

    public AssistenteViewModel(IAssistenteIA assistente, IDialogosService dialogos, TimeProvider relogio,
        IPreferenciasInterface preferencias)
    {
        (_assistente, _dialogos, _relogio, _preferencias) = (assistente, dialogos, relogio, preferencias);
        Visivel = preferencias.Carregar()?.MostrarAssistente ?? false; // oculto na primeira vez (D7)
        _carregado = true;
        Mensagens.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ConversaVazia));
        AnexosPendentes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TemAnexosPendentes));
            EnviarCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>A conversa, da mais antiga para a mais recente.</summary>
    public ObservableCollection<MensagemNaTela> Mensagens { get; } = [];

    /// <summary>Os documentos que vão com a próxima mensagem; esvaziada a cada envio (D2).</summary>
    public ObservableCollection<AnexoIA> AnexosPendentes { get; } = [];

    public bool ConversaVazia => Mensagens.Count == 0;

    public bool TemAnexosPendentes => AnexosPendentes.Count > 0;

    /// <summary>Falso enquanto não há chave de API: o painel mostra que as respostas são simuladas.</summary>
    public bool EstaConfigurado => _assistente.EstaConfigurado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    public partial string TextoDigitado { get; set; } = "";

    /// <summary>Esperando a resposta: o painel mostra "Assistente digitando…" e Enviar vira Cancelar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelarCommand))]
    public partial bool Aguardando { get; private set; }

    /// <summary>Aviso sobre um anexo que não deu para incluir (arquivo sumiu, sem permissão).</summary>
    [ObservableProperty]
    public partial string AvisoDeAnexo { get; private set; } = "";

    /// <summary>Se o painel aparece. Fica salvo entre as sessões (interface.json).</summary>
    [ObservableProperty]
    public partial bool Visivel { get; set; }

    partial void OnVisivelChanged(bool value)
    {
        if (_carregado)
            _preferencias.Salvar(new PreferenciasInterface(value));
    }

    [RelayCommand]
    private void AlternarVisibilidade() => Visivel = !Visivel;

    [RelayCommand]
    private void Fechar() => Visivel = false;

    private bool PodeEnviar() => !Aguardando && (!string.IsNullOrWhiteSpace(TextoDigitado) || AnexosPendentes.Count > 0);

    /// <summary>
    /// Manda o texto e os anexos pendentes ao assistente, com o histórico da conversa (sem os avisos locais), e
    /// acrescenta a resposta. Uma falha ou um cancelamento vira um aviso na conversa, sem interromper o painel.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeEnviar))]
    private async Task Enviar()
    {
        var texto = TextoDigitado.Trim();
        var anexos = AnexosPendentes.ToList();
        var historico = Mensagens.Select(m => m.Mensagem).Where(m => !m.EhErro).ToList();
        Mensagens.Add(new MensagemNaTela(new MensagemChat(AutorMensagem.Usuario, texto, _relogio.GetLocalNow(), anexos)));
        TextoDigitado = "";
        AnexosPendentes.Clear();
        AvisoDeAnexo = "";

        var conversa = _conversa;
        using var cancelamento = _cancelamento = new CancellationTokenSource();
        Aguardando = true;
        MensagemChat resposta;
        try
        {
            var r = await _assistente.EnviarAsync(new PedidoIA(texto, anexos, historico), cancelamento.Token);
            resposta = new MensagemChat(AutorMensagem.Assistente, r.Texto, _relogio.GetLocalNow(), []);
        }
        catch (OperationCanceledException)
        {
            resposta = Aviso("Resposta cancelada.");
        }
        catch (Exception ex)
        {
            resposta = Aviso($"Não foi possível obter a resposta: {ex.Message}");
        }
        finally
        {
            _cancelamento = null;
            Aguardando = false;
        }
        if (conversa == _conversa)
            Mensagens.Add(new MensagemNaTela(resposta));
    }

    [RelayCommand(CanExecute = nameof(Aguardando))]
    private void Cancelar() => _cancelamento?.Cancel();

    /// <summary>Escolhe documentos e os põe na lista de pendentes (sem abrir o conteúdo; repetidos entram uma vez).</summary>
    [RelayCommand]
    private async Task Anexar()
    {
        var problemas = new List<string>();
        foreach (var caminho in await _dialogos.EscolherDocumentosAsync())
        {
            try
            {
                var anexo = AnexoIA.DoArquivo(caminho);
                if (!AnexosPendentes.Any(a => string.Equals(a.CaminhoLocal, anexo.CaminhoLocal, StringComparison.Ordinal)))
                    AnexosPendentes.Add(anexo);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                problemas.Add(Path.GetFileName(caminho));
            }
        }
        AvisoDeAnexo = problemas.Count == 0 ? "" : $"Não deu para anexar: {string.Join(", ", problemas)}.";
    }

    [RelayCommand]
    private void RemoverAnexo(AnexoIA anexo) => AnexosPendentes.Remove(anexo);

    /// <summary>Esvazia a conversa (e cancela uma resposta que ainda não chegou).</summary>
    [RelayCommand]
    private void LimparConversa()
    {
        _conversa++;
        _cancelamento?.Cancel();
        Mensagens.Clear();
    }

    /// <summary>Outro projeto foi aberto (Novo, Abrir): a conversa era sobre o anterior e recomeça do zero (D4).</summary>
    public void RecomecarParaOutroProjeto()
    {
        LimparConversa();
        AnexosPendentes.Clear();
        TextoDigitado = "";
        AvisoDeAnexo = "";
    }

    private MensagemChat Aviso(string texto) => new(AutorMensagem.Assistente, texto, _relogio.GetLocalNow(), [], EhErro: true);
}
