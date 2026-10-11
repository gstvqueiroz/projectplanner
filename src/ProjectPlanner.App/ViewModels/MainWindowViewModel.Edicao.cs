using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Edicao;

namespace ProjectPlanner.App.ViewModels;

// Bloco 30: Editar > Desfazer e Refazer (vários níveis, também as alterações do assistente e as fotos), e Copiar, Colar
// e Duplicar tarefas.
public sealed partial class MainWindowViewModel
{
    private HistoricoDeAlteracoes _historico = null!; // criado em Carregar

    // Houve alteração que não entra no histórico (conversa, obra, locais...) desde o último salvamento: desfazer até o
    // estado salvo não basta para o projeto ficar "sem alterações".
    private bool _alteracoesForaDoHistorico;

    // As tarefas copiadas. Ficam durante a sessão, também ao abrir outro projeto (para colar nele).
    private TrechoCopiado? _copiadas;

    /// <summary>Editar > Desfazer: "Desfazer Duração de "Escavação"", ou só "Desfazer" sem nada para desfazer.</summary>
    public string TextoDesfazer => _historico.ProximaADesfazer is { } descricao ? $"Desfazer {descricao}" : "Desfazer";

    public string TextoRefazer => _historico.ProximaARefazer is { } descricao ? $"Refazer {descricao}" : "Refazer";

    [RelayCommand(CanExecute = nameof(PodeDesfazerAlteracao))]
    private void DesfazerAlteracao()
    {
        var descricao = VoltarUmPasso(desfazer: true);
        Mensagem = $"Desfeito: {descricao}.";
    }

    private bool PodeDesfazerAlteracao() => _historico.PodeDesfazer;

    [RelayCommand(CanExecute = nameof(PodeRefazerAlteracao))]
    private void RefazerAlteracao()
    {
        var descricao = VoltarUmPasso(desfazer: false);
        Mensagem = $"Refeito: {descricao}.";
    }

    private bool PodeRefazerAlteracao() => _historico.PodeRefazer;

    // Desfaz ou refaz uma alteração: o cronograma do histórico entra no lugar do aberto, e o projeto fica "sem
    // alterações" se voltou a ser o salvo.
    private string VoltarUmPasso(bool desfazer)
    {
        var (cronograma, descricao) = desfazer ? _historico.Desfazer(_cronograma) : _historico.Refazer(_cronograma);
        TrocarCronograma(cronograma, registrarComo: null);
        PossuiAlteracoesNaoSalvas = !_historico.NoEstadoSalvo || _alteracoesForaDoHistorico;
        OnPropertyChanged(nameof(Titulo));
        AposFotosVoltarem();
        HistoricoMudou();
        return descricao;
    }

    // As fotos podem ter voltado ou saído: a câmera da grid e o botão de fotos da barra se atualizam.
    private void AposFotosVoltarem()
    {
        foreach (var linha in Tarefas)
            linha.FotosMudaram();
        OnPropertyChanged(nameof(FotosDaSelecionada));
        OnPropertyChanged(nameof(DicaFotosDaSelecionada));
    }

    /// <summary>Uma alteração do cronograma entra no histórico do Desfazer.</summary>
    private PassoDoHistorico RegistrarNoHistorico(string descricao)
    {
        var passo = _historico.Registrar(descricao, _cronograma);
        HistoricoMudou();
        return passo;
    }

    private void RecomecarHistorico()
    {
        _historico = new HistoricoDeAlteracoes(_cronograma);
        _alteracoesForaDoHistorico = false;
        HistoricoMudou();
    }

    private void HistoricoMudou()
    {
        OnPropertyChanged(nameof(TextoDesfazer));
        OnPropertyChanged(nameof(TextoRefazer));
        DesfazerAlteracaoCommand.NotifyCanExecuteChanged();
        RefazerAlteracaoCommand.NotifyCanExecuteChanged();
        DesfazerMudou?.Invoke();
    }

    // ---- Copiar, colar e duplicar ----

    /// <summary>Se há tarefas copiadas para colar.</summary>
    public bool TemTarefasCopiadas => _copiadas is not null;

    /// <summary>Editar > Copiar tarefa: a selecionada, com as subtarefas e os vínculos entre elas (sem as fotos).</summary>
    [RelayCommand(CanExecute = nameof(HaTarefaSelecionada))]
    private void CopiarTarefa()
    {
        _copiadas = CopiaDeTarefas.Copiar(_cronograma, [TarefaSelecionada!.Tarefa.Id]);
        OnPropertyChanged(nameof(TemTarefasCopiadas));
        ColarTarefaCommand.NotifyCanExecuteChanged();
        Mensagem = $"Copiado: {_copiadas.Descricao}. Selecione onde colar (a cópia entra logo abaixo) e use Colar.";
    }

    /// <summary>
    /// Editar > Colar tarefa: as copiadas, em tarefas novas, logo abaixo da selecionada (e das subtarefas dela), no mesmo
    /// nível; sem seleção, no fim. Dá para colar várias vezes, e em outro projeto.
    /// </summary>
    [RelayCommand(CanExecute = nameof(TemTarefasCopiadas))]
    private void ColarTarefa()
    {
        if (Colar(_copiadas!, TarefaSelecionada) is { } primeira)
        {
            MarcarAlteracao($"Colar {_copiadas!.Descricao}");
            Mensagem = $"Colado: {_copiadas.Descricao}.";
            MostrarTarefa(LinhaDe(primeira.Id));
        }
    }

    /// <summary>Editar > Duplicar tarefa: uma cópia da selecionada logo abaixo dela (sem mexer no que foi copiado).</summary>
    [RelayCommand(CanExecute = nameof(HaTarefaSelecionada))]
    private void DuplicarTarefa()
    {
        var trecho = CopiaDeTarefas.Copiar(_cronograma, [TarefaSelecionada!.Tarefa.Id]);
        if (Colar(trecho, TarefaSelecionada) is { } primeira)
        {
            MarcarAlteracao($"Duplicar {trecho.Descricao}");
            Mensagem = $"Duplicado: {trecho.Descricao}.";
            MostrarTarefa(LinhaDe(primeira.Id));
        }
    }

    // Cola o trecho depois da tarefa de referência (entre as irmãs dela) e refaz as linhas; nula se recusado.
    private Tarefa? Colar(TrechoCopiado trecho, TarefaViewModel? depoisDe)
    {
        var pai = depoisDe?.Tarefa.TarefaPaiId;
        var posicao = depoisDe is null
            ? _cronograma.Tarefas.Count(t => t.TarefaPaiId is null)
            : _cronograma.Tarefas.Where(t => t.TarefaPaiId == pai).ToList().IndexOf(depoisDe.Tarefa) + 1;
        IReadOnlyList<Tarefa> novas = [];
        if (!MudarEstrutura(() => novas = CopiaDeTarefas.Colar(_cronograma, trecho, pai, posicao)) || novas.Count == 0)
            return null;
        SincronizarLinhas();
        return novas[0];
    }
}
