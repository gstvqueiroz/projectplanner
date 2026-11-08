using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.App.Tests;

// Bloco 76 (o plano "Delete, arrastar e Kanban", K2): o menu das colunas do quadro no programa, sem a janela: criar, renomear,
// colorir, limite de cards (WIP), mover, mudar o tipo e excluir (perguntando para onde vão os cards), cada um num passo do
// Desfazer. Tarefas: A 0%, B 40% e C 100%.
public class KanbanColunasTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public KanbanColunasTests()
    {
        _vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(_vm, "A", 2);
        Ambiente.AdicionarTarefa(_vm, "B", 2).PercentualConcluido = 40;
        Ambiente.AdicionarTarefa(_vm, "C", 2).PercentualConcluido = 100;
        _vm.ModoVisualizacao = ModoVisualizacao.Kanban;
    }

    private ColunaNoKanban Coluna(string nome) => _vm.Kanban.Colunas.Single(c => c.Nome == nome);

    private string Quadro() => string.Join(" | ", _vm.Kanban.Colunas.Select(c => $"{c.Nome}: {string.Join(", ", c.Cards.Select(k => k.Nome))}"));

    [Fact]
    public async Task AdicionarColuna_EntraAntesDeConcluida_NumPassoDoDesfazer_SemRecalcular()
    {
        var calculos = _ambiente.Calculadora.Chamadas;
        _ambiente.Dialogos.TextoDigitado = "Em revisão";

        await _vm.Kanban.AdicionarColunaCommand.ExecuteAsync(null);

        _vm.Kanban.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Impedida", "Em revisão", "Concluída");
        (_vm.Mensagem, _vm.TextoDesfazer).Should().Be(("Coluna \"Em revisão\" adicionada.", "Desfazer Adicionar coluna \"Em revisão\""));
        _ambiente.Calculadora.Chamadas.Should().Be(calculos, "as colunas não mudam datas");
        _ambiente.Dialogos.TextosPedidos.Single().ValorInicial.Should().Be("Em revisão");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Kanban.Colunas.Should().HaveCount(4);
    }

    [Fact]
    public async Task Renomear_ComNomeRepetido_ERecusado_SemMudarNada()
    {
        _ambiente.Dialogos.TextoDigitado = "concluída";
        await Coluna("Impedida").RenomearCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().Be("Já existe a coluna \"Concluída\" no quadro.");
        _vm.TextoDesfazer.Should().NotContain("coluna");

        _ambiente.Dialogos.TextoDigitado = "Parada";
        await Coluna("Impedida").RenomearCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().Be("A coluna \"Impedida\" agora é \"Parada\".");
        Quadro().Should().Contain("Parada: ");
    }

    [Fact]
    public async Task Limite_DestacaAColunaQuePassou_EVazioTiraOLimite()
    {
        Coluna("Em andamento").PassouDoLimite.Should().BeFalse();
        _ambiente.Dialogos.TextoDigitado = "1";
        await Coluna("A fazer").DefinirLimiteCommand.ExecuteAsync(null);
        (Coluna("A fazer").Contagem, Coluna("A fazer").PassouDoLimite).Should().Be(("1/1", false));

        _vm.MoverCards([_vm.Tarefas[1].Tarefa.Id], Coluna("A fazer").Id, 0); // B vai para A fazer (0%)
        (Coluna("A fazer").Contagem, Coluna("A fazer").PassouDoLimite).Should().Be(("2/1", true), "passou do limite: destacada (WIP)");

        _ambiente.Dialogos.TextoDigitado = "dois";
        await Coluna("A fazer").DefinirLimiteCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().Be("O limite de cards é um número inteiro (vazio: sem limite).");
        _ambiente.Dialogos.TextoDigitado = "";
        await Coluna("A fazer").DefinirLimiteCommand.ExecuteAsync(null);
        (Coluna("A fazer").Contagem, Coluna("A fazer").PassouDoLimite, _vm.Mensagem).Should().Be(("2", false, "A coluna \"A fazer\" não tem mais limite de cards."));
    }

    [Fact]
    public void CorEMover_ComOsCardsJunto()
    {
        Coluna("Impedida").ColorirCommand.Execute("#8E4EC6");
        Coluna("Impedida").MoverParaAEsquerdaCommand.Execute(null);

        _vm.Kanban.Colunas.Select(c => (c.Nome, c.Cor)).Should().Equal(
            ("A fazer", "#8A8F98"), ("Impedida", "#8E4EC6"), ("Em andamento", "#2F80ED"), ("Concluída", "#30A46C"));
        (Coluna("A fazer").PodeIrParaAEsquerda, Coluna("Concluída").PodeIrParaADireita, Coluna("Impedida").PodeIrParaAEsquerda)
            .Should().Be((false, false, true));
        _vm.Mensagem.Should().Be("A coluna \"Impedida\" agora é a 2ª do quadro.");
        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Kanban.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Impedida", "Concluída");
    }

    [Fact]
    public void MudarTipo_RecusaSeOsCardsNaoCombinam()
    {
        Coluna("Em andamento").MudarTipoCommand.Execute(TipoDeColuna.Concluida);
        _vm.Mensagem.Should().StartWith("A coluna \"Em andamento\" tem cards que não combinam com o tipo Concluída (\"B\" está com 40%)");

        Coluna("Impedida").MudarTipoCommand.Execute(TipoDeColuna.Concluida); // vazia
        (Coluna("Impedida").Tipo, _vm.Mensagem).Should().Be((TipoDeColuna.Concluida,
            "A coluna \"Impedida\" agora é do tipo Concluída: o card que entrar nela vai a 100%."));
    }

    [Fact]
    public async Task ExcluirColunaComCards_PerguntaParaOndeVao_EAplicaARegraDoPercentual()
    {
        _ambiente.Dialogos.Escolha = 2; // "Concluída"

        await Coluna("Em andamento").ExcluirCommand.ExecuteAsync(null);

        var (mensagem, opcoes) = _ambiente.Dialogos.Escolhas.Single();
        mensagem.Should().Be("A coluna \"Em andamento\" tem 1 card. Para onde ele vai? As tarefas continuam no cronograma.");
        opcoes.Should().Equal("Mover para \"Impedida\"", "Mover para \"A fazer\" (o % volta a 0)", "Mover para \"Concluída\" (vão a 100%)");
        Quadro().Should().Be("A fazer: A | Impedida:  | Concluída: C, B");
        _vm.Tarefas[1].PercentualConcluido.Should().Be(100);
        _vm.AvisoComDesfazer.Should().Be("Coluna \"Em andamento\" excluída. \"B\" foi para \"Concluída\": 100% concluída.");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        Quadro().Should().Be("A fazer: A | Em andamento: B | Impedida:  | Concluída: C");
        _vm.Tarefas[1].PercentualConcluido.Should().Be(40);
    }

    [Fact]
    public async Task ExcluirAUltimaDoTipo_EscolhaCancelada_EColunaVazia()
    {
        await Coluna("Concluída").ExcluirCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().StartWith("\"Concluída\" é a única coluna do tipo Concluída");
        _ambiente.Dialogos.Escolhas.Should().BeEmpty();

        _ambiente.Dialogos.Escolha = null; // Cancelar
        await Coluna("Em andamento").ExcluirCommand.ExecuteAsync(null);
        _vm.Kanban.Colunas.Should().HaveCount(4);

        await Coluna("Impedida").ExcluirCommand.ExecuteAsync(null); // vazia: sai sem perguntar
        _vm.Kanban.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Concluída");
        _ambiente.Dialogos.Escolhas.Should().ContainSingle("só a do Em andamento, que tinha card");
        _vm.AvisoComDesfazer.Should().Be("Coluna \"Impedida\" excluída.");
    }
}
