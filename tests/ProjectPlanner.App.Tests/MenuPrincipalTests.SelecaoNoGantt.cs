using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (D1), na janela de verdade: trocar a seleção não redesenha o Gantt. As formas continuam as mesmas; só o
// contorno da linha muda de lugar e o contorno de duas barras é trocado, com o mesmo resultado de um desenho completo.
public partial class MenuPrincipalTests
{
    private static readonly Color CorDaSelecaoNaBarra = Color.Parse("#263238");
    private static readonly Color CorDaAtrasada = Color.Parse("#EF6C00");

    private static Canvas CorpoDoGantt(Abertura a) => a.Janela.Gantt.FindControl<Canvas>("Corpo")!;

    private static Rectangle ContornoDaLinha(Abertura a) =>
        CorpoDoGantt(a).Children.OfType<Rectangle>().Single(r => r.Name == "ContornoDaSelecao");

    private static Shape BarraDe(Abertura a, TarefaViewModel linha) =>
        CorpoDoGantt(a).Children.OfType<Shape>().Single(f => f.Tag is Guid id && id == linha.Tarefa.Id);

    // O que a seleção muda no desenho: o contorno de cada barra e onde está o da linha.
    private static string Destaques(Abertura a)
    {
        var barras = CorpoDoGantt(a).Children.OfType<Shape>().Where(f => f.Tag is Guid)
            .Select(f => $"{f.Tag}:{(f.Stroke as ISolidColorBrush)?.Color}:{f.StrokeThickness}");
        var linha = ContornoDaLinha(a);
        var contorno = linha.IsVisible ? $"linha em {Canvas.GetTop(linha)}:{Canvas.GetLeft(linha)}:{linha.Width}" : "linha oculta";
        return string.Join("|", barras.Append(contorno));
    }

    private static void RedesenharTudo(Abertura a)
    {
        a.Vm.Gantt.MostrarFolga = !a.Vm.Gantt.MostrarFolga;
        a.Vm.Gantt.MostrarFolga = !a.Vm.Gantt.MostrarFolga;
        Pintar(a);
    }

