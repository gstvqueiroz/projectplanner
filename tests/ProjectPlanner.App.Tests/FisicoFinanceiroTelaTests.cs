using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.Tests;

// Bloco 35, na tela: Projeto > Cronograma físico-financeiro… e Importar valores…. "Hoje" é seg 28/09/2026, o início do
// projeto: A (5 d: 28/09–02/10) → B (10 d: 05/10–16/10).
public sealed class FisicoFinanceiroTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-ff-" + Guid.NewGuid().ToString("N"));

    public FisicoFinanceiroTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "A", 5);
        _b = Ambiente.AdicionarTarefa(_vm, "B", 10, "1");
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private FisicoFinanceiroViewModel Abrir()
    {
        _vm.FisicoFinanceiroCommand.Execute(null);
        return _ambiente.Dialogos.FisicosFinanceiros.Last();
    }

    private string Planilha(string conteudo)
    {
        var caminho = Path.Combine(_pasta, $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    [Fact]
    public void SemValores_PesaPelaDuracao_EAvisa()
    {
        var tela = Abrir();

        (tela.SemValores, tela.Aviso, tela.RotuloDoValor).Should().Be((true, FisicoFinanceiroViewModel.AvisoSemValores, "Peso (dias)"));
        tela.RotulosDosPeriodos.Should().Equal("28/09–30/09", "01/10–16/10"); // o último vai até o fim do projeto
        tela.Linhas.Select(l => (l.Nome, l.Valor)).Should().Equal(("1 A", "5"), ("2 B", "10"), ("% do período", ""), ("% acumulado", ""));
        tela.Linhas[0].Celulas.Select(c => c.Percentual).Should().Equal("60,0%", "40,0%");
        tela.Linhas[^1].Celulas.Select(c => c.Percentual).Should().Equal("20,0%", "100,0%");
        tela.Resumo.Should().Be("2 períodos · realizado em 28/09/2026: 0,0%");

        tela.MostrarColunaValorCommand.Execute(null);
        _vm.Colunas["Valor"].Visivel.Should().BeTrue();
    }

    [Fact]
    public void ComValores_TabelaEmReais_EEscolhas()
    {
        _a.Valor = "5.000";
        _b.Valor = "10.000";
        _a.PercentualConcluido = 50;
        var tela = Abrir();

        (tela.SemValores, tela.RotuloDoValor).Should().Be((false, "Valor (R$)"));
        tela.Linhas.Select(l => (l.Nome, l.Valor)).Should().Equal(("1 A", "5.000,00"), ("2 B", "10.000,00"), ("Total do período", "15.000,00"), ("Acumulado", ""));
        tela.Linhas[2].Celulas.Select(c => (c.Valor, c.Percentual)).Should().Equal(("3.000,00", "20,0%"), ("12.000,00", "80,0%"));
        tela.Linhas[3].Celulas[^1].Should().Be(new CelulaFisicoFinanceira("15.000,00", "100,0%"));
        tela.Resumo.Should().Be("Valor total: R$ 15.000,00 · 2 períodos · realizado em 28/09/2026: 16,7% (R$ 2.500,00)");

        tela.PeriodicidadeEscolhida = tela.Periodicidades[0];
        tela.RotulosDosPeriodos.Should().HaveCount(3);
        tela.LinhasEscolhidas = tela.OpcoesDeLinhas[1];
        tela.Linhas.Should().HaveCount(4);
        tela.Curva!.Series.Single().Nome.Should().Be("Cronograma atual");
    }

    [Fact]
    public void DatasDeCorte_EErro()
    {
        var tela = Abrir();

        tela.PeriodicidadeEscolhida = tela.Periodicidades.Single(p => p.Valor == Periodicidade.DatasDeCorte);
        tela.MostraCortes.Should().BeTrue();
        tela.RotulosDosPeriodos.Should().ContainSingle("sem datas, um período só");
        tela.TextoDosCortes = "02/10/2026, 09/10/2026";
        tela.RotulosDosPeriodos.Should().Equal("28/09–02/10", "03/10–09/10", "10/10–16/10");

        tela.TextoDosCortes = "32/10/2026";
        (tela.Erro, tela.Linhas.Count, tela.ExportarPdfCommand.CanExecute(null)).Should().Be(("\"32/10/2026\" não é uma data (use dd/mm/aaaa).", 0, false));
    }

    [Fact]
    public void LinhaDeBase_ComoBase_ECurvaComAsDuas()
    {
        _a.Valor = "1000";
        _ambiente.Dialogos.TextoDigitado = "";
        _vm.SalvarLinhaDeBaseCommand.Execute(null);
        _a.Duracao = 10;
        var tela = Abrir();

        tela.Bases.Select(b => b.Texto).Should().Equal("Cronograma atual", "Linha de base (LB0)");
        tela.Curva!.Series.Select(s => s.Nome).Should().Equal("Linha de base (LB0)", "Cronograma atual");
        tela.BaseEscolhida = tela.Bases[1];
        tela.Linhas[0].Celulas.Select(c => c.Valor).Should().StartWith(["600,00"], "na linha de base, A tinha 5 dias: 3 em setembro");
    }

    [Fact]
    public async Task Exportar_PdfECsv()
    {
        _a.Valor = "5.000";
        var tela = Abrir();
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "ff");

        await tela.ExportarPdfCommand.ExecuteAsync(null);
        await tela.ExportarCsvCommand.ExecuteAsync(null);

        File.ReadAllBytes(Path.Combine(_pasta, "ff.pdf"))[..5].Should().Equal("%PDF-"u8.ToArray());
        var csv = File.ReadAllText(Path.Combine(_pasta, "ff.csv"));
        csv.Should().StartWith("EDT;Etapa;Valor (R$);28/09–30/09 (R$);28/09–30/09 (%);01/10–16/10 (R$);01/10–16/10 (%)")
            .And.Contain("1;A;5000,00;3000,00;60,00;2000,00;40,00").And.Contain(";Acumulado;");
        _ambiente.Dialogos.Exportacoes.Select(e => (e.Nome, e.Extensao)).Should().Equal(
            ("Físico-financeiro - Projeto sem título", "pdf"), ("Físico-financeiro - Projeto sem título", "csv"));
        tela.Mensagem.Should().Be("Salvo em ff.csv.");

        _ambiente.Dialogos.ArquivoExportado = null;
        await tela.ExportarPdfCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Salvo em ff.csv.", "cancelou");
    }

    [Fact]
    public async Task ImportarValores_DaPlanilha_UmPassoDoDesfazer()
    {
        _ambiente.Dialogos.PlanilhaEscolhida = Planilha("Item;Descrição;Valor\n1;A;1.500,00\n2;B;2500\n9;X;10\n");

        await _vm.ImportarValoresCommand.ExecuteAsync(null);

        (_a.Valor, _b.Valor, _vm.Colunas["Valor"].Visivel).Should().Be(("1.500,00", "2.500,00", true));
        _vm.Mensagem.Should().Be("2 valores importados. 1 EDT não encontrada: 9.");
        _vm.TextoDesfazer.Should().StartWith("Desfazer Importar valores de ");
        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Tarefas.Should().OnlyContain(l => l.Valor == "");
    }

    [Fact]
    public async Task ImportarValores_PelaTelaDoFisicoFinanceiro_ATabelaSeRefaz()
    {
        var tela = Abrir();
        tela.SemValores.Should().BeTrue();
        _ambiente.Dialogos.PlanilhaEscolhida = Planilha("1;100\n2;300\n");

        await tela.ImportarValoresCommand.ExecuteAsync(null);

        (tela.SemValores, tela.Linhas[^2].Valor).Should().Be((false, "400,00"));
    }

    [Fact]
    public async Task ImportarValores_Problemas()
    {
        _ambiente.Dialogos.PlanilhaEscolhida = null;
        await _vm.ImportarValoresCommand.ExecuteAsync(null);
        _vm.TextoDesfazer.Should().NotStartWith("Desfazer Importar", "cancelou");

        _ambiente.Dialogos.PlanilhaEscolhida = Planilha("só texto\n");
        await _vm.ImportarValoresCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().StartWith("A planilha não tem linhas com EDT e valor.");

        _ambiente.Dialogos.PlanilhaEscolhida = Path.Combine(_pasta, "nao-existe.csv");
        await _vm.ImportarValoresCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().StartWith("Não foi possível ler a planilha nao-existe.csv:");

        _ambiente.Dialogos.PlanilhaEscolhida = Planilha("EDT;Valor\n9;10\n");
        await _vm.ImportarValoresCommand.ExecuteAsync(null);
        (_vm.Mensagem, _vm.TextoDesfazer.StartsWith("Desfazer Importar")).Should().Be(("0 valores importados. 1 EDT não encontrada: 9.", false));
    }
}
