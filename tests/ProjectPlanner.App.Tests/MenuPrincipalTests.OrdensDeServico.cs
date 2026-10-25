using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 58, na janela de verdade: Projeto > Ordens de serviço…, o botão da barra e a ficha com os botões do fluxo.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task OrdensDeServico_PeloMenuEPelaBarra_FichaEFluxo() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.TarefaSelecionada = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 5);
        a.Clicar("Projeto", "Ordens de serviço…");
        a.ClicarComOMouse(BotaoDaBarra(a, "ordensDeServico"));

        a.Ambiente.Dialogos.OrdensDeServico.Should().HaveCount(2);
        var tela = a.Ambiente.Dialogos.OrdensDeServico[0];
        var janela = new OrdensDeServicoWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<Button>("NovaOrdem")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        janela.FindControl<ListBox>("Lista")!.ItemCount.Should().Be(1);
        janela.FindControl<ItemsControl>("Itens")!.ItemCount.Should().Be(1);
        janela.FindControl<TextBlock>("Cabecalho")!.Text.Should().Be("OS-001 · Rascunho");
        (janela.FindControl<Button>("Emitir")!.IsVisible, janela.FindControl<Button>("Conferir")!.IsVisible).Should().Be((true, false));

        tela.Executante = "Equipe do João";
        janela.FindControl<Button>("Emitir")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        (janela.FindControl<Button>("Emitir")!.IsVisible, janela.FindControl<Button>("Aceitar")!.IsVisible).Should().Be((false, true));
        janela.FindControl<TextBlock>("Cabecalho")!.Text.Should().Be("OS-001 · Emitida");
        janela.Close();
        a.Vm.TextoDesfazer.Should().Be("Desfazer Emitir a OS-001");
    });
}
