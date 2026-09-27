using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 18: soltar uma tarefa arrastada na grid, sem mouse (a janela só traduz o mouse em linha e metade).
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

    private int Linha(TarefaViewModel linha) => _vm.LinhasVisiveis.IndexOf(linha);

    private List<string> Grid() => _vm.LinhasVisiveis.Select(l => $"{l.Numero} {l.Edt} {l.Nome}").ToList();

    [Fact]
    public void Destino_MetadeDeCima_Antes_MetadeDeBaixo_DepoisDasSubtarefas()
    {
        _vm.DestinoAoSoltar(_cobertura, Linha(_fundacao), metadeDeBaixo: false)
            .Should().Be(new DestinoDeReordenacao(0, 0));
        _vm.DestinoAoSoltar(_cobertura, Linha(_fundacao), metadeDeBaixo: true)
            .Should().Be(new DestinoDeReordenacao(1, 3), "depois da Fundação e das subtarefas dela (guia em cima da Estrutura)");
    }

    [Fact]
    public void Soltar_NoPrimeiroNivel_ReordenaAGridEAEdt()
    {
        _vm.SoltarTarefa(_cobertura, Linha(_fundacao), metadeDeBaixo: false).Should().BeTrue();

        Grid().Should().Equal("1 1 Cobertura", "2 2 Fundação", "3 2.1 Escavar", "4 2.2 Armar", "5 3 Estrutura");
        _vm.TarefaSelecionada.Should().Be(_cobertura);
        _vm.Mensagem.Should().Be("\"Cobertura\" agora é a 1.");
    }

    [Fact]
    public void Soltar_DentroDoResumo_SoAsIrmasMudam()
    {
        _vm.SoltarTarefa(_armar, Linha(_escavar), metadeDeBaixo: false).Should().BeTrue();

        Grid().Should().Equal("1 1 Fundação", "2 1.1 Armar", "3 1.2 Escavar", "4 2 Estrutura", "5 3 Cobertura");
    }

    [Fact]
    public void Soltar_AtualizaAsPredecessorasQueUsamONumeroDaLinha()
    {
        _cobertura.Predecessoras.Should().Be("4");

        _vm.SoltarTarefa(_estrutura, Linha(_fundacao), metadeDeBaixo: false);

        (_estrutura.Numero, _cobertura.Predecessoras).Should().Be((1, "1"), "o vínculo é o mesmo; o número da linha mudou");
    }

    [Fact]
    public void Soltar_NaoRecalculaOCronograma_MasOGanttSegueANovaOrdem_EOProjetoFicaAlterado()
    {
        var calculos = _ambiente.Calculadora.Chamadas;
        var datas = _vm.Tarefas.ToDictionary(l => l.Nome, l => (l.DataInicio, l.DataTermino, l.Folga, l.EhCritica));
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();

        _vm.SoltarTarefa(_cobertura, Linha(_fundacao), metadeDeBaixo: false);

        _ambiente.Calculadora.Chamadas.Should().Be(calculos, "a ordem não muda datas nem caminho crítico");
        _vm.Tarefas.ToDictionary(l => l.Nome, l => (l.DataInicio, l.DataTermino, l.Folga, l.EhCritica)).Should().Equal(datas);
        _vm.Gantt.Layout!.Barras.OrderBy(b => b.Linha).Select(b => b.Nome)
            .Should().Equal("Cobertura", "Fundação", "Escavar", "Armar", "Estrutura");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        _vm.Titulo.Should().StartWith("*");
    }

    [Theory]
    [InlineData("Escavar", false)] // uma subtarefa sobre outro nível
    [InlineData("Estrutura", true)]
    public void Soltar_ForaDoGrupoDeIrmas_Recusado_SemMudarNada(string sobre, bool metadeDeBaixo)
    {
        var antes = Grid();
        var arrastada = sobre == "Escavar" ? _estrutura : _armar; // Estrutura (1º nível) sobre Escavar; Armar (em Fundação) sobre Estrutura
        var alvo = _vm.LinhasVisiveis.Single(l => l.Nome == sobre);

        _vm.DestinoAoSoltar(arrastada, Linha(alvo), metadeDeBaixo).Should().BeNull();
        _vm.SoltarTarefa(arrastada, Linha(alvo), metadeDeBaixo).Should().BeFalse();

        Grid().Should().Equal(antes);
        _vm.Mensagem.Should().Be(MainWindowViewModel.MensagemReordenacaoInvalida);
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]  // linhas vazias do preenchimento, cabeçalho: fora das linhas
    [InlineData(5)]     // depois da última linha real
    [InlineData(-1)]
    public void Soltar_ForaDasLinhasDeTarefa_Recusado(int? linha)
    {
        var antes = Grid();

        _vm.SoltarTarefa(_cobertura, linha, metadeDeBaixo: false).Should().BeFalse();

        Grid().Should().Equal(antes);
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void Soltar_SobreSiMesma_Recusado_EAoLadoDeSiMesma_NaoMudaNada()
    {
        _vm.DestinoAoSoltar(_estrutura, Linha(_estrutura), metadeDeBaixo: false).Should().BeNull();

        _vm.SoltarTarefa(_estrutura, Linha(_cobertura), metadeDeBaixo: false).Should().BeFalse("já está antes da Cobertura");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        _vm.Mensagem.Should().NotBe(MainWindowViewModel.MensagemReordenacaoInvalida);
    }

    [Fact]
    public void Soltar_ResumoRecolhido_LevaAsSubtarefasOcultas()
    {
        _fundacao.Expandido = false;
        _vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("Fundação", "Estrutura", "Cobertura");

        _vm.SoltarTarefa(_fundacao, Linha(_cobertura), metadeDeBaixo: true).Should().BeTrue();

        _vm.Tarefas.Select(l => $"{l.Edt} {l.Nome}").Should().Equal("1 Estrutura", "2 Cobertura", "3 Fundação", "3.1 Escavar", "3.2 Armar");
        _vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("Estrutura", "Cobertura", "Fundação");
    }
}
