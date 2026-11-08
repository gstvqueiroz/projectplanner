using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 79, na janela de verdade: as abas de atalho no rodapé, à esquerda, como as planilhas do Excel. Gantt e Kanban trocam a
// visão (a da visão atual fica marcada); as outras abrem as telas, com os comandos do menu; Exibir > Abas no rodapé esconde.
public partial class MenuPrincipalTests
{
    private static Button Aba(Abertura a, string nome) => a.Janela.Abas.Children.OfType<Button>().Single(b => (string)b.Content! == nome);

    private static bool Ativa(Abertura a, string nome) => Aba(a, nome).Classes.Contains("ativa");

    [Fact]
    public Task Abas_NoRodapeAEsquerda_AcimaDaBarraDeStatus() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);

        a.Janela.Abas.Children.OfType<Button>().Select(b => (string)b.Content!).Should().Equal(
            "Gantt", "Kanban", "Orçamento", "Recursos", "Medições", "Ordens de serviço", "Custos", "Físico-financeiro", "Indicadores", "Fotos");
        var topoDaBarra = a.Janela.BarraDeAbas.TranslatePoint(default, a.Janela)!.Value.Y;
        var fimDosPaineis = a.Janela.Paineis.TranslatePoint(new Point(0, a.Janela.Paineis.Bounds.Height), a.Janela)!.Value.Y;
        var mensagem = a.Janela.StatusMensagem.TranslatePoint(default, a.Janela)!.Value.Y;
        topoDaBarra.Should().BeGreaterThanOrEqualTo(fimDosPaineis, "abaixo da área de trabalho").And.BeLessThan(mensagem, "acima da barra de status");
        Aba(a, "Gantt").TranslatePoint(default, a.Janela)!.Value.X.Should().BeLessThan(20, "no canto esquerdo");
        (Ativa(a, "Gantt"), Ativa(a, "Kanban")).Should().Be((true, false));
        Aba(a, "Orçamento").IsVisible.Should().BeTrue();
    });

    [Fact]
    public Task Abas_KanbanEGantt_TrocamAVisao_EAGanttVoltaParaOModoDeAntes() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Clicar("Exibir", "Somente Gantt");

        a.ClicarComOMouse(Aba(a, "Kanban"));
        (a.Vm.ModoVisualizacao, a.Janela.QuadroKanban.IsVisible).Should().Be((ModoVisualizacao.Kanban, true));
        (Ativa(a, "Gantt"), Ativa(a, "Kanban")).Should().Be((false, true));

        a.ClicarComOMouse(Aba(a, "Gantt"));
        a.Vm.ModoVisualizacao.Should().Be(ModoVisualizacao.SomenteGantt, "volta para a visão do cronograma de antes do Kanban");
        (Ativa(a, "Gantt"), Ativa(a, "Kanban"), a.Janela.QuadroKanban.IsVisible).Should().Be((true, false, false));
    });

    [Fact]
    public Task Abas_DasTelas_UsamOsComandosDoMenu_EAbremPorCima() => Tela.Executar(() =>
    {
        var a = Abrir();
        foreach (var (aba, comando) in new (string, ICommand)[]
                 {
                     ("Orçamento", a.Vm.OrcamentoCommand), ("Recursos", a.Vm.RecursosDoProjetoCommand), ("Medições", a.Vm.MedicoesCommand),
                     ("Ordens de serviço", a.Vm.OrdensDeServicoCommand), ("Custos", a.Vm.CustosRealizadosCommand),
                     ("Físico-financeiro", a.Vm.FisicoFinanceiroCommand), ("Indicadores", a.Vm.IndicadoresCommand),
                     ("Fotos", a.Vm.FotosDoProjetoCommand),
                 })
            Aba(a, aba).Command.Should().BeSameAs(comando, $"a aba {aba} faz o mesmo que o menu");

        a.ClicarComOMouse(Aba(a, "Orçamento"));
        a.ClicarComOMouse(Aba(a, "Medições"));

        (a.Ambiente.Dialogos.Orcamentos.Count, a.Ambiente.Dialogos.Medicoes.Count).Should().Be((1, 1));
        a.Vm.ModoVisualizacao.Should().Be(ModoVisualizacao.Dividido, "as telas abrem por cima; a visão não muda");
    });

    [Fact]
    public Task Abas_ExibirEsconde_EAEscolhaFicaSalva() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Item("Exibir", "Abas no rodapé").IsChecked.Should().BeTrue();

        a.Clicar("Exibir", "Abas no rodapé");
        Pintar(a);

        a.Janela.BarraDeAbas.IsVisible.Should().BeFalse();
        a.Ambiente.PreferenciasInterface.Salvo!.OcultarAbas.Should().BeTrue();

        var b = Abrir(amb => amb.PreferenciasInterface.Salvo = new PreferenciasInterface(false, TemaDaInterface.Claro, OcultarAbas: true));
        (b.Janela.BarraDeAbas.IsVisible, b.Item("Exibir", "Abas no rodapé").IsChecked).Should().Be((false, false));
        b.Clicar("Exibir", "Abas no rodapé");
        b.Ambiente.PreferenciasInterface.Salvo.Should().Be(new PreferenciasInterface(false, TemaDaInterface.Claro, OcultarAbas: false),
            "o tema e o resto ficam como estavam");
    });
}
