using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (B3), na janela de verdade: numa janela estreita o Gantt não some e o zoom não cobre o chat.
public partial class MenuPrincipalTests
{
    private static Abertura AbrirCom(double largura, double altura)
    {
        var a = Abrir();
        a.Janela.Width = largura;
        a.Janela.Height = altura;
        Pintar(a);
        return a;
    }

    [Fact]
    public Task JanelaPequena_ComAssistente_GanttFicaComALarguraMinima_EOZoomNaoCobreOChat() => Tela.Executar(() =>
    {
        var a = AbrirCom(1024, 640);
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Vm.Assistente.Visivel = true;
        Pintar(a);

        a.Janela.Paineis.ColumnDefinitions[0].Width.Should().Be(new GridLength(620), "a largura escolhida não muda");
        a.Janela.PainelGantt.Bounds.Width.Should().BeGreaterThanOrEqualTo(MainWindow.LarguraMinimaDoGantt - 1);
        a.Janela.Grade.Bounds.Width.Should().BeLessThan(620);
        var zoom = a.Janela.ControleDeZoom;
        var direitaDoZoom = zoom.TranslatePoint(new Point(zoom.Bounds.Width, 0), a.Janela)!.Value.X;
        var esquerdaDoChat = a.Janela.PainelAssistente.TranslatePoint(default, a.Janela)!.Value.X;
        direitaDoZoom.Should().BeLessThanOrEqualTo(esquerdaDoChat, "o zoom fica dentro do Gantt");
        a.Janela.PainelGantt.ClipToBounds.Should().BeTrue();
    });

    [Fact]
    public Task JanelaQueVoltaACrescer_AGridVoltaAosSeusPixels() => Tela.Executar(() =>
    {
        var a = AbrirCom(900, 640); // a largura mínima da janela: 620 + o Gantt mínimo não cabem
        a.Janela.Grade.Bounds.Width.Should().BeLessThan(620);

        a.Janela.Width = 1470;
        Pintar(a);

        a.Janela.Grade.Bounds.Width.Should().Be(620);
        a.Janela.Paineis.ColumnDefinitions[0].MaxWidth.Should().BeGreaterThan(620);
    });

    [Fact]
    public Task JanelaPequena_NosOutrosModos_SemLimite() => Tela.Executar(() =>
    {
        var a = AbrirCom(1000, 640);

        a.Vm.ModoVisualizacao = ViewModels.ModoVisualizacao.SomenteTabela;
        Pintar(a);

        a.Janela.Paineis.ColumnDefinitions[0].MaxWidth.Should().Be(double.PositiveInfinity);
        a.Janela.Grade.Bounds.Width.Should().BeGreaterThan(620, "só a tabela: ela ocupa a largura toda");
    });
}
