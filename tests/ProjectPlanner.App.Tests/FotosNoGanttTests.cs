using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// O ícone de câmera no Gantt (Bloco 14), agora com fotos de verdade (Bloco 22).
public class FotosNoGanttTests : TesteDeFotos
{
    private readonly TarefaViewModel _a, _b;

    public FotosNoGanttTests()
    {
        _a = Ambiente.AdicionarTarefa(Vm, "A", 3);
        _b = Ambiente.AdicionarTarefa(Vm, "B", 2);
    }

    [Fact]
    public void SemFotos_NenhumaTarefaMostraOIcone()
    {
        Vm.Gantt.TarefasComIconeDeFotos.Should().BeEmpty();
    }

    [Fact]
    public async Task SoAsTarefasComFotosMostramOIcone()
    {
        await ComFotos(_b, "1.jpg", "2.jpg", "3.jpg");

        Vm.Gantt.TarefasComIconeDeFotos.Should().Equal(_b.Tarefa.Id);
        Vm.Gantt.Layout!.Barras.Single(b => b.Nome == "B").QuantidadeDeFotos.Should().Be(3);
    }

    [Fact]
    public async Task DesligarAOpcao_EscondeOIcone_SemMexerNasFotos()
    {
        await ComFotos(_a, "1.jpg");
        await ComFotos(_b, "2.jpg");

        Vm.Gantt.MostrarFotos = false;
        Vm.Gantt.TarefasComIconeDeFotos.Should().BeEmpty();
        _a.Tarefa.Fotos.Should().ContainSingle();

        Vm.Gantt.MostrarFotos = true;
        Vm.Gantt.TarefasComIconeDeFotos.Should().Equal(_a.Tarefa.Id, _b.Tarefa.Id);
    }

    [Fact]
    public void OpcaoDeFotos_ComecaLigadaENaoMarcaOProjetoComoAlterado()
    {
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        Vm.SalvarProjetoCommand.Execute(null);

        Vm.Gantt.MostrarFotos.Should().BeTrue();
        Vm.Gantt.MostrarFotos = false;

        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public async Task ClicarNoIcone_AbreAGaleriaDaquelaTarefa_EmOrdemDeData()
    {
        Ambiente.Processador.Datas["depois.jpg"] = new DateTime(2026, 10, 5, 9, 0, 0);
        Ambiente.Processador.Datas["antes.jpg"] = new DateTime(2026, 9, 29, 9, 0, 0);
        await ComFotos(_b, "depois.jpg", "antes.jpg");

        Vm.Gantt.AbrirFotos(_b.Tarefa.Id);

        var galeria = Ambiente.Dialogos.Galerias.Last();
        galeria.Titulo.Should().Be("Fotos — 2 B");
        galeria.FotosFiltradas.Select(f => f.Imagem.NomeArquivoOriginal).Should().Equal("antes.jpg", "depois.jpg");
    }

    [Fact]
    public async Task FotosSobrevivemASalvarEReabrir_ComAsImagens()
    {
        var repositorio = new RepositorioEmMemoria();
        var vm = new MainWindowViewModel(repositorio, Ambiente.Calculadora, Ambiente.Dialogos, Ambiente.Relogio, Ambiente.Preferencias,
            Ambiente.Assistente, Ambiente.PreferenciasInterface, Ambiente.Processador, Ambiente.ConfiguracaoAssistente);
        var linha = Ambiente.AdicionarTarefa(vm, "Fundação", 3);
        vm.GerenciarFotosCommand.Execute(linha);
        await Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/a.jpg", "/fotos/b.jpg"]);
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        await vm.SalvarProjetoCommand.ExecuteAsync(null);

        var reaberta = new MainWindowViewModel(repositorio, Ambiente.Calculadora, Ambiente.Dialogos, Ambiente.Relogio, Ambiente.Preferencias,
            Ambiente.Assistente, Ambiente.PreferenciasInterface, Ambiente.Processador, Ambiente.ConfiguracaoAssistente);
        Ambiente.Dialogos.ArquivoParaAbrir = "/obra.pplan";
        await reaberta.AbrirProjetoCommand.ExecuteAsync(null);

        reaberta.Gantt.TarefasComIconeDeFotos.Should().ContainSingle();
        reaberta.GerenciarFotosCommand.Execute(reaberta.Tarefas[0]);
        var galeria = Ambiente.Dialogos.Galerias.Last();
        galeria.Grupos.SelectMany(g => g.Fotos).Select(f => ProcessadorFalso.Descrever(f.Miniatura))
            .Should().BeEquivalentTo(["mini:a.jpg", "mini:b.jpg"], "as miniaturas vêm do arquivo salvo");
    }
}
