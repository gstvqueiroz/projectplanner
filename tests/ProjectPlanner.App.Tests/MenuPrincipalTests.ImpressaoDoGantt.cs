using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Views;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.Tests;

// Bloco 57, na janela de verdade: Arquivo > Imprimir o Gantt…, o botão da barra e a prévia desenhada.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task ImprimirGantt_PeloMenuEPelaBarra_MostraAPrevia() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 5);
        a.Clicar("Arquivo", "Imprimir o Gantt…");
        a.ClicarComOMouse(BotaoDaBarra(a, "imprimirGantt"));

        a.Ambiente.Dialogos.ImpressoesDoGantt.Should().HaveCount(2);
        var tela = a.Ambiente.Dialogos.ImpressoesDoGantt[0];
        var janela = new GanttImpressoWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<ComboBox>("Folha")!.ItemCount.Should().Be(7);
        var previa = janela.FindControl<Image>("Previa")!;
        previa.Source.Should().NotBeNull();
        var antes = previa.Source;

        tela.FolhaEscolhida = tela.Folhas.Single(f => f.Valor == TamanhoDaFolha.A4);
        Dispatcher.UIThread.RunJobs();

        previa.Source.Should().NotBeSameAs(antes, "a prévia acompanha a folha");
        janela.FindControl<TextBlock>("Resumo")!.Text.Should().Be("1 página · A4 paisagem");
        janela.Close();
    });
}
