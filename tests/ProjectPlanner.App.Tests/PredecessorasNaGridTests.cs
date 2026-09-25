using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

public class PredecessorasNaGridTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b, _c;

    public PredecessorasNaGridTests()
    {
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "A", 5);
        _b = Ambiente.AdicionarTarefa(_vm, "B", 3);
        _c = Ambiente.AdicionarTarefa(_vm, "C", 2);
    }

    private IEnumerable<Predecessora> ObterVinculos(TarefaViewModel tarefa) =>
        _vm.Cronograma.PredecessorasDe(tarefa.Tarefa.Id);

    private IEnumerable<Predecessora> Vinculos() => _vm.Cronograma.Predecessoras;

    [Fact]
    public void DefinirPredecessoras_CriaOsVinculosERecalcula()
    {
        _c.Predecessoras = "1; 2ii+2";

        ObterVinculos(_c).Should().Equal(
            new Predecessora(_a.Tarefa.Id, _c.Tarefa.Id, TipoDependencia.FS, 0),
            new Predecessora(_b.Tarefa.Id, _c.Tarefa.Id, TipoDependencia.SS, 2));
        _c.Predecessoras.Should().Be("1; 2II+2", "a coluna volta na forma canônica");
        _c.InicioMaisCedo.Should().Be(Ambiente.DiaUtil(5), "A (dias 0 a 4) → TI → dia 5; B II+2 → dia 2");
        _vm.TemAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Fact]
    public void ApagarOTexto_RemoveTodasAsPredecessoras()
    {
        _c.Predecessoras = "1; 2";

        _c.Predecessoras = "";

        ObterVinculos(_c).Should().BeEmpty();
        _c.InicioMaisCedo.Should().Be(Ambiente.Segunda);
    }

    [Fact]
    public void TrocarAsPredecessoras_SubstituiAsAntigas()
    {
        _c.Predecessoras = "1";

        _c.Predecessoras = "2TT-1";

        ObterVinculos(_c).Should().Equal(new Predecessora(_b.Tarefa.Id, _c.Tarefa.Id, TipoDependencia.FF, -1));
    }

    [Theory]
    [InlineData("1XY", "*Não entendi \"1XY\"*")]
    [InlineData("9", "Não existe a linha 9.")]
    [InlineData("0", "Não existe a linha 0.")]
    [InlineData("3", "Uma tarefa não pode ser predecessora dela mesma.")]
    [InlineData("1; 1II", "Já existe um vínculo de \"A\" para \"C\".")]
    public void TextoInvalido_ExpoeErroENaoMudaOsVinculos(string texto, string mensagem)
    {
        _c.Predecessoras = "2";

        _c.Predecessoras = texto;

        _c.GetErrors(nameof(TarefaViewModel.Predecessoras)).Cast<string>().Should().ContainSingle()
            .Which.Should().Match(mensagem);
        _c.Predecessoras.Should().Be(texto, "o texto digitado continua visível para ser corrigido");
        ObterVinculos(_c).Should().Equal(new Predecessora(_b.Tarefa.Id, _c.Tarefa.Id));
    }

    [Fact]
    public void VinculoQueFechariaCiclo_ExpoeOCicloENaoMudaNada()
    {
        _b.Predecessoras = "1";
        _c.Predecessoras = "2";

        _a.Predecessoras = "3";

        _a.GetErrors(nameof(TarefaViewModel.Predecessoras)).Cast<string>().Should().ContainSingle()
            .Which.Should().Contain("dependência circular C → A → B → C");
        ObterVinculos(_a).Should().BeEmpty();
        Vinculos().Should().HaveCount(2);
    }
}
