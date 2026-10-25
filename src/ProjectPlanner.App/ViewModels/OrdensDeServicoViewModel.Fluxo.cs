using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de OrdensDeServicoViewModel.cs): o fluxo da OS (emitir, aceitar, iniciar, concluir, conferir…) e o Telegram.
public sealed partial class OrdensDeServicoViewModel
{
    // ---- O fluxo ----

    public bool PodeEmitir => Ordem?.PodeIrPara(SituacaoDaOrdem.Emitida) == true;
    public bool PodeAceitar => Ordem?.PodeIrPara(SituacaoDaOrdem.Aceita) == true;
    public bool PodeIniciar => Ordem?.PodeIrPara(SituacaoDaOrdem.EmExecucao) == true;
    public bool PodeConcluir => Ordem?.PodeIrPara(SituacaoDaOrdem.Concluida) == true;
    public bool PodeConferir => Ordem?.PodeIrPara(SituacaoDaOrdem.Conferida) == true;
    public bool PodeVoltarARascunho => Ordem?.PodeIrPara(SituacaoDaOrdem.Rascunho) == true;
    public bool PodeCancelar => Ordem?.PodeIrPara(SituacaoDaOrdem.Cancelada) == true;

    /// <summary>Bloco 59: a obra está no Telegram (o botão de mandar aparece).</summary>
    public bool TemTelegram => _publicar is not null;

    /// <summary>Bloco 59: só a OS que já saiu do rascunho vai para o grupo.</summary>
    public bool PodeMandarAoTelegram => TemTelegram && Ordem is { } o && TextosDaOrdem.EmAndamento(o.Situacao);

    /// <summary>Bloco 59: a mensagem no grupo mostra os preços e o total (a equipe toda vê).</summary>
    [ObservableProperty] public partial bool ValoresNoTelegram { get; set; } = true;

    private void AtualizarBotoes()
    {
        foreach (var nome in new[] { nameof(PodeEmitir), nameof(PodeAceitar), nameof(PodeIniciar), nameof(PodeConcluir), nameof(PodeConferir),
                     nameof(PodeVoltarARascunho), nameof(PodeCancelar), nameof(PodeMandarAoTelegram) })
            OnPropertyChanged(nome);
        foreach (var comando in new IRelayCommand[] { SalvarCommand, TrazerItensDasTarefasCommand, AdicionarItemCommand, AdicionarVerificacaoCommand,
                     ExcluirRascunhoCommand, EmitirCommand, AceitarCommand, RecusarCommand, IniciarCommand, ConcluirCommand, ConferirCommand,
                     ReprovarCommand, VoltarARascunhoCommand, CancelarCommand, SalvarPdfCommand, ImprimirCommand, MandarAoTelegramCommand,
                     RegistrarMedicaoCommand, MedirTudoOQueFaltaCommand, DuplicarCommand, LiberarRetencaoCommand, DesfazerLiberacaoCommand })
            comando.NotifyCanExecuteChanged();
    }