    [Fact]
    public Task Selecionar_NaoRedesenhaOGantt_SoMoveOContorno() => Tela.Executar(() =>
    {
        var a = Abrir();
        var t1 = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var t2 = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2, "1");
        var t3 = Ambiente.AdicionarTarefa(a.Vm, "Cobertura", 2, "2");
        a.Vm.TarefaSelecionada = null;
        Pintar(a);
        var formas = CorpoDoGantt(a).Children.ToList();
        ContornoDaLinha(a).IsVisible.Should().BeFalse("sem seleção, sem contorno");

        a.Vm.TarefaSelecionada = t2;
        Pintar(a);

        CorpoDoGantt(a).Children.Should().Equal(formas, "as mesmas formas: nada foi redesenhado");
        (ContornoDaLinha(a).IsVisible, Canvas.GetTop(ContornoDaLinha(a))).Should().Be((true, 1 * GanttViewModel.AlturaLinha));
        ((ISolidColorBrush)BarraDe(a, t2).Stroke!).Color.Should().Be(CorDaSelecaoNaBarra);
        (BarraDe(a, t1).Stroke, BarraDe(a, t3).Stroke).Should().Be((null, null));

        a.Vm.TarefaSelecionada = t3;
        Pintar(a);

        CorpoDoGantt(a).Children.Should().Equal(formas);
        Canvas.GetTop(ContornoDaLinha(a)).Should().Be(2 * GanttViewModel.AlturaLinha);
        (BarraDe(a, t2).Stroke, ((ISolidColorBrush)BarraDe(a, t3).Stroke!).Color).Should().Be((null, CorDaSelecaoNaBarra));
    });

    [Fact]
    public Task Selecionar_DaOMesmoDesenhoQueRedesenharTudo() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.InicioProjeto = new DateTime(2026, 9, 21); // uma semana antes de "hoje": a Fundação fica atrasada
        var fundacao = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 10);
        Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 5, "1");
        a.Vm.AdicionarMarcoCommand.Execute(null);
        a.Vm.AdicionarResumoCommand.Execute(null);
        Pintar(a);
        a.Vm.Gantt.TarefasAtrasadas.Should().Contain(fundacao.Tarefa.Id);

        foreach (var linha in a.Vm.LinhasVisiveis.Cast<TarefaViewModel?>().Prepend(null).Append(fundacao).Append(null).ToList())
        {
            a.Vm.TarefaSelecionada = linha;
            Pintar(a);
            var semRedesenhar = Destaques(a);

            RedesenharTudo(a);

            Destaques(a).Should().Be(semRedesenhar, $"selecionando {linha?.Nome ?? "nada"}");
        }
    });

    [Fact]
    public Task Selecionar_AtrasadaEDepoisOutra_VoltaOContornoLaranja() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.InicioProjeto = new DateTime(2026, 9, 21);
        var fundacao = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 10);
        var estrutura = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 5, "1");
        a.Vm.TarefaSelecionada = estrutura;
        Pintar(a);
        BarraDe(a, fundacao).Should().Match<Shape>(f => ((ISolidColorBrush)f.Stroke!).Color == CorDaAtrasada && f.StrokeThickness == 2);

        a.Vm.TarefaSelecionada = fundacao;
        Pintar(a);
        BarraDe(a, fundacao).Should().Match<Shape>(f => ((ISolidColorBrush)f.Stroke!).Color == CorDaSelecaoNaBarra && f.StrokeThickness == 1.5);

        a.Vm.TarefaSelecionada = estrutura;
        Pintar(a);
        BarraDe(a, fundacao).Should().Match<Shape>(f => ((ISolidColorBrush)f.Stroke!).Color == CorDaAtrasada && f.StrokeThickness == 2);
    });

    [Fact]
    public Task SelecaoDesligadaEReligada_SemRedesenhar() => Tela.Executar(() =>
    {
        var a = Abrir();
        var t1 = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Vm.TarefaSelecionada = t1;
        Pintar(a);
        var formas = CorpoDoGantt(a).Children.ToList();

        a.Clicar("Exibir", "Seleção");
        Pintar(a);
        (a.Vm.MostrarSelecao, ContornoDaLinha(a).IsVisible, BarraDe(a, t1).Stroke).Should().Be((false, false, (IBrush?)null));

        a.Clicar("Exibir", "Seleção");
        Pintar(a);
        (ContornoDaLinha(a).IsVisible, ((ISolidColorBrush)BarraDe(a, t1).Stroke!).Color).Should().Be((true, CorDaSelecaoNaBarra));
        CorpoDoGantt(a).Children.Should().Equal(formas);
    });

    [Fact]
    public Task DestaqueDeTarefaSemBarra_NaoQuebra_ENaoContornaNada() => Tela.Executar(() =>
    {
        var a = Abrir();
        var t1 = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Vm.TarefaSelecionada = t1;
        Pintar(a);

        a.Vm.Gantt.TarefaDestacada = Guid.NewGuid(); // uma tarefa que não está no Gantt
        Pintar(a);

        (ContornoDaLinha(a).IsVisible, BarraDe(a, t1).Stroke).Should().Be((false, (IBrush?)null));
    });

    [Fact]
    public Task RedesenhoCompleto_MantemOContornoNaSelecionada() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var t2 = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2, "1");
        a.Vm.TarefaSelecionada = t2;
        Pintar(a);

        t2.Duracao = 4; // recálculo: layout novo, desenho completo
        Pintar(a);

        (ContornoDaLinha(a).IsVisible, Canvas.GetTop(ContornoDaLinha(a))).Should().Be((true, 1 * GanttViewModel.AlturaLinha));
        ((ISolidColorBrush)BarraDe(a, t2).Stroke!).Color.Should().Be(CorDaSelecaoNaBarra);
        CorpoDoGantt(a).Children.OfType<Rectangle>().Count(r => r.Name == "ContornoDaSelecao").Should().Be(1, "um contorno só");
    });
}
