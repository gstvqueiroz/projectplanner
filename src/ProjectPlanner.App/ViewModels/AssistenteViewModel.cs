using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core.Assistente;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// O que uma resposta do assistente fez no cronograma (Bloco 24): aplicada (com Desfazer enquanto for a última),
/// recusada (com o motivo) ou cancelada pelo usuário.
/// </summary>
public sealed partial class AlteracoesNaConversa(ResultadoDaAplicacao resultado) : ObservableObject
{
    private string? _textoSalvo;

    public ResultadoDaAplicacao Resultado { get; } = resultado;

    public bool Aplicadas => Resultado.Situacao == SituacaoDaAplicacao.Aplicada;

    /// <summary>"Cronograma alterado" (Bloco 24) ou "Obra atualizada" (o cartão do Bloco 41).</summary>
    public string Prefixo { get; init; } = "Cronograma alterado";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Texto))]
    public partial bool Desfeitas { get; set; }

    [ObservableProperty]
    public partial bool PodeDesfazer { get; set; }

    public string Texto => _textoSalvo ?? (Desfeitas
        ? $"Desfeito: {Resultado.Texto}"
        : Resultado.Situacao switch
        {
            SituacaoDaAplicacao.Aplicada when Resultado.Marca is null => "Nenhuma mudança no cronograma.",
            SituacaoDaAplicacao.Aplicada => $"{Prefixo}: {Resultado.Texto}",
            SituacaoDaAplicacao.Recusada => $"Nada foi alterado. {Resultado.Texto}",
            _ => Resultado.Texto,
        });

    /// <summary>Bloco 26: o resultado guardado no projeto, de uma sessão anterior (sem Desfazer), com o texto como estava.</summary>
    public static AlteracoesNaConversa Salvas(string texto, bool aplicadas) =>
        new(new ResultadoDaAplicacao(aplicadas ? SituacaoDaAplicacao.Aplicada : SituacaoDaAplicacao.Recusada, texto)) { _textoSalvo = texto };
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

    // Bloco 26: a escolha do modo, as perguntas e o plano.
    public TipoDeResposta Tipo => Mensagem.Resposta?.Tipo ?? TipoDeResposta.Resposta;
    public bool EscolheModo => Tipo == TipoDeResposta.EscolherModo;
    public IReadOnlyList<PerguntaNaTela> Perguntas { get; } = Mensagem.Resposta?.Tipo == TipoDeResposta.Perguntas
        ? [.. Mensagem.Resposta.Perguntas.Select(p => new PerguntaNaTela(p))]
        : [];
    public bool TemPerguntas => Perguntas.Count > 0;
    public PlanoNaTela? Plano { get; } = Mensagem.Resposta is { Tipo: TipoDeResposta.Plano, Plano: { } plano } ? new PlanoNaTela(plano) : null;
    public bool TemPlano => Plano is not null;

    /// <summary>Tem botões (o cartão do modo, as perguntas ou o plano): o balão ocupa a largura do painel.</summary>
    public bool Interativa => EscolheModo || TemPerguntas || TemPlano || Cartao is not null;

    /// <summary>A posição desta mensagem na conversa guardada no projeto; nula num aviso local.</summary>
    public int? IndiceNaConversa { get; init; }

    /// <summary>Bloco 41: as atualizações da obra propostas nesta resposta, para aplicar ou descartar.</summary>
    public CartaoDeAtualizacoes? Cartao { get; init; }

    public bool TemCartao => Cartao is not null;

    /// <summary>Bloco 41: a tela que o assistente abriu (o botão para abrir de novo).</summary>
    public TelaParaAbrir? Tela { get; init; }

    public bool TemTela => Tela is not null;
}