    // Aplica um passo à OS escolhida (troca na lista, entra no Desfazer) e mostra a mensagem.
    /// <param name="atualizarTarefas">Bloco 60: o medido leva o % das tarefas da OS (se a opção está ligada).</param>
    private bool Executar(Func<OrdemDeServico> passo, string descricao, string mensagem, bool atualizarTarefas = false)
    {
        OrdemDeServico nova;
        try
        {
            nova = passo();
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        _cronograma.OrdensDeServico.Substituir(nova);
        var tarefasMudaram = atualizarTarefas && AtualizarTarefas && AtualizarTarefasCom(nova);
        _mudou(descricao, tarefasMudaram);
        Atualizar();
        Mensagem = mensagem;
        if (nova.NoTelegram && _publicar is not null)
            _ = AtualizarNoTelegramAsync(nova);
        return true;
    }

    // Bloco 59: a OS que já está no grupo acompanha cada passo dado no programa (sem atrapalhar se o servidor não responder).
    private async Task AtualizarNoTelegramAsync(OrdemDeServico ordem)
    {
        try
        {
            await _publicar!(ordem, ValoresNoTelegram);
        }
        catch (IA.Conta.ErroDaContaException ex)
        {
            Mensagem += $" A mensagem no Telegram não foi atualizada agora ({ex.Message})";
        }
    }

    /// <summary>Bloco 59: manda a OS (ou a versão de agora) aos grupos da obra, com os botões para a equipe.</summary>
    [RelayCommand(CanExecute = nameof(PodeMandarAoTelegram))]
    private async Task MandarAoTelegram()
    {
        var o = Ordem!;
        int grupos;
        try
        {
            grupos = await _publicar!(o, ValoresNoTelegram);
        }
        catch (IA.Conta.ErroDaContaException ex)
        {
            Mensagem = $"Não deu para mandar a {o.Codigo} ao Telegram: {ex.Message}";
            return;
        }
        if (grupos == 0)
        {
            Mensagem = "A obra ainda não tem um grupo no Telegram: crie um grupo com a equipe, ponha o bot nele e mande /obra lá. Depois, mande a OS de novo.";
            return;
        }
        if (!o.NoTelegram)
        {
            _cronograma.OrdensDeServico.Substituir(o.MandadaAoTelegram());
            _mudou($"Mandar a {o.Codigo} ao Telegram", false);
            Atualizar();
        }
        Mensagem = $"{o.Codigo} mandada {(grupos == 1 ? "ao grupo" : $"aos {grupos} grupos")} da obra no Telegram: a equipe aceita, começa e termina pelos botões, " +
                   "e você traz em Conta > Buscar do Telegram. As próximas mudanças vão junto.";
    }

    [RelayCommand(CanExecute = nameof(PodeEmitir))]
    private void Emitir()
    {
        if (!SalvarSePreciso())
            return;
        var o = Ordem!;
        Executar(() => o.Emitir(Agora), $"Emitir a {o.Codigo}",
            $"{o.Codigo} emitida para {o.Dados.Executante}. Gere o PDF para assinar (ou mande pelo Telegram).");
    }

    [RelayCommand(CanExecute = nameof(PodeAceitar))]
    private void Aceitar()
    {
        var o = Ordem!;
        Executar(() => o.Aceitar(Agora), $"{o.Codigo} aceita", $"{o.Codigo} aceita pelo executante.");
    }

    [RelayCommand(CanExecute = nameof(PodeAceitar))]
    private async Task Recusar()
    {
        var o = Ordem!;
        if (await _dialogos.PedirTextoAsync("Recusar a OS", $"Por que a {o.Codigo} foi recusada? (fica no histórico)") is not { } motivo)
            return;
        Executar(() => o.Recusar(Agora, motivo), $"{o.Codigo} recusada",
            $"{o.Codigo} recusada. Para corrigir e emitir de novo, clique em Voltar a rascunho.");
    }

    [RelayCommand(CanExecute = nameof(PodeIniciar))]
    private void Iniciar()
    {
        var o = Ordem!;
        Executar(() => o.Iniciar(Agora), $"Iniciar a {o.Codigo}", $"{o.Codigo} em execução.");
    }

    [RelayCommand(CanExecute = nameof(PodeConcluir))]
    private void Concluir()
    {
        var o = Ordem!;
        Executar(() => o.Concluir(Agora), $"Concluir a {o.Codigo}", $"{o.Codigo} concluída pelo executante: falta a conferência.");
    }

    /// <summary>Aprova o serviço; o que ainda não foi medido vira a medição final (Bloco 60), e as tarefas vão ao % medido.</summary>
    [RelayCommand(CanExecute = nameof(PodeConferir))]
    private void Conferir()
    {
        var o = Ordem!;
        var falta = o.PorMedir;
        Executar(() => o.MedirOQueFalta(Guid.NewGuid(), Hoje, Agora).Conferir(Agora), $"Conferir a {o.Codigo}",
            falta > 0 ? $"{o.Codigo} conferida e aprovada; o que faltava medir ({Moeda.ComSimbolo(falta)}) virou a medição final, para pagar."
                : $"{o.Codigo} conferida e aprovada.", atualizarTarefas: true);
    }

    [RelayCommand(CanExecute = nameof(PodeConferir))]
    private async Task Reprovar()
    {
        var o = Ordem!;
        if (await _dialogos.PedirTextoAsync("Reprovar na conferência", $"O que precisa ser refeito na {o.Codigo}? (fica no histórico)") is not { } motivo)
            return;
        Executar(() => o.Reprovar(Agora, motivo), $"Reprovar a {o.Codigo}", $"{o.Codigo} reprovada: voltou à execução.");
    }

    [RelayCommand(CanExecute = nameof(PodeVoltarARascunho))]
    private void VoltarARascunho()
    {
        var o = Ordem!;
        Executar(() => o.VoltarARascunho(Agora), $"{o.Codigo} de volta a rascunho", $"{o.Codigo} voltou a rascunho: corrija e emita de novo.");
    }

    [RelayCommand(CanExecute = nameof(PodeCancelar))]
    private async Task Cancelar()
    {
        var o = Ordem!;
        if (await _dialogos.PedirTextoAsync("Cancelar a OS", $"Por que cancelar a {o.Codigo}? (fica no histórico)") is not { } motivo)
            return;
        Executar(() => o.Cancelar(Agora, motivo), $"Cancelar a {o.Codigo}", $"{o.Codigo} cancelada.");
    }
}
