using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 41, na tela: o chat consulta a obra (a situação vai no retrato), propõe atualizações num cartão (aplicar ou descartar,
// com Desfazer) e abre telas. "Hoje" é seg 28/09/2026: 1 Escavação (5 d, R$ 5.000) → 2 Sapatas (10 d, R$ 10.000).
public sealed class ChatCentralDaObraTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public ChatCentralDaObraTests()
    {
        _vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(_vm, "Escavação", 5).Valor = "5.000";
        Ambiente.AdicionarTarefa(_vm, "Sapatas", 10, "1").Valor = "10.000";
    }

    private AssistenteViewModel Painel => _vm.Assistente;

    private async Task<MensagemNaTela> Enviar(string texto, RespostaIA resposta)
    {
        _ambiente.Assistente.ResponderComAlteracoes = _ => resposta;
        Painel.TextoDigitado = texto;
        await Painel.EnviarCommand.ExecuteAsync(null);
        return Painel.Mensagens.Last();
    }

    private static RespostaIA ComAtualizacoes(params AtualizacaoDaObra[] atualizacoes) =>
        new("Proponho estas atualizações.") { Atualizacoes = atualizacoes };

    [Fact]
    public async Task Retrato_LevaASituacaoDaObra()
    {
        var c = _vm.Cronograma;
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 9, 28), c.Tarefas[0].Id, CategoriaDeCusto.Material, "Diesel", null, 800));
        c.Contrato.NovaMedicao(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28));
        c.Contrato.Medir(1, c.Tarefas[0].Id, 20);

        await Enviar("como está a obra?", new RespostaIA("Está assim."));

        var obra = _ambiente.Assistente.Pedidos.Single().Contexto!.Obra!;
        obra.Should().Contain(l => l.StartsWith("Prazo: físico previsto"))
            .And.Contain("Medição 01 (28/09/2026 a 28/09/2026): em rascunho; medido no período R$ 1.000,00; acumulado R$ 1.000,00; líquido R$ 1.000,00.")
            .And.Contain("Custos realizados por categoria: Material R$ 800,00.")
            .And.Contain("Custo 28/09/2026: Diesel (Material) R$ 800,00 · tarefa 1.");
    }

    [Fact]
    public async Task Cartao_MostraCadaAtualizacao_EAsQueNaoValem()
    {
        var m = await Enviar("a escavação está em 60%, gastei 3.200 de cimento nela e mede 50% dela", ComAtualizacoes(
            new AtualizacaoDaObra("percentual", "1", Percentual: 60),
            new AtualizacaoDaObra("custo", "1", Data: "2026-09-28", Categoria: "material", Descricao: "Cimento", Valor: 3200),
            new AtualizacaoDaObra("medicao", "1", Percentual: 50),
            new AtualizacaoDaObra("inicio", "2", Data: "2026-10-12"),
            new AtualizacaoDaObra("linhaDeBase", Nome: "Antes das chuvas"),
            new AtualizacaoDaObra("percentual", "9", Percentual: 10),
            new AtualizacaoDaObra("percentual", "2", Percentual: 150),
            new AtualizacaoDaObra("medicao", "2")));

        var cartao = m.Cartao!;
        (m.TemCartao, m.Interativa, cartao.Pendente, cartao.Titulo, cartao.Validos, cartao.TextoDoBotao)
            .Should().Be((true, true, true, "8 atualizações propostas", 5, "Aplicar as 5 válidas"));
        cartao.Itens.Select(i => i.Texto).Should().Equal(
            "1 Escavação: 0% → 60% concluído",
            "Custo de 28/09/2026: Cimento (Material) · R$ 3.200,00 · 1 Escavação",
            "Medição 01 (nova, 28/09 a 28/09/2026): 1 Escavação, 50%",
            "2 Sapatas: não iniciar antes de 12/10/2026 (hoje começa em 05/10/2026)",
            "Salvar a linha de base \"Antes das chuvas\"",
            "Tarefa 9: % concluído",
            "2 Sapatas: % concluído",
            "Medição 01 (nova, 28/09 a 28/09/2026): 2 Sapatas, ?");
        cartao.Itens.Select(i => i.Problema).Should().Equal(null, null, null, null, null,
            "Não dá: não há a tarefa nº 9 no cronograma.", "Não dá: o % precisa ir de 0 a 100.", "Não dá: informe a quantidade (ou o %) medida no período.");
        _vm.Cronograma.Tarefas[0].PercentualConcluido.Should().Be(0, "nada muda antes de aplicar");
        _vm.Conversa.Mensagens[^1].ResultadoDasAlteracoes.Should().StartWith("Atualizações propostas (não aplicadas): 1 Escavação: 0% → 60% concluído;");
    }

    // A correção da 0.41.1: com nenhuma válida, o botão não promete "Aplicar as 0 válidas".
    [Fact]
    public async Task SemNenhumaValida_NadaParaAplicar()
    {
        var cartao = (await Enviar("x", ComAtualizacoes(new AtualizacaoDaObra("percentual", "1")))).Cartao!;

        (cartao.Validos, cartao.TemValidos, cartao.TextoDoBotao).Should().Be((0, false, "Nada para aplicar"));
    }

    [Fact]
    public async Task Aplicar_TudoNumPassoDoDesfazer_EDesfazerPeloCartao()
    {
        var cartao = (await Enviar("atualiza", ComAtualizacoes(
            new AtualizacaoDaObra("percentual", "1", Percentual: 60),
            new AtualizacaoDaObra("custo", "1", Data: "2026-09-28", Categoria: "material", Descricao: "Cimento", Valor: 3200),
            new AtualizacaoDaObra("medicao", "1", Percentual: 50),
            new AtualizacaoDaObra("inicio", "2", Data: "2026-10-12"),
            new AtualizacaoDaObra("linhaDeBase", Nome: "Antes das chuvas")))).Cartao!;

        await Painel.AplicarCartaoCommand.ExecuteAsync(cartao);

        var c = _vm.Cronograma;
        (c.Tarefas[0].PercentualConcluido, c.Custos.Lancamentos.Single().Valor, c.Contrato.Medicoes.Single().Numero).Should().Be((60, 3200m, 1));
        c.Tarefas[1].NaoIniciarAntesDe.Should().Be(new DateTime(2026, 10, 12));
        c.LinhasDeBase.Single().Nome.Should().Be("Antes das chuvas");
        _vm.Tarefas[1].DataInicio.Should().Be(new DateTime(2026, 10, 12), "recalculado");
        (cartao.Estado, cartao.Pendente, cartao.Aplicacao!.PodeDesfazer).Should().Be((EstadoDoCartao.Aplicado, false, true));
        cartao.Aplicacao.Texto.Should().Be("Obra atualizada: 5 atualizações da obra");
        _vm.TextoDesfazer.Should().Be("Desfazer Atualizações do assistente");
        _vm.Conversa.Mensagens[^1].ResultadoDasAlteracoes.Should().StartWith("Atualizações aplicadas: ");

        await Painel.DesfazerCommand.ExecuteAsync(cartao.Aplicacao);

        c = _vm.Cronograma;
        (c.Tarefas[0].PercentualConcluido, c.Custos.Lancamentos.Count, c.Contrato.Medicoes.Count, c.LinhasDeBase.Count).Should().Be((0, 0, 0, 0));
        (cartao.Aplicacao.Desfeitas, cartao.Aplicacao.PodeDesfazer).Should().Be((true, false));
    }

    [Fact]
    public async Task Descartar_OuRecusadoPorqueOCronogramaMudou()
    {
        var descartado = (await Enviar("x", ComAtualizacoes(new AtualizacaoDaObra("percentual", "1", Percentual: 60)))).Cartao!;
        Painel.DescartarCartaoCommand.Execute(descartado);
        (descartado.Estado, descartado.TextoDoEstado, _vm.Cronograma.Tarefas[0].PercentualConcluido).Should().Be((EstadoDoCartao.Descartado, "Descartadas: nada mudou.", 0));
        _vm.Conversa.Mensagens[^1].ResultadoDasAlteracoes.Should().StartWith("Atualizações descartadas: ");
        await Painel.AplicarCartaoCommand.ExecuteAsync(descartado);
        _vm.Cronograma.Tarefas[0].PercentualConcluido.Should().Be(0, "um cartão descartado não aplica mais");

        var cartao = (await Enviar("y", ComAtualizacoes(new AtualizacaoDaObra("percentual", "2", Percentual: 30)))).Cartao!;
        _vm.TarefaSelecionada = _vm.Tarefas[1];
        _vm.RemoverTarefaCommand.Execute(null);

        await Painel.AplicarCartaoCommand.ExecuteAsync(cartao);

        (cartao.Estado, cartao.TextoDoEstado).Should().Be((EstadoDoCartao.Recusado, "Nada mudou. A tarefa não existe mais."));
    }

    [Fact]
    public async Task CartaoGuardado_VoltaComoTexto_AoReabrirAConversa()
    {
        var cartao = (await Enviar("x", ComAtualizacoes(new AtualizacaoDaObra("percentual", "1", Percentual: 60)))).Cartao!;
        await Painel.AplicarCartaoCommand.ExecuteAsync(cartao);

        Painel.RecomecarParaOutroProjeto(); // mostra a conversa guardada do mesmo projeto

        var m = Painel.Mensagens.Last();
        (m.TemCartao, m.Alteracoes!.Texto).Should().Be((false, "Atualizações aplicadas: 1 Escavação: 0% → 60% concluído."));
    }

    [Fact]
    public async Task Abrir_ATelaNaAbaPedida_EOBotaoAbreDeNovo()
    {
        var m = await Enviar("mostra os custos", new RespostaIA("Abri os indicadores.") { Abrir = new TelaParaAbrir("indicadores", "custos") });

        var d = _ambiente.Dialogos;
        d.Indicadores.Single().AbaEscolhida.Should().Be(IndicadoresViewModel.AbaCustos);
        (m.TemTela, m.Tela!.Rotulo).Should().Be((true, "Abrir Indicadores"));
        await Painel.AbrirTelaCommand.ExecuteAsync(m.Tela);
        d.Indicadores.Should().HaveCount(2);

        _vm.Cronograma.Contrato.NovaMedicao(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28));
        await _vm.AbrirTelaAsync(new TelaParaAbrir("medicoes", "aditivos", 1));
        (d.Medicoes.Last().AbaEscolhida, d.Medicoes.Last().Selecionada!.Numero).Should().Be((1, 1));
        await _vm.AbrirTelaAsync(new TelaParaAbrir("orcamento", "sugestoes"));
        d.Orcamentos.Last().AbaEscolhida.Should().Be(OrcamentoViewModel.AbaDasSugestoes);
        await _vm.AbrirTelaAsync(new TelaParaAbrir("relatorioFotografico", De: "2026-09-01", Ate: "2026-09-25"));
        (d.Relatorios.Last().De, d.Relatorios.Last().Ate).Should().Be((new DateTime(2026, 9, 1), new DateTime(2026, 9, 25)));
        foreach (var tela in new[] { "custos", "recursos", "usoDeRecursos", "fisicoFinanceiro", "fotosDoProjeto", "resumo", "linhasDeBase", "relatorioFotografico" })
            await _vm.AbrirTelaAsync(new TelaParaAbrir(tela));
        (d.Custos.Count, d.Relatorios.Count, d.FotosDoProjeto.Count, d.UsosDeRecursos.Count).Should().Be((1, 2, 1, 1));
    }
}
