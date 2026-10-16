using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 38, na tela: Projeto > Orçamento…, aba Sugestões da IA, e o pedido pelo chat do assistente. A base tem a alvenaria
// 87495 (38,60/m²) e o chapisco 87879; tarefas: Alvenaria do térreo (5 d, m²) e Pintura (2 d); o pedreiro é um recurso.
public sealed class SugestoesDeOrcamentoTelaTests
{
    private static readonly BaseDeReferencia Sinapi = new("SINAPI", "SP", "09/2026", true);

    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly Recurso _pedreiro = new(Guid.NewGuid(), "PEDREIRO", TipoDeRecurso.Profissional, null, 224);

    public SugestoesDeOrcamentoTelaTests()
    {
        _ambiente.Biblioteca.Salvar(new TabelaDeReferencia(Sinapi,
            [new Insumo("88309", "PEDREIRO", "H", TipoDeInsumo.MaoDeObra, 28), new Insumo("7271", "BLOCO CERAMICO", "UN", TipoDeInsumo.Material, 1.2m)],
            [
                new Composicao("87495", "ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS 9X19X19 CM", "M2", [new(false, "88309", 0.8m), new(false, "7271", 13.5m)]),
                new Composicao("87879", "CHAPISCO EM ALVENARIAS INTERNAS", "M2", [new(false, "88309", 0.1m)]),
            ]));
        _vm = _ambiente.CriarViewModel();
        _vm.Cronograma.Recursos.Adicionar(_pedreiro); // antes das tarefas: as edições seguintes o levam para o histórico do Desfazer
        Ambiente.AdicionarTarefa(_vm, "Alvenaria do térreo", 5).Unidade = "m²";
        Ambiente.AdicionarTarefa(_vm, "Pintura", 2);
        _vm.Conversa.Ficha.Definir("Área construída", "120 m²");
    }

    private Guid Alvenaria => _vm.Cronograma.Tarefas[0].Id;

    private Guid Pintura => _vm.Cronograma.Tarefas[1].Id;

    private OrcamentoViewModel Abrir()
    {
        _vm.OrcamentoCommand.Execute(null);
        return _ambiente.Dialogos.Orcamentos.Last();
    }

    // A resposta completa: composição e quantidade da alvenaria, estimativa da pintura, BDI, equipe, um código fora da base e um alerta.
    private void ResponderTudo() => _ambiente.Consultor.Responder = _ => new RespostaDoConsultor("Sugeri as vedações e a pintura.",
    [
        new SugestaoDaIA("composicao", "bloco de 9 cm", "T1", Codigo: "87495"),
        new SugestaoDaIA("quantidade", "1,2 × 120 × 2,8 − 15%", "T1", Unidade: "m²", Valor: 342.72m),
        new SugestaoDaIA("estimativa", "faixa usual", "T2", Descricao: "Pintura acrílica", Unidade: "m²", CustoDeMateriais: 8, CustoDeMaoDeObra: 12),
        new SugestaoDaIA("bdi", "faixa do TCU", Valor: 25),
        new SugestaoDaIA("disponivel", "uma equipe", Recurso: "R1", Valor: 2),
        new SugestaoDaIA("composicao", "não existe", "T2", Codigo: "99999"),
        new SugestaoDaIA("alerta", "", Descricao: "Falta o chapisco."),
        new SugestaoDaIA("quantidade", "tarefa que não veio no pedido", "T9", Valor: 3),
    ]);