/// <summary>
/// O painel lateral do assistente de IA (Bloco 19): a conversa, os anexos que vão com a próxima mensagem, o envio e a
/// espera pela resposta. Só conhece <see cref="IAssistenteIA"/>: com uma chave configurada (Bloco 23), quem responde é o
/// Gemini, recebendo também o retrato do cronograma aberto; sem chave, o <see cref="AssistenteIAIndisponivel"/>.
/// </summary>
/// <remarks>
/// Bloco 26: a conversa e a ficha são do projeto (<see cref="ICronogramaDoAssistente.Conversa"/>) e ficam no .pplan. Num
/// pedido de cronograma, o assistente pergunta como trabalhar (cartão "Montar agora" / "Estudar e conversar antes"); no
/// estudo, faz perguntas com respostas para clicar e apresenta o plano para aprovar antes de montar. A ficha do projeto
/// (o que já foi definido) aparece no topo do painel, editável, e vai com cada pedido.
/// </remarks>
public sealed partial class AssistenteViewModel : ViewModelBase
{
    public const string TextoMontarAgora = "Pode montar agora, com o que já tem.";
    public const string TextoEstudarAntes = "Prefiro estudar e conversar antes de montar.";
    public const string TextoPlanoAprovado = "Plano aprovado: pode montar o cronograma completo.";
    public const string TextoPularParaOPlano = "Pode parar de perguntar: mostre o plano com o que já tem.";
    public const string PrefixoDoAjuste = "Ajuste no plano: ";

    private readonly IAssistenteIA _assistente;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly IPreferenciasInterface _preferencias;
    private readonly IConfiguracaoAssistente _configuracao;
    private readonly ICronogramaDoAssistente _cronograma;
    private readonly bool _carregado;
    private CancellationTokenSource? _cancelamento;
    private int _geracao; // muda a cada limpeza: uma resposta atrasada de uma conversa limpa é descartada
    private ConversaDoProjeto _conversa;

