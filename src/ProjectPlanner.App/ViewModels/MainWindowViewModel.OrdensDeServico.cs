using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

// Bloco 58: Projeto > Ordens de serviço….
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task OrdensDeServico()
    {
        Recalcular(); // as datas da OS nova vêm do cronograma calculado
        var projeto = _cronograma.Id;
        Func<OrdemDeServico, bool, Task<int>>? publicar = TelegramDisponivel && ObraNoTelegram
            ? (o, comValores) => _telegram!.PublicarOrdemAsync(projeto, o.Id, o.Numero, o.Dados.Titulo, TextosDaOrdem.ParaOTelegram(o, comValores),
                o.Situacao.ToString(), CancellationToken.None)
            : null;
        return _dialogos.MostrarOrdensDeServicoAsync(new OrdensDeServicoViewModel(_cronograma, _dialogos, _relogio, OrdensMudaram,
            TarefaSelecionada?.Tarefa.Id, publicar));
    }

    /// <summary>
    /// Bloco 59: os passos que a equipe deu nas OS pelo Telegram (aceitou, recusou, começou, terminou) entram nas OS, com o nome de
    /// quem fez no histórico; um passo que a OS não aceita mais (já mudou no programa) é deixado de lado.
    /// </summary>
    /// <returns>O que mudou ("OS-003 aceita (João)") e quantos ficaram de lado.</returns>
    private (List<string> Mudancas, int Ignorados) AplicarOrdensDoTelegram(IEnumerable<ItemDoTelegram> itens)
    {
        var (mudancas, ignorados) = (new List<string>(), 0);
        foreach (var e in itens.Select(i => i.Ordem).OfType<EventoDeOrdemDoTelegram>().OrderBy(e => e.Quando))
        {
            if (_cronograma.OrdensDeServico.Obter(e.Ordem) is not { } ordem)
            {
                ignorados++;
                continue;
            }
            try
            {
                var nova = ordem.PeloTelegram(e.Acao, e.Quando.Kind == DateTimeKind.Utc ? e.Quando.ToLocalTime() : e.Quando, e.Autor, e.Motivo);
                _cronograma.OrdensDeServico.Substituir(nova);
                mudancas.Add($"{nova.Codigo} {TextosDaOrdem.Texto(nova.Situacao).ToLowerInvariant()} ({e.Autor})");
            }
            catch (DomainValidationException)
            {
                ignorados++;
            }
        }
        if (mudancas.Count > 0)
            OrdensMudaram($"Telegram: {string.Join(", ", mudancas)}", false);
        return (mudancas, ignorados);
    }

    // As OS entram no Desfazer; quando mexem nas tarefas (o % da conferência), o cronograma é recalculado.
    private void OrdensMudaram(string descricao, bool tarefasMudaram)
    {
        if (tarefasMudaram)
        {
            MarcarAlteracao(descricao);
            return;
        }
        RegistrarNoHistorico(descricao);
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
    }
}
