using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.Tests;

// Bloco 32, na tela: Projeto > Recursos… (com o catálogo pessoal), Tarefa > Recursos da tarefa… e as colunas Recursos,
// Custo orçado e Margem. Alvenaria (5 d, Valor R$ 5.000) e Pintura (2 d).
public sealed class RecursosTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _alvenaria, _pintura;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-recursos-" + Guid.NewGuid().ToString("N"));

    public RecursosTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _alvenaria = Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        _pintura = Ambiente.AdicionarTarefa(_vm, "Pintura", 2, "1");
        _alvenaria.Valor = "5.000";
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private RecursosViewModel AbrirRecursos()
    {
        _vm.RecursosDoProjetoCommand.Execute(null);
        return _ambiente.Dialogos.Recursos.Last();
    }

    private static void Cadastrar(RecursosViewModel tela, string nome, int tipo, string custo, string unidade = "", string disponivel = "")
    {
        tela.NovoCommand.Execute(null);
        (tela.Nome, tela.TipoEscolhido) = (nome, tela.Tipos[tipo]);
        (tela.Unidade, tela.Custo, tela.Disponivel) = (unidade, custo, disponivel);
        tela.SalvarCommand.Execute(null);
    }

    private RecursosDaTarefaViewModel AbrirDaTarefa(TarefaViewModel linha)
    {
        _vm.TarefaSelecionada = linha;
        _vm.RecursosDaTarefaCommand.Execute(null);
        return _ambiente.Dialogos.RecursosDasTarefas.Last();
    }

    [Fact]
    public void Recursos_CadastrarAlterarERemover()
    {
        var tela = AbrirRecursos();
        tela.SemRecursos.Should().BeTrue();

        Cadastrar(tela, "Pedreiro", 0, "280", disponivel: "2");
        Cadastrar(tela, "Cimento CP-II", 1, "38,90", "sc");

        tela.Lista.Select(r => (r.Nome, r.Tipo, r.Custo, r.Disponivel, r.Uso)).Should().Equal(
            ("Pedreiro", "Profissional", "280,00/dia", "2 por dia", "não usado"), ("Cimento CP-II", "Material", "38,90/sc", "", "não usado"));
        (_vm.TextoDesfazer, tela.Mensagem).Should().Be(("Desfazer Recurso \"Cimento CP-II\"", "Cadastrado: Cimento CP-II. Atribua-o às tarefas em Tarefa > Recursos da tarefa…."));

        Cadastrar(tela, "pedreiro", 0, "1");
        tela.Mensagem.Should().Be("Já existe o recurso \"pedreiro\" (profissional).");
        tela.Selecionado = tela.Lista[0];
        (tela.TextoDoSalvar, tela.Nome, tela.Custo, tela.Disponivel, tela.PorDia).Should().Be(("Salvar alteração", "Pedreiro", "280,00", "2", true));
        tela.Custo = "300";
        tela.SalvarCommand.Execute(null);
        tela.Lista[0].Custo.Should().Be("300,00/dia");

        tela.Selecionado = tela.Lista[1];
        tela.RemoverCommand.Execute(null);
        tela.Lista.Should().ContainSingle();
    }

    [Fact]
    public void RecursosDaTarefa_AtribuirMudarETirar_ComAsColunasDaGrid()
    {
        var recursos = AbrirRecursos();
        Cadastrar(recursos, "Pedreiro", 0, "280");
        Cadastrar(recursos, "Tijolo", 1, "950", "mil");
        var tela = AbrirDaTarefa(_alvenaria);
        tela.Detalhe.Should().Be("5 dias úteis · Valor R$ 5.000,00");

        tela.RecursoEscolhido = tela.Disponiveis.Single(d => d.Texto.StartsWith("Pedreiro"));
        tela.RotuloDaQuantidade.Should().Be("Quantos por dia");
        tela.QuantidadeNova = "2";
        tela.AdicionarCommand.Execute(null);
        tela.RecursoEscolhido = tela.Disponiveis.Single();
        tela.RotuloDaQuantidade.Should().Be("Quantidade (mil)");
        tela.QuantidadeNova = "3";
        tela.AdicionarCommand.Execute(null);

        tela.Linhas.Select(l => (l.Nome, l.Quantidade, l.ComoMede, l.Total, l.Custo)).Should().Equal(
            ("Pedreiro", "2", "por dia", "10 dias", "2.800,00"), ("Tijolo", "3", "mil", "3 mil", "2.850,00"));
        (tela.CustoOrcado, tela.Margem, tela.MargemNegativa).Should().Be(("R$ 5.650,00", "R$ -650,00", true));
        (_alvenaria.Recursos, _alvenaria.CustoOrcado, _alvenaria.Margem, _alvenaria.MargemNegativa).Should()
            .Be(("2 Pedreiro; 3 mil Tijolo", "5.650,00", "-650,00", true));
        _vm.TextoDesfazer.Should().Be("Desfazer Recursos de \"Alvenaria\"");

        tela.Linhas[0].Quantidade = "1";
        tela.Linhas[0].Total.Should().Be("5 dias");
        tela.Linhas[0].Quantidade = "x";
        tela.Mensagem.Should().Be("\"x\" não é um número (use, por exemplo, 12,5).");
        tela.RemoverCommand.Execute(tela.Linhas[1]);
        tela.Linhas.Should().ContainSingle();
        tela.QuantidadeNova = "0";
        tela.AdicionarCommand.Execute(null);
        tela.Mensagem.Should().Be("A quantidade do recurso precisa ser maior que zero.");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Tarefas[0].Atribuicoes.Should().HaveCount(2, "desfazer volta o tijolo");
    }

    [Fact]
    public void RecursosDaTarefa_SemSelecaoOuResumo_Avisa_ERecursoEmUsoNaoSai()
    {
        _vm.TarefaSelecionada = null;
        _vm.RecursosDaTarefaCommand.Execute(null);
        _vm.Mensagem.Should().Be("Selecione uma tarefa para ver os recursos dela.");
        var recursos = AbrirRecursos();
        Cadastrar(recursos, "Servente", 0, "180");
        var tela = AbrirDaTarefa(_pintura);
        tela.QuantidadeNova = "1";
        tela.AdicionarCommand.Execute(null);

        recursos.Selecionado = recursos.Lista[0];
        recursos.RemoverCommand.Execute(null);

        recursos.Mensagem.Should().Be("\"Servente\" está em 1 tarefa (2 Pintura): tire-o delas antes de remover.");
        recursos.Lista[0].Uso.Should().Be("2 dias · 1 tarefa");
    }

    [Fact]
    public void Catalogo_GuardarETrazer()
    {
        var tela = AbrirRecursos();
        Cadastrar(tela, "Pedreiro", 0, "280");
        Cadastrar(tela, "Areia média", 1, "120", "m³");
        tela.GuardarTodosNoCatalogoCommand.Execute(null);
        (tela.Catalogo.Count, tela.Mensagem).Should().Be((2, "2 recursos guardados no seu catálogo."));

        // Em outro projeto, o catálogo continua lá.
        _ambiente.Dialogos.RespostaAlteracoesPendentes = Services.RespostaAlteracoesPendentes.NaoSalvar;
        _vm.NovoProjetoCommand.Execute(null);
        var outra = AbrirRecursos();
        outra.SelecionadoNoCatalogo = outra.Catalogo.Single(c => c.Nome == "Areia média");
        outra.TrazerDoCatalogoCommand.Execute(null);

        outra.Lista.Single().Should().Match<RecursoNaLista>(r => r.Nome == "Areia média" && r.Custo == "120,00/m³");
        outra.Lista.Single().Recurso.Id.Should().NotBe(_ambiente.Catalogo.Recursos.Single(r => r.Nome == "Areia média").Id);
        outra.TrazerDoCatalogoCommand.Execute(null);
        outra.Mensagem.Should().Be("Areia média já estava no projeto: o custo foi atualizado pelo do catálogo.");
    }

    [Fact]
    public async Task Planilha_ImportarEExportar()
    {
        var planilha = Path.Combine(_pasta, "insumos.csv");
        File.WriteAllText(planilha, "Insumo;Unidade;Preço;Fornecedor\nPedreiro;dia;300;Equipe\nCimento CP-II;sc;39,50;Depósito\n");
        _ambiente.Dialogos.PlanilhaEscolhida = planilha;
        var tela = AbrirRecursos();

        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("2 recursos novos, 0 atualizados.");
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "recursos");
        await tela.ExportarCsvCommand.ExecuteAsync(null);

        File.ReadAllText(Path.Combine(_pasta, "recursos.csv")).Should().Contain("Pedreiro;Profissional;dia;300,00;;Equipe").And.Contain("Cimento CP-II;Material;sc;39,50");
        _vm.Cronograma.Recursos.Lista.Select(r => r.Tipo).Should().Equal(TipoDeRecurso.Profissional, TipoDeRecurso.Material);
    }
}
