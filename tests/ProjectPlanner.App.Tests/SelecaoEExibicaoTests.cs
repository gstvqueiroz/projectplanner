using System.ComponentModel;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Cenário: A (3 dias) → B (2 dias), críticas; C (1 dia) solta, com folga.
public class SelecaoEExibicaoTests
{
    private readonly Ambiente _ambiente = new();

    private MainWindowViewModel Cenario()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "A", 3);
        Ambiente.AdicionarTarefa(vm, "B", 2, "1");
        Ambiente.AdicionarTarefa(vm, "C", 1);
        return vm;
    }

    private static List<string?> AvisosDe(INotifyPropertyChanged origem)
    {
        var avisos = new List<string?>();
        origem.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);
        return avisos;
    }

    // --- Seleção Grid → Gantt ---

    [Fact]
    public void SelecionarNaGrid_DestacaABarraDaMesmaTarefaNoGantt()
    {
        var vm = Cenario();
        var avisos = AvisosDe(vm.Gantt);

        vm.TarefaSelecionada = vm.Tarefas[1];

        vm.Gantt.TarefaDestacada.Should().Be(vm.Tarefas[1].Tarefa.Id);
        avisos.Should().Contain(nameof(GanttViewModel.TarefaDestacada));
        vm.Gantt.Layout!.Barras.Where(vm.Gantt.EstaDestacada).Select(b => b.Nome).Should().Equal("B");
    }

    [Fact]
    public void TrocarASelecao_MoveODestaque()
    {
        var vm = Cenario();
        vm.TarefaSelecionada = vm.Tarefas[0];

        vm.TarefaSelecionada = vm.Tarefas[2];

        vm.Gantt.Layout!.Barras.Where(vm.Gantt.EstaDestacada).Select(b => b.Nome).Should().Equal("C");
    }

    [Fact]
    public void SemSelecao_NenhumaBarraDestacada()
    {
        var vm = Cenario();

        vm.TarefaSelecionada = null;

        vm.Gantt.TarefaDestacada.Should().BeNull();
        vm.Gantt.Layout!.Barras.Should().NotContain(b => vm.Gantt.EstaDestacada(b));
    }

    [Fact]
    public void RemoverATarefaSelecionada_ODestaquePassaParaANovaSelecao()
    {
        var vm = Cenario();
        vm.TarefaSelecionada = vm.Tarefas[2];

        vm.RemoverTarefaCommand.Execute(null);

        vm.TarefaSelecionada!.Nome.Should().Be("B");
        vm.Gantt.TarefaDestacada.Should().Be(vm.TarefaSelecionada.Tarefa.Id);
    }

    [Fact]
    public void RecalcularOCronograma_MantemODestaque()
    {
        var vm = Cenario();
        vm.TarefaSelecionada = vm.Tarefas[0];

        vm.Tarefas[0].Duracao = 5;

        vm.Gantt.Layout!.Barras.Where(vm.Gantt.EstaDestacada).Select(b => b.Nome).Should().Equal("A");
    }

    // --- Gantt → Grid ---

    [Fact]
    public void ClicarNumaBarra_SelecionaALinhaDaTarefa()
    {
        var vm = Cenario();
        vm.TarefaSelecionada = vm.Tarefas[0];
        var barraDeC = vm.Gantt.Layout!.Barras.Single(b => b.Nome == "C");

        vm.Gantt.ClicarNaBarra(barraDeC.TarefaId);

        vm.TarefaSelecionada.Should().BeSameAs(vm.Tarefas[2]);
        vm.Gantt.TarefaDestacada.Should().Be(barraDeC.TarefaId);
        vm.RemoverTarefaCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ClicarNumaBarraDeTarefaInexistente_LimpaASelecaoSemErro()
    {
        var vm = Cenario();
        vm.TarefaSelecionada = vm.Tarefas[0];

        vm.Gantt.ClicarNaBarra(Guid.NewGuid());

        vm.TarefaSelecionada.Should().BeNull();
        vm.Gantt.TarefaDestacada.Should().BeNull();
    }

    // --- Nenhuma seleção visual ---

    [Fact]
    public void MostrarSelecaoDesligado_TiraODestaqueMasASelecaoContinua()
    {
        var vm = Cenario();
        vm.TarefaSelecionada = vm.Tarefas[1];

        vm.MostrarSelecao = false;

        vm.TarefaSelecionada.Should().BeSameAs(vm.Tarefas[1]);
        vm.Gantt.TarefaDestacada.Should().BeNull();
        vm.RemoverTarefaCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void MostrarSelecaoDesligado_EdicaoERemocaoContinuamFuncionando()
    {
        var vm = Cenario();
        vm.MostrarSelecao = false;
        vm.TarefaSelecionada = vm.Tarefas[0];

        vm.Tarefas[0].Nome = "Fundação";
        vm.Tarefas[0].Duracao = 4;

        vm.Tarefas[0].Tarefa.Nome.Should().Be("Fundação");
        vm.Gantt.Layout!.Barras[0].Largura.Should().Be(4 * vm.Gantt.PixelsPorDia);
        vm.Gantt.TarefaDestacada.Should().BeNull();

        vm.RemoverTarefaCommand.Execute(null);
        vm.Tarefas.Select(t => t.Nome).Should().Equal("B", "C");
    }

    [Fact]
    public void MostrarSelecaoDesligado_ClicarNaBarraSelecionaSemDestacar()
    {
        var vm = Cenario();
        vm.MostrarSelecao = false;

        vm.Gantt.ClicarNaBarra(vm.Tarefas[2].Tarefa.Id);

        vm.TarefaSelecionada.Should().BeSameAs(vm.Tarefas[2]);
        vm.Gantt.TarefaDestacada.Should().BeNull();
    }

    [Fact]
    public void MostrarSelecaoReligado_ODestaqueVoltaNaTarefaSelecionada()
    {
        var vm = Cenario();
        vm.MostrarSelecao = false;
        vm.TarefaSelecionada = vm.Tarefas[2];

        vm.MostrarSelecao = true;

        vm.Gantt.TarefaDestacada.Should().Be(vm.Tarefas[2].Tarefa.Id);
    }

    [Fact]
    public void OpcoesDeExibicao_ComecamLigadas()
    {
        var vm = _ambiente.CriarViewModel();

        vm.MostrarSelecao.Should().BeTrue();
        vm.Gantt.MostrarCaminhoCritico.Should().BeTrue();
    }

    [Fact]
    public void MudarAsOpcoesDeExibicao_NaoMarcaOProjetoComoAlterado()
    {
        // São da sessão, não do projeto: não vão para o .pplan nem pedem para salvar.
        var vm = Cenario();
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);
        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();

        vm.MostrarSelecao = false;
        vm.Gantt.MostrarCaminhoCritico = false;
        vm.TarefaSelecionada = vm.Tarefas[0];

        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    // --- Caminho crítico ---

    [Fact]
    public void CaminhoCriticoLigado_BarrasESetasCriticasPintadasComoCriticas()
    {
        var vm = Cenario();

        var gantt = vm.Gantt;
        gantt.Layout!.Barras.Select(b => (b.Nome, gantt.PintarComoCritica(b))).Should().Equal(
            ("A", true), ("B", true), ("C", false));
        gantt.Layout.Setas.Should().ContainSingle().Which.Should().Match<Core.Gantt.SetaGantt>(s => gantt.PintarComoCritica(s));
    }

    [Fact]
    public void CaminhoCriticoDesligado_NadaPintadoComoCriticoSemMudarOCalculo()
    {
        var vm = Cenario();
        var chamadasAoCpm = _ambiente.Calculadora.Chamadas;
        var antes = vm.Tarefas.Select(t => (t.EhCritica, t.Folga)).ToList();
        var avisos = AvisosDe(vm.Gantt);

        vm.Gantt.MostrarCaminhoCritico = false;

        avisos.Should().Contain(nameof(GanttViewModel.MostrarCaminhoCritico));
        vm.Gantt.Layout!.Barras.Should().NotContain(b => vm.Gantt.PintarComoCritica(b));
        vm.Gantt.Layout.Setas.Should().NotContain(s => vm.Gantt.PintarComoCritica(s));
        // O cálculo continua o mesmo: os dados ainda dizem quais são críticas.
        vm.Gantt.Layout.Barras.Select(b => b.EhCritica).Should().Equal(true, true, false);
        vm.Tarefas.Select(t => (t.EhCritica, t.Folga)).Should().Equal(antes);
        vm.Tarefas.Select(t => t.Tarefa.EhCritica).Should().Equal(true, true, false);
        _ambiente.Calculadora.Chamadas.Should().Be(chamadasAoCpm);
    }

    [Fact]
    public void CaminhoCriticoDesligado_ContinuaDesligadoAposRecalcular()
    {
        var vm = Cenario();
        vm.Gantt.MostrarCaminhoCritico = false;

        vm.Tarefas[0].Duracao = 5;
        vm.RecalcularCronogramaCommand.Execute(null);

        vm.Gantt.MostrarCaminhoCritico.Should().BeFalse();
        vm.Gantt.Layout!.Barras.Should().NotContain(b => vm.Gantt.PintarComoCritica(b));
    }

    [Fact]
    public void CaminhoCriticoReligado_VoltaAPintarAsCriticas()
    {
        var vm = Cenario();
        vm.Gantt.MostrarCaminhoCritico = false;

        vm.Gantt.MostrarCaminhoCritico = true;

        vm.Gantt.Layout!.Barras.Count(b => vm.Gantt.PintarComoCritica(b)).Should().Be(2);
    }
}
