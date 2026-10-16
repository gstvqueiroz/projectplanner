using Avalonia;
using Avalonia.Controls;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 39, na janela de verdade: os botões novos da barra de ícones, Exibir > Barra de ícones (guardado em
// interface.json), os divisores só entre grupos à vista e o "»" com o que não cabe na largura.
public partial class MenuPrincipalTests
{
    private static Button BotaoDaBarra(Abertura a, string id) => a.Janela.BotoesDaBarra.Single(b => (string)b.Tag! == id);

    private static IEnumerable<string> NaBarra(Abertura a) => a.Janela.BotoesDaBarra.Where(b => b.IsVisible).Select(b => (string)b.Tag!);

    [Fact]
    public Task Barra_TodosOsBotoesDoCatalogo_NaOrdem_ComDicaEAtalho() => Tela.Executar(() =>
    {
        var a = Abrir();

        a.Janela.BotoesDaBarra.Select(b => (string)b.Tag!).Should().Equal(a.Vm.Barra.Botoes.Select(b => b.Id), "a barra segue o catálogo");
        a.Janela.BotoesDaBarra.Select(b => Avalonia.Automation.AutomationProperties.GetName(b))
            .Should().Equal(a.Vm.Barra.Botoes.Select(b => b.Nome));
        NaBarra(a).Should().HaveCount(26, "com a janela larga cabem todos");
        (a.Janela.BotaoMais.IsVisible, a.Janela.AcessoRapido.Children.OfType<Border>().Count(d => d.IsVisible)).Should().Be((false, 6)); // 7 grupos
        var mac = AtalhosDeTeclado.EhMac;
        ((string)ToolTip.GetTip(BotaoDaBarra(a, "novo"))!).Should().Be(AtalhosDeTeclado.Dica("Novo projeto", AcaoComAtalho.Novo, mac));
        ((string)ToolTip.GetTip(BotaoDaBarra(a, "abrir"))!).Should().Contain("Abrir projeto (");
    });

    [Fact]
    public Task Barra_BotoesNovos_AbremAsTelas() => Tela.Executar(() =>
    {
        var a = Abrir();
        var d = a.Ambiente.Dialogos;

        foreach (var id in new[] { "relatorio", "fotosDoProjeto", "medicoes", "orcamento", "indicadores" })
            a.ClicarComOMouse(BotaoDaBarra(a, id));

        (d.Relatorios.Count, d.FotosDoProjeto.Count, d.Medicoes.Count, d.Orcamentos.Count, d.Indicadores.Count).Should().Be((1, 1, 1, 1, 1));
    });

    [Fact]
    public Task Barra_OpcoesDoGantt_LigamEDesligam_EOBotaoFicaMarcado() => Tela.Executar(() =>
    {
        var a = Abrir();
        var critico = BotaoDaBarra(a, "caminhoCritico");
        (a.Vm.Gantt.MostrarCaminhoCritico, critico.Classes.Contains("ativo")).Should().Be((true, true));

        a.ClicarComOMouse(critico);
        a.ClicarComOMouse(BotaoDaBarra(a, "linhaHoje"));
        a.ClicarComOMouse(BotaoDaBarra(a, "linhaDeBaseNoGantt"));

        (a.Vm.Gantt.MostrarCaminhoCritico, a.Vm.Gantt.MostrarHoje, a.Vm.Gantt.MostrarLinhaDeBase).Should().Be((false, false, false));
        critico.Classes.Contains("ativo").Should().BeFalse();
        a.Item("Exibir", "Caminho crítico").IsChecked.Should().BeFalse("o menu e o botão mostram o mesmo");
        a.Clicar("Exibir", "Caminho crítico");
        (a.Vm.Gantt.MostrarCaminhoCritico, critico.Classes.Contains("ativo")).Should().Be((true, true));
    });

    [Fact]
    public Task Barra_EsconderPeloMenu_GuardaNasPreferencias_EOsDivisoresAcompanham() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Item("Exibir", "Barra de ícones").Items.OfType<MenuItem>().Select(i => (string)i.Header!).Should()
            .Equal("Arquivo", "Edição", "Tarefa", "Planejamento", "Obra", "Exibição", "Assistente", "Mostrar todos os botões");

        foreach (var nome in new[] { "Novo projeto", "Abrir projeto", "Salvar" })
            a.Clicar("Exibir", "Barra de ícones", "Arquivo", nome);
        Pintar(a);

        NaBarra(a).Should().NotContain(["novo", "abrir", "salvar"]).And.HaveCount(23);
        a.Ambiente.PreferenciasInterface.Salvo!.OcultosNaBarra.Should().Equal("novo", "abrir", "salvar");
        a.Janela.AcessoRapido.Children.OfType<Border>().First().IsVisible.Should().BeFalse("não há botão antes do primeiro divisor");
        a.Janela.AcessoRapido.Children.OfType<Border>().Count(d => d.IsVisible).Should().Be(5);

        a.Clicar("Exibir", "Barra de ícones", "Mostrar todos os botões");
        Pintar(a);
        NaBarra(a).Should().HaveCount(26);
        a.Ambiente.PreferenciasInterface.Salvo!.OcultosNaBarra.Should().BeNull();
    });

    [Fact]
    public Task Barra_OsEscondidos_VoltamEscondidos_EOTemaNaoOsApaga() => Tela.Executar(() =>
    {
        var a = Abrir(amb => amb.PreferenciasInterface.Salvo = new PreferenciasInterface(false, TemaDaInterface.Claro, ["medicoes", "assistente"]));

        NaBarra(a).Should().NotContain(["medicoes", "assistente"]).And.HaveCount(24);
        a.Item("Exibir", "Barra de ícones", "Obra", "Medições").IsChecked.Should().BeFalse();
        a.Janela.AcessoRapido.Children.OfType<Border>().Last().IsVisible.Should().BeFalse("o Assistente, sozinho no último grupo, saiu");

        a.Vm.DefinirTemaCommand.Execute(TemaDaInterface.Escuro);

        a.Ambiente.PreferenciasInterface.Salvo.Should().Be(a.Ambiente.PreferenciasInterface.Salvo! with { Tema = TemaDaInterface.Escuro });
        a.Ambiente.PreferenciasInterface.Salvo!.OcultosNaBarra.Should().Equal("medicoes", "assistente");
    });

    [Fact]
    public Task Barra_JanelaEstreita_OQueNaoCabeVaiParaOMais_NaOrdem() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Janela.MinWidth = 300;
        a.Janela.Width = 520;
        Pintar(a);

        a.Janela.BotaoMais.IsVisible.Should().BeTrue();
        var mostrados = NaBarra(a).ToList();
        var noMais = a.Janela.BotoesNoMais.Select(b => (string)b.Tag!).ToList();
        mostrados.Concat(noMais).Should().Equal(a.Vm.Barra.Botoes.Select(b => b.Id), "os primeiros ficam, os do fim vão para o »");
        mostrados.Should().StartWith(["novo", "abrir", "salvar"]);
        var direita = a.Janela.AcessoRapido.Children.OfType<Control>().Where(c => c.IsVisible)
            .Max(c => c.TranslatePoint(new Avalonia.Point(c.Bounds.Width, 0), a.Janela)!.Value.X);
        direita.Should().BeLessThanOrEqualTo(a.Janela.BotaoMais.TranslatePoint(default, a.Janela)!.Value.X, "nada fica por baixo do »");

        var itens = a.Janela.MenuMais.Items.OfType<MenuItem>().ToList();
        itens.Select(i => (string)i.Header!).Should().Equal(a.Janela.BotoesNoMais.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
        var orcamento = itens.Single(i => (string)i.Header! == "Orçamento");
        orcamento.Command!.Execute(orcamento.CommandParameter);
        a.Ambiente.Dialogos.Orcamentos.Should().ContainSingle("o item do » faz o mesmo que o botão");

        a.Janela.Width = 1470;
        Pintar(a);
        (NaBarra(a).Count(), a.Janela.BotaoMais.IsVisible).Should().Be((26, false));
    });
}
