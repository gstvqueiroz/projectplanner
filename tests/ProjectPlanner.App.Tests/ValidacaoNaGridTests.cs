using ProjectPlanner.App.Services;
using System.ComponentModel;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Valores inválidos nunca lançam exceção para a tela: o domínio recusa, e o ViewModel expõe a
// mensagem como erro da propriedade (borda vermelha) e na barra de status, mantendo o valor digitado.
public class ValidacaoNaGridTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _tarefa;

    public ValidacaoNaGridTests()
    {
        _vm = _ambiente.CriarViewModel();
        _tarefa = Ambiente.AdicionarTarefa(_vm, "A", 5);
    }

    [Fact]
    public void DuracaoNegativa_ExpoeErroAmigavelSemLancarEMantemATarefa()
    {
        var editar = () => _tarefa.Duracao = -3;

        editar.Should().NotThrow();
        _tarefa.HasErrors.Should().BeTrue();
        _tarefa.GetErrors(nameof(TarefaViewModel.Duracao)).Cast<string>().Should().ContainSingle()
            .Which.Should().Be("A duração não pode ser negativa (informado: -3).");
        _tarefa.Duracao.Should().Be(-3, "o valor digitado continua visível para ser corrigido");
        _tarefa.Tarefa.Duracao.Should().Be(5);
        _vm.Mensagem.Should().Be(
            "Linha 1, Duração: A duração não pode ser negativa (informado: -3). Corrija o valor ou tecle Esc para desfazer.");
    }

    [Fact]
    public void CancelarAEdicao_VoltaAoValorEmVigorELimpaOErro()
    {
        _tarefa.Duracao = -3;
        _tarefa.Nome = "";

        ((IEditableObject)_tarefa).CancelEdit();

        _tarefa.HasErrors.Should().BeFalse();
        _tarefa.Duracao.Should().Be(5);
        _tarefa.Nome.Should().Be("A");
        _vm.Mensagem.Should().BeEmpty();
    }

    [Fact]
    public void VoltarAoValorEmVigorDepoisDeUmaRecusa_LimpaAMensagem()
    {
        // É o que o DataGrid faz no Esc: regrava o valor original antes de avisar o cancelamento.
        _tarefa.Duracao = -3;

        _tarefa.Duracao = 5;

        _tarefa.HasErrors.Should().BeFalse();
        _vm.Mensagem.Should().BeEmpty();
    }

    [Fact]
    public void CorrigirOValor_LimpaOErro()
    {
        _tarefa.Duracao = -1; // 0 deixou de ser erro no Bloco 11: faz da tarefa um marco

        _tarefa.Duracao = 4;

        _tarefa.HasErrors.Should().BeFalse();
        _tarefa.Tarefa.Duracao.Should().Be(4);
    }

    [Fact]
    public void VoltarAoValorAtual_DesfazAErroSemMarcarAlteracao()
    {
        _tarefa.PercentualConcluido = 150;

        _tarefa.PercentualConcluido = 0;

        _tarefa.HasErrors.Should().BeFalse();
        _tarefa.PercentualConcluido.Should().Be(0);
    }

    [Fact]
    public void ErroAvisaATelaPeloErrorsChanged()
    {
        var propriedades = new List<string?>();
        _tarefa.ErrorsChanged += (_, e) => propriedades.Add(e.PropertyName);

        _tarefa.Nome = "  ";

        propriedades.Should().Equal(nameof(TarefaViewModel.Nome));
        _tarefa.GetErrors(nameof(TarefaViewModel.Nome)).Cast<string>().Should().Equal("O nome da tarefa é obrigatório.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void PercentualForaDoIntervalo_ExpoeErro(int percentual)
    {
        _tarefa.PercentualConcluido = percentual;

        _tarefa.GetErrors(nameof(TarefaViewModel.PercentualConcluido)).Cast<string>().Should().ContainSingle()
            .Which.Should().StartWith("O percentual concluído deve estar entre 0 e 100");
    }

    [Fact]
    public void TerminoAntesDoInicio_ExpoeErro()
    {
        _tarefa.DataTermino = Ambiente.Segunda.AddDays(-3);

        _tarefa.GetErrors(nameof(TarefaViewModel.DataTermino)).Cast<string>().Should().ContainSingle()
            .Which.Should().Contain("não pode ser anterior à data de início");
        _tarefa.Tarefa.DataTermino.Should().Be(new DateTime(2026, 10, 2));
    }

    [Fact]
    public void DataApagada_ExpoeErro()
    {
        _tarefa.DataInicio = null;

        _tarefa.GetErrors(nameof(TarefaViewModel.DataInicio)).Cast<string>().Should().Equal("Informe a data de início.");
    }

    [Fact]
    public void NomeDoProjetoVazio_ExpoeErroNoCabecalho()
    {
        _vm.NomeProjeto = "";

        _vm.GetErrors(nameof(MainWindowViewModel.NomeProjeto)).Cast<string>().Should().Equal("O nome do projeto é obrigatório.");
        _vm.Mensagem.Should().Be("O nome do projeto é obrigatório.");
        _vm.Titulo.Should().Be($"*Projeto sem título — ProjectPlanner {VersaoDoProduto.Atual}", "o nome recusado não entra no título");
    }
}
