using ProjectPlanner.IA;

namespace ProjectPlanner.App.Services;

/// <summary>Como terminou a tentativa de aplicar as alterações de uma resposta do assistente.</summary>
public enum SituacaoDaAplicacao
{
    /// <summary>Todas aplicadas; dá para desfazer pela <see cref="ResultadoDaAplicacao.Marca"/>.</summary>
    Aplicada,

    /// <summary>Uma alteração não valia: nenhuma foi aplicada (o texto diz qual e por quê).</summary>
    Recusada,

    /// <summary>O usuário não quis aplicar (removeria tarefas com fotos).</summary>
    Cancelada,
}

/// <param name="Texto">Aplicada: o resumo ("12 tarefas criadas · …"); recusada: o motivo; cancelada: o aviso.</param>
/// <param name="Marca">Identifica a aplicação para o Desfazer (só numa aplicada).</param>
public sealed record ResultadoDaAplicacao(SituacaoDaAplicacao Situacao, string Texto, object? Marca = null);

/// <summary>
/// O cronograma aberto, como o painel do assistente o usa (Bloco 24): o retrato que vai com cada mensagem, a aplicação
/// das alterações que voltam na resposta (todas ou nenhuma) e o Desfazer da última aplicação.
/// </summary>
public interface ICronogramaDoAssistente
{
    ContextoDoProjeto Retrato();

    /// <summary>Bloco 38: abre o orçamento na aba Sugestões da IA, pedindo sugestões com este foco, e espera a janela fechar.</summary>
    Task AbrirSugestoesDeOrcamentoAsync(string pedido);

    /// <summary>Aplica as alterações, achando as tarefas existentes pelo <paramref name="retrato"/> mandado com o pedido.</summary>
    Task<ResultadoDaAplicacao> AplicarAsync(IReadOnlyList<AlteracaoDoCronograma> alteracoes, ContextoDoProjeto retrato);

    /// <summary>
    /// Se a aplicação ainda pode ser desfeita: está no histórico do Desfazer (Bloco 30: qualquer uma, não só a última),
    /// e o projeto não foi trocado.
    /// </summary>
    bool PodeDesfazer(object marca);

    /// <summary>Bloco 30: a aplicação foi desfeita (pelo Desfazer da conversa ou pelo Desfazer geral) e não refeita.</summary>
    bool FoiDesfeita(object marca);

    /// <summary>
    /// Volta o cronograma para como estava antes da aplicação. Se houve alterações depois dela, elas também são desfeitas
    /// (perguntando antes); tudo pode voltar com o Refazer.
    /// </summary>
    /// <returns>Se desfez.</returns>
    Task<bool> DesfazerAsync(object marca);

    /// <summary>O histórico do Desfazer mudou (alteração, desfazer, refazer, outro projeto).</summary>
    event Action? DesfazerMudou;

    /// <summary>A conversa e a ficha do projeto aberto (Bloco 26), guardadas no .pplan.</summary>
    Core.Assistente.ConversaDoProjeto Conversa { get; }

    /// <summary>
    /// A ficha, o modo ou a aprovação do plano mudaram: o projeto passa a ter alterações não salvas. As mensagens sozinhas
    /// não marcam o projeto (vão junto quando ele for salvo).
    /// </summary>
    void ConversaMudou();
}
