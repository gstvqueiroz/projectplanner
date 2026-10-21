using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResponderPerguntasCommand))]
    public partial string TextoDigitado { get; set; } = "";

    /// <summary>Esperando a resposta: o painel mostra "Assistente digitando…" e Enviar vira Cancelar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelarCommand))]
    public partial bool Aguardando { get; private set; }

    partial void OnAguardandoChanged(bool value) => AtualizarInteracoes();

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

    private async Task EnviarAsync(string texto, IReadOnlyList<AnexoIA> anexos, bool planoAgora = false)
    {
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
        try
        {
            var retrato = _cronograma.Retrato();
            var r = await _assistente.EnviarAsync(new PedidoIA(texto, anexos, historico, retrato, Conducao(planoAgora)), cancelamento.Token);
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
                            new PedidoIA(PedidoDeCorrecao(aplicacao.Texto), [], tentativa, novoRetrato, Conducao(planoAgora: false)),
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
            Acrescentar(resposta, resposta.EhErro ? null : alteracoes, resposta.EhErro ? null : cartao, resposta.EhErro ? null : tela);
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
        _cancelamento?.Cancel();
        AnexosPendentes.Clear();
        TextoDigitado = "";
        AvisoDeAnexo = "";
        _conversa = _cronograma.Conversa;
        MostrarConversa();
    }

    private MensagemChat Aviso(string texto) => new(AutorMensagem.Assistente, texto, _relogio.GetLocalNow(), [], EhErro: true);

    // ---- Bloco 26: a conversa guardada no projeto ----

    // Acrescenta a mensagem no painel e, se não for um aviso local, na conversa do projeto.
    private void Acrescentar(MensagemChat mensagem, AlteracoesNaConversa? alteracoes = null, CartaoDeAtualizacoes? cartao = null, TelaParaAbrir? tela = null)
    {
        int? indice = null;
        if (!mensagem.EhErro)
        {
            _conversa.Acrescentar(ParaAConversa(mensagem, alteracoes) with
            {
                ResultadoDasAlteracoes = TextoGuardado(alteracoes, cartao),
            });
            indice = _conversa.Mensagens.Count - 1;
        }
        Mensagens.Add(new MensagemNaTela(mensagem, alteracoes) { IndiceNaConversa = indice, Cartao = cartao, Tela = tela });
        AtualizarEstudo();
    }

    // Mostra a conversa guardada no projeto (as alterações de sessões anteriores aparecem como texto, sem Desfazer).
    private void MostrarConversa()
    {
        Mensagens.Clear();
        for (var i = 0; i < _conversa.Mensagens.Count; i++)
        {
            var m = _conversa.Mensagens[i];
            var chat = new MensagemChat(m.Autor == AutorNaConversa.Usuario ? AutorMensagem.Usuario : AutorMensagem.Assistente,
                m.Texto, m.DataHora, [.. m.Anexos.Select(a => new AnexoIA(a.Caminho, a.Tamanho))], Resposta: DaConversa(m));
            var alteracoes = m.ResultadoDasAlteracoes is { } texto ? AlteracoesNaConversa.Salvas(texto, m.AlteracoesAplicadas) : null;
            Mensagens.Add(new MensagemNaTela(chat, alteracoes) { IndiceNaConversa = i });
        }
        AtualizarFicha();
        AtualizarEstudo();
    }

    private static MensagemDaConversa ParaAConversa(MensagemChat m, AlteracoesNaConversa? alteracoes)
    {
        var r = m.Resposta;
        return new MensagemDaConversa(
            m.Autor == AutorMensagem.Usuario ? AutorNaConversa.Usuario : AutorNaConversa.Assistente,
            m.Texto,
            m.DataHora,
            r?.Tipo switch
            {
                TipoDeResposta.EscolherModo => TipoDaMensagem.EscolhaDeModo,
                TipoDeResposta.Perguntas => TipoDaMensagem.Perguntas,
                TipoDeResposta.Plano => TipoDaMensagem.Plano,
                _ => TipoDaMensagem.Texto,
            },
            alteracoes?.Texto,
            alteracoes?.Aplicadas ?? false)
        {
            Anexos = [.. m.Anexos.Select(a => new AnexoNaConversa(a.CaminhoLocal, a.TamanhoEmBytes))],
            Perguntas = [.. (r?.Perguntas ?? []).Select(p => new PerguntaNaConversa(p.Texto, p.Opcoes))],
            Plano = r?.Plano is { } plano
                ? new PlanoNaConversa([.. plano.Etapas.Select(e => new EtapaDoPlano(e.Nome, e.Duracao, e.Observacao))], plano.Termino, plano.Premissas)
                : null,
        };
    }

    private static RespostaIA? DaConversa(MensagemDaConversa m) => m.Tipo switch
    {
        TipoDaMensagem.EscolhaDeModo => new RespostaIA(m.Texto) { Tipo = TipoDeResposta.EscolherModo },
        TipoDaMensagem.Perguntas => new RespostaIA(m.Texto)
        {
            Tipo = TipoDeResposta.Perguntas,
            Perguntas = [.. m.Perguntas.Select(p => new PerguntaIA(p.Texto, p.Opcoes))],
        },
        TipoDaMensagem.Plano when m.Plano is { } plano => new RespostaIA(m.Texto)
        {
            Tipo = TipoDeResposta.Plano,
            Plano = new PlanoIA([.. plano.Etapas.Select(e => new EtapaIA(e.Nome, e.DuracaoEmDias, e.Observacao))], plano.TerminoEstimado, plano.Premissas),
        },
        _ => null,
    };

    // ---- Bloco 26: a condução (modo, perguntas, plano) ----

    private ConducaoDaConversa Conducao(bool planoAgora) => new(
        _conversa.Modo switch
        {
            ModoDaConversa.Direto => ModoDeTrabalho.Direto,
            ModoDaConversa.Estudo => ModoDeTrabalho.Estudo,
            _ => ModoDeTrabalho.NaoEscolhido,
        },
        _configuracao.PreferenciaDeModo,
        [.. _conversa.Ficha.Itens.Select(i => new ItemDeFicha(i.Campo, i.Valor))],
        _conversa.RodadasDePerguntas,
        _conversa.PlanoAprovado,
        planoAgora);

    // O que a resposta muda na condução: a ficha, o modo (perguntas e plano são do estudo) e o fim do estudo (o plano
    // aprovado virou cronograma).
    private void Conduzir(RespostaIA resposta, AlteracoesNaConversa? alteracoes)
    {
        var mudou = _conversa.Ficha.Mesclar(resposta.Ficha.Select(i => new ItemDaFicha(i.Campo, i.Valor))) > 0;
        if (resposta.Tipo is TipoDeResposta.Perguntas or TipoDeResposta.Plano && _conversa.Modo == ModoDaConversa.NaoEscolhido)
            mudou |= _conversa.DefinirModo(ModoDaConversa.Estudo);
        if (resposta.Tipo == TipoDeResposta.Plano)
            mudou |= _conversa.DefinirPlanoAprovado(false); // um plano novo precisa de aprovação nova
        if (_conversa.PlanoAprovado && alteracoes is { Aplicadas: true, Resultado.Marca: not null })
            mudou |= _conversa.DefinirModo(ModoDaConversa.Direto); // estudo concluído: daqui em diante, ajustes diretos
        if (mudou)
        {
            _cronograma.ConversaMudou();
            AtualizarFicha();
        }
    }

    /// <summary>A última mensagem do assistente, a única com o cartão, as perguntas ou o plano ativos.</summary>
    public MensagemNaTela? Ultima => Mensagens.LastOrDefault(m => !m.DoUsuario && !m.EhErro);

    private bool EhAUltimaAtiva(MensagemNaTela? mensagem) => !Aguardando && mensagem is not null && ReferenceEquals(mensagem, Ultima)
                                                              && ReferenceEquals(mensagem, Mensagens.LastOrDefault());

    private bool PodeEscolherModo(string? modo) => EhAUltimaAtiva(Ultima) && Ultima!.EscolheModo && modo is "direto" or "estudo";

    /// <summary>O cartão "Como você prefere?": "direto" monta agora; "estudo" estuda e conversa antes. Vale para a conversa.</summary>
    [RelayCommand(CanExecute = nameof(PodeEscolherModo))]
    private Task EscolherModo(string? modo)
    {
        var estudo = modo == "estudo";
        if (_conversa.DefinirModo(estudo ? ModoDaConversa.Estudo : ModoDaConversa.Direto))
            _cronograma.ConversaMudou();
        AtualizarEstudo();
        return EnviarAsync(estudo ? TextoEstudarAntes : TextoMontarAgora, []);
    }

    private bool PodeResponderPerguntas(MensagemNaTela? mensagem) =>
        EhAUltimaAtiva(mensagem) && mensagem!.TemPerguntas
        && (mensagem.Perguntas.Any(p => p.Respondida) || !string.IsNullOrWhiteSpace(TextoDigitado));

    /// <summary>Manda as respostas escolhidas ou escritas (com o texto da caixa, se houver, como observação).</summary>
    [RelayCommand(CanExecute = nameof(PodeResponderPerguntas))]
    private Task ResponderPerguntas(MensagemNaTela? mensagem)
    {
        var texto = "Respostas:\n" + string.Join("\n", mensagem!.Perguntas.Select(p => $"- {p.Texto} {p.Resposta ?? PerguntaNaTela.SemResposta}"));
        if (!string.IsNullOrWhiteSpace(TextoDigitado))
            texto += "\n" + TextoDigitado.Trim();
        var anexos = AnexosPendentes.ToList();
        TextoDigitado = "";
        AnexosPendentes.Clear();
        AvisoDeAnexo = "";
        return EnviarAsync(texto, anexos);
    }

    private bool PodeTratarOPlano(MensagemNaTela? mensagem) => EhAUltimaAtiva(mensagem) && mensagem!.TemPlano;

    /// <summary>Aprova o plano: o assistente monta o cronograma completo seguindo ele.</summary>
    [RelayCommand(CanExecute = nameof(PodeTratarOPlano))]
    private Task AprovarPlano(MensagemNaTela? mensagem)
    {
        _conversa.DefinirModo(ModoDaConversa.Estudo);
        if (_conversa.DefinirPlanoAprovado(true))
            _cronograma.ConversaMudou();
        AtualizarEstudo();
        return EnviarAsync(TextoPlanoAprovado, []);
    }

    /// <summary>Começa uma mensagem de ajuste do plano na caixa de texto, para o usuário completar.</summary>
    [RelayCommand(CanExecute = nameof(PodeTratarOPlano))]
    private void AjustarPlano(MensagemNaTela? mensagem)
    {
        if (!TextoDigitado.StartsWith(PrefixoDoAjuste, StringComparison.Ordinal))
            TextoDigitado = PrefixoDoAjuste + TextoDigitado;
        FocoNaEntradaPedido?.Invoke();
    }

    /// <summary>A view põe o cursor na caixa de texto (Ajustar o plano).</summary>
    public event Action? FocoNaEntradaPedido;

    private bool PodePularParaOPlano() => !Aguardando && EmEstudo && !_conversa.PlanoAprovado && Ultima?.Tipo != TipoDeResposta.Plano
                                          && Mensagens.Count > 0;

    /// <summary>Para as perguntas: o assistente apresenta o plano com o que já tem (premissas comuns no resto).</summary>
    [RelayCommand(CanExecute = nameof(PodePularParaOPlano))]
    private Task PularParaOPlano() => EnviarAsync(TextoPularParaOPlano, [], planoAgora: true);

    /// <summary>No modo de estudo (desde a escolha até o cronograma ser montado).</summary>
    public bool EmEstudo => _conversa.Modo == ModoDaConversa.Estudo;

    /// <summary>"Estudo · perguntas (2 de 4)": onde o estudo está; vazio fora dele.</summary>
    public string EtapaDoEstudo => _conversa.Modo switch
    {
        ModoDaConversa.Estudo when _conversa.PlanoAprovado => "Estudo · montando o cronograma",
        ModoDaConversa.Estudo when Ultima?.Tipo == TipoDeResposta.Plano => "Estudo · plano para aprovar",
        ModoDaConversa.Estudo when _conversa.RodadasDePerguntas > 0 =>
            $"Estudo · perguntas ({_conversa.RodadasDePerguntas} de {ConducaoDaConversa.MaximoDeRodadas})",
        ModoDaConversa.Estudo => "Estudo · lendo e entendendo",
        ModoDaConversa.Direto => "Modo: montar direto",
        _ => "",
    };

    /// <summary>O atalho "Pular para o plano" aparece só quando vale (no estudo, antes do plano).</summary>
    public bool MostrarPularParaOPlano => PodePularParaOPlano();

    public bool TemEtapaDoEstudo => EtapaDoEstudo.Length > 0;

    private void AtualizarEstudo()
    {
        OnPropertyChanged(nameof(EmEstudo));
        OnPropertyChanged(nameof(EtapaDoEstudo));
        OnPropertyChanged(nameof(TemEtapaDoEstudo));
        AtualizarInteracoes();
    }

    private MensagemNaTela? _observada;

    // Os botões do cartão, das perguntas e do plano só valem na última mensagem, e não durante a espera.
    private void AtualizarInteracoes()
    {
        if (_observada is not null)
            foreach (var pergunta in _observada.Perguntas)
                pergunta.PropertyChanged -= PerguntaRespondida;
        _observada = Ultima;
        if (_observada is not null)
            foreach (var pergunta in _observada.Perguntas)
                pergunta.PropertyChanged += PerguntaRespondida;
        OnPropertyChanged(nameof(Ultima));
        OnPropertyChanged(nameof(MostrarPularParaOPlano));
        EscolherModoCommand.NotifyCanExecuteChanged();
        ResponderPerguntasCommand.NotifyCanExecuteChanged();
        AprovarPlanoCommand.NotifyCanExecuteChanged();
        AjustarPlanoCommand.NotifyCanExecuteChanged();
        PularParaOPlanoCommand.NotifyCanExecuteChanged();
    }

    private void PerguntaRespondida(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ResponderPerguntasCommand.NotifyCanExecuteChanged();

    // ---- Bloco 26: a ficha do projeto ----

    /// <summary>O que já foi definido sobre o projeto, com os valores editáveis.</summary>
    public ObservableCollection<CampoDaFichaNaTela> Ficha { get; } = [];

    public bool TemFicha => Ficha.Count > 0;

    public string TituloDaFicha => Ficha.Count == 0 ? "Ficha do projeto" : $"Ficha do projeto ({Ficha.Count})";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdicionarCampoCommand))]
    public partial string NovoCampo { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdicionarCampoCommand))]
    public partial string NovoValor { get; set; } = "";

    /// <summary>Um campo ou valor que a ficha recusou (vazio, longo demais, ficha cheia).</summary>
    [ObservableProperty]
    public partial string AvisoDaFicha { get; private set; } = "";

    private bool PodeAdicionarCampo() => !string.IsNullOrWhiteSpace(NovoCampo) && !string.IsNullOrWhiteSpace(NovoValor);

    [RelayCommand(CanExecute = nameof(PodeAdicionarCampo))]
    private void AdicionarCampo()
    {
        if (!DefinirNaFicha(NovoCampo, NovoValor))
            return;
        (NovoCampo, NovoValor) = ("", "");
        AtualizarFicha();
    }

    [RelayCommand]
    private void RemoverCampo(CampoDaFichaNaTela? campo)
    {
        if (campo is not null && DefinirNaFicha(campo.Campo, null))
            AtualizarFicha();
    }

    private void ValorDaFichaMudou(CampoDaFichaNaTela campo)
    {
        if (DefinirNaFicha(campo.Campo, campo.Valor) && string.IsNullOrWhiteSpace(campo.Valor))
            AtualizarFicha(); // valor apagado: o campo sai da ficha
    }

    private bool DefinirNaFicha(string campo, string? valor)
    {
        try
        {
            AvisoDaFicha = "";
            if (_conversa.Ficha.Definir(campo, valor))
                _cronograma.ConversaMudou();
            return true;
        }
        catch (DomainValidationException ex)
        {
            AvisoDaFicha = ex.Message;
            return false;
        }
    }

    private void AtualizarFicha()
    {
        Ficha.Clear();
        foreach (var item in _conversa.Ficha.Itens)
            Ficha.Add(new CampoDaFichaNaTela(item, ValorDaFichaMudou));
    }
}
