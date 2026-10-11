using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 35, na janela de verdade: Projeto > Cronograma físico-financeiro…, a tabela e a Curva S desenhada.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task FisicoFinanceiro_PeloMenu_TabelaECurvaS() => Tela.Executar(() =>
    {
        var a = Abrir();
        var tarefa = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 5);
        tarefa.Valor = "1.000";
        a.Ambiente.Dialogos.TextoDigitado = "";
        a.Clicar("Projeto", "Salvar linha de base…");
        a.Clicar("Projeto", "Cronograma físico-financeiro…");

        var tela = a.Ambiente.Dialogos.FisicosFinanceiros.Should().ContainSingle().Subject;
        var janela = new FisicoFinanceiroWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<ItemsControl>("Linhas")!.ItemCount.Should().Be(3, "a tarefa, o total do período e o acumulado");
        janela.FindControl<ComboBox>("Base")!.ItemCount.Should().Be(2);
        janela.FindControl<TextBox>("Cortes")!.IsVisible.Should().BeFalse();
        janela.FindControl<TextBlock>("Resumo")!.Text.Should().StartWith("Valor total: R$ 1.000,00");

        janela.FindControl<TabControl>("Abas")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var curva = janela.FindControl<CurvaSView>("CurvaS")!;
        curva.Curva!.Series.Should().HaveCount(2);
        (curva.Bounds.Width > 100, curva.Bounds.Height > 100).Should().Be((true, true));
        janela.Close();
    });
}
