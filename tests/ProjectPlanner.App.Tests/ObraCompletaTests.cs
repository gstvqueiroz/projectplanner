using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 54: a obra completa: depois de montar o cronograma, o botão do orçamento; o orçamento da obra inteira em partes; e a
// equipe das estimativas da IA virando recursos da obra.
public sealed class ObraCompletaTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public ObraCompletaTests()
    {
        _ambiente.Assistente.EstaConfigurado = true;
        _vm = _ambiente.CriarViewModel();
    }

    private static RespostaIA Cronograma(int tarefas) => new("Montei o cronograma.")
    {
        Alteracoes = [.. Enumerable.Range(1, tarefas).Select(i => new AlteracaoDoCronograma("criar", Ref: $"n{i}", Nome: $"Tarefa {i}", Duracao: 3))],
    };

    private OrcamentoViewModel AbrirOrcamento()
    {
        _vm.OrcamentoCommand.Execute(null);
        return _ambiente.Dialogos.Orcamentos.Last();
    }

    [Fact]
    public async Task MontouOCronograma_OBotaoAbreOOrcamentoDaObraInteira_ESomeNoProximoEnvio()
    {
        _ambiente.Assistente.ResponderComAlteracoes = _ => Cronograma(6);
        _vm.Assistente.TextoDigitado = "monte o cronograma de uma casa";
        await _vm.Assistente.EnviarCommand.ExecuteAsync(null);

        _vm.Cronograma.Tarefas.Should().HaveCount(6);
        _vm.Assistente.PodeEstimarOrcamento.Should().BeTrue();
        await _vm.Assistente.EstimarOrcamentoCommand.ExecuteAsync(null);

        var tela = _ambiente.Dialogos.Orcamentos.Single();
        (tela.AbaEscolhida, tela.Foco).Should().Be((OrcamentoViewModel.AbaDasSugestoes, AssistenteViewModel.PedidoDoOrcamentoCompleto));
        _ambiente.Consultor.Pedidos.Single().Tarefas.Should().HaveCount(6);
        _vm.Assistente.PodeEstimarOrcamento.Should().BeFalse("usado uma vez");

        // Um ajuste pequeno (menos de 5 tarefas) ou um pedido que já trouxe o orçamento não mostram o botão.
        _ambiente.Assistente.ResponderComAlteracoes = _ => Cronograma(2);
        _vm.Assistente.TextoDigitado = "acrescente duas tarefas";
        await _vm.Assistente.EnviarCommand.ExecuteAsync(null);
        _vm.Assistente.PodeEstimarOrcamento.Should().BeFalse();
        _ambiente.Assistente.ResponderComAlteracoes = _ => Cronograma(6) with { PedidoDeOrcamento = AssistenteViewModel.PedidoDoOrcamentoCompleto };
        _vm.Assistente.TextoDigitado = "monte outro com orçamento";
        await _vm.Assistente.EnviarCommand.ExecuteAsync(null);
        (_vm.Assistente.PodeEstimarOrcamento, _ambiente.Dialogos.Orcamentos.Count).Should().Be((false, 2), "abriu direto, sem o botão");
    }

    [Fact]
    public async Task ObraGrande_OrcamentoEmPartes_JuntaAsSugestoes_UmBdiSo_EAvisaAParteQueFalhou()
    {
        for (var i = 1; i <= 65; i++)
            Ambiente.AdicionarTarefa(_vm, $"Serviço {i}", 2);
        var chamadas = 0;
        _ambiente.Consultor.Responder = p =>
        {
            chamadas++;
            if (chamadas == 2)
                throw new ErroDoAssistenteException("A IA demorou demais.");
            return new RespostaDoConsultor($"Parte {chamadas}.",
            [
                new SugestaoDaIA("bdi", "faixa do TCU", Valor: 25),
                new SugestaoDaIA("quantidade", "conta", "T1", Unidade: "m²", Valor: 10),
                new SugestaoDaIA("alerta", "", Descricao: "Falta a limpeza final."),
            ]);
        };
        var tela = AbrirOrcamento();

        await tela.PedirSugestoesCommand.ExecuteAsync(null);

        _ambiente.Consultor.Pedidos.Select(p => p.Tarefas.Count).Should().Equal(30, 30, 5);
        _ambiente.Consultor.Pedidos.SelectMany(p => p.Tarefas.Select(t => t.Nome)).Should().OnlyHaveUniqueItems("cada tarefa vai numa parte só");
        tela.Sugestoes.Select(s => s.Grupo).Should().BeEquivalentTo(["BDI", "Quantidade", "Quantidade", "Alerta"], "um BDI e um alerta igual só");
        tela.Sugestoes.Where(s => s.Grupo == "Quantidade").Select(s => s.Alvo).Should().Equal("1 Serviço 1", "61 Serviço 61");
        tela.TextoDaConsulta.Should().StartWith("Sugestões em 3 partes. Parte 1. A parte 2 de 3 não veio (A IA demorou demais.). Peça de novo para completar.");
        ConsultaDeOrcamento.Lotes(_vm.Cronograma).Select(l => l.Count).Should().Equal(30, 30, 5);
    }

    [Fact]
    public async Task EstimativaComEquipe_CriaOsProfissionais_ReaproveitaODeMesmoNome_EAtribuiATarefa()
    {
        var pedreiro = new Recurso(Guid.NewGuid(), "PEDREIRO", TipoDeRecurso.Profissional, null, 224);
        _vm.Cronograma.Recursos.Adicionar(pedreiro);
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        Ambiente.AdicionarTarefa(_vm, "Entrega", 0);
        _ambiente.Consultor.Responder = _ => new RespostaDoConsultor("Estimei.",
        [
            new SugestaoDaIA("estimativa", "faixa usual", "T1", Descricao: "Alvenaria de blocos", Unidade: "m²", CustoDeMateriais: 30, CustoDeMaoDeObra: 25,
                Equipe: [new PessoaDaEquipe("Pedreiro", 2, 250), new PessoaDaEquipe("Servente", 2, 160)]),
        ]);
        var tela = AbrirOrcamento();
        await tela.PedirSugestoesCommand.ExecuteAsync(null);
        tela.Sugestoes.Single().Texto.Should().EndWith("· equipe: 2 Pedreiro, 2 Servente");

        tela.AplicarSugestoesCommand.Execute(null);

        var recursos = _vm.Cronograma.Recursos.Lista;
        recursos.Select(r => (r.Nome, r.Tipo, r.CustoUnitario)).Should().Equal(("PEDREIRO", TipoDeRecurso.Profissional, 224m),
            ("Servente", TipoDeRecurso.Profissional, 160m));
        recursos[1].Fornecedor.Should().Be(AplicacaoDeSugestoes.OrigemDaEquipe);
        var alvenaria = _vm.Cronograma.Tarefas[0];
        alvenaria.Atribuicoes.Select(a => (a.RecursoId, a.Quantidade)).Should().Equal((pedreiro.Id, 2m), (recursos[1].Id, 2m));
    }

    [Fact]
    public void Equipe_NumMarcoNaoEntra_EUmMaterialDeMesmoNomeFicaDeFora()
    {
        var cronograma = _vm.Cronograma;
        cronograma.Recursos.Adicionar(new Recurso(Guid.NewGuid(), "Servente", TipoDeRecurso.Material, "un", 1));
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        Ambiente.AdicionarTarefa(_vm, "Entrega", 0);
        var equipe = new[] { new MembroDaEquipe("Servente", 2), new MembroDaEquipe("Pedreiro", 1.5m) };

        AplicacaoDeSugestoes.Aplicar(cronograma, null,
        [
            new SugestaoDeOrcamento(TipoDeSugestao.Estimativa, "x", cronograma.Tarefas[0].Id, Descricao: "Alvenaria", Unidade: "m²", CustoDeMateriais: 1) { Equipe = equipe },
            new SugestaoDeOrcamento(TipoDeSugestao.Estimativa, "x", cronograma.Tarefas[1].Id, Descricao: "Entrega", Unidade: "un", CustoDeMateriais: 1) { Equipe = equipe },
        ]);

        cronograma.Recursos.Lista.Select(r => r.Nome).Should().Equal("Servente", "Pedreiro");
        cronograma.Tarefas[0].Atribuicoes.Select(a => a.Quantidade).Should().Equal(1.5m);
        cronograma.Tarefas[1].Atribuicoes.Should().BeEmpty("um marco não tem equipe");
    }
}