    [Fact]
    public async Task SemIA_NemTarefasNemGrupos_SoAvisa()
    {
        _ambiente.Consultor.EstaConfigurado = false;
        var tela = Abrir();
        tela.IaConfigurada.Should().BeFalse();
        await tela.PedirSugestoesCommand.ExecuteAsync(null);
        tela.TextoDaConsulta.Should().Be(AnalistaIndisponivel.Mensagem);

        _ambiente.Consultor.EstaConfigurado = true;
        (tela.PedirComposicoes, tela.PedirQuantidades, tela.PedirRevisao, tela.PedirEquipes) = (false, false, false, false);
        await tela.PedirSugestoesCommand.ExecuteAsync(null);
        tela.TextoDaConsulta.Should().Be("Marque o que a IA deve sugerir.");
        _ambiente.Consultor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Pedido_ComABaseAFichaOFocoOsGruposAsCandidatasEOsRecursos()
    {
        var tela = Abrir();
        tela.BaseDasSugestoes.Should().StartWith("As composições vêm da base SINAPI SP 09/2026 desonerado");
        tela.Foco = " só as vedações ";
        tela.PedirEquipes = false;

        await tela.PedirSugestoesCommand.ExecuteAsync(null);

        var pedido = _ambiente.Consultor.Pedidos.Single();
        (pedido.Obra, pedido.Foco, pedido.Grupos, pedido.Base).Should()
            .Be((_vm.Cronograma.Nome, "só as vedações", GruposDeSugestao.Composicoes | GruposDeSugestao.Quantidades | GruposDeSugestao.Revisao,
                "SINAPI SP 09/2026 desonerado"));
        pedido.Ficha.Should().Equal("Área construída: 120 m²");
        pedido.Tarefas.Select(t => (t.Id, t.Edt, t.Nome, t.Dias, t.Unidade)).Should().Equal(("T1", "1", "Alvenaria do térreo", 5, "m²"), ("T2", "2", "Pintura", 2, null));
        pedido.Tarefas[0].Candidatas.Should().Equal(new CandidataParaIA("87495", "ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS 9X19X19 CM", "M2", 38.6m),
            new CandidataParaIA("87879", "CHAPISCO EM ALVENARIAS INTERNAS", "M2", 2.8m));
        pedido.Tarefas[1].Candidatas.Should().BeEmpty();
        pedido.Recursos.Should().Equal(new RecursoParaOrcamento("R1", "PEDREIRO", "Profissional", null, 0));
        tela.TextoDaConsulta.Should().Be("Nada a sugerir. Nenhuma sugestão.");
    }

    [Fact]
    public async Task Pedido_SemBase_SoEstimativas_EAsHorasDaEquipeDaComposicao()
    {
        _ambiente.Biblioteca.Remover(Sinapi);
        var c = _vm.Cronograma;
        c.Tarefas[0].DefinirQuantidade(100);
        c.Orcamento.Associar(c.Tarefas[0], new TabelaDeReferencia(Sinapi, [new Insumo("88309", "PEDREIRO", "H", TipoDeInsumo.MaoDeObra, 28)],
            [new Composicao("1", "ALVENARIA", "M2", [new(false, "88309", 0.8m)])]).Abrir("1"));
        var tela = Abrir();
        tela.BaseDasSugestoes.Should().StartWith("Nenhuma base importada");

        await tela.PedirSugestoesCommand.ExecuteAsync(null);

        var pedido = _ambiente.Consultor.Pedidos.Single();
        pedido.Base.Should().BeNull();
        pedido.Tarefas.Should().AllSatisfy(t => t.Candidatas.Should().BeEmpty());
        pedido.Tarefas.Select(t => t.Id).Should().Equal("T1", "T2");
        pedido.Tarefas[0].Composicao.Should().Be("SINAPI 1 ALVENARIA (M2) · custo 22.40");
        pedido.Tarefas[0].Equipe.Should().Equal([new HorasNaTarefa("PEDREIRO", 80, 2)]); // 0,8 h × 100 m² ÷ 8 h ÷ 5 dias
    }

    [Fact]
    public async Task Lista_ComAsMarcadasOAlertaEAsQueNaoValem()
    {
        ResponderTudo();
        var tela = Abrir();

        await tela.PedirSugestoesCommand.ExecuteAsync(null);

        tela.TextoDaConsulta.Should().Be("Sugeri as vedações e a pintura. 6 sugestões e 1 alerta: marque as que quer e clique em Aplicar marcadas.");
        tela.Sugestoes.Select(s => (s.Grupo, s.Alvo, s.Aceita, s.PodeAceitar)).Should().Equal(
            ("Composição", "1 Alvenaria do térreo", true, true),
            ("Quantidade", "1 Alvenaria do térreo", true, true),
            ("Composição", "2 Pintura", false, false),
            ("Estimativa IA", "2 Pintura", true, true),
            ("Equipe", "PEDREIRO", true, true),
            ("BDI", "Orçamento", true, true),
            ("Alerta", "Orçamento", true, true));
        tela.Sugestoes[0].Texto.Should().Be("SINAPI 87495 — ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS 9X19X19 CM (M2) · custo 38,60");
        tela.Sugestoes[0].Justificativa.Should().Be("bloco de 9 cm");
        tela.Sugestoes[2].Motivo.Should().Be("a composição 99999 não está na base SINAPI SP 09/2026 desonerado");
        tela.Sugestoes[2].Aceita = true;
        tela.Sugestoes[2].Aceita.Should().BeFalse("não dá para aceitar uma que não vale");
        tela.Sugestoes[6].EhAlerta.Should().BeTrue();
        tela.AplicarSugestoesCommand.CanExecute(null).Should().BeTrue();
        tela.DesmarcarTodasCommand.Execute(null);
        tela.AplicarSugestoesCommand.CanExecute(null).Should().BeFalse();
        tela.MarcarTodasCommand.Execute(null);
        tela.Sugestoes.Count(s => s.Aceita).Should().Be(5);
    }

    [Fact]
    public async Task AplicarMarcadas_UmPassoDoDesfazer_AsFeitasSaemDaLista()
    {
        ResponderTudo();
        var tela = Abrir();
        await tela.PedirSugestoesCommand.ExecuteAsync(null);
        tela.Sugestoes.Single(s => s.Grupo == "BDI").Aceita = false;

        tela.AplicarSugestoesCommand.Execute(null);

        var c = _vm.Cronograma;
        tela.Mensagem.Should().Be("4 sugestões aplicadas. Confira na aba Planilha; Editar > Desfazer volta tudo.");
        _vm.TextoDesfazer.Should().Be("Desfazer Sugestões da IA no orçamento");
        (c.Orcamento.ComposicaoDe(Alvenaria)!.Codigo, c.Tarefas[0].Quantidade, c.Orcamento.ComposicaoDe(Pintura)!.EhEstimativa)
            .Should().Be(("87495", 342.72m, true));
        (c.Recursos.Obter(_pedreiro.Id)!.Disponivel, c.Orcamento.Configuracao.Bdi).Should().Be((2m, 0m));
        tela.Linhas[0].Quantidade.Should().Be("≈ 342,72");
        tela.Linhas[1].Fonte.Should().Be("Estimativa IA");
        tela.BasesUsadas.Should().Be("Preços: SINAPI SP 09/2026 desonerado · Estimativa IA: preço estimado pela IA, sem composição de base · ≈ quantidade estimada pela IA");
        tela.Sugestoes.Select(s => s.Grupo).Should().Equal("Composição", "BDI", "Alerta");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        c = _vm.Cronograma;
        (c.Orcamento.Servicos.Count, c.Tarefas[0].Quantidade, c.Recursos.Obter(_pedreiro.Id)!.Disponivel).Should().Be((0, null, null));
    }

    [Fact]
    public async Task AplicarOBdi_AtualizaOCampoDaPlanilha()
    {
        _ambiente.Consultor.Responder = _ => new RespostaDoConsultor("", [new SugestaoDaIA("bdi", "faixa do TCU", Valor: 22.5m)]);
        var tela = Abrir();
        await tela.PedirSugestoesCommand.ExecuteAsync(null);

        tela.AplicarSugestoesCommand.Execute(null);

        (tela.Bdi, tela.Totais[1].Rotulo).Should().Be(("22,5", "BDI (22,5%)"));
    }

    [Fact]
    public async Task ErroDaIA_ECancelar()
    {
        var tela = Abrir();
        _ambiente.Consultor.Erro = new ErroDoAssistenteException("O limite de uso do Gemini foi atingido.");
        await tela.PedirSugestoesCommand.ExecuteAsync(null);
        (tela.TextoDaConsulta, tela.Consultando).Should().Be(("O limite de uso do Gemini foi atingido.", false));

        _ambiente.Consultor.Erro = null;
        _ambiente.Consultor.Segurar = new TaskCompletionSource();
        var consulta = tela.PedirSugestoesCommand.ExecuteAsync(null);
        (tela.Consultando, tela.PedirSugestoesCommand.CanExecute(null), tela.CancelarConsultaCommand.CanExecute(null)).Should().Be((true, false, true));
        tela.TextoDaConsulta.Should().Be("Pedindo sugestões à IA (2 tarefas, 1 recurso)… isso pode levar um minuto.");
        tela.CancelarConsultaCommand.Execute(null);
        await consulta;
        (tela.TextoDaConsulta, tela.Consultando).Should().Be(("Consulta cancelada.", false));

        consulta = tela.PedirSugestoesCommand.ExecuteAsync(null);
        tela.Fechando();
        await consulta;
        tela.TextoDaConsulta.Should().Be("Consulta cancelada.", "fechar a janela cancela");
    }

    // ---- Pelo chat ----

    [Fact]
    public async Task Chat_PedidoDeOrcamento_AbreAAbaDasSugestoesJaPedindo()
    {
        ResponderTudo();
        _ambiente.Assistente.ResponderComAlteracoes = _ => new RespostaIA("Abri as sugestões de orçamento das vedações.") { PedidoDeOrcamento = "composições das vedações" };
        _vm.Assistente.TextoDigitado = "orce as vedações pela SINAPI";

        await _vm.Assistente.EnviarCommand.ExecuteAsync(null);

        var tela = _ambiente.Dialogos.Orcamentos.Single();
        (tela.AbaEscolhida, tela.Foco).Should().Be((OrcamentoViewModel.AbaDasSugestoes, "composições das vedações"));
        _ambiente.Consultor.Pedidos.Single().Foco.Should().Be("composições das vedações");
        tela.Sugestoes.Should().HaveCount(7);
        _vm.Assistente.Mensagens.Last().Mensagem.Texto.Should().Be("Abri as sugestões de orçamento das vedações.");
    }

    [Fact]
    public async Task Chat_RespostaSemPedido_NaoAbre_ERetratoLevaOOrcamento()
    {
        var c = _vm.Cronograma;
        c.Tarefas[0].DefinirQuantidade(100);
        c.Orcamento.Associar(c.Tarefas[0], _ambiente.Biblioteca.Carregar(Sinapi)!.Abrir("87495"));
        _vm.Assistente.TextoDigitado = "quanto custa a alvenaria?";

        await _vm.Assistente.EnviarCommand.ExecuteAsync(null);

        _ambiente.Dialogos.Orcamentos.Should().BeEmpty();
        var retrato = _ambiente.Assistente.Pedidos.Single().Contexto!;
        retrato.Orcamento.Should().HaveCount(2);
        retrato.Orcamento![1].Should().Be("1 Alvenaria do térreo: SINAPI 87495 · 100,00 M2 × 38,60 = 3.860,00");
    }
}
