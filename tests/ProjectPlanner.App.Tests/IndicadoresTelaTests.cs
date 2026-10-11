using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.Tests;

// Bloco 37, na tela: Projeto > Custos realizados… e Indicadores…, e a página de indicadores no boletim e no relatório
// fotográfico. "Hoje" é seg 28/09/2026, o início do projeto: Escavação (5 d: 28/09–02/10, R$ 5.000) → Sapatas (10 d:
// 05/10–16/10, R$ 10.000).
public sealed class IndicadoresTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-indicadores-" + Guid.NewGuid().ToString("N"));

    public IndicadoresTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "Escavação", 5);
        _b = Ambiente.AdicionarTarefa(_vm, "Sapatas", 10, "1");
        _a.Valor = "5.000";
        _b.Valor = "10.000";
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private CustosViewModel AbrirCustos()
    {
        _vm.CustosRealizadosCommand.Execute(null);
        return _ambiente.Dialogos.Custos.Last();
    }

    private IndicadoresViewModel AbrirIndicadores()
    {
        _vm.IndicadoresCommand.Execute(null);
        return _ambiente.Dialogos.Indicadores.Last();
    }

    // ---- Custos realizados ----

    [Fact]
    public void Custos_LancarAlterarERemover_ComOsTotaisEODesfazer()
    {
        var tela = AbrirCustos();
        (tela.SemCustos, tela.TextoDoSalvar, tela.TarefaEscolhida.Texto).Should().Be((true, "Adicionar", "— Custo geral da obra —"));

        (tela.TarefaEscolhida, tela.Descricao, tela.Documento, tela.Valor) = (tela.Tarefas[1], "Cimento CP-II", "NF 123", "1.200");
        tela.SalvarCommand.Execute(null);

        tela.Lista.Single().Should().Match<CustoNaLista>(c => c.Tarefa == "1 Escavação" && c.Categoria == "Material" && c.Valor == "1.200,00" && c.Data == "28/09/2026");
        (tela.Total, tela.Mensagem, tela.Descricao).Should().Be(("R$ 1.200,00", "Lançado: Cimento CP-II (R$ 1.200,00).", ""));
        tela.PorCategoria.Should().Equal(new LinhaDoResumoNaTela("Material", "R$ 1.200,00"));
        tela.PorMes.Should().Equal(new LinhaDoResumoNaTela("set/26", "R$ 1.200,00"));
        (_vm.TextoDesfazer, _vm.PossuiAlteracoesNaoSalvas).Should().Be(("Desfazer Custo \"Cimento CP-II\"", true));

        tela.Selecionado = tela.Lista[0];
        (tela.TextoDoSalvar, tela.Descricao, tela.Valor, tela.TarefaEscolhida.Texto).Should().Be(("Salvar alteração", "Cimento CP-II", "1.200,00", "1 Escavação"));
        tela.Valor = "1.350";
        tela.SalvarCommand.Execute(null);
        (tela.Lista.Single().Valor, tela.Mensagem).Should().Be(("1.350,00", "Alterado: Cimento CP-II (R$ 1.350,00)."));

        tela.Valor = "abc";
        tela.SalvarCommand.Execute(null);
        tela.Mensagem.Should().StartWith("\"abc\" não é um valor em reais");
        tela.Selecionado = tela.Lista[0];
        tela.RemoverCommand.Execute(null);
        (tela.SemCustos, _vm.Cronograma.Custos.Vazio).Should().Be((true, true));

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Custos.Total.Should().Be(1350, "desfazer volta o custo removido");
    }

    [Fact]
    public async Task Custos_ImportarPlanilha()
    {
        var planilha = Path.Combine(_pasta, "custos.csv");
        File.WriteAllText(planilha, "Data;EDT;Categoria;Descrição;Valor\n01/10/2026;2;Folha;Armadores;3.000\n02/10/2026;;Locação;Betoneira;450\n");
        _ambiente.Dialogos.PlanilhaEscolhida = planilha;
        var tela = AbrirCustos();

        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);

        tela.Mensagem.Should().Be("2 custos importados (R$ 3.450,00).");
        tela.Lista.Select(c => (c.Tarefa, c.Categoria)).Should().Equal(("2 Sapatas", "Mão de obra"), ("Custo geral", "Equipamento"));
        _vm.TextoDesfazer.Should().Be("Desfazer Importar custos de custos.csv");

        _ambiente.Dialogos.PlanilhaEscolhida = Path.Combine(_pasta, "nao-existe.csv");
        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);
        tela.Mensagem.Should().StartWith("Não foi possível ler a planilha nao-existe.csv:");
    }

    // ---- Indicadores ----

    [Fact]
    public void Indicadores_CartoesEtapasECurva_NaDataDeStatus()
    {
        _a.PercentualConcluido = 100;
        _vm.Cronograma.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 10, 2), _a.Tarefa.Id, CategoriaDeCusto.MaoDeObra, "Folha", null, 4000));
        var tela = AbrirIndicadores();
        tela.Cabecalho.Should().Be("Indicadores em 28/09/2026");

        tela.DataDeStatus = new DateTime(2026, 10, 2);

        (tela.Cabecalho, tela.Subtitulo).Should().Be(("Indicadores em 02/10/2026", "Base: Cronograma atual · 0 tarefas atrasadas · 0 medições aprovadas"));
        tela.Cartoes.Select(c => (c.Rotulo, c.Valor)).Should().Equal(
            ("Físico previsto", "33,3%"), ("Físico realizado", "33,3%"), ("Financeiro medido", "0,0%"), ("Custo real", "R$ 4.000,00"),
            ("IDP (prazo)", "1,00"), ("IDC (custo)", "1,25"), ("Estimativa no término", "R$ 12.000,00"), ("Término pela tendência", "16/10/2026"));
        tela.Etapas.Select(e => (e.Nome, e.Orcamento, e.Realizado, e.CustoReal)).Should().Equal(("1 Escavação", "5.000,00", "100,0%", "4.000,00"), ("2 Sapatas", "10.000,00", "0,0%", "0,00"));
        (tela.SemAtrasos, tela.SemValores, tela.Curva!.Series.Select(s => s.Nome)).Should().BeEquivalentTo((true, false, new[] { "Cronograma atual", "Custo real" }));

        tela.DataDeStatus = null;
        (tela.Erro, tela.Painel, tela.ExportarPdfCommand.CanExecute(null)).Should().Be(("Escolha a data de status.", null, false));
        tela.HojeCommand.Execute(null);
        tela.Erro.Should().BeEmpty();
    }

    [Fact]
    public void Indicadores_SemValores_Avisam()
    {
        _a.Valor = "";
        _b.Valor = "";

        var tela = AbrirIndicadores();

        (tela.SemValores, tela.AvisoSemValores).Should().Be((true, TextosDosIndicadores.SemValores));
    }

    [Fact]
    public async Task Indicadores_ExportarPdfEWord()
    {
        var tela = AbrirIndicadores();
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "painel");

        await tela.ExportarPdfCommand.ExecuteAsync(null);
        await tela.ExportarWordCommand.ExecuteAsync(null);

        File.ReadAllBytes(Path.Combine(_pasta, "painel.pdf"))[..5].Should().Equal("%PDF-"u8.ToArray());
        File.ReadAllBytes(Path.Combine(_pasta, "painel.docx"))[..2].Should().Equal("PK"u8.ToArray());
        _ambiente.Dialogos.Exportacoes.Select(e => e.Nome).Should().Equal("Indicadores 2026-09-28 - Projeto sem título", "Indicadores 2026-09-28 - Projeto sem título");
        tela.Mensagem.Should().Be("Salvo em painel.docx.");
    }

    // ---- A página de indicadores nos relatórios ----

    [Fact]
    public async Task Boletim_ComIndicadores_TemUmaPaginaAMais()
    {
        _vm.MedicoesCommand.Execute(null);
        var medicoes = _ambiente.Dialogos.Medicoes.Last();
        medicoes.NovaMedicaoCommand.Execute(null);
        medicoes.Itens[0].NoPeriodo = "50";
        int Paginas(string arquivo) => System.Text.RegularExpressions.Regex.Matches(
            System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(arquivo)), @"/Type\s*/Page\b").Count;

        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "sem");
        await medicoes.ExportarPdfCommand.ExecuteAsync(null);
        medicoes.ComIndicadores = true;
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "com");
        await medicoes.ExportarPdfCommand.ExecuteAsync(null);

        Paginas(Path.Combine(_pasta, "com.pdf")).Should().Be(Paginas(Path.Combine(_pasta, "sem.pdf")) + 1);
    }
}
