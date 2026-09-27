using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Cronograma de referência (dias úteis a partir de seg 28/09 = dia 0):
//   1 A (3 dias)                    dias 0–2, crítica
//   2 B (2 dias, pred 1)            dias 3–4, crítica
//   3 C (1 dia, sem vínculos)       dia 0, folga até o fim do projeto (dia 5) = 5
//   4 D (1 dia, pred "1II+2; 2")    II+2 pede início ≥ 2, FS de B pede ≥ 5 → dia 5, crítica
public class NovasColunasTests
{
    private readonly Ambiente _ambiente = new();

    private MainWindowViewModel Cenario()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "A", 3);
        Ambiente.AdicionarTarefa(vm, "B", 2, "1");
        Ambiente.AdicionarTarefa(vm, "C", 1);
        Ambiente.AdicionarTarefa(vm, "D", 1, "1II+2; 2");
        return vm;
    }

    [Fact]
    public void Folga_MostraAFolgaCalculadaPeloCpm()
    {
        var vm = Cenario();

        vm.Tarefas.Select(t => (t.Nome, t.Folga)).Should().Equal(
            ("A", 0), ("B", 0), ("C", (int?)5), ("D", 0));
    }

    [Fact]
    public void Sucessoras_MostraQuemDependeDeCadaTarefaNaNotacaoDasPredecessoras()
    {
        var vm = Cenario();

        vm.Tarefas.Select(t => (t.Nome, t.Sucessoras)).Should().Equal(
            ("A", "2; 4II+2"), ("B", "4"), ("C", ""), ("D", ""));
    }

    [Fact]
    public void Sucessoras_AcompanhaMudancasNasPredecessorasDeOutraLinha()
    {
        var vm = Cenario();
        var avisos = new List<string?>();
        vm.Tarefas[2].PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.Tarefas[3].Predecessoras = "3TT-1";

        vm.Tarefas[2].Sucessoras.Should().Be("4TT-1");
        vm.Tarefas[0].Sucessoras.Should().Be("2");
        avisos.Should().Contain(nameof(TarefaViewModel.Sucessoras));
    }

    [Fact]
    public void Sucessoras_RenumeraAoRemoverUmaLinha()
    {
        var vm = Cenario();
        vm.TarefaSelecionada = vm.Tarefas[1]; // B: D perde o vínculo com ela e passa a ser a linha 3

        vm.RemoverTarefaCommand.Execute(null);

        vm.Tarefas[0].Sucessoras.Should().Be("3II+2");
    }

    [Fact]
    public void Edt_EONumeroDaLinhaEAcompanhaARenumeracao()
    {
        var vm = Cenario();
        vm.Tarefas.Select(t => t.Edt).Should().Equal("1", "2", "3", "4");
        vm.TarefaSelecionada = vm.Tarefas[0];

        vm.RemoverTarefaCommand.Execute(null);

        vm.Tarefas.Select(t => t.Edt).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void Notas_EditarGuardaNaTarefaEMarcaAlteracao()
    {
        var vm = Cenario();
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);

        vm.Tarefas[0].Notas = "Conferir o solo antes";

        vm.Tarefas[0].Tarefa.Notas.Should().Be("Conferir o solo antes");
        vm.Tarefas[0].Notas.Should().Be("Conferir o solo antes");
        vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Notas_ApagarDeixaATarefaSemNotas(string vazio)
    {
        var vm = Cenario();
        vm.Tarefas[0].Notas = "Algo";

        vm.Tarefas[0].Notas = vazio;

        vm.Tarefas[0].Tarefa.Notas.Should().BeNull();
        vm.Tarefas[0].Notas.Should().Be("");
    }

    [Fact]
    public void Notas_MesmoValor_NaoMarcaAlteracao()
    {
        var vm = Cenario();
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);

        vm.Tarefas[2].Notas = "";

        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void Notas_PersistemAoSalvarEReabrirPeloRepositorio()
    {
        var repositorio = new RepositorioEmMemoria();
        MainWindowViewModel Janela() => new(repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias,
                _ambiente.Assistente, _ambiente.PreferenciasInterface);
        var vm = Janela();
        Ambiente.AdicionarTarefa(vm, "A", 3).Notas = "Linha 1\nLinha 2";
        Ambiente.AdicionarTarefa(vm, "B", 2);
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);

        var reaberta = Janela();
        _ambiente.Dialogos.ArquivoParaAbrir = "/obra.pplan";
        reaberta.AbrirProjetoCommand.Execute(null);

        reaberta.Tarefas.Select(t => t.Notas).Should().Equal("Linha 1\nLinha 2", "");
        reaberta.Tarefas[1].Tarefa.Notas.Should().BeNull();
    }

    // --- Cor de crítica na grid e o controle do Bloco 9 ---

    [Fact]
    public void CaminhoCriticoLigado_SoAsCriticasTemFundoDeCritica()
    {
        var vm = Cenario();

        vm.Tarefas.Select(t => t.DestacarComoCritica).Should().Equal(true, true, false, true);
    }

    [Fact]
    public void CaminhoCriticoDesligado_NenhumaLinhaComFundoDeCriticaMasOCalculoContinua()
    {
        var vm = Cenario();
        var avisos = new List<string?>();
        vm.Tarefas[0].PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.Gantt.MostrarCaminhoCritico = false;

        vm.Tarefas.Should().NotContain(t => t.DestacarComoCritica);
        avisos.Should().Contain(nameof(TarefaViewModel.DestacarComoCritica));
        vm.Tarefas.Select(t => t.EhCritica).Should().Equal(true, true, false, true);
    }

    [Fact]
    public void CaminhoCriticoReligado_OFundoVoltaEAcompanhaORecalculo()
    {
        var vm = Cenario();
        vm.Gantt.MostrarCaminhoCritico = false;
        vm.Gantt.MostrarCaminhoCritico = true;

        vm.Tarefas[2].Duracao = 10; // C passa a ser a mais longa: só ela fica crítica

        vm.Tarefas.Select(t => t.DestacarComoCritica).Should().Equal(false, false, true, false);
    }
}
