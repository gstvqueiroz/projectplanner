using System.ComponentModel;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Blocos 17 e 22: os pontos de entrada da galeria e o indicador de fotos na grid e no Gantt, com fotos de verdade.
public class GerenciarFotosTests : TesteDeFotos
{
    private readonly TarefaViewModel _a, _b;

    public GerenciarFotosTests()
    {
        _a = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        _b = Ambiente.AdicionarTarefa(Vm, "Estrutura", 2);
    }

    [Fact]
    public void GerenciarFotos_TarefaSemFotos_AbreAGaleriaVaziaSemErro()
    {
        Vm.TarefaSelecionada = _a;

        Vm.GerenciarFotosCommand.Execute(null);

        var galeria = Ambiente.Dialogos.Galerias.Single();
        (galeria.Titulo, galeria.Vazia, galeria.Contador, galeria.Grupos.Count).Should().Be(("Fotos — 1 Fundação", true, "0 fotos", 0));
    }

    [Fact]
    public async Task GerenciarFotos_PelaLinha_MesmoComOutraSelecionada()
    {
        await ComFotos(_b, "formas.jpg");
        Vm.TarefaSelecionada = _a;

        Vm.GerenciarFotosCommand.Execute(_b);

        var galeria = Ambiente.Dialogos.Galerias.Last();
        (galeria.Titulo, galeria.Contador).Should().Be(("Fotos — 2 Estrutura", "1 foto"));
    }

    [Fact]
    public void GerenciarFotos_SemTarefaSelecionada_Desabilitado_ComADica()
    {
        Vm.TarefaSelecionada = null;

        Vm.GerenciarFotosCommand.CanExecute(null).Should().BeFalse();
        Vm.DicaGerenciarFotos.Should().Be("Selecione uma tarefa para gerenciar suas fotos");
        Vm.GerenciarFotosCommand.CanExecute(_a).Should().BeTrue();
    }

    [Fact]
    public async Task Indicador_ApareceNaGridENoGantt_ComOContador()
    {
        var avisos = new List<string?>();
        ((INotifyPropertyChanged)_a).PropertyChanged += (_, e) => avisos.Add(e.PropertyName);
        (_a.TemFotos, _a.QuantidadeDeFotos).Should().Be((false, 0));

        await ComFotos(_a, "1.jpg", "2.jpg");

        (_a.TemFotos, _a.QuantidadeDeFotos, _a.DicaDasFotos).Should().Be((true, 2, "2 fotos — abrir a galeria"));
        avisos.Should().Contain([nameof(TarefaViewModel.TemFotos), nameof(TarefaViewModel.QuantidadeDeFotos)]);
        Vm.Gantt.TarefasComIconeDeFotos.Should().Equal(_a.Tarefa.Id);
        Vm.Gantt.Layout!.Barras.Single(b => b.Nome == "Fundação").QuantidadeDeFotos.Should().Be(2);
    }

    [Fact]
    public async Task Indicador_SomeAoExcluirTodas()
    {
        var galeria = await ComFotos(_a, "1.jpg");
        galeria.SelecionarTodasCommand.Execute(null);

        await galeria.ExcluirCommand.ExecuteAsync(null);

        (_a.TemFotos, Vm.Gantt.TarefasComIconeDeFotos.Count).Should().Be((false, 0));
    }

    [Fact]
    public async Task Indicador_CliqueAbreAGaleriaDaquelaLinha()
    {
        await ComFotos(_b, "1.jpg");
        Vm.TarefaSelecionada = _a;

        _b.GerenciarFotosCommand.Execute(null);

        Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — 2 Estrutura");
    }
}
