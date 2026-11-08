using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.App.ViewModels;

// Bloco 75 (o plano "Delete, arrastar e Kanban", K1 a K7): o quadro Kanban, uma visão das mesmas tarefas. Clicar num card
// seleciona a tarefa (a mesma seleção da grade e do Gantt); soltar cards numa coluna aplica as regras do quadro (o % segue o
// card) num passo só do Desfazer; o Delete num card exclui a tarefa com as regras de sempre. Bloco 76: o menu das colunas.
public sealed partial class MainWindowViewModel : IAcoesDasColunas
{
    /// <summary>O quadro Kanban (Exibir > Kanban), refeito a cada alteração, como o Gantt.</summary>
    public KanbanViewModel Kanban { get; } = new();

    private void LigarOQuadro()
    {
        Kanban.CardClicado += ClicarNoCard;
        Kanban.VazioClicado += () => TarefaSelecionada = null;
        Kanban.PreverMovimento = PreverMovimentoNoQuadro;
        Kanban.Mover = MoverCards;
        Kanban.Acoes = this;
        Kanban.AcoesDosCards = this; // Bloco 77
        Kanban.LinhaDa = tarefaId => Tarefas.FirstOrDefault(l => l.Tarefa.Id == tarefaId);
        Kanban.DefinirModoDasRaias = DefinirModoDasRaias; // Bloco 78
        Kanban.MoverParaRaia = MoverParaRaia;
    }

    // ---- Bloco 78 (K6): as raias ----

    /// <summary>Sem raias, por resumo ou por responsável: fica no projeto e é um passo do Desfazer.</summary>
    public void DefinirModoDasRaias(ModoDasRaias modo)
    {
        if (_cronograma.Quadro.Raias == modo)
            return;
        var nome = modo switch { ModoDasRaias.PorResumo => "por resumo", ModoDasRaias.PorResponsavel => "por responsável", _ => "desligadas" };
        MudarOQuadro($"Raias {nome}", () =>
        {
            RegrasDasRaias.DefinirModo(_cronograma, modo);
            return (modo switch
            {
                ModoDasRaias.PorResumo => "Raias por resumo: arrastar um card para outra raia muda o resumo da tarefa (ela vai para o fim do grupo).",
                ModoDasRaias.PorResponsavel => "Raias por responsável: arrastar um card para outra raia troca o profissional da tarefa.",
                _ => "Quadro sem raias.",
            }, false);
        });
    }

