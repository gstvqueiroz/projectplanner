using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Kanban;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 80: o quadro Kanban pela IA (no painel do assistente e pelo Telegram, que chegam ao mesmo cartão): o tipo "quadro", com
// mover o card de coluna (o % segue a coluna), itens e marcar no checklist, etiquetas, arquivar, coluna nova e raias.
// Tarefas: 1 Escavação (0%), 2 Formas (40%), 3 Concretagem (0%).
public class KanbanPelaIaTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public KanbanPelaIaTests()
    {
        _vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(_vm, "Escavação", 2);
        Ambiente.AdicionarTarefa(_vm, "Formas", 3).PercentualConcluido = 40;
        Ambiente.AdicionarTarefa(_vm, "Concretagem", 1);
    }

    private static AtualizacaoDaObra Q(string acao, string? tarefa = null, string? coluna = null, string? descricao = null, string? nome = null) =>
        new("quadro", tarefa, Descricao: descricao, Nome: nome, Acao: acao, Coluna: coluna);

    private string Quadro() => string.Join(" | ", _vm.Kanban.Colunas.Select(c => $"{c.Nome}: {string.Join(", ", c.Cards.Select(k => k.Nome))}"));

    [Fact]
    public async Task TodasAsAcoes_PeloCartao_NumPassoDoDesfazer()
    {
        var cartao = _vm.ConferirAtualizacoes([
            Q("mover", "2", "impedida"),
            Q("mover", "1", "Concluída"),
            Q("item", "3", descricao: "Conferir as formas; Lançar o concreto"),
            Q("marcarItem", "3", descricao: "conferir as formas"),
            Q("etiqueta", "3", nome: "Urgente"),
            Q("coluna", nome: "Em revisão"),
            Q("raias", nome: "resumo"),
        ], _vm.RetratoParaOAssistente());

        cartao.Select(i => (i.Texto, i.Problema)).Should().Equal(
            ("2 Formas: card para \"impedida\"", null),
            ("1 Escavação: card para \"Concluída\" (100%)", null),
            ("3 Concretagem: checklist + Conferir as formas; Lançar o concreto", null),
            ("3 Concretagem: marcar \"conferir as formas\" no checklist", null),
            ("3 Concretagem: etiqueta \"Urgente\"", null),
            ("Quadro: coluna nova \"Em revisão\" (antes de Concluída)", null),
            ("Quadro: raias resumo", null));

        var resultado = await _vm.AplicarAtualizacoesAsync([.. cartao.Select(i => i.Operacao!)], "Quadro");

        resultado.Situacao.Should().Be(SituacaoDaAplicacao.Aplicada);
        (_vm.Tarefas[0].PercentualConcluido, _vm.Tarefas[1].PercentualConcluido).Should().Be((100, 40), "o % seguiu a coluna; Impedida não muda o %");
        var quadro = _vm.Cronograma.Quadro;
        quadro.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Impedida", "Em revisão", "Concluída");
        quadro.Raias.Should().Be(ModoDasRaias.PorResumo);
        var concretagem = quadro.CardDe(_vm.Tarefas[2].Tarefa.Id)!;
        concretagem.Checklist.Should().Equal(new ItemDoChecklist("Conferir as formas", true), new ItemDoChecklist("Lançar o concreto", false));
        quadro.Etiquetas.Single().Nome.Should().Be("Urgente", "a etiqueta que não existia foi criada");
        concretagem.Etiquetas.Should().Equal(quadro.Etiquetas.Single().Id);
        _vm.Kanban.CardDe(_vm.Tarefas[1].Tarefa.Id).Should().NotBeNull();
        _vm.Kanban.ColunaDe(_vm.Tarefas[1].Tarefa.Id)!.Nome.Should().Be("Impedida");

        _vm.TextoDesfazer.Should().Be("Desfazer Atualizações do assistente");
        _vm.DesfazerAlteracaoCommand.Execute(null);
        (_vm.Cronograma.Quadro.Colunas.Count, _vm.Cronograma.Quadro.Raias, _vm.Tarefas[0].PercentualConcluido).Should().Be((4, ModoDasRaias.Nenhuma, 0));
    }

    [Fact]
    public async Task ArquivarTirarEtiqueta_EOsProblemasSaoExplicados()
    {
        await _vm.AplicarAtualizacoesAsync([.. _vm.ConferirAtualizacoes([Q("etiqueta", "1", nome: "Elétrica")], _vm.RetratoParaOAssistente())
            .Select(i => i.Operacao!)], "Etiqueta");

        var cartao = _vm.ConferirAtualizacoes([
            Q("arquivar", "1"),
            Q("tirarEtiqueta", "1", nome: "eletrica"),
            Q("mover", "2", "Revisão"),
            Q("marcarItem", "2", descricao: "nada"),
            Q("mover", "9", "Concluída"),
            Q("raias", nome: "por cor"),
            Q("pular", "1"),
            Q("mover", "3"),
        ], _vm.RetratoParaOAssistente());

        cartao.Select(i => i.Problema).Should().Equal(
            null,
            null,
            "O quadro não tem a coluna \"Revisão\" (tem A fazer, Em andamento, Impedida, Concluída).",
            "O checklist de \"Formas\" não tem o item \"nada\".",
            "não há a tarefa nº 9 no cronograma",
            "Raias: resumo, responsavel ou nenhuma.",
            "ação desconhecida (mover, item, marcarItem, etiqueta, tirarEtiqueta, arquivar, desarquivar, coluna, raias)",
            "informe a coluna");

        await _vm.AplicarAtualizacoesAsync([.. cartao.Take(2).Select(i => i.Operacao!)], "Arquivar");
        Quadro().Should().Be("A fazer: Concretagem | Em andamento: Formas | Impedida:  | Concluída: ");
        _vm.Cronograma.Quadro.CardDe(_vm.Tarefas[0].Tarefa.Id)!.Should().Match<CardDoQuadro>(k => k.Arquivado && k.Etiquetas.Count == 0);

        // Mover um card arquivado o traz de volta, na coluna pedida.
        await _vm.AplicarAtualizacoesAsync([.. _vm.ConferirAtualizacoes([Q("mover", "1", "em andamento")], _vm.RetratoParaOAssistente())
            .Select(i => i.Operacao!)], "Mover");
        Quadro().Should().Be("A fazer: Concretagem | Em andamento: Formas, Escavação | Impedida:  | Concluída: ");
    }

    [Fact]
    public async Task Retrato_TemAsColunasEAsEtiquetasDoProjeto_ParaAIAUsarOsNomes()
    {
        await _vm.AplicarAtualizacoesAsync([.. _vm.ConferirAtualizacoes([Q("etiqueta", "2", nome: "Urgente")], _vm.RetratoParaOAssistente())
            .Select(i => i.Operacao!)], "Etiqueta");

        _vm.RetratoParaOAssistente().Quadro.Should().Contain("Etiquetas do projeto: Urgente")
            .And.Contain("Impedida (tipo Em andamento): vazia");
        InstrucoesDoAssistente.Controle.Should().Contain("tipo quadro com acao mover");
        CatalogoDaIa.Referencia.Should().Contain("quadro: acao, tarefa, coluna, descricao, nome");
    }
}
