using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Edicao;

namespace ProjectPlanner.App.Tests;

// Bloco 18 (Bloco 73: as regras do plano "Arrastar para mover"): soltar tarefas arrastadas na grid, sem mouse (a janela só
// traduz o mouse na lacuna entre as linhas e no puxão para o lado). Linhas: 0 Fundação · 1 Escavar · 2 Armar · 3 Estrutura ·
// 4 Cobertura; a lacuna g é o espaço antes da linha g.
public class ReordenacaoNaGridTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _fundacao, _escavar, _armar, _estrutura, _cobertura;

    // 1 Fundação / 1.1 Escavar / 1.2 Armar / 2 Estrutura / 3 Cobertura
    public ReordenacaoNaGridTests()
    {
        _vm = _ambiente.CriarViewModel();
        _fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _escavar = Subtarefa(_fundacao, "Escavar");
        _armar = Subtarefa(_fundacao, "Armar");
        _estrutura = Ambiente.AdicionarTarefa(_vm, "Estrutura", 2);
        _cobertura = Ambiente.AdicionarTarefa(_vm, "Cobertura", 2, "4");
        _ambiente.Dialogos.ArquivoParaSalvar = "/projetos/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);
    }

    private TarefaViewModel Subtarefa(TarefaViewModel pai, string nome)
    {
        _vm.TarefaSelecionada = pai;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        var linha = _vm.TarefaSelecionada!;
        linha.Nome = nome;
        return linha;
    }

    private List<string> Grid() => _vm.LinhasVisiveis.Select(l => $"{l.Numero} {l.Edt} {l.Nome}").ToList();

    [Fact]
    public void Soltar_NoPrimeiroNivel_ReordenaAGridEAEdt()
    {
        _vm.MoverArrastando([_cobertura], 0).Should().BeTrue();

        Grid().Should().Equal("1 1 Cobertura", "2 2 Fundação", "3 2.1 Escavar", "4 2.2 Armar", "5 3 Estrutura");
        _vm.TarefaSelecionada.Should().Be(_cobertura);
        _vm.Mensagem.Should().Be("\"Cobertura\" agora é a 1.");
    }

    [Fact]
    public void Soltar_DentroDoResumo_SoAsIrmasMudam()
    {
        _vm.MoverArrastando([_armar], 1).Should().BeTrue();

        Grid().Should().Equal("1 1 Fundação", "2 1.1 Armar", "3 1.2 Escavar", "4 2 Estrutura", "5 3 Cobertura");
    }

    [Fact]
    public void Soltar_AtualizaAsPredecessorasQueUsamONumeroDaLinha()
    {
        _cobertura.Predecessoras.Should().Be("4");

        _vm.MoverArrastando([_estrutura], 0);

        (_estrutura.Numero, _cobertura.Predecessoras).Should().Be((1, "1"), "o vínculo é o mesmo; o número da linha mudou");
    }

    [Fact]
    public void SoReordenar_NaoRecalculaOCronograma_MasOGanttSegueANovaOrdem_EOProjetoFicaAlterado()
    {
        var calculos = _ambiente.Calculadora.Chamadas;
        var datas = _vm.Tarefas.ToDictionary(l => l.Nome, l => (l.DataInicio, l.DataTermino, l.Folga, l.EhCritica));
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();

        _vm.MoverArrastando([_cobertura], 0);

        _ambiente.Calculadora.Chamadas.Should().Be(calculos, "a ordem não muda datas nem caminho crítico");
        _vm.Tarefas.ToDictionary(l => l.Nome, l => (l.DataInicio, l.DataTermino, l.Folga, l.EhCritica)).Should().Equal(datas);
        _vm.Gantt.Layout!.Barras.OrderBy(b => b.Linha).Select(b => b.Nome)
            .Should().Equal("Cobertura", "Fundação", "Escavar", "Armar", "Estrutura");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        _vm.Titulo.Should().StartWith("*");
    }

    [Fact]
    public void Soltar_EmOutroGrupo_EntraNele_MantendoONivelQueCabe_ERecalcula()
    {
        var calculos = _ambiente.Calculadora.Chamadas;

        _vm.MoverArrastando([_estrutura], 2).Should().BeTrue("entre Escavar e Armar o primeiro nível não cabe");

        Grid().Should().Equal("1 1 Fundação", "2 1.1 Escavar", "3 1.2 Estrutura", "4 1.3 Armar", "5 2 Cobertura");
        _ambiente.Calculadora.Chamadas.Should().BeGreaterThan(calculos, "o resumo ganhou uma tarefa: as datas dele mudam");
        _vm.TextoDesfazer.Should().Be("Desfazer Mover \"Estrutura\"");
    }

    [Fact]
    public void PuxarParaADireita_ViraSubtarefaDaLinhaDeCima_EOBloqueioAvisaOMotivo()
    {
        _vm.PreverMovimento([_estrutura], 3, puxao: 1).Dica.Should().Be("Tornar subtarefa de \"Armar\"");
        _vm.MoverArrastando([_estrutura], 3, puxao: 1).Should().BeTrue();
        Grid().Should().Equal("1 1 Fundação", "2 1.1 Escavar", "3 1.2 Armar", "4 1.2.1 Estrutura", "5 2 Cobertura");
        _vm.Mensagem.Should().Be("\"Estrutura\" agora é a 1.2.1. \"Armar\" virou resumo.");

        _vm.MoverArrastando([_cobertura], 4, puxao: 1).Should().BeFalse("Estrutura tem vínculo com Cobertura");
        _vm.Mensagem.Should().StartWith("\"Estrutura\" tem vínculos");
    }

    [Fact]
    public void Bloqueado_ResumoDentroDeleMesmo_SemMudarNada()
    {
        var antes = Grid();

        _vm.MoverArrastando([_fundacao], 2).Should().BeFalse();

        Grid().Should().Equal(antes);
        _vm.Mensagem.Should().Be(RegrasDeMovimento.DentroDeSiMesmo);
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void ForaDasLinhas_NaoFazNada(int lacuna)
    {
        var (antes, mensagem) = (Grid(), _vm.Mensagem);

        _vm.MoverArrastando([_cobertura], lacuna).Should().BeFalse();

        Grid().Should().Equal(antes);
        _vm.Mensagem.Should().Be(mensagem, "fora das linhas, soltar cancela sem aviso");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void AoLadoDeSiMesma_NaoMudaNada(int lacuna)
    {
        _vm.MoverArrastando([_estrutura], lacuna).Should().BeFalse();

        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void ResumoRecolhido_LevaAsSubtarefasOcultas()
    {
        _fundacao.Expandido = false;
        _vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("Fundação", "Estrutura", "Cobertura");

        _vm.MoverArrastando([_fundacao], 3).Should().BeTrue();

        _vm.Tarefas.Select(l => $"{l.Edt} {l.Nome}").Should().Equal("1 Estrutura", "2 Cobertura", "3 Fundação", "3.1 Escavar", "3.2 Armar");
        _vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("Estrutura", "Cobertura", "Fundação");
    }

    [Fact]
    public void Varias_VaoJuntas_EOTecladoMoveEntreAsIrmas()
    {
        _vm.MoverArrastando([_cobertura, _estrutura], 0).Should().BeTrue();
        Grid().Should().Equal("1 1 Estrutura", "2 2 Cobertura", "3 3 Fundação", "4 3.1 Escavar", "5 3.2 Armar");
        _vm.Selecionadas.Should().Equal(_estrutura, _cobertura);

        _vm.TarefaSelecionada = _armar;
        _vm.MoverParaCimaCommand.Execute(null);
        _vm.MoverParaCimaCommand.CanExecute(null).Should().BeFalse("é a primeira do grupo: não sai dele");
        _vm.TarefaSelecionada = _fundacao;
        _vm.MoverParaCimaCommand.Execute(null);
        Grid().Should().Equal("1 1 Estrutura", "2 2 Fundação", "3 2.1 Armar", "4 2.2 Escavar", "5 3 Cobertura");
        _vm.TextoDesfazer.Should().Be("Desfazer Mover \"Fundação\"");
    }
}
