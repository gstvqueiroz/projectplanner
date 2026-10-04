using System.ComponentModel;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (F1): o botão de fotos na barra de ícones, com quantas fotos tem a tarefa selecionada.
public class BotaoDeFotosTests : TesteDeFotos
{
    [Fact]
    public void SemSelecao_DesabilitadoComADica()
    {
        Vm.TarefaSelecionada = null;

        (Vm.FotosDaSelecionada, Vm.GerenciarFotosCommand.CanExecute(null)).Should().Be((0, false));
        Vm.DicaFotosDaSelecionada.Should().Be("Fotos da tarefa: selecione uma tarefa para ver as fotos dela");
    }

    [Fact]
    public async Task ContadorEDica_AcompanhamASelecaoEAsFotos()
    {
        var fundacao = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        var estrutura = Ambiente.AdicionarTarefa(Vm, "Estrutura", 2);
        Vm.TarefaSelecionada = fundacao;
        Vm.DicaFotosDaSelecionada.Should().Be("Fotos de \"Fundação\": nenhuma ainda — abrir a galeria para adicionar");
        var avisos = new List<string?>();
        ((INotifyPropertyChanged)Vm).PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        await ComFotos(fundacao, "a.jpg");
        (Vm.FotosDaSelecionada, Vm.DicaFotosDaSelecionada).Should().Be((1, "Fotos de \"Fundação\": 1 foto — abrir a galeria"));
        avisos.Should().Contain(nameof(MainWindowViewModel.FotosDaSelecionada));

        await Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/b.jpg"]);
        Vm.DicaFotosDaSelecionada.Should().Be("Fotos de \"Fundação\": 2 fotos — abrir a galeria");

        Vm.TarefaSelecionada = estrutura;
        (Vm.FotosDaSelecionada, Vm.GerenciarFotosCommand.CanExecute(null)).Should().Be((0, true));
    }

    [Fact]
    public void Comando_AbreAGaleriaDaSelecionada()
    {
        Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        var estrutura = Ambiente.AdicionarTarefa(Vm, "Estrutura", 2);
        Vm.TarefaSelecionada = estrutura;

        Vm.GerenciarFotosCommand.Execute(null);

        Ambiente.Dialogos.Galerias.Single().Titulo.Should().Be("Fotos — 2 Estrutura");
    }
}
