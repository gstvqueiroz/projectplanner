using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (E1): exportar o cronograma para CSV ("hoje" é seg 28/09/2026).
public sealed class ExportacaoCsvTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-csv-" + Guid.NewGuid().ToString("N"));

    public ExportacaoCsvTests()
    {
        _vm = _ambiente.CriarViewModel();
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Tabela_CamposProtegidos_ECrlf()
    {
        var csv = TabelaCsv.Gerar(["A", "B"], [["1;2", "diz \"oi\""], ["linha\nquebrada", ""]]);

        csv.Should().Be("A;B\r\n\"1;2\";\"diz \"\"oi\"\"\"\r\n\"linha\nquebrada\";\r\n");
    }

    [Fact]
    public void Tabela_LinhaComCamposAMais_Recusada_ETextoLivreSemFormula()
    {
        var errada = () => TabelaCsv.Gerar(["A", "B"], [["só um"]]);
        errada.Should().Throw<ArgumentException>().WithMessage("Uma linha tem 1 campos; o cabeçalho tem 2.*");

        new[] { "=SOMA(A1)", "+1", "-x", "@cmd", "Normal", "", null }.Select(TabelaCsv.TextoLivre)
            .Should().Equal("'=SOMA(A1)", "'+1", "'-x", "'@cmd", "Normal", "", "");
        TabelaCsv.Codificacao.GetPreamble().Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public void TabelaDoCronograma_UmaLinhaPorTarefa_ComAsDatasDoCalculo()
    {
        var fase = Ambiente.AdicionarTarefa(_vm, "Fundação", 1);
        _vm.TarefaSelecionada = fase;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        var escavar = _vm.TarefaSelecionada!;
        (escavar.Nome, escavar.Duracao, escavar.PercentualConcluido, escavar.Notas) = ("Escavar", 3, 100, "Solo; argiloso");
        Ambiente.AdicionarTarefa(_vm, "Entrega", 0, "2");
        _vm.Tarefas[0].Expandido = false; // recolhido também vai

        _vm.TabelaEmCsv().Split("\r\n").Should().Equal(
            "Nº;EDT;Nível;Nome;Tipo;Duração (dias úteis);Início (cronograma);Término (cronograma);% concluído;Predecessoras;Sucessoras;Folga (dias úteis);Crítica;Fotos;Notas",
            "1;1;1;Fundação;Resumo;3;28/09/2026;30/09/2026;100;;;0;Sim;0;",
            "2;1.1;2;Escavar;Tarefa;3;28/09/2026;30/09/2026;100;;3;0;Sim;0;\"Solo; argiloso\"",
            "3;2;1;Entrega;Marco;0;30/09/2026;30/09/2026;0;2;;0;Sim;0;",
            "");
    }

    [Fact]
    public void Exportar_GravaComBom_EAvisa()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _ambiente.Dialogos.ArquivoCsv = Path.Combine(_pasta, "obra.csv");

        _vm.ExportarCsvCommand.Execute(null);

        var bytes = File.ReadAllBytes(_ambiente.Dialogos.ArquivoCsv);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        File.ReadAllText(_ambiente.Dialogos.ArquivoCsv, TabelaCsv.Codificacao).Should().Be(_vm.TabelaEmCsv());
        (_vm.Mensagem, _ambiente.Dialogos.CsvSugerido).Should().Be(("Cronograma exportado para obra.csv (1 tarefa).", "Projeto sem título.csv"));
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue("exportar não salva o projeto");
    }

    [Fact]
    public void Exportar_Cancelado_NaoGravaNada_EComArquivoSalvoSugereONome()
    {
        _ambiente.Dialogos.ArquivoParaSalvar = "/projetos/Residência Silva.pplan";
        _vm.SalvarProjetoCommand.Execute(null);

        _vm.ExportarCsvCommand.Execute(null);

        (_ambiente.Dialogos.CsvSugerido, Directory.GetFiles(_pasta).Length).Should().Be(("Residência Silva.csv", 0));
        _vm.Mensagem.Should().StartWith("Projeto salvo em");
    }

    [Fact]
    public void Exportar_PastaQueNaoExiste_Avisa()
    {
        _ambiente.Dialogos.ArquivoCsv = Path.Combine(_pasta, "sumiu", "obra.csv");

        _vm.ExportarCsvCommand.Execute(null);

        _vm.Mensagem.Should().Be("Não foi possível exportar o cronograma. A pasta do arquivo não existe mais.");
    }
}
