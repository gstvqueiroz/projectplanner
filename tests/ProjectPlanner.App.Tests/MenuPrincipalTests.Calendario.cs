using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Views;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// Bloco 71, na janela de verdade: Projeto > Calendário de trabalho…, o botão da barra e a tela, que aplica e fecha.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task Calendario_PeloMenuEPelaBarra_ATelaMostraEAplica() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 10);
        a.Clicar("Projeto", "Calendário de trabalho…");
        a.ClicarComOMouse(BotaoDaBarra(a, "calendario"));

        a.Ambiente.Dialogos.Calendarios.Should().HaveCount(2);
        var tela = a.Ambiente.Dialogos.Calendarios[1];
        var janela = new CalendarioWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<ItemsControl>("Dias")!.ItemCount.Should().Be(7);
        janela.FindControl<CheckBox>("FeriadosNacionais")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<TextBlock>("Descricao")!.Text.Should().Be("Segunda a sexta · feriados nacionais");
        janela.FindControl<ItemsControl>("DiasForaDaRegra")!.ItemCount.Should().BeGreaterThan(0);

        janela.FindControl<Button>("Aplicar")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        janela.IsVisible.Should().BeFalse("Aplicar fecha a janela");
        tela.Resultado!.FeriadosNacionais.Should().BeTrue();
    });
}
