using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.App.Tests;

// Bloco 36, na tela: Projeto > Medições… (medições, aditivos, regras do contrato e plano) e as colunas Unidade, Quantidade e
// Preço unitário na grid. "Hoje" é seg 28/09/2026, o início do projeto: Escavação (5 d: 28/09–02/10; 100 m³, R$ 5.000) →
// Sapatas (10 d: 05/10–16/10; sem quantidade, R$ 10.000).
public sealed class MedicoesTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-medicoes-" + Guid.NewGuid().ToString("N"));

    public MedicoesTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "Escavação", 5);
        _b = Ambiente.AdicionarTarefa(_vm, "Sapatas", 10, "1");
        _a.Valor = "5.000";
        _a.Unidade = "m³";
        _a.Quantidade = "100";
        _b.Valor = "10.000";
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private MedicoesViewModel Abrir()
    {
        _vm.MedicoesCommand.Execute(null);
        return _ambiente.Dialogos.Medicoes.Last();
    }

    // A medição nº 01 aberta, com 40 m³ de escavação (R$ 2.000) e 30% das sapatas (R$ 3.000).
    private MedicoesViewModel ComMedicao()
    {
        var tela = Abrir();
        tela.NovaMedicaoCommand.Execute(null);
        tela.Itens[0].NoPeriodo = "40";
        tela.Itens[1].NoPeriodo = "30%";
        return tela;
    }

    private static string? Resumo(MedicoesViewModel tela, string rotulo) => tela.Resumo.FirstOrDefault(r => r.Rotulo.StartsWith(rotulo))?.Valor;

    // ---- Grid: unidade, quantidade e preço unitário ----

    [Fact]
    public void Grid_UnidadeQuantidadeEPrecoUnitario()
    {
        (_a.Unidade, _a.Quantidade, _a.PrecoUnitario, _b.PrecoUnitario).Should().Be(("m³", "100,00", "50,00", ""));

        _a.Quantidade = "0";
        _vm.Mensagem.Should().Contain("maior que zero");
        _a.Quantidade = "doze";
        _vm.Mensagem.Should().Contain("\"doze\" não é um número");
        _vm.AvisarSemUnidadeNoResumo();
        _vm.Mensagem.Should().Be("Um resumo não tem unidade nem quantidade: elas são das tarefas dentro dele.");
    }

    // ---- Medições ----

    [Fact]
    public void NovaMedicao_PeloPlano_EOSemMedicoes()
    {
        var tela = Abrir();
        (tela.SemMedicoes, tela.TemSelecionada).Should().Be((true, false));

        tela.NovaMedicaoCommand.Execute(null);

        (tela.Lista.Count, tela.Selecionada!.Numero, tela.EhRascunho, tela.TituloDaMedicao).Should().Be((1, 1, true, "Medição nº 01 · Rascunho"));
        (tela.De, tela.Ate).Should().Be((new DateTime(2026, 9, 28), new DateTime(2026, 9, 30)), "o plano é mensal: até o fim do mês");
        tela.Itens.Select(i => (i.Edt, i.Unidade, i.Contratado, i.PodeMedir)).Should().Equal(("1", "m³", "100,00", true), ("2", "%", "100,00", true));
        tela.Mensagem.Should().StartWith("Medição nº 01 aberta (28/09/2026 a 30/09/2026).");
        _vm.TextoDesfazer.Should().Be("Desfazer Nova medição");
    }

    [Fact]
    public void Medir_PelaLinha_AtualizaOBoletimEODesfazer()
    {
        var tela = ComMedicao();

        (tela.Itens[0].ValorNoPeriodo, tela.Itens[1].ValorNoPeriodo, tela.Itens[1].PercentualAcumulado).Should().Be(("2.000,00", "3.000,00", "30,0%"));
        Resumo(tela, "Medido nesta medição").Should().Be("R$ 5.000,00");
        tela.Lista[0].Liquido.Should().Be("R$ 5.000,00");
        (_vm.TextoDesfazer, _vm.PossuiAlteracoesNaoSalvas).Should().Be(("Desfazer Medição nº 01: Sapatas", true));

        tela.Itens[0].NoPeriodo = "200";
        tela.Mensagem.Should().StartWith("\"Escavação\": 200,00 m³ passa do saldo do contrato (100,00 m³).");
        tela.Itens[0].NoPeriodo.Should().Be("40,00", "o recusado não fica");
        tela.Itens[0].NoPeriodo = "x";
        tela.Mensagem.Should().Be("\"x\" não é um número (use, por exemplo, 12,5).");
        tela.Itens[0].NoPeriodo = "";
        tela.Itens[0].ValorNoPeriodo.Should().Be("0,00");
    }

    [Fact]
    public void Sugerir_RegrasDoContrato_IndiceGlosaEDevolucao()
    {
        var tela = Abrir();
        tela.NovaMedicaoCommand.Execute(null);
        _a.PercentualConcluido = 50;

        tela.SugerirPeloPercentualConcluidoCommand.Execute(null);
        tela.Itens[0].NoPeriodo.Should().Be("50,00");
        tela.Mensagem.Should().StartWith("1 item medido pelo % concluído");

        tela.Retencao = "40";
        tela.SalvarConfiguracaoCommand.Execute(null);
        tela.Mensagem.Should().Be("A retenção vai de 0% a 30%.");
        (tela.Retencao, tela.NomeDoIndice, tela.IndiceBase) = ("5", "INCC-DI", "1.000");
        tela.SalvarConfiguracaoCommand.Execute(null);
        (tela.TemReajuste, tela.RotuloDoIndice).Should().Be((true, "Índice do período (INCC-DI; base 1.000)"));

        tela.Indice = "1.100";
        Resumo(tela, "Reajuste").Should().Be("+ R$ 250,00");
        (tela.NovaGlosaDescricao, tela.NovaGlosaValor) = ("Retrabalho", "50");
        tela.AdicionarGlosaCommand.Execute(null);
        (tela.Glosas.Single().Valor, Resumo(tela, "Glosas"), Resumo(tela, "Retenção (5%)"), Resumo(tela, "Valor líquido")).Should()
            .Be(("R$ 50,00", "− R$ 50,00", "− R$ 135,00", "R$ 2.565,00"));
        tela.RemoverGlosaCommand.Execute(tela.Glosas[0]);
        tela.Glosas.Should().BeEmpty();

        tela.DevolverRetencao = true;
        Resumo(tela, "Valor líquido").Should().Be("R$ 2.750,00", "devolvendo a retenção, esta medição não retém");
    }

    [Fact]
    public void Aprovar_TravaATelaEAGrid_ReabrirEPagar()
    {
        var tela = ComMedicao();
        _ambiente.Dialogos.Confirmar = false;
        tela.AprovarCommand.Execute(null);
        tela.EhRascunho.Should().BeTrue("não confirmou");

        _ambiente.Dialogos.Confirmar = true;
        tela.AprovarCommand.Execute(null);

        (tela.EhAprovada, tela.Itens[0].PodeMedir, tela.Lista[0].Situacao).Should().Be((true, false, "Aprovada em 28/09/2026"));
        _a.Valor = "1";
        (_a.Tarefa.Valor, _vm.Mensagem.Contains(ContratoDoProjeto.MotivoDoTravamento)).Should().Be((5000m, true));
        _a.Quantidade = "1";
        _a.Tarefa.Quantidade.Should().Be(100);
        var nova = Ambiente.AdicionarTarefa(_vm, "Limpeza", 1);
        nova.Unidade = "m²";
        nova.Tarefa.Unidade.Should().Be("m²", "uma tarefa sem valor ainda não é item: a unidade pode mudar");

        _ambiente.Dialogos.TextoDigitado = "32/10/2026";
        tela.MarcarPagaCommand.Execute(null);
        tela.Mensagem.Should().Be("\"32/10/2026\" não é uma data (use dd/mm/aaaa).");
        _ambiente.Dialogos.TextoDigitado = "05/10/2026";
        tela.MarcarPagaCommand.Execute(null);
        (tela.EhPaga, tela.Lista[0].Situacao, tela.ReabrirCommand.CanExecute(null)).Should().Be((true, "Paga em 05/10/2026", false));
        tela.DesmarcarPagaCommand.Execute(null);
        tela.ReabrirCommand.Execute(null);
        (tela.EhRascunho, tela.Itens[0].PodeMedir).Should().Be((true, true));
    }

    [Fact]
    public void Remover_SoORascunhoDoFim()
    {
        var tela = ComMedicao();
        tela.RemoverMedicaoCommand.CanExecute(null).Should().BeTrue();
        tela.AprovarCommand.Execute(null);
        tela.RemoverMedicaoCommand.CanExecute(null).Should().BeFalse();
        tela.NovaMedicaoCommand.Execute(null);

        tela.RemoverMedicaoCommand.Execute(null);

        (tela.Lista.Count, tela.Selecionada!.Numero).Should().Be((1, 1));
    }

    // ---- Aditivos ----

    [Fact]
    public void Aditivo_PelaTela_ComOValorPeloPrecoUnitario_ERemover()
    {
        var tela = ComMedicao();
        tela.AprovarCommand.Execute(null);
        var escavacao = tela.ItensDoAditivo.Single(i => i.Nome == "Escavação");
        escavacao.Quantidade = "20";
        (escavacao.NovaQuantidade, escavacao.NovoValor).Should().Be(("120,00", "6.000,00"));

        tela.RegistrarAditivoCommand.Execute(null);
        tela.Mensagem.Should().Be("Escreva o motivo do aditivo.");
        tela.MotivoDoAditivo = "Ampliação da escavação";
        tela.RegistrarAditivoCommand.Execute(null);

        (_a.Valor, _a.Quantidade, tela.Aditivos.Count).Should().Be(("6.000,00", "120,00", 1));
        tela.Aditivos[0].Detalhe.Should().Be("1 +20,00 m³ (+ R$ 1.000,00)");
        tela.Mensagem.Should().Be("Aditivo nº 1 registrado: o contrato passou a R$ 16.000,00.");
        _vm.TextoDesfazer.Should().Be("Desfazer Registrar aditivo");
        tela.ItensDoAditivo.Single(i => i.Nome == "Sapatas").SoValor.Should().BeTrue("medida em % e já medida");

        tela.RemoverUltimoAditivoCommand.Execute(null);
        (_a.Valor, tela.Aditivos.Count).Should().Be(("5.000,00", 0));
    }

    // ---- Contrato, plano, exportações e fotos ----

    [Fact]
    public void ResumoDoContratoEPlano()
    {
        var tela = ComMedicao();
        tela.AprovarCommand.Execute(null);

        tela.ResumoDoContrato.Select(r => (r.Rotulo, r.Valor)).Should().Contain([("Valor atual do contrato", "R$ 15.000,00"), ("Medido (aprovadas)", "R$ 5.000,00 (33,3%)")]);
        tela.BaseDoPlano.Should().Be("Previsto pelo físico-financeiro de: Cronograma atual");
        tela.Plano.Select(p => (p.Periodo, p.Previsto, p.Medicoes, p.Medido)).First().Should().Be(("28/09–30/09", "3.000,00", "nº 01", "5.000,00"));
    }

    [Fact]
    public async Task Exportar_PdfWordEExcel()
    {
        var tela = ComMedicao();
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "bm");

        await tela.ExportarPdfCommand.ExecuteAsync(null);
        await tela.ExportarWordCommand.ExecuteAsync(null);
        await tela.ExportarExcelCommand.ExecuteAsync(null);

        File.ReadAllBytes(Path.Combine(_pasta, "bm.pdf"))[..5].Should().Equal("%PDF-"u8.ToArray());
        File.ReadAllBytes(Path.Combine(_pasta, "bm.docx"))[..2].Should().Equal("PK"u8.ToArray());
        File.ReadAllBytes(Path.Combine(_pasta, "bm.xlsx"))[..2].Should().Equal("PK"u8.ToArray());
        _ambiente.Dialogos.Exportacoes.Select(e => (e.Nome, e.Extensao)).Should().Equal(
            ("Boletim de medição 01 - Projeto sem título", "pdf"), ("Boletim de medição 01 - Projeto sem título", "docx"),
            ("Boletim de medição 01 - Projeto sem título", "xlsx"));
        tela.Mensagem.Should().Be("Salvo em bm.xlsx (rascunho: os números ainda podem mudar).");
    }

    [Fact]
    public async Task FotosDoPeriodo_AbreORelatorioComOPeriodoDaMedicao()
    {
        var tela = ComMedicao();

        await tela.RelatorioFotograficoDoPeriodoCommand.ExecuteAsync(null);

        var relatorio = _ambiente.Dialogos.Relatorios.Single();
        (relatorio.De, relatorio.Ate, relatorio.TarefasEscolhidas.Valor).Should()
            .Be((new DateTime(2026, 9, 28), new DateTime(2026, 9, 30), TarefasDoRelatorio.EmExecucaoNoPeriodo));
    }

    [Fact]
    public void Desfazer_DepoisDeFechar_VoltaAMedicao()
    {
        var tela = ComMedicao();
        tela.AprovarCommand.Execute(null);

        _vm.DesfazerAlteracaoCommand.Execute(null);

        Abrir().EhRascunho.Should().BeTrue();
        _vm.Cronograma.Contrato.Travado.Should().BeFalse();
    }

    [Fact]
    public async Task ImportarValores_ComQuantidades_MostraAsColunas()
    {
        var planilha = Path.Combine(_pasta, "orcamento.csv");
        File.WriteAllText(planilha, "Item;Unid.;Quant.;Total\n1;m³;80;4.000\n2;vb;1;9.000\n");
        _ambiente.Dialogos.PlanilhaEscolhida = planilha;

        await _vm.ImportarValoresCommand.ExecuteAsync(null);

        (_a.Quantidade, _b.Unidade, _vm.Colunas["Unidade"].Visivel, _vm.Colunas["Quantidade"].Visivel).Should().Be(("80,00", "vb", true, true));
        _vm.Mensagem.Should().Be("2 valores importados (com unidade e quantidade).");
    }
}
