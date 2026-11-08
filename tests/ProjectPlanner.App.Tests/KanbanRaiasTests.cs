using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Kanban;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 78 (o plano "Delete, arrastar e Kanban", K6 e os ganchos de IA do K7): as raias por resumo e por responsável, o
// checklist sugerido pela IA, o resumo da semana pelo assistente e o quadro no retrato da IA. Estrutura: 1 Fundação (1.1
// Escavação, 1.2 Sapatas 40%) · 2 Estrutura (2.1 Pilares) · 3 Entrega (marco).
public class KanbanRaiasTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _fundacao, _escavacao, _sapatas, _estrutura, _pilares, _entrega;

    public KanbanRaiasTests()
    {
        _vm = _ambiente.CriarViewModel();
        _fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 1);
        _escavacao = Subtarefa(_fundacao, "Escavação");
        _sapatas = Subtarefa(_fundacao, "Sapatas");
        _sapatas.PercentualConcluido = 40;
        _estrutura = Ambiente.AdicionarTarefa(_vm, "Estrutura", 1);
        _pilares = Subtarefa(_estrutura, "Pilares");
        _entrega = Ambiente.AdicionarTarefa(_vm, "Entrega", 0);
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

    // "1 Fundação: A fazer[Escavação] Em andamento[Sapatas] | …"
    private string Raias() => string.Join(" | ", _vm.Kanban.RaiasNaTela.Select(r =>
        $"{r.Nome}: " + string.Join(" ", r.Celulas.Where(c => c.Cards.Count > 0).Select(c => $"{c.Coluna.Nome}[{string.Join(", ", c.Cards.Select(k => k.Nome))}]"))));

    private CelulaNoKanban Celula(string raia, string coluna) =>
        _vm.Kanban.RaiasNaTela.Single(r => r.Nome == raia).Celulas.Single(c => c.Coluna.Nome == coluna);

    [Fact]
    public void RaiasPorResumo_PeloSeletor_FicamNoProjeto_EOSeletorVoltaNoDesfazer()
    {
        _vm.Kanban.ComRaias.Should().BeFalse();

        _vm.Kanban.Raias = _vm.Kanban.OpcoesDeRaias.Single(o => o.Modo == ModoDasRaias.PorResumo);

        (_vm.Cronograma.Quadro.Raias, _vm.Kanban.ComRaias, _vm.TextoDesfazer).Should().Be((ModoDasRaias.PorResumo, true, "Desfazer Raias por resumo"));
        Raias().Should().Be("1 Fundação: A fazer[Escavação] Em andamento[Sapatas] | 2 Estrutura: A fazer[Pilares] | Sem resumo: A fazer[Entrega]");
        _vm.Kanban.RaiasNaTela.Select(r => r.Contagem).Should().Equal("2 cards", "1 card", "1 card");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        (_vm.Kanban.Raias!.Modo, _vm.Kanban.ComRaias, _vm.Kanban.RaiasNaTela.Count).Should().Be((ModoDasRaias.Nenhuma, false, 0));
    }

    // Bloco 87: cada raia recolhe e expande (recolhida, só a contagem de cada coluna), com o andamento dela no cabeçalho.
    [Fact]
    public void Raias_RecolherEExpandir_AContagemDeCadaColuna_EOAndamento()
    {
        _vm.DefinirModoDasRaias(ModoDasRaias.PorResumo);
        var fundacao = _vm.Kanban.RaiasNaTela.Single(r => r.Nome == "1 Fundação");

        (fundacao.Recolhida, fundacao.Seta, fundacao.Andamento, fundacao.Percentual).Should().Be((false, "▾", "20% concluído", 20),
            "Escavação 0% e Sapatas 40%, com a mesma duração");
        (Celula("1 Fundação", "A fazer").Contagem, Celula("1 Fundação", "Concluída").Contagem).Should().Be(("1 card", "—"));
        _vm.Kanban.RaiasNaTela.Select(r => r.Andamento).Should().Equal("20% concluído", "0% concluído", "0% concluído");

        fundacao.AlternarCommand.Execute(null);
        (fundacao.Recolhida, fundacao.Seta).Should().Be((true, "▸"));
        _vm.Kanban.ExpandirRaiasCommand.Execute(null);
        _vm.Kanban.RaiasNaTela.Should().OnlyContain(r => !r.Recolhida);
        _vm.Kanban.RecolherRaiasCommand.Execute(null);
        _vm.Kanban.RaiasNaTela.Should().OnlyContain(r => r.Recolhida);
        _vm.TextoDesfazer.Should().Be("Desfazer Raias por resumo", "recolher é só da tela: não entra no Desfazer");
    }

    // Bloco 87: a raia que aparece com tudo concluído já vem recolhida; o que a pessoa escolhe fica ao mudar o quadro; com um filtro,
    // as raias sem nenhum card que passe nele saem (e voltam como estavam).
    [Fact]
    public void RaiaConcluida_JaVemRecolhida_AEscolhaFica_EComFiltroAsVaziasSaem()
    {
        _escavacao.PercentualConcluido = 100;
        _sapatas.PercentualConcluido = 100;
        _vm.DefinirModoDasRaias(ModoDasRaias.PorResumo);
        var fundacao = _vm.Kanban.RaiasNaTela.Single(r => r.Nome == "1 Fundação");
        _vm.Kanban.RaiasNaTela.Select(r => r.Recolhida).Should().Equal(true, false, false);

        fundacao.AlternarCommand.Execute(null);
        _pilares.PercentualConcluido = 30;
        _vm.Kanban.RaiasNaTela.Single(r => r.Nome == "1 Fundação").Should().BeSameAs(fundacao).And.Match<RaiaNoKanban>(r => !r.Recolhida);

        _vm.Kanban.Busca = "Pilares";
        _vm.Kanban.RaiasNaTela.Select(r => r.Nome).Should().Equal("2 Estrutura");
        _vm.Kanban.Busca = "";
        _vm.Kanban.RaiasNaTela.Select(r => (r.Nome, r.Recolhida)).Should().Equal(("1 Fundação", false), ("2 Estrutura", false), ("Sem resumo", false));
    }

    [Fact]
    public void ArrastarParaOutraRaia_MudaOResumo_ATarefaVaiParaOFimDoGrupo_NumPassoDoDesfazer()
    {
        _vm.DefinirModoDasRaias(ModoDasRaias.PorResumo);
        var destino = Celula("2 Estrutura", "Em andamento");

        _vm.Kanban.PreverNaCelula([_escavacao.Tarefa.Id], destino, 0)!.Dica.Should().Be("Mover para a raia \"2 Estrutura\" · Mover para \"Em andamento\"");
        _vm.Kanban.SoltarNaCelula([_escavacao.Tarefa.Id], destino, 0).Should().BeTrue();

        _vm.Tarefas.Select(l => $"{l.Edt} {l.Nome}").Should().Equal("1 Fundação", "1.1 Sapatas", "2 Estrutura", "2.1 Pilares", "2.2 Escavação", "3 Entrega");
        Raias().Should().Be("1 Fundação: Em andamento[Sapatas] | 2 Estrutura: A fazer[Pilares] Em andamento[Escavação] | Sem resumo: A fazer[Entrega]");
        _vm.AvisoComDesfazer.Should().Be("\"Escavação\" agora está em \"Estrutura\" (no fim do grupo). \"Escavação\" foi para \"Em andamento\".");
        _vm.TextoDesfazer.Should().Be("Desfazer Mover \"Escavação\" para a raia 2 Estrutura");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Tarefas.Select(l => l.Nome).Should().Equal("Fundação", "Escavação", "Sapatas", "Estrutura", "Pilares", "Entrega");
        Raias().Should().StartWith("1 Fundação: A fazer[Escavação] Em andamento[Sapatas]");
    }

    [Fact]
    public void NaMesmaRaia_ArrastarEhComoNaColuna()
    {
        _vm.DefinirModoDasRaias(ModoDasRaias.PorResumo);

        _vm.Kanban.SoltarNaCelula([_escavacao.Tarefa.Id], Celula("1 Fundação", "Concluída"), 0).Should().BeTrue();

        _escavacao.PercentualConcluido.Should().Be(100);
        _vm.Tarefas.Select(l => l.Nome).Should().Equal(["Fundação", "Escavação", "Sapatas", "Estrutura", "Pilares", "Entrega"], "o resumo não mudou");
        _vm.TextoDesfazer.Should().Be("Desfazer Mover \"Escavação\" para Concluída");
    }

    [Fact]
    public void RaiasPorResponsavel_ArrastarTrocaOProfissional()
    {
        var recursos = _vm.Cronograma.Recursos;
        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, "dia", 200);
        var armador = new Recurso(Guid.NewGuid(), "Armador", TipoDeRecurso.Profissional, "dia", 230);
        recursos.Adicionar(pedreiro);
        recursos.Adicionar(armador);
        recursos.Atribuir(_sapatas.Tarefa, [new AtribuicaoDeRecurso(pedreiro.Id, 2)]);
        _vm.DefinirModoDasRaias(ModoDasRaias.PorResponsavel);
        Raias().Should().Be("Pedreiro: Em andamento[Sapatas] | Armador:  | Sem responsável: A fazer[Escavação, Pilares, Entrega]");

        _vm.Kanban.SoltarNaCelula([_sapatas.Tarefa.Id], Celula("Armador", "Em andamento"), 0).Should().BeTrue();

        _sapatas.Tarefa.Atribuicoes.Should().Equal(new AtribuicaoDeRecurso(armador.Id, 2));
        _vm.AvisoComDesfazer.Should().Be("\"Sapatas\" agora é de Armador (era Pedreiro).");
        Raias().Should().StartWith("Pedreiro:  | Armador: Em andamento[Sapatas]");
        _vm.Kanban.CardDe(_sapatas.Tarefa.Id)!.Responsaveis.Should().Be("Armador");
    }

    [Fact]
    public async Task SugerirItens_DaIA_EntramDesmarcados_SemRepetir_NumPassoDoDesfazer()
    {
        _vm.Kanban.AbrirCard(_sapatas.Tarefa.Id);
        var painel = _vm.Kanban.Painel!;
        painel.NovoItem = "Marcar os eixos";
        painel.AdicionarItemCommand.Execute(null);
        _vm.Cronograma.Assistente.Ficha.Definir("Área construída", "120 m²");

        await painel.SugerirItensCommand.ExecuteAsync(null);

        var pedido = _ambiente.Sugestor.Pedidos.Single();
        pedido.Should().Match<PedidoDeChecklist>(p => p.Tarefa == "Sapatas" && p.Resumo == "Fundação" && p.Duracao == 1);
        (pedido.JaNoChecklist.Single(), pedido.Ficha.Single()).Should().Be(("Marcar os eixos", "Área construída: 120 m²"));
        painel.Checklist.Select(i => (i.Texto, i.Feito)).Should().Equal(
            ("Marcar os eixos", false), ("Conferir o material", false), ("Conferir o prumo", false));
        (_vm.Mensagem, _vm.TextoDesfazer).Should().Be(("A IA sugeriu 2 itens para \"Sapatas\": revise e tire os que não servem.",
            "Desfazer Checklist sugerido pela IA para \"Sapatas\""));
        _sapatas.PercentualConcluido.Should().Be(40);

        _ambiente.Sugestor.Erro = new ErroDoAssistenteException("Sem conexão com a IA.");
        await painel.SugerirItensCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().Be("Sem conexão com a IA.");
        _ambiente.Sugestor.EstaConfigurado = false;
        await painel.SugerirItensCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().Be(AnalistaIndisponivel.Mensagem);
    }

    [Fact]
    public async Task ResumoDaSemana_PeloAssistente_ComOQuadroNoRetrato()
    {
        _ambiente.Assistente.EstaConfigurado = true;
        _vm.Kanban.AbrirCard(_sapatas.Tarefa.Id);
        _vm.Kanban.Painel!.NovoItem = "Conferir a armação";
        _vm.Kanban.Painel.AdicionarItemCommand.Execute(null);

        await _vm.Kanban.ResumoDaSemanaCommand.ExecuteAsync(null);

        _vm.Assistente.Visivel.Should().BeTrue();
        var pedido = _ambiente.Assistente.Pedidos.Single();
        pedido.Mensagem.Should().Be(MainWindowViewModel.PedidoDoResumoDaSemana);
        pedido.Contexto!.Quadro.Should().Equal(
            "A fazer (tipo A fazer): Nº 2, Nº 5, Nº 6",
            "Em andamento (tipo Em andamento): Nº 3",
            "Impedida (tipo Em andamento): vazia",
            "Concluída (tipo Concluída): vazia",
            "Checklists: Nº 3 0 de 1");
        pedido.Contexto.ComoTexto().Should().Contain("Quadro Kanban (cada card é uma tarefa sem subtarefas, pelo Nº;")
            .And.Contain("- Em andamento (tipo Em andamento): Nº 3");
    }
}
