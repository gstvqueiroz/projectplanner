using System.ComponentModel;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// Base do relatório fotográfico (Bloco 14): o ícone de câmera no Gantt e a galeria de uma tarefa.
public class FotosNoGanttTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b;

    public FotosNoGanttTests()
    {
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "A", 3);
        _b = Ambiente.AdicionarTarefa(_vm, "B", 2);
    }

    // Pelo caminho do usuário (Bloco 17): abre a galeria da tarefa e clica em Adicionar foto três vezes.
    private void FotosDeExemploEm(TarefaViewModel linha)
    {
        _vm.GerenciarFotosCommand.Execute(linha);
        var galeria = _ambiente.Dialogos.Galerias.Last();
        for (var i = 0; i < 3; i++)
            galeria.AdicionarFotoCommand.Execute(null);
    }

    [Fact]
    public void SemFotos_NenhumaTarefaMostraOIcone()
    {
        _vm.Gantt.TarefasComIconeDeFotos.Should().BeEmpty();
    }

    [Fact]
    public void SoAsTarefasComFotosMostramOIcone()
    {
        FotosDeExemploEm(_b);

        _vm.Gantt.TarefasComIconeDeFotos.Should().Equal(_b.Tarefa.Id);
        _vm.Gantt.Layout!.Barras.Single(b => b.Nome == "B").QuantidadeDeFotos.Should().Be(3);
    }

    [Fact]
    public void DesligarAOpcao_EscondeOIconeDeTodas_ReligarMostraDeNovo()
    {
        FotosDeExemploEm(_a);
        FotosDeExemploEm(_b);
        var avisos = new List<string?>();
        ((INotifyPropertyChanged)_vm.Gantt).PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        _vm.Gantt.MostrarFotos = false;

        _vm.Gantt.TarefasComIconeDeFotos.Should().BeEmpty();
        _vm.Gantt.Layout!.Barras.Should().NotContain(b => _vm.Gantt.MostraIconeDeFotos(b));
        avisos.Should().Contain(nameof(GanttViewModel.TarefasComIconeDeFotos));
        _a.Tarefa.Fotos.Should().HaveCount(3); // é só exibição: as fotos continuam lá

        _vm.Gantt.MostrarFotos = true;

        _vm.Gantt.TarefasComIconeDeFotos.Should().Equal(_a.Tarefa.Id, _b.Tarefa.Id);
    }

    [Fact]
    public void OpcaoDeFotos_ComecaLigadaENaoMarcaOProjetoComoAlterado()
    {
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);

        _vm.Gantt.MostrarFotos.Should().BeTrue();
        _vm.Gantt.MostrarFotos = false;

        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void ClicarNoIcone_AbreAGaleriaDaquelaTarefaEmOrdemDeData()
    {
        _b.Tarefa.AdicionarFoto(new FotoTarefa(_b.Tarefa.Id, new DateTime(2026, 10, 5), "exemplo://depois", "Concretagem"));
        _b.Tarefa.AdicionarFoto(new FotoTarefa(_b.Tarefa.Id, new DateTime(2026, 9, 29), "exemplo://antes"));

        _vm.Gantt.AbrirFotos(_b.Tarefa.Id);

        var galeria = _ambiente.Dialogos.Galerias.Should().ContainSingle().Subject;
        galeria.Titulo.Should().Be("Fotos — B");
        galeria.Resumo.Should().Be("2 fotos, da mais antiga para a mais recente");
        galeria.Fotos.Select(f => (f.Data, f.Descricao)).Should().Equal(("29/09/2026", "(sem descrição)"), ("05/10/2026", "Concretagem"));
    }

    [Fact]
    public void FotosSobrevivemASalvarEReabrir()
    {
        var repositorio = new RepositorioEmMemoria();
        var vm = new MainWindowViewModel(repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias);
        var linha = Ambiente.AdicionarTarefa(vm, "Fundação", 3);
        vm.GerenciarFotosCommand.Execute(linha);
        for (var i = 0; i < 3; i++)
            _ambiente.Dialogos.Galerias.Last().AdicionarFotoCommand.Execute(null);
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);

        var reaberta = new MainWindowViewModel(repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias);
        _ambiente.Dialogos.ArquivoParaAbrir = "/obra.pplan";
        reaberta.AbrirProjetoCommand.Execute(null);

        reaberta.Gantt.TarefasComIconeDeFotos.Should().ContainSingle();
        reaberta.Tarefas[0].Tarefa.Fotos.Should().HaveCount(3);
    }
}
