using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.Tests;

// Bloco 77 (o plano "Delete, arrastar e Kanban", K4 e K5): o painel do card, o checklist, as etiquetas, arquivar, "+ Adicionar
// card" e os filtros, no programa sem a janela. Início do projeto 21/09/2026 (hoje é 28/09): 1 Fundação (1.1 Escavação, 3 dias:
// atrasada) · 2 Formas (7 dias, 40%: vence nesta semana) · 3 Pintura (20 dias).
public class KanbanCardsTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _fundacao, _escavacao, _formas, _pintura;

    public KanbanCardsTests()
    {
        _vm = _ambiente.CriarViewModel();
        _vm.InicioProjeto = new DateTime(2026, 9, 21);
        _fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 1);
        _vm.TarefaSelecionada = _fundacao;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        _escavacao = _vm.TarefaSelecionada!;
        _escavacao.Nome = "Escavação";
        _escavacao.Duracao = 3;
        _formas = Ambiente.AdicionarTarefa(_vm, "Formas", 7);
        _formas.PercentualConcluido = 40;
        _pintura = Ambiente.AdicionarTarefa(_vm, "Pintura", 20);
        _vm.ModoVisualizacao = ModoVisualizacao.Kanban;
    }

    private ColunaNoKanban Coluna(string nome) => _vm.Kanban.Colunas.Single(c => c.Nome == nome);

    private string Quadro() => string.Join(" | ", _vm.Kanban.Colunas.Select(c => $"{c.Nome}: {string.Join(", ", c.Cards.Select(k => k.Nome))}"));

    private PainelDoCard Abrir(TarefaViewModel linha)
    {
        _vm.Kanban.AbrirCard(linha.Tarefa.Id);
        return _vm.Kanban.Painel!;
    }

    [Fact]
    public void Painel_TemOsCamposDaTarefa_EEditarAliMudaNoCronograma_EODesfazerRefazOPainel()
    {
        var painel = Abrir(_formas);

        (painel.Linha, painel.Identificacao, painel.Coluna, painel.Resumo).Should().Be((_formas, "2 · Nº 3", "Em andamento", null));
        Abrir(_escavacao).Resumo.Should().Be("Fundação");
        painel = Abrir(_formas);

        painel.Linha!.PercentualConcluido = 100;
        painel.Coluna.Should().Be("Concluída", "o card seguiu o % (K3)");
        Quadro().Should().EndWith("Concluída: Formas");
        painel.Linha.Nome = "Formas e armação";
        _vm.Kanban.CardDe(_formas.Tarefa.Id)!.Nome.Should().Be("Formas e armação");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.DesfazerAlteracaoCommand.Execute(null);

        _vm.Kanban.Painel.Should().BeSameAs(painel, "o painel continua aberto");
        painel.Linha.Should().NotBeSameAs(_formas, "as linhas foram refeitas pelo Desfazer");
        (painel.Linha!.Nome, painel.Linha.PercentualConcluido, painel.Coluna).Should().Be(("Formas", 40, "Em andamento"));
        _vm.Kanban.FecharCardCommand.Execute(null);
        _vm.Kanban.Painel.Should().BeNull();
    }

    [Fact]
    public void Checklist_NaoMudaOPercentual_NemRecalcula_ECadaMudancaEUmPasso()
    {
        var painel = Abrir(_formas);
        var calculos = _ambiente.Calculadora.Chamadas;

        painel.NovoItem = "Montar as formas";
        painel.AdicionarItemCommand.Execute(null);
        painel.NovoItem = "Conferir o prumo";
        painel.AdicionarItemCommand.Execute(null);
        painel.Checklist[0].Feito = true;

        (painel.NovoItem, painel.ProgressoDoChecklist).Should().Be(("", "1 de 2"));
        _vm.Kanban.CardDe(_formas.Tarefa.Id)!.Checklist.Should().Be("1/2");
        _formas.PercentualConcluido.Should().Be(40, "o checklist não muda o % (decisão do plano)");
        _ambiente.Calculadora.Chamadas.Should().Be(calculos);
        _vm.TextoDesfazer.Should().Be("Desfazer Marcar item do checklist de \"Formas\"");

        painel.Checklist[1].Texto = "Conferir o prumo e o nível";
        painel.Checklist[0].RemoverCommand.Execute(null);
        painel.Checklist.Select(i => (i.Texto, i.Feito)).Should().Equal(("Conferir o prumo e o nível", false));

        _vm.DesfazerAlteracaoCommand.Execute(null);
        painel.Checklist.Select(i => (i.Texto, i.Feito)).Should().Equal(("Montar as formas", true), ("Conferir o prumo e o nível", false));
    }

    [Fact]
    public async Task Etiquetas_NovaPeloPainel_JaMarcada_EOFiltroPorEtiqueta()
    {
        var painel = Abrir(_formas);
        _ambiente.Dialogos.TextoDigitado = "Urgente";

        await painel.NovaEtiquetaCommand.ExecuteAsync(null);

        painel.Etiquetas.Select(e => (e.Nome, e.Marcada)).Should().Equal(("Urgente", true));
        _vm.Kanban.CardDe(_formas.Tarefa.Id)!.Etiquetas.Should().Equal(new EtiquetaNoCard("Urgente", "#E5484D"));
        _vm.Mensagem.Should().Be("Etiqueta \"Urgente\" criada e marcada em \"Formas\".");

        _vm.Kanban.FiltroEtiqueta = _vm.Kanban.OpcoesDeEtiqueta.Single(o => o.Nome == "Urgente");
        Quadro().Should().Be("A fazer:  | Em andamento: Formas | Impedida:  | Concluída: ");
        Coluna("A fazer").Contagem.Should().Be("0 de 2");

        Abrir(_pintura).Etiquetas.Single().Marcada = true;
        Quadro().Should().Be("A fazer: Pintura | Em andamento: Formas | Impedida:  | Concluída: ");
        painel = _vm.Kanban.Painel!;
        painel.Etiquetas.Single().Marcada = false;
        Quadro().Should().Be("A fazer:  | Em andamento: Formas | Impedida:  | Concluída: ");
    }

    [Fact]
    public void Arquivar_TiraDoQuadro_ATarefaFica_EOsArquivadosTrazemDeVolta()
    {
        var painel = Abrir(_formas);

        painel.ArquivarCommand.Execute(null);

        Quadro().Should().Be("A fazer: Escavação, Pintura | Em andamento:  | Impedida:  | Concluída: ");
        _vm.Tarefas.Should().Contain(_formas, "a tarefa continua no cronograma");
        (_vm.Kanban.TextoDosArquivados, _vm.Kanban.Arquivados.Single().Nome).Should().Be(("Arquivados (1)", "Formas"));
        _vm.AvisoComDesfazer.Should().StartWith("Card \"Formas\" arquivado");
        painel.Arquivado.Should().BeTrue("o painel continua aberto, com Desarquivar");

        painel.DesarquivarCommand.Execute(null);
        Quadro().Should().Be("A fazer: Escavação, Pintura | Em andamento: Formas | Impedida:  | Concluída: ");
        _vm.Kanban.TextoDosArquivados.Should().Be("Arquivados (0)");
    }

    [Fact]
    public async Task AdicionarCard_NoFimDoCronograma_ComARegraDoPercentualDaColuna()
    {
        _ambiente.Dialogos.TextoDigitado = "Limpeza final";

        await Coluna("Concluída").AdicionarCardCommand.ExecuteAsync(null);

        var nova = _vm.Tarefas[^1];
        (nova.Nome, nova.Duracao, nova.PercentualConcluido, nova.Tarefa.TarefaPaiId).Should().Be(("Limpeza final", 1, 100, null));
        Quadro().Should().EndWith("Concluída: Limpeza final");
        _vm.TarefaSelecionada.Should().Be(nova);
        (_vm.Mensagem, _vm.TextoDesfazer).Should().Be(("Card \"Limpeza final\" criado em \"Concluída\": é a tarefa 5 do cronograma, já com 100%.",
            "Desfazer Adicionar card \"Limpeza final\""));

        _ambiente.Dialogos.TextoDigitado = "  ";
        await Coluna("A fazer").AdicionarCardCommand.ExecuteAsync(null);
        _vm.Tarefas.Should().HaveCount(5, "sem nome, não cria");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Tarefas.Select(l => l.Nome).Should().Equal("Fundação", "Escavação", "Formas", "Pintura");
    }

    [Fact]
    public void Filtros_BuscaSemAcento_Responsavel_Resumo_Atrasadas_EDaSemana()
    {
        _vm.Kanban.Busca = "ESCAVACAO";
        Quadro().Should().Be("A fazer: Escavação | Em andamento:  | Impedida:  | Concluída: ");
        _vm.Kanban.HaFiltro.Should().BeTrue();
        _vm.Kanban.LimparFiltrosCommand.Execute(null);
        _vm.Kanban.HaFiltro.Should().BeFalse();

        _vm.Kanban.FiltroResumo = _vm.Kanban.OpcoesDeResumo.Single(o => o.Nome == "1 Fundação");
        Quadro().Should().Be("A fazer: Escavação | Em andamento:  | Impedida:  | Concluída: ");
        _vm.Kanban.LimparFiltrosCommand.Execute(null);

        _vm.Kanban.SoAtrasadas = true;
        Quadro().Should().Be("A fazer: Escavação | Em andamento:  | Impedida:  | Concluída: ", "término 23/09, antes de hoje (28/09)");
        (_vm.Kanban.SoAtrasadas, _vm.Kanban.SoDestaSemana) = (false, true);
        Quadro().Should().Be("A fazer:  | Em andamento: Formas | Impedida:  | Concluída: ", "término 29/09, até domingo (04/10)");
        _vm.Kanban.SoDestaSemana = false;

        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, "dia", 200);
        _vm.Cronograma.Recursos.Adicionar(pedreiro);
        _vm.Cronograma.Recursos.Atribuir(_pintura.Tarefa, [new AtribuicaoDeRecurso(pedreiro.Id, 1)]);
        _vm.RecalcularCronogramaCommand.Execute(null);
        _vm.Kanban.OpcoesDeResponsavel.Select(o => o.Nome).Should().Equal("Todos os responsáveis", "Pedreiro");
        _vm.Kanban.FiltroResponsavel = _vm.Kanban.OpcoesDeResponsavel[1];
        Quadro().Should().Be("A fazer: Pintura | Em andamento:  | Impedida:  | Concluída: ");

        _vm.Kanban.Busca = "nada disso";
        _vm.Kanban.NenhumNoFiltro.Should().BeTrue();
    }

    [Fact]
    public void SoltarComFiltro_CaiAntesDoCardVisivelDaquelaPosicao()
    {
        // A fazer: Escavação, Pintura. Com a busca "pintura", só Pintura aparece; soltar Formas na lacuna 0 (antes de Pintura)
        // é antes de Pintura entre todos: depois de Escavação.
        _vm.Kanban.Busca = "pintura";
        Coluna("A fazer").Cards.Select(k => k.Nome).Should().Equal("Pintura");

        _vm.Kanban.Soltar([_formas.Tarefa.Id], Coluna("A fazer").Id, 0).Should().BeTrue();

        _vm.Kanban.LimparFiltrosCommand.Execute(null);
        Coluna("A fazer").Cards.Select(k => k.Nome).Should().Equal("Escavação", "Formas", "Pintura");
        _formas.PercentualConcluido.Should().Be(0, "foi para A fazer");
    }

    [Fact]
    public async Task ExcluirERecursos_PeloPainel()
    {
        await Abrir(_pintura).RecursosCommand.ExecuteAsync(null);
        _ambiente.Dialogos.RecursosDasTarefas.Should().ContainSingle();
        _vm.TarefaSelecionada.Should().Be(_pintura);

        await _vm.Kanban.Painel!.ExcluirCommand.ExecuteAsync(null);

        _vm.Tarefas.Select(l => l.Nome).Should().Equal("Fundação", "Escavação", "Formas");
        _vm.Kanban.Painel.Should().BeNull("a tarefa saiu: o painel fecha");
    }
}
