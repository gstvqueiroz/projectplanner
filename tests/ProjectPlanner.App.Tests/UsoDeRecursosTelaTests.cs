using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.Tests;

// Bloco 34, na tela: Projeto > Uso de recursos…. "Hoje" é seg 28/09/2026: A (5 d) e B (3 d) em paralelo; C (2 d) depois de A.
// Pedreiro (2 por dia): A 2, B 1 e C 1 por dia; cimento: 100 sc em A.
public sealed class UsoDeRecursosTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-uso-" + Guid.NewGuid().ToString("N"));

    public UsoDeRecursosTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        var a = Ambiente.AdicionarTarefa(_vm, "A", 5);
        var b = Ambiente.AdicionarTarefa(_vm, "B", 3);
        var c = Ambiente.AdicionarTarefa(_vm, "C", 2, "1");
        var recursos = _vm.Cronograma.Recursos;
        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, null, 300, 2);
        var cimento = new Recurso(Guid.NewGuid(), "Cimento", TipoDeRecurso.Material, "sc", 40);
        recursos.Adicionar(pedreiro);
        recursos.Adicionar(cimento);
        recursos.Atribuir(a.Tarefa, [new(pedreiro.Id, 2), new(cimento.Id, 100)]);
        recursos.Atribuir(b.Tarefa, [new(pedreiro.Id, 1)]);
        recursos.Atribuir(c.Tarefa, [new(pedreiro.Id, 1)]);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private UsoDeRecursosViewModel Abrir()
    {
        _vm.UsoDeRecursosCommand.Execute(null);
        return _ambiente.Dialogos.UsosDeRecursos.Last();
    }

    [Fact]
    public void Histograma_ESuperalocacoes()
    {
        var tela = Abrir();

        tela.Recursos.Select(r => r.Texto).Should().Equal("Pedreiro ⚠", "Cimento");
        (tela.RecursoEscolhido!.Texto, tela.Uso!.Pico).Should().Be(("Pedreiro ⚠", 3m));
        tela.ResumoDoRecurso.Should().Be("Pico de 3 por dia · disponível 2 · 3 dias acima · 15 dias de trabalho no projeto");
        tela.Superalocacoes.Should().Equal(new SuperalocacaoNaTela("Pedreiro", "28/09 a 30/09/2026 (3 dias úteis)", "3 por dia (disponível 2)", "1 A, 2 B"));
        tela.RecursoEscolhido = tela.Recursos[1];
        tela.ResumoDoRecurso.Should().Be("100 sc no projeto, de 28/09/2026 a 02/10/2026");
    }

    [Fact]
    public void Nivelamento_SugerirEAplicar_UmPassoDoDesfazer()
    {
        var tela = Abrir();
        tela.AplicarNivelamentoCommand.CanExecute(null).Should().BeFalse();

        tela.SugerirNivelamentoCommand.Execute(null);

        tela.TextoDaSugestao.Should().Be("1 tarefa muda; o término passa de 06/10/2026 para 07/10/2026.");
        tela.Mudancas.Should().Equal(new MudancaNaTela("2 B", "28/09/2026", "05/10/2026", "5 dias úteis", "adiada para liberar o recurso"));
        _ambiente.Dialogos.Confirmar = false;
        tela.AplicarNivelamentoCommand.Execute(null);
        _vm.Cronograma.Tarefas[1].NaoIniciarAntesDe.Should().BeNull("não confirmou");

        _ambiente.Dialogos.Confirmar = true;
        tela.AplicarNivelamentoCommand.Execute(null);

        (tela.Mensagem, tela.SemSuperalocacoes, _vm.TextoDesfazer).Should()
            .Be(("Nivelamento aplicado: 1 tarefa ganhou \"não iniciar antes de\".", true, "Desfazer Nivelar recursos"));
        _vm.Tarefas[1].DataInicio.Should().Be(new DateTime(2026, 10, 5));
        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Tarefas[1].NaoIniciarAntesDe.Should().BeNull();
    }

    [Fact]
    public async Task Necessidades_PorPeriodoETipo_EExportar()
    {
        var tela = Abrir();

        tela.Necessidades.Select(n => (n.Periodo, n.Recurso, n.Quantidade, n.Custo)).Should().Equal(
            ("28/09–04/10", "Pedreiro", "13 dias", "3.900,00"), ("28/09–04/10", "Cimento", "100 sc", "4.000,00"), ("05/10–06/10", "Pedreiro", "2 dias", "600,00"));
        tela.TotalDasNecessidades.Should().Be("Total: R$ 8.500,00");
        tela.TipoEscolhido = tela.Tipos.Single(t => t.Texto == "Materiais");
        tela.Necessidades.Should().ContainSingle().Which.Recurso.Should().Be("Cimento");
        tela.PeriodicidadeEscolhida = tela.Periodicidades[2];
        tela.Necessidades.Select(n => (n.Periodo, n.Quantidade)).Should().Equal(("28/09–30/09", "60 sc"), ("01/10–06/10", "40 sc"));

        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "necessidades");
        await tela.ExportarNecessidadesCommand.ExecuteAsync(null);
        File.ReadAllText(Path.Combine(_pasta, "necessidades.csv")).Should().StartWith("Período;De;Até;Recurso")
            .And.Contain("Cimento;Material;sc;60;2400,00", "exporta o que está na tela (só materiais, mensal)");
    }

    [Fact]
    public void SemRecursos_Avisa()
    {
        _vm.NovoProjetoCommand.Execute(null);
        _ambiente.Dialogos.RespostaAlteracoesPendentes = Services.RespostaAlteracoesPendentes.NaoSalvar;
        _vm.NovoProjetoCommand.Execute(null);

        var tela = Abrir();

        (tela.SemRecursos, tela.Recursos.Count, tela.Necessidades.Count, tela.SemSuperalocacoes).Should().Be((true, 0, 0, true));
    }
}
