using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// O que uma resposta do assistente fez no cronograma (Bloco 24): aplicada (com Desfazer enquanto for a última),
/// recusada (com o motivo) ou cancelada pelo usuário.
/// </summary>
public sealed partial class AlteracoesNaConversa(ResultadoDaAplicacao resultado) : ObservableObject
{
    public ResultadoDaAplicacao Resultado { get; } = resultado;

    public bool Aplicadas => Resultado.Situacao == SituacaoDaAplicacao.Aplicada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Texto))]
    public partial bool Desfeitas { get; set; }

    [ObservableProperty]
    public partial bool PodeDesfazer { get; set; }

    public string Texto => Desfeitas
        ? $"Desfeito: {Resultado.Texto}"
        : Resultado.Situacao switch
        {
            SituacaoDaAplicacao.Aplicada when Resultado.Marca is null => "Nenhuma mudança no cronograma.",
            SituacaoDaAplicacao.Aplicada => $"Cronograma alterado: {Resultado.Texto}",
            SituacaoDaAplicacao.Recusada => $"Nada foi alterado. {Resultado.Texto}",
            _ => Resultado.Texto,
        };
}

/// <summary>Uma mensagem da conversa como o painel a mostra.</summary>
public sealed record MensagemNaTela(MensagemChat Mensagem, AlteracoesNaConversa? Alteracoes = null)
{
    public bool TemAlteracoes => Alteracoes is not null;

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
/// com a próxima mensagem, o envio e a espera pela resposta. Só conhece <see cref="IAssistenteIA"/>: com uma chave
/// configurada (Bloco 23), quem responde é o Gemini, recebendo também o retrato do cronograma aberto; sem chave, o
/// <see cref="AssistenteIAIndisponivel"/>.
/// </summary>
public sealed partial class AssistenteViewModel : ViewModelBase
{
    private readonly IAssistenteIA _assistente;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly IPreferenciasInterface _preferencias;
    private readonly IConfiguracaoAssistente _configuracao;
    private readonly ICronogramaDoAssistente _cronograma;
    private readonly bool _carregado;
    private CancellationTokenSource? _cancelamento;
    private int _conversa; // muda a cada limpeza: uma resposta atrasada de uma conversa limpa é descartada

    public AssistenteViewModel(IAssistenteIA assistente, IDialogosService dialogos, TimeProvider relogio,
        IPreferenciasInterface preferencias, IConfiguracaoAssistente configuracao, ICronogramaDoAssistente cronograma)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(cronograma);
        (_assistente, _dialogos, _relogio, _preferencias) = (assistente, dialogos, relogio, preferencias);
        (_configuracao, _cronograma) = (configuracao, cronograma);
        cronograma.DesfazerMudou += AtualizarDesfazer;
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

    /// <summary>"Gemini (Google) · gemini-2.5-flash" no cabeçalho, com uma chave configurada.</summary>
    public string DescricaoDoModelo => EstaConfigurado ? $"{_configuracao.NomeDoProvedor} · {_configuracao.Modelo}" : "";

    /// <summary>Abre as configurações do assistente (chave e modelo); o painel reflete a mudança na hora.</summary>
    [RelayCommand]
    private async Task Configurar()
    {
        await _dialogos.MostrarConfiguracaoAssistenteAsync(new ConfiguracaoAssistenteViewModel(_configuracao, _dialogos));
        OnPropertyChanged(nameof(EstaConfigurado));
        OnPropertyChanged(nameof(DescricaoDoModelo));
    }

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
        // Só o painel muda: o resto do interface.json (o tema) fica como está.
        if (_carregado)
            _preferencias.Salvar((_preferencias.Carregar() ?? new PreferenciasInterface(false)) with { MostrarAssistente = value });
    }

    [RelayCommand]
    private void AlternarVisibilidade() => Visivel = !Visivel;

    [RelayCommand]
    private void Fechar() => Visivel = false;

    private bool PodeEnviar() => !Aguardando && (!string.IsNullOrWhiteSpace(TextoDigitado) || AnexosPendentes.Count > 0);

