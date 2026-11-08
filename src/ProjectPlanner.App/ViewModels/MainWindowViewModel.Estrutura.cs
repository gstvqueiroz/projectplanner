using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Edicao;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de MainWindowViewModel.cs): a estrutura do cronograma: adicionar, inserir, subtarefa, rebaixar, promover, remover e arrastar.
public sealed partial class MainWindowViewModel
{
    /// <summary>Adiciona uma tarefa no fim da lista, no primeiro nível.</summary>
    [RelayCommand]
    private void AdicionarTarefa()
    {
        var tarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        _cronograma.AdicionarTarefa(tarefa);
        AposMudarAEstrutura(selecionar: tarefa, "Adicionar tarefa");
    }

    /// <summary>
    /// Tarefa > Inserir tarefa acima e o menu de contexto da grid (Bloco 21): uma tarefa nova logo antes da
    /// tarefa da linha (ou da selecionada), entre as irmãs dela.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeInserirJuntoDe))]
    private void InserirTarefaAcima(TarefaViewModel? linha) => InserirJuntoDe(linha ?? TarefaSelecionada!, depois: false);

    /// <summary>
    /// Inserir tarefa abaixo: logo depois da tarefa da linha (ou da selecionada) e das subtarefas dela, no mesmo
    /// nível (abaixo de um resumo, depois do resumo inteiro; subtarefa nova é com Adicionar subtarefa).
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeInserirJuntoDe))]
    private void InserirTarefaAbaixo(TarefaViewModel? linha) => InserirJuntoDe(linha ?? TarefaSelecionada!, depois: true);

    private bool PodeInserirJuntoDe(TarefaViewModel? linha) => (linha ?? TarefaSelecionada) is not null;

    // A tarefa nova é como a do Adicionar tarefa; só a posição muda. O cronograma é recalculado depois, como
    // em qualquer tarefa nova (ela entra no CPM).
    private void InserirJuntoDe(TarefaViewModel referencia, bool depois)
    {
        var pai = referencia.Tarefa.TarefaPaiId;
        var posicao = _cronograma.Tarefas.Where(t => t.TarefaPaiId == pai).ToList().IndexOf(referencia.Tarefa) + (depois ? 1 : 0);
        var tarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        if (MudarEstrutura(() => _cronograma.InserirTarefaNaPosicao(tarefa, pai, posicao)))
            AposMudarAEstrutura(selecionar: tarefa, "Inserir tarefa");
    }

    /// <summary>Acrescenta um marco (tarefa de duração 0) no fim da lista, como o Adicionar tarefa.</summary>
    [RelayCommand]
    private void AdicionarMarco()
    {
        var marco = new Tarefa("Novo marco", _cronograma.DataInicio, 0);
        _cronograma.AdicionarTarefa(marco);
        AposMudarAEstrutura(selecionar: marco, "Adicionar marco");
    }

    /// <summary>
    /// Acrescenta no fim da lista um resumo com uma subtarefa dentro (um resumo sem subtarefas seria só
    /// uma tarefa), e seleciona a subtarefa, como o MS Project faz sem nada selecionado.
    /// </summary>
    [RelayCommand]
    private void AdicionarResumo()
    {
        var resumo = new Tarefa("Novo resumo", _cronograma.DataInicio, 1);
        var subtarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        _cronograma.AdicionarTarefa(resumo);
        _cronograma.AdicionarTarefa(subtarefa, resumo.Id);
        SincronizarLinhas();
        Expandir(LinhaDe(resumo.Id));
        AposMudarAEstrutura(selecionar: subtarefa, "Adicionar resumo");
    }

    /// <summary>Adiciona uma tarefa como última subtarefa da selecionada, que vira resumo (e é expandido).</summary>
    [RelayCommand(CanExecute = nameof(HaTarefaSelecionada))]
    private void AdicionarSubtarefa()
    {
        var pai = TarefaSelecionada!;
        var tarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        if (!MudarEstrutura(() => _cronograma.AdicionarTarefa(tarefa, pai.Tarefa.Id)))
            return;
        Expandir(pai);
        AposMudarAEstrutura(selecionar: tarefa, $"Adicionar subtarefa em \"{pai.Nome}\"");
    }

    /// <summary>
    /// Recua a selecionada um nível (Alt+Shift+→): ela vira a última subtarefa da tarefa de cima no
    /// mesmo nível, que vira resumo (e é expandido).
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeRebaixar))]
    private void Rebaixar()
    {
        var linha = TarefaSelecionada!;
        if (!MudarEstrutura(() => _cronograma.Rebaixar(linha.Tarefa.Id)))
            return;
        Expandir(LinhaDe(linha.Tarefa.TarefaPaiId!.Value));
        AposMudarAEstrutura(selecionar: linha.Tarefa, $"Rebaixar \"{linha.Nome}\"");
    }

    /// <summary>
    /// Sobe a selecionada um nível (Alt+Shift+←): ela sai do resumo em que está e vai para logo depois
    /// dele; as tarefas abaixo dela continuam no resumo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodePromover))]
    private void Promover()
    {
        var linha = TarefaSelecionada!;
        if (MudarEstrutura(() => _cronograma.Promover(linha.Tarefa.Id)))
            AposMudarAEstrutura(selecionar: linha.Tarefa, $"Promover \"{linha.Nome}\"");
    }

    /// <summary>Remove a selecionada; um resumo sai com todas as subtarefas, depois de confirmar.</summary>
    /// <summary>
    /// Bloco 72 (o plano "Excluir com Delete"): exclui as selecionadas pelas regras de <see cref="RegrasDeExclusao"/>. Tarefa
    /// comum ou marco sai na hora; com resumo na seleção, a janela pergunta se sai o grupo inteiro ou só o resumo; com
    /// progresso registrado, fotos ou mais de 10 tarefas, pergunta antes. Tudo num passo só do Desfazer, com o aviso
    /// "Desfazer" por alguns segundos; a seleção passa para a tarefa que ocupou o lugar (ou a de cima, se era a última).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HaTarefaSelecionada))]
    private async Task RemoverTarefa()
    {
        var selecionadas = Selecionadas;
        var plano = RegrasDeExclusao.Planejar(_cronograma, selecionadas.Select(l => l.Tarefa.Id));
        if (plano.Vazio)
            return;
        var modo = ModoDosResumos.GrupoInteiro;
        if (plano.TemResumo)
        {
            var escolha = await _dialogos.EscolherAsync(plano.Resumos.Count == 1 ? "Excluir resumo" : "Excluir resumos", plano.PerguntaDoResumo,
                ["Excluir grupo inteiro", plano.Resumos.Count == 1 ? "Excluir só o resumo" : "Excluir só os resumos"]);
            if (escolha is null)
                return;
            modo = escolha == 1 ? ModoDosResumos.SoOResumo : ModoDosResumos.GrupoInteiro;
        }
        else if (plano.PedeConfirmacao && !await _dialogos.ConfirmarAsync("Excluir tarefas", plano.PerguntaSemResumo, "Excluir"))
            return;

        var ids = selecionadas.Select(l => l.Tarefa.Id).ToHashSet();
        var lugar = LinhasVisiveis.Select((l, i) => (l, i)).Where(x => ids.Contains(x.l.Tarefa.Id)).Select(x => x.i).DefaultIfEmpty(0).Min();
        var substituta = EhKanban ? Kanban.Substituta(ids) : null; // Bloco 75 (E7 no quadro): o card que fica no lugar
        try
        {
            RegrasDeExclusao.Aplicar(_cronograma.Copiar(), plano, modo); // primeiro numa cópia: se o domínio recusar, nada muda
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        var resultado = RegrasDeExclusao.Aplicar(_cronograma, plano, modo);
        SincronizarLinhas();
        foreach (var linha in Tarefas)
            linha.AtualizarTela(); // a EDT e as predecessoras (pelo Nº das linhas, que mudou)
        TarefaSelecionada = EhKanban ? (substituta is { } s ? Tarefas.FirstOrDefault(l => l.Tarefa.Id == s) : null)
            : LinhasVisiveis.Count == 0 ? null : LinhasVisiveis[Math.Clamp(lugar, 0, LinhasVisiveis.Count - 1)];
        MarcarAlteracao(plano.Selecionadas.Count == 1 ? $"Excluir \"{plano.Selecionadas[0].Nome}\"" : $"Excluir {plano.Selecionadas.Count} tarefas");
        Mensagem = resultado.Texto;
        AvisoComDesfazer = resultado.Texto;
    }

    private bool HaTarefaSelecionada() => TarefaSelecionada is not null;

    /// <summary>
    /// Bloco 73: onde as tarefas arrastadas cairiam na lacuna <paramref name="lacuna"/> entre as linhas visíveis (0 é antes da
    /// primeira), com o puxão para o lado (1: subtarefa da linha de cima; −1: sobe um nível), pelas regras de
    /// <see cref="RegrasDeMovimento"/> (as mesmas no Gantt). Não muda nada.
    /// </summary>
    public PlanoDeMovimento PreverMovimento(IReadOnlyList<TarefaViewModel> arrastadas, int lacuna, int puxao = 0)
    {
        ArgumentNullException.ThrowIfNull(arrastadas);
        return RegrasDeMovimento.Planejar(_cronograma, [.. LinhasVisiveis.Select(l => l.Tarefa.Id)], arrastadas.Select(l => l.Tarefa.Id), lacuna, puxao);
    }

    /// <summary>
    /// Bloco 73 (o plano "Arrastar para mover", M1 a M7): solta as tarefas arrastadas. Muda só a ordem e a hierarquia, nunca
    /// datas nem vínculos; só reordenar não recalcula (redesenha o Gantt na ordem nova), e mudar de resumo recalcula os resumos.
    /// Um passo só do Desfazer. Num destino bloqueado, avisa o motivo; fora das linhas, não faz nada.
    /// </summary>
    /// <returns>Se mudou algo.</returns>
    public bool MoverArrastando(IReadOnlyList<TarefaViewModel> arrastadas, int lacuna, int puxao = 0)
    {
        var plano = PreverMovimento(arrastadas, lacuna, puxao);
        if (!plano.Valido)
        {
            if (plano.Motivo is { } motivo)
                Mensagem = motivo;
            return false;
        }
        if (!plano.MudaAlgo)
            return false;
        try
        {
            RegrasDeMovimento.Aplicar(_cronograma.Copiar(), plano); // primeiro numa cópia: se o domínio recusar, nada muda
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        var soAOrdem = plano.Tarefas.All(id => _cronograma.Tarefas.First(t => t.Id == id).TarefaPaiId == plano.Pai) && plano.ViraResumo is null;
        var resultado = RegrasDeMovimento.Aplicar(_cronograma, plano);
        var movidas = plano.Tarefas.Select(id => Tarefas.First(l => l.Tarefa.Id == id)).ToList();
        var descricao = movidas.Count == 1 ? $"Mover \"{movidas[0].Nome}\"" : $"Mover {movidas.Count} tarefas";
        SincronizarLinhas();
        foreach (var linha in Tarefas)
            linha.AtualizarTela(); // EDT e predecessoras/sucessoras (que usam o Nº das linhas)
        DefinirSelecao(movidas.Where(LinhasVisiveis.Contains), movidas[0]);
        if (soAOrdem)
        {
            // A ordem não muda datas nem caminho crítico: redesenha o Gantt sem recalcular (Bloco 18).
            Gantt.RedesenharNaNovaOrdem();
            RegistrarNoHistorico(descricao);
            PossuiAlteracoesNaoSalvas = true;
            OnPropertyChanged(nameof(Titulo));
        }
        else
            MarcarAlteracao(descricao);
        Mensagem = resultado.Texto;
        return true;
    }

    /// <summary>Bloco 73 (M7): Alt+Shift+↑ sobe a tarefa uma posição entre as irmãs (pulando grupos inteiros, sem sair do grupo).</summary>
    [RelayCommand(CanExecute = nameof(PodeMoverParaCima))]
    private void MoverParaCima() => MoverEntreIrmas(-1);

    /// <summary>Bloco 73 (M7): Alt+Shift+↓ desce a tarefa uma posição entre as irmãs.</summary>
    [RelayCommand(CanExecute = nameof(PodeMoverParaBaixo))]
    private void MoverParaBaixo() => MoverEntreIrmas(1);

    private bool PodeMoverParaCima() => PosicaoEntreIrmas() is { Posicao: > 0 };

    private bool PodeMoverParaBaixo() => PosicaoEntreIrmas() is { } p && p.Posicao < p.Irmas - 1;

    private (int Posicao, int Irmas)? PosicaoEntreIrmas()
    {
        if (TarefaSelecionada is not { } linha)
            return null;
        var irmas = _cronograma.Tarefas.Where(t => t.TarefaPaiId == linha.Tarefa.TarefaPaiId).ToList();
        return (irmas.IndexOf(linha.Tarefa), irmas.Count);
    }

    private void MoverEntreIrmas(int passo)
    {
        if (TarefaSelecionada is not { } linha || PosicaoEntreIrmas() is not { } atual)
            return;
        if (!_cronograma.MoverEntreIrmas(linha.Tarefa.Id, Math.Clamp(atual.Posicao + passo, 0, atual.Irmas - 1)))
            return;
        SincronizarLinhas();
        foreach (var l in Tarefas)
            l.AtualizarTela();
        TarefaSelecionada = linha;
        Gantt.RedesenharNaNovaOrdem();
        RegistrarNoHistorico($"Mover \"{linha.Nome}\"");
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
        Mensagem = $"\"{linha.Nome}\" agora é a {linha.Edt}.";
        MoverParaCimaCommand.NotifyCanExecuteChanged();
        MoverParaBaixoCommand.NotifyCanExecuteChanged();
    }

    private bool PodePromover() => TarefaSelecionada?.Tarefa.TarefaPaiId is not null;

    // A mesma regra do domínio: há tarefa acima no mesmo nível, e ela pode virar resumo (não é marco nem tem vínculos).
    private bool PodeRebaixar() => TarefaSelecionada is { } linha && _cronograma.PodeRebaixar(linha.Tarefa.Id);

    /// <summary>
    /// Num resumo, duração, datas, % e predecessoras vêm das subtarefas: a grid não entra em edição
    /// nessas células e avisa por quê.
    /// </summary>
    public void AvisarValorCalculadoNoResumo() =>
        Mensagem = "Num resumo, esse valor é calculado a partir das subtarefas.";

    /// <summary>Bloco 36: tentou editar a unidade ou a quantidade de um resumo.</summary>
    public void AvisarSemUnidadeNoResumo() => Mensagem = Tarefa.SemUnidadeNoResumo;

    // Aplica uma mudança de estrutura no cronograma; se o domínio recusar, só mostra o motivo.
    private bool MudarEstrutura(Action mudanca)
    {
        try
        {
            mudanca();
            return true;
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
    }

    // Expande o resumo que acabou de receber uma subtarefa. As linhas visíveis e o Gantt são refeitos
    // logo em seguida (AposMudarAEstrutura), já com a tarefa nova calculada.
    private void Expandir(TarefaViewModel resumo)
    {
        _mudandoEstrutura = true;
        try
        {
            resumo.Expandido = true;
        }
        finally
        {
            _mudandoEstrutura = false;
        }
    }

    private void AposMudarAEstrutura(Tarefa selecionar, string descricao)
    {
        SincronizarLinhas();
        TarefaSelecionada = LinhaDe(selecionar.Id);
        MarcarAlteracao(descricao);
    }
}
