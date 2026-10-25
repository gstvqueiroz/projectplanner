using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// Bloco 51: o aviso de custo do nível Automático, antes de mandar um pedido caro: o que a triagem achou, o modelo
/// recomendado com a estimativa e a alternativa no Padrão. O envio espera a escolha.
/// </summary>
public sealed partial class ConfirmacaoDeCusto(AvaliacaoDoPedido avaliacao, decimal? saldo) : ObservableObject
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public AvaliacaoDoPedido Avaliacao { get; } = avaliacao;

    internal TaskCompletionSource<string> Escolha { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Titulo => string.IsNullOrWhiteSpace(Avaliacao.Motivo)
        ? "Este pedido pode gastar mais créditos."
        : $"Este pedido parece {(Avaliacao.Complexidade == "complexa" ? "complexo" : "maior")}: {Avaliacao.Motivo}.";

    /// <summary>"Cerca de 70 créditos (1% do seu saldo)."</summary>
    public string Estimativa =>
        $"Cerca de {Creditos(Avaliacao.CreditosEstimados)} créditos no {Avaliacao.Nome}" +
        (saldo is > 0 ? $" ({Math.Max(1, Math.Round(Avaliacao.CreditosEstimados * 100 / saldo.Value)).ToString("0", Brasil)}% do seu saldo)." : ".") +
        (Avaliacao.LimitadoPeloPlano ? " O plano Profissional libera os modelos mais fortes." : "");

    public string TextoDoRecomendado => $"Usar {Avaliacao.Nome} (~{Creditos(Avaliacao.CreditosEstimados)})";

    public string TextoDoPadrao => $"Tentar no {Avaliacao.NomePadrao} (~{Creditos(Avaliacao.CreditosNoPadrao)})";

    /// <summary>O recomendado já é o do Padrão (um pedido grande no plano Grátis, por exemplo): um botão só.</summary>
    public bool MostrarPadrao => Avaliacao.Modelo != Avaliacao.ModeloPadrao;

    /// <summary>Não perguntar de novo nesta conversa (vale até limpar a conversa ou trocar de projeto).</summary>
    [ObservableProperty]
    public partial bool NaoPerguntarDeNovo { get; set; }

    private static string Creditos(decimal valor) => valor.ToString("N0", Brasil);
}

// Bloco 51: o nível Automático no painel: a triagem antes de cada envio, a confirmação dos pedidos caros e o
// "Refazer com um modelo melhor".
public sealed partial class AssistenteViewModel
{
    private bool _naoPerguntarNestaConversa;
    private (string Texto, IReadOnlyList<AnexoIA> Anexos)? _ultimoEnvio;

    /// <summary>O Automático está escolhido (pela conta): cada envio passa pela triagem.</summary>
    public bool UsaAutomatico => _configuracao.Modelo == NiveisDaConta.Automatico && Conta.Conectada && !_configuracao.ChaveDeDesenvolvimento;

    /// <summary>A confirmação de custo esperando a escolha; nula fora dela.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemConfirmacao))]
    public partial ConfirmacaoDeCusto? Confirmacao { get; private set; }

    public bool TemConfirmacao => Confirmacao is not null;

    /// <summary>"Último pedido: Claude Haiku 5.5 (simples)", embaixo do seletor, com o Automático.</summary>
    [ObservableProperty]
    public partial string UltimaEscolha { get; private set; } = "";

    /// <summary>O modelo acima do que respondeu (liberado no plano), para refazer a última resposta; nulo sem um.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeRefazerMelhor), nameof(TextoDoRefazer))]
    [NotifyCanExecuteChangedFor(nameof(RefazerMelhorCommand))]
    public partial ModeloDaConta? ModeloMelhor { get; private set; }

    public bool PodeRefazerMelhor => ModeloMelhor is not null && _ultimoEnvio is not null;

    public string TextoDoRefazer => ModeloMelhor is { } m ? $"Refazer com o {m.Nome} (~{m.CreditosPorPedido} créditos)" : "";

    [RelayCommand]
    private void UsarRecomendado() => Escolher(c => c.Avaliacao.Modelo);

    [RelayCommand]
    private void UsarPadrao() => Escolher(c => c.Avaliacao.ModeloPadrao);

    [RelayCommand]
    private void CancelarConfirmacao() => Confirmacao?.Escolha.TrySetCanceled();

    private void Escolher(Func<ConfirmacaoDeCusto, string> modelo)
    {
        if (Confirmacao is not { } confirmacao)
            return;
        if (confirmacao.NaoPerguntarDeNovo)
            _naoPerguntarNestaConversa = true;
        confirmacao.Escolha.TrySetResult(modelo(confirmacao));
    }