    /// <summary>
    /// Manda o texto e os anexos pendentes ao assistente, com o histórico da conversa (sem os avisos locais) e o retrato
    /// do cronograma, e acrescenta a resposta. As alterações do cronograma que vierem nela são aplicadas (Bloco 24); se
    /// uma não valer, nada é aplicado e o assistente recebe o erro uma vez para corrigir. Uma falha ou um cancelamento
    /// vira um aviso na conversa, sem interromper o painel.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeEnviar))]
    private async Task Enviar()
    {
        var texto = TextoDigitado.Trim();
        var anexos = AnexosPendentes.ToList();
        var historico = Mensagens.Select(m => m.Mensagem).Where(m => !m.EhErro).ToList();
        var doUsuario = new MensagemChat(AutorMensagem.Usuario, texto, _relogio.GetLocalNow(), anexos);
        Mensagens.Add(new MensagemNaTela(doUsuario));
        TextoDigitado = "";
        AnexosPendentes.Clear();
        AvisoDeAnexo = "";

        var conversa = _conversa;
        using var cancelamento = _cancelamento = new CancellationTokenSource();
        Aguardando = true;
        MensagemChat resposta;
        AlteracoesNaConversa? alteracoes = null;
        try
        {
            var retrato = _cronograma.Retrato();
            var r = await _assistente.EnviarAsync(new PedidoIA(texto, anexos, historico, retrato), cancelamento.Token);
            if (r.Alteracoes.Count > 0 && conversa == _conversa)
            {
                var aplicacao = await _cronograma.AplicarAsync(r.Alteracoes, retrato);
                if (aplicacao.Situacao == SituacaoDaAplicacao.Recusada)
                {
                    // Nada mudou no cronograma: o assistente recebe o erro e manda a lista inteira de novo, corrigida.
                    var tentativa = historico.Append(doUsuario)
                        .Append(new MensagemChat(AutorMensagem.Assistente, r.Texto, _relogio.GetLocalNow(), [])).ToList();
                    var novoRetrato = _cronograma.Retrato();
                    RespostaIA? corrigida;
                    try
                    {
                        corrigida = await _assistente.EnviarAsync(
                            new PedidoIA(PedidoDeCorrecao(aplicacao.Texto), [], tentativa, novoRetrato), cancelamento.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        corrigida = null; // fica a primeira resposta, com o motivo da recusa
                    }
                    if (corrigida is { Alteracoes.Count: > 0 } && conversa == _conversa)
                    {
                        r = corrigida;
                        aplicacao = await _cronograma.AplicarAsync(corrigida.Alteracoes, novoRetrato);
                    }
                }
                alteracoes = new AlteracoesNaConversa(aplicacao)
                {
                    PodeDesfazer = aplicacao.Marca is { } marca && _cronograma.PodeDesfazer(marca),
                };
            }
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
            Mensagens.Add(new MensagemNaTela(resposta, resposta.EhErro ? null : alteracoes));
    }

    /// <summary>O pedido de correção mandado quando as alterações de uma resposta não puderam ser aplicadas.</summary>
    public static string PedidoDeCorrecao(string motivo) =>
        $"As alterações da sua resposta anterior não foram aplicadas, e o cronograma não mudou. Motivo: {motivo} " +
        "Corrija e mande de novo a lista completa de alterações (os Nº continuam os do retrato desta mensagem).";

    /// <summary>Volta o cronograma para como estava antes da resposta (só a última aplicação pode ser desfeita).</summary>
    [RelayCommand]
    private async Task Desfazer(AlteracoesNaConversa? alteracoes)
    {
        if (alteracoes?.Resultado.Marca is not { } marca)
            return;
        if (await _cronograma.DesfazerAsync(marca))
            alteracoes.Desfeitas = true;
        AtualizarDesfazer();
    }

    private void AtualizarDesfazer()
    {
        foreach (var alteracoes in Mensagens.Select(m => m.Alteracoes).OfType<AlteracoesNaConversa>())
            alteracoes.PodeDesfazer = !alteracoes.Desfeitas && alteracoes.Resultado.Marca is { } marca && _cronograma.PodeDesfazer(marca);
    }

    [RelayCommand(CanExecute = nameof(Aguardando))]
    private void Cancelar() => _cancelamento?.Cancel();

    /// <summary>
    /// Escolhe documentos e os põe na lista de pendentes (sem abrir o conteúdo; repetidos entram uma vez). Formatos que
    /// o assistente não lê e arquivos grandes demais ficam de fora, com o motivo (Bloco 23).
    /// </summary>
    [RelayCommand]
    private async Task Anexar()
    {
        var sumidos = new List<string>();
        var recusados = new List<string>();
        foreach (var caminho in await _dialogos.EscolherDocumentosAsync())
        {
            AnexoIA anexo;
            try
            {
                anexo = AnexoIA.DoArquivo(caminho);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                sumidos.Add(Path.GetFileName(caminho));
                continue;
            }
            if (AnexosAceitos.Problema(anexo) is { } problema)
                recusados.Add(problema);
            else if (!AnexosPendentes.Any(a => string.Equals(a.CaminhoLocal, anexo.CaminhoLocal, StringComparison.Ordinal)))
                AnexosPendentes.Add(anexo);
        }
        var avisos = recusados.ToList();
        if (sumidos.Count > 0)
            avisos.Insert(0, $"Não deu para anexar: {string.Join(", ", sumidos)}.");
        AvisoDeAnexo = string.Join("\n", avisos);
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
