using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (P3, P8): ir para a tarefa, rolar até ela, ajustar o zoom ao projeto e ir para hoje ("hoje" é seg 28/09/2026).
public class NavegacaoTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly List<TarefaViewModel> _mostradas = [];
    private readonly List<double> _rolagens = [];

    public NavegacaoTests()
    {
        _vm = _ambiente.CriarViewModel();
        _vm.LinhaParaMostrar += _mostradas.Add;
        _vm.Gantt.RolagemPedida += _rolagens.Add;
    }

    // 1 Fase (resumo) > 1.1 A, 1.2 B (pred 2); 2 C (pred 3).
    private void Montar()
    {
        var fase = Ambiente.AdicionarTarefa(_vm, "Fase", 1);
        _vm.TarefaSelecionada = fase;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        _vm.TarefaSelecionada!.Nome = "A";
        _vm.TarefaSelecionada = fase;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        (_vm.TarefaSelecionada!.Nome, _vm.TarefaSelecionada.Duracao, _vm.TarefaSelecionada.Predecessoras) = ("B", 5, "2");
        Ambiente.AdicionarTarefa(_vm, "C", 3, "3");
    }

    [Theory]
    [InlineData("3", "B")]
    [InlineData(" 4 ", "C")]
    [InlineData("1.2", "B")]
    [InlineData("2", "A")]
    public void IrParaTarefa_PeloNumeroOuPelaEdt(string digitado, string nome)
    {
        Montar();
        _ambiente.Dialogos.TextoDigitado = digitado;

        _vm.IrParaTarefaCommand.Execute(null);

        _vm.TarefaSelecionada!.Nome.Should().Be(nome);
        _mostradas.Should().Equal(_vm.TarefaSelecionada);
        _rolagens.Should().Equal(_vm.Gantt.Layout!.Barras.Single(b => b.Nome == nome).X);
    }

    [Fact]
    public void IrParaTarefa_DentroDeResumoRecolhido_AbreOResumo()
    {
        Montar();
        _vm.Tarefas[0].Expandido = false;
        _vm.LinhasVisiveis.Select(l => l.Nome).Should().Equal("Fase", "C");
        _ambiente.Dialogos.TextoDigitado = "3";

        _vm.IrParaTarefaCommand.Execute(null);

        (_vm.Tarefas[0].Expandido, _vm.TarefaSelecionada!.Nome).Should().Be((true, "B"));
        _vm.LinhasVisiveis.Should().Contain(_vm.TarefaSelecionada);
    }

    [Fact]
    public void IrParaTarefa_PerguntaComASelecionadaComoSugestao()
    {
        Montar();
        _vm.TarefaSelecionada = _vm.Tarefas[3];
        _ambiente.Dialogos.TextoDigitado = null;

        _vm.IrParaTarefaCommand.Execute(null);

        _ambiente.Dialogos.TextosPedidos.Single().Should().Be(("Ir para a tarefa", "Nº (de 1 a 4) ou EDT da tarefa:", "4"));
        (_vm.TarefaSelecionada.Nome, _mostradas.Count).Should().Be(("C", 0), "cancelou");
    }

    [Theory]
    [InlineData("9")]
    [InlineData("0")]
    [InlineData("1.9")]
    [InlineData("fundação")]
    public void IrParaTarefa_QueNaoExiste_Avisa(string digitado)
    {
        Montar();
        _vm.TarefaSelecionada = null;
        _ambiente.Dialogos.TextoDigitado = digitado;

        _vm.IrParaTarefaCommand.Execute(null);

        _vm.Mensagem.Should().Be($"Não existe a tarefa \"{digitado}\". Use o Nº (de 1 a 4) ou a EDT, como 1.2.");
        (_vm.TarefaSelecionada, _mostradas.Count).Should().Be(((TarefaViewModel?)null, 0));
    }

    [Fact]
    public void IrParaTarefa_ProjetoVazio_NemPergunta()
    {
        _vm.IrParaTarefaCommand.Execute(null);

        (_vm.Mensagem, _ambiente.Dialogos.TextosPedidos.Count).Should().Be(("O projeto ainda não tem tarefas.", 0));
    }

    [Fact]
    public void RolarAteTarefa_SoComSelecao()
    {
        Montar();
        _vm.TarefaSelecionada = null;
        _vm.RolarAteTarefaCommand.CanExecute(null).Should().BeFalse();

        _vm.TarefaSelecionada = _vm.Tarefas[3];
        _vm.RolarAteTarefaCommand.Execute(null);

        _mostradas.Should().Equal(_vm.Tarefas[3]);
        _rolagens.Should().Equal(_vm.Gantt.Layout!.Barras.Single(b => b.Nome == "C").X);
    }

    [Fact]
    public void AjustarAoProjeto_OProjetoInteiroCabeNaLargura()
    {
        Montar(); // 1 + 5 + 3 dias úteis a partir de seg 28/09: termina na qui 08/10
        _vm.Gantt.AtualizarAreaVisivel(900, 400);
        _vm.Gantt.AjustarAoProjetoCommand.CanExecute(null).Should().BeTrue();

        _vm.Gantt.AjustarAoProjetoCommand.Execute(null);

        // 28/09 a 08/10 são 11 dias corridos, + 3 antes do início + 2 de folga = 16 dias em 900 px.
        _vm.Gantt.PixelsPorDia.Should().Be(56.2);
        _rolagens.Should().Equal(0d);
        var ultima = _vm.Gantt.Layout!.Barras.Max(b => b.Direita);
        ultima.Should().BeLessThanOrEqualTo(900);
    }

    [Fact]
    public void AjustarAoProjeto_ProjetoLongo_ParaNaEscalaMinima_ESemLarguraOuTarefasNaoDa()
    {
        _vm.Gantt.AtualizarAreaVisivel(900, 400);
        _vm.Gantt.AjustarAoProjetoCommand.CanExecute(null).Should().BeFalse("sem tarefas");

        Ambiente.AdicionarTarefa(_vm, "Longa", 600);
        _vm.Gantt.AjustarAoProjetoCommand.Execute(null);
        _vm.Gantt.PixelsPorDia.Should().Be(GanttViewModel.EscalaMinima);

        _vm.Gantt.AtualizarAreaVisivel(0, 400);
        _vm.Gantt.AjustarAoProjetoCommand.CanExecute(null).Should().BeFalse("a área ainda não tem largura");
    }

    [Fact]
    public void IrParaHoje_RolaAteODiaDeHoje_SoDentroDoEixo()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 5);

        _vm.Gantt.IrParaHojeCommand.Execute(null);

        _rolagens.Should().Equal((Ambiente.Segunda - _vm.Gantt.Layout!.InicioEixo).Days * _vm.Gantt.PixelsPorDia);
        _vm.Gantt.MostrarHoje = false;
        _vm.Gantt.IrParaHojeCommand.CanExecute(null).Should().BeTrue("ir para hoje não depende da linha estar à vista");

        _vm.InicioProjeto = new DateTime(2027, 3, 1);
        _vm.Gantt.IrParaHojeCommand.CanExecute(null).Should().BeFalse("hoje ficou fora do eixo");
    }
}
