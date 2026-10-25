using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

// Bloco 58: Projeto > Ordens de serviço….
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task OrdensDeServico() => AbrirOrdensAsync(null);

    /// <summary>Bloco 64: abre as ordens de serviço já com uma OS nova (da tarefa selecionada, se houver).</summary>
    [RelayCommand]
    private Task NovaOrdemDeServico() => AbrirOrdensAsync(null, criarNova: true);

    /// <summary>Bloco 64: Projeto > Relatório das ordens de serviço….</summary>
    [RelayCommand]
    private Task RelatorioDasOrdens()
    {
        Recalcular();
        return _dialogos.MostrarRelatorioDasOrdensAsync(new RelatorioDasOrdensViewModel(_cronograma, _dialogos, _relogio, CabecalhoDosRelatorios));
    }

    /// <summary>
    /// Bloco 65: o cabeçalho dos relatórios das OS: a obra (do projeto) e quem emite, com o logotipo (as preferências do relatório
    /// fotográfico, as mesmas do boletim do contrato).
    /// </summary>
    internal Core.Relatorios.CabecalhoDoRelatorio CabecalhoDosRelatorios()
    {
        var preferencias = _preferenciasRelatorio.Carregar();
        byte[]? logotipo = null;
        try
        {
            if (preferencias.Logotipo is { } arquivo && File.Exists(arquivo))
                logotipo = File.ReadAllBytes(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem o logotipo, os relatórios saem do mesmo jeito.
        }
        return new Core.Relatorios.CabecalhoDoRelatorio(_cronograma.Nome, _cronograma.Obra, preferencias.Responsavel, preferencias.Registro, logotipo);
    }

    void IEdicaoCronograma.AbrirOrdens(TarefaViewModel linha)
    {
        TarefaSelecionada = linha;
        _ = AbrirOrdensAsync(_cronograma.OrdensDeServico.DaTarefa(linha.Tarefa.Id).FirstOrDefault()?.Id);
    }

    private async Task AbrirOrdensAsync(Guid? ordem, bool criarNova = false)
    {
        Recalcular(); // as datas da OS nova vêm do cronograma calculado
        var projeto = _cronograma.Id;
        Func<OrdemDeServico, bool, Task<int>>? publicar = TelegramDisponivel && ObraNoTelegram
            ? (o, comValores) => _telegram!.PublicarOrdemAsync(projeto, o.Id, o.Numero, o.Dados.Titulo, TextosDaOrdem.ParaOTelegram(o, comValores),
                o.Situacao.ToString(), CancellationToken.None)
            : null;
        await _dialogos.MostrarOrdensDeServicoAsync(new OrdensDeServicoViewModel(_cronograma, _dialogos, _relogio, OrdensMudaram,
            TarefaSelecionada?.Tarefa.Id, publicar, ordem, criarNova, CabecalhoDosRelatorios, NovoRelatorioFotografico));
        AtualizarOrdensNasLinhas();
    }

    /// <summary>Bloco 61: a marca "OS" de cada linha da grid (as OS da tarefa, menos as canceladas) e se alguma venceu.</summary>
    private void AtualizarOrdensNasLinhas()
    {
        var hoje = DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);
        var noGantt = new Dictionary<Guid, MarcaDeOrdemNoGantt>();
        foreach (var linha in Tarefas)
        {
            var ordens = _cronograma.OrdensDeServico.DaTarefa(linha.Tarefa.Id);
            linha.OrdensDaTarefa = ordens.Count == 0 ? null : string.Join("\n", ordens.Select(o => TextosDaOrdem.Linha(o, hoje)));
            linha.OrdemVencida = ordens.Any(o => o.Vencida(hoje));
            if (linha.OrdensDaTarefa is { } dica)
                noGantt[linha.Tarefa.Id] = new MarcaDeOrdemNoGantt(dica, linha.OrdemVencida);
        }
        // Bloco 64: a mesma marca nas barras do Gantt (só redesenha se mudou).
        if (noGantt.Count != Gantt.Ordens.Count || noGantt.Any(o => !Gantt.Ordens.TryGetValue(o.Key, out var antes) || antes != o.Value))
            Gantt.Ordens = noGantt;
    }

    /// <summary>Bloco 61: as OS na situação da obra que vai ao assistente (no programa e no Telegram).</summary>
    private IEnumerable<string> OrdensParaOAssistente()
    {
        var hoje = DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);
        var ordens = _cronograma.OrdensDeServico.Todas.Where(o => o.Situacao != SituacaoDaOrdem.Cancelada).ToList();
        if (ordens.Count == 0)
            yield break;
        var edt = _cronograma.NumeracaoEdt();
        yield return $"Ordens de serviço: {ordens.Count}; em andamento {ordens.Count(o => TextosDaOrdem.EmAndamento(o.Situacao))}, " +
                     $"vencidas {ordens.Count(o => o.Vencida(hoje))}, esperando conferência {ordens.Count(o => o.Situacao == SituacaoDaOrdem.Concluida)}; " +
                     $"medido a pagar {Moeda.ComSimbolo(ordens.Sum(o => o.APagar))}; por medir {Moeda.ComSimbolo(ordens.Where(o => TextosDaOrdem.EmAndamento(o.Situacao)).Sum(o => o.PorMedir))} " +
                     "(o usuário cria e muda as OS em Projeto > Ordens de serviço…; o assistente não muda as OS).";
        foreach (var o in ordens.TakeLast(40))
        {
            var tarefas = string.Join(", ", o.Dados.Tarefas.Where(edt.ContainsKey).Select(id => edt[id]));
            yield return $"{TextosDaOrdem.Linha(o, hoje)}: {o.Dados.Titulo}; {TextosDaOrdem.Texto(o.Dados.Tipo).ToLowerInvariant()}; " +
                         $"{TextosDaOrdem.Texto(o.Dados.FormaDePagamento).ToLowerInvariant()}; valor {Moeda.ComSimbolo(o.Total)}, medido {Moeda.ComSimbolo(o.TotalMedido)}, " +
                         $"pago {Moeda.ComSimbolo(o.TotalPago)}" + (tarefas.Length > 0 ? $"; tarefas {tarefas}" : "") + ".";
        }
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
        {
            OrdensMudaram($"Telegram: {string.Join(", ", mudancas)}", false);
            AtualizarOrdensNasLinhas();
        }
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