    public AssistenteViewModel(IAssistenteIA assistente, IDialogosService dialogos, TimeProvider relogio,
        IPreferenciasInterface preferencias, IConfiguracaoAssistente configuracao, ICronogramaDoAssistente cronograma)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(cronograma);
        (_assistente, _dialogos, _relogio, _preferencias) = (assistente, dialogos, relogio, preferencias);
        (_configuracao, _cronograma) = (configuracao, cronograma);
        // O projeto ainda está sendo montado: a conversa dele chega em RecomecarParaOutroProjeto, ao carregar.
        _conversa = new ConversaDoProjeto();
        cronograma.DesfazerMudou += AtualizarDesfazer;
        Visivel = preferencias.Carregar()?.MostrarAssistente ?? false; // oculto na primeira vez (D7)
        _carregado = true;
        Mensagens.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ConversaVazia));
            AtualizarInteracoes();
        };
        AnexosPendentes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TemAnexosPendentes));
            EnviarCommand.NotifyCanExecuteChanged();
        };
        Ficha.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TemFicha));
            OnPropertyChanged(nameof(TituloDaFicha));
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
    public string DescricaoDoModelo => !EstaConfigurado ? ""
        : _configuracao.ChaveDeDesenvolvimento ? $"Gemini (GEMINI_API_KEY) · {_configuracao.Modelo}"
        : $"Conta ProjectPlanner · {Conta.Modelos.FirstOrDefault(m => m.Id == _configuracao.Modelo)?.Nome ?? _configuracao.Modelo}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResponderPerguntasCommand))]
    public partial string TextoDigitado { get; set; } = "";

    /// <summary>Esperando a resposta: o painel mostra "Assistente digitando…" e Enviar vira Cancelar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelarCommand))]
    public partial bool Aguardando { get; private set; }

    partial void OnAguardandoChanged(bool value)
    {
        AtualizarInteracoes();
        RefazerMelhorCommand.NotifyCanExecuteChanged();
        EstimarOrcamentoCommand.NotifyCanExecuteChanged();
    }

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
        if (value && _carregado)
            AtualizarNiveis(); // Bloco 46: ao abrir o painel, os níveis da conta
    }

    [RelayCommand]
    private void AlternarVisibilidade() => Visivel = !Visivel;

    [RelayCommand]
    private void Fechar() => Visivel = false;

    private bool PodeEnviar() => !Aguardando && (!string.IsNullOrWhiteSpace(TextoDigitado) || AnexosPendentes.Count > 0);

    /// <summary>
    /// Manda o texto e os anexos pendentes ao assistente, com o histórico da conversa (sem os avisos locais), o retrato
    /// do cronograma e a condução da conversa (Bloco 26), e acrescenta a resposta. As alterações do cronograma que vierem
    /// nela são aplicadas (Bloco 24); se uma não valer, nada é aplicado e o assistente recebe o erro uma vez para corrigir.
    /// Uma falha ou um cancelamento vira um aviso na conversa, sem interromper o painel.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeEnviar))]
    private Task Enviar()
    {
        var (texto, anexos) = (TextoDigitado.Trim(), AnexosPendentes.ToList());
        TextoDigitado = "";
        AnexosPendentes.Clear();
        AvisoDeAnexo = "";
        return EnviarAsync(texto, anexos);
    }

    /// <param name="modelo">Bloco 51: o modelo deste envio ("Refazer com um modelo melhor"); nulo, o Automático escolhe ou vale o configurado.</param>
    private async Task EnviarAsync(string texto, IReadOnlyList<AnexoIA> anexos, bool planoAgora = false, string? modelo = null)
    {
        ModeloMelhor = null;
        PodeEstimarOrcamento = false;
        var criadas = 0;
        var historico = Mensagens.Select(m => m.Mensagem).Where(m => !m.EhErro).ToList();
        var doUsuario = new MensagemChat(AutorMensagem.Usuario, texto, _relogio.GetLocalNow(), anexos);
        Acrescentar(doUsuario);

        var geracao = _geracao;
        using var cancelamento = _cancelamento = new CancellationTokenSource();
        Aguardando = true;
        MensagemChat resposta;
        AlteracoesNaConversa? alteracoes = null;
        string? pedidoDeOrcamento = null;
        CartaoDeAtualizacoes? cartao = null;
        TelaParaAbrir? tela = null;
        string? importarFotos = null;
        string? escolha = null;
        try
        {
            var retrato = _cronograma.Retrato();
            var montagem = planoAgora || texto is TextoMontarAgora or TextoPlanoAprovado or TextoPularParaOPlano; // 0.55.1
            escolha = modelo ?? await EscolherModeloAsync(texto, anexos, historico, retrato, montagem, cancelamento.Token); // Bloco 51
            var r = await _assistente.EnviarAsync(new PedidoIA(texto, anexos, historico, retrato, Conducao(planoAgora)) { Modelo = escolha },
                cancelamento.Token);
            if (r.Alteracoes.Count > 0 && geracao == _geracao)
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
                            new PedidoIA(PedidoDeCorrecao(aplicacao.Texto), [], tentativa, novoRetrato, Conducao(planoAgora: false)) { Modelo = escolha },
                            cancelamento.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        corrigida = null; // fica a primeira resposta, com o motivo da recusa
                    }
                    if (corrigida is { Alteracoes.Count: > 0 } && geracao == _geracao)
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
            if (geracao == _geracao)
            {
                criadas = TarefasCriadas(r, alteracoes); // Bloco 54
                Conduzir(r, alteracoes);
                pedidoDeOrcamento = r.PedidoDeOrcamento;
                // Bloco 41: as atualizações da obra viram um cartão (conferido pelo retrato desta resposta).
                if (r.Atualizacoes.Count > 0)
                    cartao = new CartaoDeAtualizacoes(_cronograma.ConferirAtualizacoes(r.Atualizacoes, _cronograma.Retrato()));
                tela = r.Abrir;
                importarFotos = r.ImportarFotos;
            }
            resposta = new MensagemChat(AutorMensagem.Assistente, r.Texto, _relogio.GetLocalNow(), [], Resposta: r.Estruturada ? r : null);
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
        if (geracao == _geracao)
        {
            Acrescentar(resposta, resposta.EhErro ? null : alteracoes, resposta.EhErro ? null : cartao, resposta.EhErro ? null : tela);
            // Bloco 51: uma resposta que não mexeu na obra pode ser refeita com um modelo melhor.
            OferecerRefazer(texto, anexos, escolha, !resposta.EhErro && alteracoes is null && cartao is null && tela is null
                && pedidoDeOrcamento is null && importarFotos is null && (resposta.Resposta is null || resposta.Resposta.Tipo == TipoDeResposta.Resposta));
            // Bloco 54: montou um cronograma (e não pediu o orçamento junto): oferece estimar o orçamento e os recursos.
            PodeEstimarOrcamento = !resposta.EhErro && criadas >= TarefasParaOferecerOrcamento && pedidoDeOrcamento is null;
        }
        // Bloco 41: a tela que o usuário pediu para ver.
        if (tela is not null && !resposta.EhErro && geracao == _geracao)
            await _cronograma.AbrirTelaAsync(tela);
        // Bloco 42: as fotos anexadas a esta mensagem (ou as da pasta da obra) vão para a importação, com a IA classificando.
        if (importarFotos is not null && !resposta.EhErro && geracao == _geracao)
        {
            var fotos = anexos.Select(a => a.CaminhoLocal).Where(c => ViewModels.FotosDoProjetoViewModel.ExtensoesDeFoto.Contains(Path.GetExtension(c).ToLowerInvariant())).ToList();
            if (importarFotos == OrigensDasFotos.Pasta || fotos.Count > 0)
                await _cronograma.ImportarFotosAsync(importarFotos, fotos);
        }
        // Bloco 38: o assistente pediu sugestões de orçamento: elas aparecem em Projeto > Orçamento…, para o usuário aceitar.
        if (pedidoDeOrcamento is not null && !resposta.EhErro && geracao == _geracao)
            await _cronograma.AbrirSugestoesDeOrcamentoAsync(pedidoDeOrcamento);
    }

    /// <summary>O pedido de correção mandado quando as alterações de uma resposta não puderam ser aplicadas.</summary>
    public static string PedidoDeCorrecao(string motivo) =>
        $"As alterações da sua resposta anterior não foram aplicadas, e o cronograma não mudou. Motivo: {motivo} " +
        "Corrija e mande de novo a lista completa de alterações (os Nº continuam os do retrato desta mensagem).";

    /// <summary>
    /// Volta o cronograma para como estava antes da resposta. Bloco 30: vale para qualquer resposta ainda no histórico do
    /// Desfazer (as alterações feitas depois dela também são desfeitas, perguntando antes), e o Refazer traz de volta.
    /// </summary>
    [RelayCommand]
    private async Task Desfazer(AlteracoesNaConversa? alteracoes)
    {
        if (alteracoes?.Resultado.Marca is not { } marca)
            return;
        await _cronograma.DesfazerAsync(marca);
        AtualizarDesfazer();
    }

    // Cada resposta aplicada mostra se está feita (com o Desfazer) ou desfeita, pelo histórico do Desfazer: vale também o
    // Desfazer e o Refazer gerais (Cmd/Ctrl+Z). Uma resposta que saiu do histórico continua como estava.
    private void AtualizarDesfazer()
    {
        foreach (var aplicacao in Mensagens.Select(m => m.Cartao?.Aplicacao).OfType<AlteracoesNaConversa>().Where(a => a.Resultado.Marca is not null))
        {
            var marcaDoCartao = aplicacao.Resultado.Marca!;
            aplicacao.Desfeitas = _cronograma.FoiDesfeita(marcaDoCartao) || (!_cronograma.PodeDesfazer(marcaDoCartao) && aplicacao.Desfeitas);
            aplicacao.PodeDesfazer = _cronograma.PodeDesfazer(marcaDoCartao);
        }
        foreach (var mensagem in Mensagens.Where(m => m.Alteracoes?.Resultado.Marca is not null))
        {
            var alteracoes = mensagem.Alteracoes!;
            var marca = alteracoes.Resultado.Marca!;
            var feita = _cronograma.PodeDesfazer(marca);
            var desfeitas = _cronograma.FoiDesfeita(marca) || (!feita && alteracoes.Desfeitas);
            if (desfeitas != alteracoes.Desfeitas)
            {
                alteracoes.Desfeitas = desfeitas;
                // A conversa guardada mostra o "Desfeito" ao reabrir.
                if (mensagem.IndiceNaConversa is { } i && i < _conversa.Mensagens.Count)
                    _conversa.Substituir(i, _conversa.Mensagens[i] with { ResultadoDasAlteracoes = TextoGuardado(alteracoes, mensagem.Cartao) });
            }
            alteracoes.PodeDesfazer = feita;
        }
    }

    [RelayCommand(CanExecute = nameof(Aguardando))]
    private void Cancelar() => _cancelamento?.Cancel();

    /// <summary>
    /// Escolhe documentos e os põe na lista de pendentes (sem abrir o conteúdo; repetidos entram uma vez). Formatos que
    /// o assistente não lê e arquivos grandes demais ficam de fora, com o motivo (Bloco 23).
    /// </summary>
    /// <summary>0.47.2: converte as fotos HEIC para JPG ao anexar (os níveis com Claude não leem HEIC); nulo, elas seguem como estão.</summary>
    public IConversorDeHeic? ConversorDeHeic { get; set; }

    [RelayCommand]
    private async Task Anexar()
    {
        var sumidos = new List<string>();
        var recusados = new List<string>();
        var convertidas = new List<string>();
        foreach (var escolhido in await _dialogos.EscolherDocumentosAsync())
        {
            var caminho = escolhido;
            if (ConversorDeHeic is { } conversor && ConversorDeHeicPeloSips.Extensoes.Contains(Path.GetExtension(caminho))
                && conversor.ParaJpeg(caminho) is { } jpeg)
            {
                caminho = jpeg;
                convertidas.Add(Path.GetFileName(escolhido));
            }
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
        if (convertidas.Count > 0)
            avisos.Add($"Convertida para JPG (todos os níveis leem): {string.Join(", ", convertidas)}.");
        AvisoDeAnexo = string.Join("\n", avisos);
    }

    [RelayCommand]
    private void RemoverAnexo(AnexoIA anexo) => AnexosPendentes.Remove(anexo);

    /// <summary>
    /// Esvazia a conversa (e cancela uma resposta que ainda não chegou). O modo recomeça; a ficha fica, porque é do
    /// projeto (Bloco 26).
    /// </summary>
    [RelayCommand]
    private void LimparConversa()
    {
        _geracao++;
        RecomecarAutomatico();
        _cancelamento?.Cancel();
        Mensagens.Clear();
        var mudou = _conversa.Modo != ModoDaConversa.NaoEscolhido || _conversa.PlanoAprovado;
        _conversa.LimparMensagens();
        if (mudou)
            _cronograma.ConversaMudou();
        AtualizarEstudo();
    }

    /// <summary>
    /// Outro projeto foi aberto (Novo, Abrir): o painel passa a mostrar a conversa e a ficha dele (vazias num projeto
    /// novo; as guardadas no arquivo, desde o Bloco 26).
    /// </summary>
    public void RecomecarParaOutroProjeto()
    {
        _geracao++;
        RecomecarAutomatico();
        _cancelamento?.Cancel();
        AnexosPendentes.Clear();
        TextoDigitado = "";
        AvisoDeAnexo = "";
        _conversa = _cronograma.Conversa;
        MostrarConversa();
    }

    private MensagemChat Aviso(string texto) => new(AutorMensagem.Assistente, texto, _relogio.GetLocalNow(), [], EhErro: true);

}
