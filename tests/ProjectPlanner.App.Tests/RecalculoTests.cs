using ProjectPlanner.App.ViewModels;
using static ProjectPlanner.App.Tests.Ambiente;

namespace ProjectPlanner.App.Tests;

public class RecalculoTests
{
    private readonly Ambiente _ambiente = new();

    // O cenário de referência do Bloco 3, montado pela grid. Dia útil 0 = seg 28/09/2026.
    //
    //   Nº  Tarefa  Duração  Predecessoras   Ida      Volta    Folga
    //   1     A        3      —              0–2      0–2      0
    //   2     B        4      1              3–6      3–6      0
    //   3     C        2      1              3–4      7–8      4
    //   4     D        5      2              7–11     7–11     0
    //   5     E        3      3              5–7      9–11     4
    //   6     F        2      4; 5           12–13    12–13    0
    //
    // Fim do projeto: dia 13 (qui 15/10/2026).
    private static MainWindowViewModel CenarioDeReferencia(Ambiente ambiente)
    {
        var vm = ambiente.CriarViewModel();
        AdicionarTarefa(vm, "A", 3);
        AdicionarTarefa(vm, "B", 4, "1");
        AdicionarTarefa(vm, "C", 2, "1");
        AdicionarTarefa(vm, "D", 5, "2");
        AdicionarTarefa(vm, "E", 3, "3");
        AdicionarTarefa(vm, "F", 2, "4; 5");
        return vm;
    }

    [Fact]
    public void RecalcularCronograma_AtualizaESEFLSLFFolgaECriticaDeCadaTarefa()
    {
        var vm = CenarioDeReferencia(_ambiente);
        var chamadasAntes = _ambiente.Calculadora.Chamadas;

        vm.RecalcularCronogramaCommand.Execute(null);

        _ambiente.Calculadora.Chamadas.Should().Be(chamadasAntes + 1);
        vm.Tarefas.Select(t => (t.Nome, t.InicioMaisCedo, t.TerminoMaisCedo, t.InicioMaisTarde, t.TerminoMaisTarde, t.Folga, t.EhCritica))
            .Should().Equal(
                ("A", DiaUtil(0), DiaUtil(2), DiaUtil(0), DiaUtil(2), 0, true),
                ("B", DiaUtil(3), DiaUtil(6), DiaUtil(3), DiaUtil(6), 0, true),
                ("C", DiaUtil(3), DiaUtil(4), DiaUtil(7), DiaUtil(8), 4, false),
                ("D", DiaUtil(7), DiaUtil(11), DiaUtil(7), DiaUtil(11), 0, true),
                ("E", DiaUtil(5), DiaUtil(7), DiaUtil(9), DiaUtil(11), 4, false),
                ("F", DiaUtil(12), DiaUtil(13), DiaUtil(12), DiaUtil(13), 0, true));
        vm.TerminoProjeto.Should().Be(new DateTime(2026, 10, 15));
        vm.Mensagem.Should().Be("Cronograma recalculado.");
    }

    [Fact]
    public void RecalcularCronograma_AvisaATelaDasColunasCalculadas()
    {
        var vm = CenarioDeReferencia(_ambiente);
        var avisadas = new List<string?>();
        vm.Tarefas[2].PropertyChanged += (_, e) => avisadas.Add(e.PropertyName);

        vm.RecalcularCronogramaCommand.Execute(null);

        avisadas.Should().Contain([
            nameof(TarefaViewModel.InicioMaisCedo), nameof(TarefaViewModel.TerminoMaisCedo),
            nameof(TarefaViewModel.InicioMaisTarde), nameof(TarefaViewModel.TerminoMaisTarde),
            nameof(TarefaViewModel.Folga), nameof(TarefaViewModel.EhCritica),
        ]);
    }

    [Fact]
    public void Editar_RecalculaSozinho()
    {
        // C passa de 2 para 7 dias: o ramo A → C → E → F vira o crítico, e B e D ganham 1 dia de folga.
        var vm = CenarioDeReferencia(_ambiente);

        vm.Tarefas[2].Duracao = 7;

        vm.Tarefas.Where(t => t.EhCritica).Select(t => t.Nome).Should().Equal("A", "C", "E", "F");
        vm.Tarefas[1].Folga.Should().Be(1);
        vm.TerminoProjeto.Should().Be(DiaUtil(14));
    }

    [Fact]
    public void Recalcular_ComErroNoCalculo_MostraAMensagemSemLancar()
    {
        // Projeto começando na última segunda representável: B não cabe antes de 31/12/9999.
        var vm = _ambiente.CriarViewModel();
        vm.InicioProjeto = new DateTime(9999, 12, 27);
        AdicionarTarefa(vm, "A", 5);

        var adicionar = () => AdicionarTarefa(vm, "B", 1, "1");

        adicionar.Should().NotThrow();
        vm.Mensagem.Should().StartWith("Não foi possível calcular o cronograma.");
    }
}
