using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.App.Views;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.App.Tests;

// Bloco 27, na janela de verdade: Arquivo > Relatório fotográfico… abre a tela com os campos e a prévia. Bloco 88: Arquivo >
// Relatório personalizado… abre a tela dos modelos, com as seções em cartões, a prévia e o menu das seções.
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

    [Fact]
    public Task RelatorioPersonalizado_PeloMenu_CartoesPreviaEMenuDasSecoes() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);

        a.Clicar("Arquivo", "Relatório personalizado…");

        var relatorio = a.Ambiente.Dialogos.RelatoriosPersonalizados.Should().ContainSingle().Subject;
        relatorio.Espera = TimeSpan.Zero;
        var janela = new RelatorioPersonalizadoWindow(relatorio);
        janela.Show();
        relatorio.Mudou();
        Esperar(relatorio.Atualizacao);

        janela.Title.Should().StartWith("Relatório personalizado — ");
        janela.FindControl<ComboBox>("Modelos")!.SelectedItem.Should().BeSameAs(relatorio.ModeloEscolhido);
        janela.FindControl<TextBlock>("TextoDaPagina")!.Text.Should().Be($"Página 1 de {relatorio.TotalDePaginas}");
        janela.FindControl<Image>("Previa")!.Source.Should().NotBeNull();
        janela.FindControl<Button>("GerarPdf")!.IsEffectivelyEnabled.Should().BeTrue();
        janela.FindControl<Button>("ExcluirModelo")!.IsEffectivelyEnabled.Should().BeFalse("um modelo pronto não se exclui");
        janela.FindControl<ListBox>("Cores")!.SelectedItem.Should().Be(TemaDoRelatorio.Cores[0]);
        Cartoes(janela).Should().HaveCount(ModelosProntos.Mensal.Secoes.Count);

        var gantt = Cartoes(janela).Single(c => c.DataContext is SecaoNaTela { Tipo: TipoDeSecao.Gantt });
        gantt.GetVisualDescendants().OfType<CheckBox>().First().IsChecked = false;
        Esperar(relatorio.Atualizacao);
        relatorio.ModeloAtual().Secoes.Single(s => s.Tipo == TipoDeSecao.Gantt).Ativa.Should().BeFalse();

        janela.FindControl<ListBox>("Cores")!.SelectedIndex = 4;
        relatorio.Cor.Should().Be("#C62828");
        janela.FindControl<TextBox>("CorPropria")!.Text.Should().Be("#C62828");

        var menu = RelatorioPersonalizadoWindow.MenuDeSecoes(relatorio);
        menu.Items.OfType<MenuItem>().Select(m => m.Header).Should().Equal(Enum.GetValues<TipoDeSecao>().Select(SecaoDoRelatorio.TituloPadrao));
        var observacoes = menu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Observações");
        observacoes.Command!.Execute(observacoes.CommandParameter);
        Esperar(relatorio.Atualizacao);
        var texto = Cartoes(janela).Should().HaveCount(ModelosProntos.Mensal.Secoes.Count + 1).And.Subject.Last();
        texto.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible).Should().HaveCount(2, "o título e o texto, abertos");
        janela.Close();
    });

    private static List<Border> Cartoes(Window janela)
    {
        Dispatcher.UIThread.RunJobs();
        return [.. janela.FindControl<ItemsControl>("Secoes")!.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("secaoDoRelatorio"))];
    }
}