    /// <summary>
    /// K6: leva os cards para outra raia (muda o resumo da tarefa, que vai para o fim do grupo, ou o responsável) e para a coluna,
    /// na lacuna entre todos os cards dela, com a regra do % de lá. Um passo só do Desfazer, com o aviso.
    /// </summary>
    /// <returns>Se mudou algo.</returns>
    public bool MoverParaRaia(IReadOnlyList<Guid> tarefas, string raia, Guid colunaId, int lacuna)
    {
        ArgumentNullException.ThrowIfNull(tarefas);
        var modo = _cronograma.Quadro.Raias;
        if (modo == ModoDasRaias.Nenhuma || tarefas.Count == 0)
            return false;
        try
        {
            var copia = _cronograma.Copiar(); // primeiro numa cópia: se o domínio recusar, nada muda
            RegrasDasRaias.MoverParaRaia(copia, modo, tarefas, raia);
            if (RegrasDoQuadro.Planejar(copia, tarefas, colunaId, lacuna) is { Valido: true, MudaAlgo: true } teste)
                RegrasDoQuadro.Aplicar(copia, teste);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        var nomeDaRaia = RegrasDasRaias.Raias(_cronograma, modo).First(r => r.Chave == raia).Nome;
        var colunasAntes = tarefas.ToDictionary(id => id, id => _cronograma.Quadro.CardDe(id)?.ColunaId);
        var resultado = RegrasDasRaias.MoverParaRaia(_cronograma, modo, tarefas, raia);
        var plano = RegrasDoQuadro.Planejar(_cronograma, tarefas, colunaId, lacuna);
        string? daColuna = null;
        if (plano is { Valido: true, MudaAlgo: true })
        {
            var texto = RegrasDoQuadro.Aplicar(_cronograma, plano).Texto;
            if (plano.Tarefas.Any(id => colunasAntes[id] != colunaId))
                daColuna = texto;
        }
        if (resultado.MudouAEstrutura)
        {
            SincronizarLinhas();
            foreach (var linha in Tarefas)
                linha.AtualizarTela(); // a EDT e as predecessoras (pelo Nº das linhas, que mudou)
        }
        var movidas = tarefas.Select(id => Tarefas.FirstOrDefault(l => l.Tarefa.Id == id)).OfType<TarefaViewModel>().ToList();
        DefinirSelecao(movidas, movidas.FirstOrDefault());
        MarcarAlteracao(movidas.Count == 1 ? $"Mover \"{movidas[0].Nome}\" para a raia {nomeDaRaia}" : $"Mover {movidas.Count} cards para a raia {nomeDaRaia}");
        Mensagem = string.Join(" ", new[] { resultado.Texto, daColuna }.Where(t => !string.IsNullOrEmpty(t)));
        AvisoComDesfazer = Mensagem;
        return true;
    }

    /// <summary>O que aconteceria ao soltar os cards das tarefas na coluna, na lacuna dada (0 é antes do primeiro card). Não muda nada.</summary>
    public PlanoDoQuadro PreverMovimentoNoQuadro(IReadOnlyList<Guid> tarefas, Guid colunaId, int lacuna) =>
        RegrasDoQuadro.Planejar(_cronograma, tarefas, colunaId, lacuna);

    /// <summary>
    /// K3 e K5: solta os cards na coluna. Entrar numa Concluída põe 100%; sair dela volta ao % de antes; entrar numa A fazer
    /// põe 0% (com o Desfazer no aviso); reordenar não muda o cronograma (nem recalcula). Um passo só do Desfazer.
    /// </summary>
    /// <returns>Se mudou algo.</returns>
    public bool MoverCards(IReadOnlyList<Guid> tarefas, Guid colunaId, int lacuna)
    {
        ArgumentNullException.ThrowIfNull(tarefas);
        var plano = PreverMovimentoNoQuadro(tarefas, colunaId, lacuna);
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
            RegrasDoQuadro.Aplicar(_cronograma.Copiar(), plano); // primeiro numa cópia: se o domínio recusar, nada muda
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        var resultado = RegrasDoQuadro.Aplicar(_cronograma, plano);
        DefinirSelecao(plano.Tarefas.Select(LinhaDe), LinhaDe(plano.Tarefas[0]));
        RegistrarMudancaDoQuadro(resultado.Descricao, recalcular: !plano.SoOQuadro);
        Mensagem = resultado.Texto;
        if (plano.ZeraProgresso)
            AvisoComDesfazer = resultado.Texto;
        return true;
    }

    /// <summary>
    /// Um clique num card: sozinho seleciona só a tarefa; com Ctrl (⌘) acrescenta ou tira; com Shift, o intervalo na ordem do
    /// quadro. Vale também para uma tarefa que a grade não mostra (dentro de um resumo recolhido).
    /// </summary>
    public void ClicarNoCard(Guid tarefaId, bool adicionar, bool intervalo)
    {
        if (Tarefas.FirstOrDefault(l => l.Tarefa.Id == tarefaId) is not { } linha)
        {
            TarefaSelecionada = null;
            return;
        }
        if (adicionar)
        {
            var lista = Selecionadas.ToList();
            if (!lista.Remove(linha))
                lista.Add(linha);
            DefinirSelecao(lista, lista.Contains(linha) ? linha : null);
            return;
        }
        var ordem = Kanban.OrdemDosCards.ToList();
        if (intervalo && TarefaSelecionada is { } ancora && ordem.IndexOf(ancora.Tarefa.Id) is var de and >= 0 && ordem.IndexOf(tarefaId) is var ate and >= 0)
            DefinirSelecao(ordem.Skip(Math.Min(de, ate)).Take(Math.Abs(ate - de) + 1).Select(LinhaDe), linha);
        else
            DefinirSelecao([linha], linha);
    }

    // ---- Bloco 76 (K2): as colunas ----

    async Task IAcoesDasColunas.AdicionarColunaAsync()
    {
        var nome = await _dialogos.PedirTextoAsync("Nova coluna",
            "Nome da coluna nova (ela entra antes de Concluída, do tipo Em andamento; o tipo e o lugar mudam pelo menu ⋯ dela):", "Em revisão");
        if (nome is null)
            return;
        MudarOQuadro($"Adicionar coluna \"{nome.Trim()}\"", () => ($"Coluna \"{RegrasDasColunas.Adicionar(_cronograma, nome).Nome}\" adicionada.", false));
    }

    async Task IAcoesDasColunas.RenomearColunaAsync(Guid colunaId)
    {
        if (_cronograma.Quadro.Coluna(colunaId) is not { } coluna)
            return;
        var nome = await _dialogos.PedirTextoAsync("Renomear coluna", "Nome da coluna:", coluna.Nome);
        if (nome is null || nome.Trim() == coluna.Nome)
            return;
        MudarOQuadro($"Renomear coluna \"{coluna.Nome}\"", () =>
        {
            RegrasDasColunas.Renomear(_cronograma, colunaId, nome);
            return ($"A coluna \"{coluna.Nome}\" agora é \"{nome.Trim()}\".", false);
        });
    }

    void IAcoesDasColunas.ColorirColuna(Guid colunaId, string cor)
    {
        if (_cronograma.Quadro.Coluna(colunaId) is not { } coluna || coluna.Cor == cor)
            return;
        MudarOQuadro($"Cor da coluna \"{coluna.Nome}\"", () =>
        {
            RegrasDasColunas.Colorir(_cronograma, colunaId, cor);
            return ($"Cor da coluna \"{coluna.Nome}\" mudada.", false);
        });
    }

    async Task IAcoesDasColunas.DefinirLimiteDaColunaAsync(Guid colunaId)
    {
        if (_cronograma.Quadro.Coluna(colunaId) is not { } coluna)
            return;
        var texto = await _dialogos.PedirTextoAsync("Limite de cards",
            $"Quantos cards a coluna \"{coluna.Nome}\" pode ter? Passou disso, ela fica destacada. Deixe vazio para não ter limite.",
            coluna.Limite?.ToString(System.Globalization.CultureInfo.CurrentCulture));
        if (texto is null)
            return;
        int? limite = null;
        if (!string.IsNullOrWhiteSpace(texto))
        {
            if (!int.TryParse(texto.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out var numero))
            {
                Mensagem = "O limite de cards é um número inteiro (vazio: sem limite).";
                return;
            }
            limite = numero;
        }
        if (limite == coluna.Limite)
            return;
        MudarOQuadro($"Limite de cards de \"{coluna.Nome}\"", () =>
        {
            RegrasDasColunas.DefinirLimite(_cronograma, colunaId, limite);
            return (limite is { } l ? $"A coluna \"{coluna.Nome}\" fica destacada com mais de {l} cards." : $"A coluna \"{coluna.Nome}\" não tem mais limite de cards.", false);
        });
    }

    void IAcoesDasColunas.MoverColuna(Guid colunaId, int passo)
    {
        if (_cronograma.Quadro.Coluna(colunaId) is not { } coluna)
            return;
        var indice = _cronograma.Quadro.Colunas.ToList().IndexOf(coluna) + Math.Sign(passo);
        if (indice < 0 || indice >= _cronograma.Quadro.Colunas.Count)
            return;
        MudarOQuadro($"Mover coluna \"{coluna.Nome}\"", () =>
        {
            RegrasDasColunas.Mover(_cronograma, colunaId, passo);
            return ($"A coluna \"{coluna.Nome}\" agora é a {indice + 1}ª do quadro.", false);
        });
    }

    void IAcoesDasColunas.MudarTipoDaColuna(Guid colunaId, TipoDeColuna tipo)
    {
        if (_cronograma.Quadro.Coluna(colunaId) is not { } coluna || coluna.Tipo == tipo)
            return;
        MudarOQuadro($"Tipo da coluna \"{coluna.Nome}\"", () =>
        {
            RegrasDasColunas.MudarTipo(_cronograma, colunaId, tipo);
            return ($"A coluna \"{coluna.Nome}\" agora é do tipo {QuadroDoProjeto.NomeDoTipo(tipo)}" + tipo switch
            {
                TipoDeColuna.Concluida => ": o card que entrar nela vai a 100%.",
                TipoDeColuna.AFazer => ": o card que entrar nela vai a 0%.",
                _ => ": o card que entrar nela mantém o %.",
            }, false);
        });
    }

    /// <summary>K2: excluir uma coluna com cards pergunta para onde eles vão (com a regra do % de lá); a última de um tipo não sai.</summary>
    async Task IAcoesDasColunas.ExcluirColunaAsync(Guid colunaId)
    {
        if (_cronograma.Quadro.Coluna(colunaId) is null)
            return;
        var plano = RegrasDasColunas.PlanejarExclusao(_cronograma, colunaId);
        if (plano.Motivo is { } motivo)
        {
            Mensagem = motivo;
            return;
        }
        Guid? destino = null;
        if (plano.Cards > 0)
        {
            var opcoes = plano.Destinos.Select(d => $"Mover para \"{d.Nome}\"" + (d.Tipo == plano.Coluna.Tipo ? "" : d.Tipo switch
            {
                TipoDeColuna.Concluida => " (vão a 100%)",
                TipoDeColuna.AFazer => " (o % volta a 0)",
                _ => plano.Coluna.Tipo == TipoDeColuna.Concluida ? " (voltam ao % de antes)" : "",
            })).ToList();
            var cards = plano.Cards == 1 ? "1 card" : $"{plano.Cards} cards";
            var escolha = await _dialogos.EscolherAsync("Excluir coluna",
                $"A coluna \"{plano.Coluna.Nome}\" tem {cards}. Para onde {(plano.Cards == 1 ? "ele vai" : "eles vão")}? As tarefas continuam no cronograma.", opcoes);
            if (escolha is not { } i || i < 0 || i >= plano.Destinos.Count)
                return;
            destino = plano.Destinos[i].Id;
        }
        if (MudarOQuadro($"Excluir coluna \"{plano.Coluna.Nome}\"", () =>
            {
                var resultado = RegrasDasColunas.Excluir(_cronograma, colunaId, destino);
                return (resultado.Texto, resultado.MudouPercentual);
            }))
            AvisoComDesfazer = Mensagem;
    }

    // Uma mudança no quadro (colunas, cards, etiquetas): um passo do Desfazer; só recalcula se algum % mudou. As regras conferem
    // antes de mexer: recusada, nada muda e o motivo vai para a barra de status.
    private bool MudarOQuadro(string descricao, Func<(string Texto, bool Recalcular)> mudanca)
    {
        (string Texto, bool Recalcular) resultado;
        try
        {
            resultado = mudanca();
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        RegistrarMudancaDoQuadro(descricao, resultado.Recalcular);
        Mensagem = resultado.Texto;
        return true;
    }

    // Um passo do Desfazer para o que mudou no quadro. Sem % mudado, nenhuma data muda: não recalcula (como reordenar na grade).
    private void RegistrarMudancaDoQuadro(string descricao, bool recalcular)
    {
        if (recalcular)
        {
            MarcarAlteracao(descricao); // o % dos resumos, a Curva S, os indicadores...
            return;
        }
        AvisoComDesfazer = null;
        RegistrarNoHistorico(descricao);
        PossuiAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
        AtualizarQuadro();
    }

    private void AtualizarQuadro() =>
        Kanban.Atualizar(_cronograma, _relogio.GetLocalNow().Date, Selecionadas.Select(l => l.Tarefa.Id).ToHashSet());
}
