using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 27, na janela de verdade: Arquivo > Relatório fotográfico… abre a tela com os campos e a prévia.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task Relatorio_PeloMenu_TelaComPreviaEBotoes() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);

        a.Clicar("Arquivo", "Relatório fotográfico…");

        var relatorio = a.Ambiente.Dialogos.Relatorios.Should().ContainSingle().Subject;
        var janela = new RelatorioFotograficoWindow(relatorio);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<TextBlock>("Previa")!.Text.Should().Be("Relatório nº 1 · 0 fotos · 2 páginas");
        janela.FindControl<Button>("GerarPdf")!.IsEffectivelyEnabled.Should().BeFalse("não há fotos");
        janela.FindControl<ComboBox>("FotosPorPagina")!.SelectedItem!.ToString().Should().Be("2 por página");
        janela.FindControl<ComboBox>("Ramo")!.IsVisible.Should().BeFalse();
        janela.Close();
    });
}
