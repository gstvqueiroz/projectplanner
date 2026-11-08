using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.Tests;

// Bloco 75 (o plano "Delete, arrastar e Kanban", K1 a K7): o quadro Kanban no programa, sem a janela. Estrutura:
// 1 Fundação (1.1 Escavar, 1.2 Armar) · 2 Estrutura (40%) · 3 Entrega (marco).
public class KanbanTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _fundacao, _escavar, _armar, _estrutura, _entrega;

    public KanbanTests()
    {
        _vm = _ambiente.CriarViewModel();
        _fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _escavar = Subtarefa(_fundacao, "Escavar");
        _armar = Subtarefa(_fundacao, "Armar");
        _estrutura = Ambiente.AdicionarTarefa(_vm, "Estrutura", 2);
        _entrega = Ambiente.AdicionarTarefa(_vm, "Entrega", 0);
        _estrutura.PercentualConcluido = 40;
        _vm.ModoVisualizacao = ModoVisualizacao.Kanban;
    }

    private TarefaViewModel Subtarefa(TarefaViewModel pai, string nome)
    {
        _vm.TarefaSelecionada = pai;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        var linha = _vm.TarefaSelecionada!;
        linha.Nome = nome;
        return linha;
    }

    private ColunaNoKanban Coluna(string nome) => _vm.Kanban.Colunas.Single(c => c.Nome == nome);

    private TarefaViewModel Linha(string nome) => _vm.Tarefas.Single(l => l.Nome == nome);

    // "A fazer: Escavar, Armar, Entrega | Em andamento: Estrutura | Impedida:  | Concluída: "
    private string Quadro() => string.Join(" | ", _vm.Kanban.Colunas.Select(c => $"{c.Nome}: {string.Join(", ", c.Cards.Select(k => k.Nome))}"));

    [Fact]
    public void Quadro_ColunasPadrao_ComOsCardsDasTarefasSemSubtarefas()
    {
        Quadro().Should().Be("A fazer: Escavar, Armar, Entrega | Em andamento: Estrutura | Impedida:  | Concluída: ");
        var escavar = _vm.Kanban.CardDe(_escavar.Tarefa.Id)!;
        (escavar.Resumo, escavar.EhMarco, escavar.Percentual, escavar.Datas, escavar.Atrasada)
            .Should().Be(("Fundação", false, 0, "28/09 – 28/09", false), "o resumo aparece como etiqueta (K1)");
        _vm.Kanban.CardDe(_entrega.Tarefa.Id)!.Should().Match<CardNoKanban>(k => k.EhMarco && k.Datas == "28/09" && k.Resumo == null);
        _vm.Kanban.CardDe(_fundacao.Tarefa.Id).Should().BeNull("o resumo não vira card");
        _vm.Kanban.Colunas.Select(c => c.Contagem).Should().Equal("3", "1", "0", "0");
        _vm.Kanban.SemCards.Should().BeFalse();
    }

    [Fact]
    public void MoverParaConcluida_Poe100NaGrade_NumPassoDoDesfazer()
    {
        _vm.MoverCards([_estrutura.Tarefa.Id], Coluna("Concluída").Id, 0).Should().BeTrue();

        _estrutura.PercentualConcluido.Should().Be(100);
        _vm.Mensagem.Should().Be("\"Estrutura\" foi para \"Concluída\": 100% concluída.");
        _vm.TextoDesfazer.Should().Be("Desfazer Mover \"Estrutura\" para Concluída");
        _vm.Selecionadas.Should().Equal(_estrutura);
        _vm.AvisoComDesfazer.Should().BeNull("o % não se perdeu: só a barra de status avisa");
        Quadro().Should().EndWith("Concluída: Estrutura");

        _vm.DesfazerAlteracaoCommand.Execute(null);

        Linha("Estrutura").PercentualConcluido.Should().Be(40);
        Quadro().Should().Be("A fazer: Escavar, Armar, Entrega | Em andamento: Estrutura | Impedida:  | Concluída: ");
        _vm.RefazerAlteracaoCommand.Execute(null);
        Linha("Estrutura").PercentualConcluido.Should().Be(100);
    }

    [Fact]
    public void SairDeConcluida_VoltaAoPercentualDeAntes_EAFazerZeraComODesfazerNoAviso()
    {
        _vm.MoverCards([_estrutura.Tarefa.Id], Coluna("Concluída").Id, 0);
        _vm.MoverCards([_estrutura.Tarefa.Id], Coluna("Impedida").Id, 0);
        _estrutura.PercentualConcluido.Should().Be(40);

        _vm.MoverCards([_estrutura.Tarefa.Id], Coluna("A fazer").Id, 0);

        _estrutura.PercentualConcluido.Should().Be(0);
        _vm.AvisoComDesfazer.Should().Be("\"Estrutura\" foi para \"A fazer\": o % voltou a 0 (era 40%).");
        _vm.DesfazerAlteracaoCommand.Execute(null);
        Linha("Estrutura").PercentualConcluido.Should().Be(40);
        Quadro().Should().Contain("Impedida: Estrutura");
    }

    [Fact]
    public void Reordenar_NaColuna_NaoRecalcula_MasMarcaOProjeto_EEUmPassoDoDesfazer()
    {
        _ambiente.Dialogos.ArquivoParaSalvar = "/projetos/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);
        var calculos = _ambiente.Calculadora.Chamadas;

        _vm.MoverCards([_entrega.Tarefa.Id], Coluna("A fazer").Id, 0).Should().BeTrue();

        Quadro().Should().StartWith("A fazer: Entrega, Escavar, Armar |");
        _ambiente.Calculadora.Chamadas.Should().Be(calculos, "a ordem dos cards não muda o cronograma");
        _vm.Tarefas.Select(l => l.Nome).Should().Equal("Fundação", "Escavar", "Armar", "Estrutura", "Entrega");
        (_vm.PossuiAlteracoesNaoSalvas, _vm.TextoDesfazer).Should().Be((true, "Desfazer Reordenar \"Entrega\""));
        _vm.MoverCards([_entrega.Tarefa.Id], Coluna("A fazer").Id, 1).Should().BeFalse("soltar no mesmo lugar não muda nada");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        Quadro().Should().StartWith("A fazer: Escavar, Armar, Entrega |");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("voltou a ser o salvo");
    }

    [Fact]
    public void PercentualMudadoNaGrade_LevaOCard()
    {
        _escavar.PercentualConcluido = 30;
        Quadro().Should().Be("A fazer: Armar, Entrega | Em andamento: Estrutura, Escavar | Impedida:  | Concluída: ");

        _escavar.PercentualConcluido = 100;
        Quadro().Should().EndWith("Concluída: Escavar");

        _escavar.PercentualConcluido = 0;
        Quadro().Should().StartWith("A fazer: Armar, Entrega, Escavar |");
    }

    [Fact]
    public void CliqueNoCard_SelecionaNaGradeENoGantt_ComCtrlEShiftNaOrdemDoQuadro()
    {
        _vm.Kanban.ClicarNoCard(_armar.Tarefa.Id, adicionar: false, intervalo: false);
        (_vm.TarefaSelecionada, _vm.Gantt.TarefaDestacada).Should().Be((_armar, _armar.Tarefa.Id));
        _vm.Kanban.CardDe(_armar.Tarefa.Id)!.Selecionado.Should().BeTrue();

        _vm.Kanban.ClicarNoCard(_estrutura.Tarefa.Id, adicionar: true, intervalo: false);
        _vm.Selecionadas.Should().Equal(_armar, _estrutura);
        _vm.Kanban.Colunas.SelectMany(c => c.Cards).Where(k => k.Selecionado).Select(k => k.Nome).Should().Equal("Armar", "Estrutura");

        // Shift: de Escavar até Estrutura na ordem do quadro (A fazer: Escavar, Armar, Entrega; Em andamento: Estrutura).
        _vm.Kanban.ClicarNoCard(_escavar.Tarefa.Id, adicionar: false, intervalo: false);
        _vm.Kanban.ClicarNoCard(_estrutura.Tarefa.Id, adicionar: false, intervalo: true);
        _vm.Selecionadas.Select(l => l.Nome).Should().Equal("Escavar", "Armar", "Estrutura", "Entrega");

        _vm.Kanban.ClicarNoVazio();
        _vm.Selecionadas.Should().BeEmpty();
        _vm.Kanban.Colunas.SelectMany(c => c.Cards).Should().OnlyContain(k => !k.Selecionado);
    }

    [Fact]
    public void CardDeTarefaDentroDeResumoRecolhido_TambemSeleciona()
    {
        _fundacao.Expandido = false;

        _vm.Kanban.ClicarNoCard(_armar.Tarefa.Id, adicionar: false, intervalo: false);

        _vm.TarefaSelecionada.Should().Be(_armar, "o quadro mostra a tarefa mesmo com o resumo recolhido na grade");
    }

    [Fact]
    public async Task Excluir_NoQuadro_SelecionaOCardQueFicaNoLugar_EODesfazerTrazDeVolta()
    {
        _vm.Kanban.ClicarNoCard(_armar.Tarefa.Id, adicionar: false, intervalo: false);

        await _vm.RemoverTarefaCommand.ExecuteAsync(null);

        Quadro().Should().StartWith("A fazer: Escavar, Entrega |", "o card some do quadro (E7)");
        _vm.TarefaSelecionada!.Nome.Should().Be("Entrega", "o card que ocupou o lugar");
        _vm.AvisoComDesfazer.Should().NotBeNull();
        _vm.DesfazerAlteracaoCommand.Execute(null);
        Quadro().Should().StartWith("A fazer: Escavar, Armar, Entrega |");
    }

    [Fact]
    public void TarefaQueViraResumo_SaiDoQuadro_EASubtarefaNovaEntraEmAFazer()
    {
        var pilares = Subtarefa(_estrutura, "Pilares");

        Quadro().Should().Be("A fazer: Escavar, Armar, Entrega, Pilares | Em andamento:  | Impedida:  | Concluída: ");
        _vm.Kanban.CardDe(pilares.Tarefa.Id)!.Resumo.Should().Be("Estrutura");
    }

    [Fact]
    public void Atrasada_TerminoAntesDeHojeEAbaixoDe100()
    {
        _vm.InicioProjeto = new DateTime(2026, 9, 1);

        _vm.Kanban.CardDe(_escavar.Tarefa.Id)!.Should().Match<CardNoKanban>(k => k.Atrasada && k.Datas == "01/09 – 01/09");
        _escavar.PercentualConcluido = 100;
        _vm.Kanban.CardDe(_escavar.Tarefa.Id)!.Atrasada.Should().BeFalse();
        _vm.Kanban.CardDe(_escavar.Tarefa.Id)!.Dica.Should().Be("1.1 · Escavar · 01/09/2026 a 01/09/2026 · 100% (duplo clique abre o card)");
    }

    [Fact]
    public void Responsaveis_SaoOsProfissionaisDaTarefa()
    {
        var recursos = _vm.Cronograma.Recursos;
        var nomes = new[] { "Pedreiro", "Servente", "Carpinteiro" };
        foreach (var nome in nomes)
            recursos.Adicionar(new Recurso(Guid.NewGuid(), nome, TipoDeRecurso.Profissional, "dia", 200));
        var cimento = new Recurso(Guid.NewGuid(), "Cimento", TipoDeRecurso.Material, "sc", 40);
        recursos.Adicionar(cimento);
        recursos.Atribuir(_escavar.Tarefa, [.. recursos.Lista.Select(r => new AtribuicaoDeRecurso(r.Id, 1))]);
        recursos.Atribuir(_armar.Tarefa, [new AtribuicaoDeRecurso(cimento.Id, 10)]);

        _vm.RecalcularCronogramaCommand.Execute(null);

        _vm.Kanban.CardDe(_escavar.Tarefa.Id)!.Responsaveis.Should().Be("Pedreiro, Servente +1");
        _vm.Kanban.CardDe(_armar.Tarefa.Id)!.Responsaveis.Should().BeNull("material não é responsável");
    }

    [Fact]
    public async Task ArquivoDeAntesDoQuadro_AbreComAsColunasPadrao_DistribuindoPeloPercentual()
    {
        var antigo = new Cronograma("Obra antiga", Ambiente.Segunda);
        foreach (var (nome, percentual) in new[] { ("Limpeza", 100), ("Gabarito", 60), ("Escavação", 0) })
        {
            var tarefa = new Tarefa(nome, Ambiente.Segunda, 2);
            antigo.AdicionarTarefa(tarefa);
            tarefa.AtualizarPercentualConcluido(percentual);
        }
        _ambiente.Repositorio.AoAbrir = _ => antigo;
        _ambiente.Dialogos.ArquivoParaAbrir = "/obras/antiga.pplan";
        _ambiente.Dialogos.RespostaAlteracoesPendentes = Services.RespostaAlteracoesPendentes.NaoSalvar;

        await _vm.AbrirProjetoCommand.ExecuteAsync(null);

        Quadro().Should().Be("A fazer: Escavação | Em andamento: Gabarito | Impedida:  | Concluída: Limpeza");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("montar o quadro ao abrir não é uma alteração");
    }
}
