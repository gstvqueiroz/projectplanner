using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 54: a obra completa. Depois que o assistente monta um cronograma, o painel oferece estimar o orçamento e os recursos
// da obra inteira (decisão do usuário: um botão, não automático, porque gasta créditos). Ele abre Projeto > Orçamento com as
// sugestões da IA para todas as tarefas: composições da base (se importada) ou preços estimados, quantidades e equipes.
public sealed partial class AssistenteViewModel
{
    /// <summary>A partir de quantas tarefas criadas numa resposta o painel oferece o orçamento (um cronograma, não um ajuste).</summary>
    public const int TarefasParaOferecerOrcamento = 5;

    public const string PedidoDoOrcamentoCompleto =
        "orçamento completo da obra: composições ou preços estimados, quantidades e equipes de todas as tarefas";

    public const string TextoDoEstimarOrcamento = "Estimar orçamento e recursos desta obra";

    /// <summary>A última resposta montou um cronograma: o botão do orçamento aparece (some no próximo envio).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EstimarOrcamentoCommand))]
    public partial bool PodeEstimarOrcamento { get; private set; }

    [RelayCommand(CanExecute = nameof(PodeEstimar))]
    private async Task EstimarOrcamento()
    {
        PodeEstimarOrcamento = false;
        await _cronograma.AbrirSugestoesDeOrcamentoAsync(PedidoDoOrcamentoCompleto);
    }

    private bool PodeEstimar() => PodeEstimarOrcamento && !Aguardando;

    /// <summary>Quantas tarefas a resposta criou (as alterações aplicadas).</summary>
    private static int TarefasCriadas(RespostaIA resposta, AlteracoesNaConversa? alteracoes) =>
        alteracoes is { Aplicadas: true } ? resposta.Alteracoes.Count(a => a.Acao == AcoesNoCronograma.Criar) : 0;
}
