using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 67: a IA no programa todo: os dados das tarefas e da obra, os recursos e as atribuições, as medições do contrato, os
// pagamentos e as cópias das OS, e as telas com parâmetros (a folha do Gantt, o filtro do relatório das OS).
public sealed class IaNoProgramaTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public IaNoProgramaTests()
    {
        _vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        Ambiente.AdicionarTarefa(_vm, "Reboco", 4, "1");
    }

    private IReadOnlyList<ItemDoCartao> Cartao(params AtualizacaoDaObra[] atualizacoes) =>
        _vm.ConferirAtualizacoes(atualizacoes, _vm.RetratoParaOAssistente());

    private async Task Aplicar(IReadOnlyList<ItemDoCartao> itens)
    {
        itens.Where(i => !i.Valido).Should().BeEmpty();
        await _vm.AplicarAtualizacoesAsync([.. itens.Select(i => i.Operacao!)], "Atualizações do assistente");
    }

    [Fact]
    public async Task DadosDaTarefa_EDaObra()
    {
        var cartao = Cartao(
            new AtualizacaoDaObra("tarefa", "1", Quantidade: 120, Valor: 3600, Prazo: "2026-10-09", Unidade: "m²"),
            new AtualizacaoDaObra("obra", Contratante: "Prefeitura de Volta Redonda", Endereco: "Rua A, 10"));

        cartao.Select(i => i.Texto).Should().Equal("1 Alvenaria: valor R$ 3.600,00 · 120 m² · prazo 09/10/2026",
            "Dados da obra: contratante Prefeitura de Volta Redonda · endereço Rua A, 10");
        await Aplicar(cartao);

        var t = _vm.Cronograma.Tarefas[0];
        (t.Valor, t.Unidade, t.Quantidade, t.Prazo).Should().Be((3600m, "m²", 120m, (DateTime?)new DateTime(2026, 10, 9)));
        (_vm.Cronograma.Obra.Contratante, _vm.Cronograma.Obra.Endereco).Should().Be(("Prefeitura de Volta Redonda", "Rua A, 10"));
    }

    [Fact]
    public async Task Recursos_CriaEAtribuiNaMesmaResposta_EDepoisAltera()
    {
        var cartao = Cartao(
            new AtualizacaoDaObra("recurso", Valor: 280, Quantidade: 4, Nome: "Pedreiro", TipoDoRecurso: "profissional"),
            new AtualizacaoDaObra("atribuir", "1", Quantidade: 2, Recurso: "pedreiro"));

        cartao.Select(i => (i.Texto, i.Problema)).Should().Equal(("Novo recurso \"Pedreiro\" · profissional · R$ 280,00 por dia · 4 por dia", null),
            ("1 Alvenaria: 2 × pedreiro", null));
        await Aplicar(cartao);
        var pedreiro = _vm.Cronograma.Recursos.Lista.Single();
        (pedreiro.Tipo, pedreiro.CustoUnitario, pedreiro.Disponivel).Should().Be((TipoDeRecurso.Profissional, 280m, 4m));
        _vm.Cronograma.Tarefas[0].Atribuicoes.Single().Should().Be(new AtribuicaoDeRecurso(pedreiro.Id, 2));

        await Aplicar(Cartao(new AtualizacaoDaObra("recurso", Valor: 300, Nome: "Pedreiro")));
        _vm.Cronograma.Recursos.Lista.Single().CustoUnitario.Should().Be(300);
        Cartao(new AtualizacaoDaObra("atribuir", "2", Quantidade: 1, Recurso: "Servente")).Single().Problema
            .Should().Be("O recurso \"Servente\" não está no projeto: crie antes.");
        await Aplicar(Cartao(new AtualizacaoDaObra("atribuir", "1", Quantidade: 0, Recurso: "Pedreiro")));
        _vm.Cronograma.Tarefas[0].Atribuicoes.Should().BeEmpty("quantidade zero tira");
    }

    [Fact]
    public async Task OS_PagarDuplicarPelaIA_ECustosLancados()
    {
        await Aplicar(Cartao(new AtualizacaoDaObra("os", Valor: 3000, Titulo: "Alvenaria", Executante: "Construtora Silva", Tarefas: "1")));
        await Aplicar(Cartao(new AtualizacaoDaObra("situacaoOs", Os: 1, Acao: "emitir")));
        await Aplicar(Cartao(new AtualizacaoDaObra("situacaoOs", Os: 1, Acao: "aceitar")));
        await Aplicar(Cartao(new AtualizacaoDaObra("situacaoOs", Percentual: 50, Os: 1, Acao: "medir")));

        var pagar = Cartao(new AtualizacaoDaObra("situacaoOs", Documento: "NF 77", Os: 1, Acao: "pagar"));
        pagar.Single().Texto.Should().Be("OS-001: pagar as medições abertas (NF 77)");
        await Aplicar(pagar);
        await Aplicar(Cartao(new AtualizacaoDaObra("situacaoOs", Os: 1, Acao: "duplicar")));

        var ordens = _vm.Cronograma.OrdensDeServico.Todas;
        (ordens.Count, ordens[0].TotalPago, ordens[1].Situacao).Should().Be((2, 1500m, SituacaoDaOrdem.Rascunho));
        _vm.Cronograma.Custos.Lancamentos.Single().Should().Match<Core.Indicadores.LancamentoDeCusto>(l => l.Valor == 1500 && l.Documento == "NF 77");
        Cartao(new AtualizacaoDaObra("situacaoOs", Os: 1, Acao: "pagar")).Single().Problema.Should().Be("A OS-001 não tem medição a pagar.");
        Cartao(new AtualizacaoDaObra("medicaoContrato", Medicao: 1, Acao: "aprovar")).Single().Problema.Should().Be("não há a medição nº 01");
    }

    [Fact]
    public async Task Telas_AFolhaDoGantt_EOFiltroDoRelatorioDasOrdens()
    {
        await _vm.AbrirTelaAsync(new TelaParaAbrir(TelasDoPrograma.ImprimirGantt, "A1"));
        await _vm.AbrirTelaAsync(new TelaParaAbrir(TelasDoPrograma.RelatorioDasOrdens, "vencidas", De: "2026-09-01", Ate: "2026-09-30"));

        _ambiente.Dialogos.ImpressoesDoGantt.Single().FolhaEscolhida.Valor.Should().Be(ProjectPlanner.Relatorios.TamanhoDaFolha.A1);
        var relatorio = _ambiente.Dialogos.RelatoriosDasOrdens.Single();
        (relatorio.SituacaoEscolhida.Valor, relatorio.De, relatorio.Ate).Should().Be((SituacoesDoRelatorio.Vencidas, new DateTime(2026, 9, 1), (DateTime?)new DateTime(2026, 9, 30)));
    }

    [Fact]
    public void Resposta_ComOsCamposNovos()
    {
        var r = FormatoDaResposta.Interpretar(""""
            {"mensagem": "Proponho.", "alteracoes": [], "atualizacoes": [
              {"tipo": "recurso", "nome": "Betoneira", "tipoDoRecurso": "equipamento", "valor": 90, "fornecedor": "Loca Já"},
              {"tipo": "obra", "contratante": "Prefeitura", "contrato": "12/2026"},
              {"tipo": "medicaoContrato", "medicao": 2, "acao": "pagar", "data": "2026-10-30"}
            ], "abrir": {"tela": "imprimirGantt", "aba": "A3"}}
            """");

        r.Atualizacoes.Should().Equal(
            new AtualizacaoDaObra("recurso", Valor: 90, Nome: "Betoneira", TipoDoRecurso: "equipamento", Fornecedor: "Loca Já"),
            new AtualizacaoDaObra("obra", Contratante: "Prefeitura", Contrato: "12/2026"),
            new AtualizacaoDaObra("medicaoContrato", Medicao: 2, Data: "2026-10-30", Acao: "pagar"));
        r.Abrir.Should().Be(new TelaParaAbrir(TelasDoPrograma.ImprimirGantt, "A3"));
    }
}