    [RelayCommand(CanExecute = nameof(PodeRefazer))]
    private Task RefazerMelhor()
    {
        if (_ultimoEnvio is not { } ultimo || ModeloMelhor is not { } melhor)
            return Task.CompletedTask;
        return EnviarAsync(ultimo.Texto, ultimo.Anexos, modelo: melhor.Id);
    }

    private bool PodeRefazer() => PodeRefazerMelhor && !Aguardando;

    /// <summary>
    /// O modelo deste envio pelo Automático: a triagem do servidor e, num pedido caro, a confirmação. Nulo fora do Automático
    /// ou se a triagem falhar (o servidor manda o "automatico" ao Padrão).
    /// </summary>
    /// <exception cref="OperationCanceledException">O usuário cancelou na confirmação.</exception>
    private async Task<string?> EscolherModeloAsync(string texto, IReadOnlyList<AnexoIA> anexos, IReadOnlyList<MensagemChat> historico,
        ContextoDoProjeto? retrato, CancellationToken cancelamento)
    {
        if (!UsaAutomatico)
            return null;
        var caracteres = texto.Length + (retrato?.ComoTexto().Length ?? 0) + historico.TakeLast(LimitesDaConversa.MensagensDoHistorico).Sum(m => m.Texto.Length);
        var pedido = new PedidoDeAvaliacao(texto.Length > 2_000 ? texto[..2_000] : texto, anexos.Count, anexos.Sum(a => a.TamanhoEmBytes), caracteres,
            retrato?.Tarefas.Count ?? 0);
        AvaliacaoDoPedido avaliacao;
        try
        {
            avaliacao = await Conta.AvaliarAsync(pedido, cancelamento);
        }
        catch (Exception ex) when (ex is ErroDaContaException or ErroDoAssistenteException)
        {
            UltimaEscolha = "";
            return null;
        }
        UltimaEscolha = $"Último pedido: {avaliacao.Nome} ({NomeDaComplexidade(avaliacao.Complexidade)})";
        if (!avaliacao.PedirConfirmacao || _naoPerguntarNestaConversa)
            return avaliacao.Modelo;
        var confirmacao = new ConfirmacaoDeCusto(avaliacao, Conta.CreditosRestantes);
        Confirmacao = confirmacao;
        try
        {
            var escolhido = await confirmacao.Escolha.Task.WaitAsync(cancelamento);
            if (escolhido == avaliacao.ModeloPadrao)
                UltimaEscolha = $"Último pedido: {avaliacao.NomePadrao} (você escolheu)";
            return escolhido;
        }
        finally
        {
            Confirmacao = null;
        }
    }

    private static string NomeDaComplexidade(string complexidade) => complexidade switch
    {
        "complexa" => "pedido complexo",
        "media" => "pedido médio",
        _ => "pedido simples",
    };

    /// <summary>Depois de uma resposta pelo Automático que não mexeu na obra: oferece refazer com o modelo acima.</summary>
    private void OferecerRefazer(string texto, IReadOnlyList<AnexoIA> anexos, string? modeloUsado, bool podeRefazer)
    {
        ModeloMelhor = null;
        _ultimoEnvio = null;
        if (!podeRefazer || !UsaAutomatico)
            return;
        var usado = Conta.Modelos.FirstOrDefault(m => m.Id == modeloUsado) ?? Conta.Modelos.FirstOrDefault(m => m.Nivel == NiveisDaConta.Padrao);
        var nivel = NiveisDaConta.EmOrdem.ToList().IndexOf(usado?.Nivel ?? NiveisDaConta.Padrao);
        _ultimoEnvio = (texto, anexos);
        ModeloMelhor = Conta.Modelos.Where(m => m.Liberado && m.Disponivel && NiveisDaConta.EmOrdem.ToList().IndexOf(m.Nivel) > nivel)
            .OrderBy(m => NiveisDaConta.EmOrdem.ToList().IndexOf(m.Nivel)).LastOrDefault();
    }

    /// <summary>Bloco 51: a conversa recomeçou: volta a perguntar antes dos pedidos caros.</summary>
    private void RecomecarAutomatico()
    {
        _naoPerguntarNestaConversa = false;
        PodeEstimarOrcamento = false; // Bloco 54
        Confirmacao?.Escolha.TrySetCanceled();
        ModeloMelhor = null;
        _ultimoEnvio = null;
    }

    /// <summary>
    /// Bloco 51: quem está no Padrão (ou num modelo que saiu da lista) passa ao Automático, uma vez só; quem escolheu um nível
    /// forte continua nele.
    /// </summary>
    private void OferecerAutomatico()
    {
        if (!Conta.Conectada || _configuracao.AutomaticoOferecido || Conta.Modelos.All(m => m.Id != NiveisDaConta.Automatico))
            return;
        var atual = Conta.Modelos.FirstOrDefault(m => m.Id == _configuracao.Modelo);
        _configuracao.OferecerAutomatico(trocar: atual is null || atual.Nivel == NiveisDaConta.Padrao);
    }
}
