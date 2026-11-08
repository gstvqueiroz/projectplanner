using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 81, na janela de verdade: Projeto > Diário de obra…, o botão da barra e a aba do rodapé abrem o diário; na tela, a lista
// dos dias troca o dia (salvando o formulário), o dia fechado não edita e fechar a janela salva o que está nela.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task Diario_PeloMenuPelaBarraEPelaAba_ATelaTrocaDeDia_EFecharSalva() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Escavação", 3);
        a.Clicar("Projeto", "Diário de obra…");
        a.ClicarComOMouse(BotaoDaBarra(a, "diario"));
        a.ClicarComOMouse(Aba(a, "Diário"));

        a.Ambiente.Dialogos.Diarios.Should().HaveCount(3);
        var tela = a.Ambiente.Dialogos.Diarios[2];
        var janela = new DiarioDeObraWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        T Controle<T>(string nome) where T : Control => janela.FindControl<T>(nome)!;
        Controle<ScrollViewer>("FormularioDoDia").IsVisible.Should().BeFalse("sem dias, o formulário não aparece");

        Controle<Button>("NovoDia").Command!.Execute(null);
        Controle<Button>("NovoDia").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Controle<ScrollViewer>("FormularioDoDia").IsVisible.Should().BeTrue();
        var lista = Controle<ListBox>("ListaDeDias");
        (lista.ItemCount, lista.SelectedIndex, Controle<TextBlock>("CabecalhoDoDia").Text).Should().Be((2, 1, "RDO nº 002 · 29/09/2026 · terça-feira"));
        var ocorrencias = Controle<TextBox>("Ocorrencias");
        ocorrencias.Text = "Entrega de aço.";

        lista.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        a.Vm.Cronograma.Diario.Dia(new DateOnly(2026, 9, 29))!.Ocorrencias.Should().Be("Entrega de aço.", "trocar de dia salva");
        (lista.SelectedIndex, Controle<TextBlock>("CabecalhoDoDia").Text, ocorrencias.Text)
            .Should().Be((0, "RDO nº 001 · 28/09/2026 · segunda-feira", ""));

        Controle<Button>("FecharDia").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        (ocorrencias.IsEffectivelyEnabled, Controle<Button>("ReabrirDia").IsVisible, Controle<Button>("SalvarDia").IsVisible).Should().Be((false, true, false));

        lista.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        ocorrencias.Text = "Visita do cliente.";
        janela.Close();
        Dispatcher.UIThread.RunJobs();

        a.Vm.Cronograma.Diario.Dia(new DateOnly(2026, 9, 29))!.Ocorrencias.Should().Be("Visita do cliente.", "fechar a janela salva");
        a.Vm.TextoDesfazer.Should().Be("Desfazer Diário de 29/09/2026");
    });

    [Fact]
    public Task Diario_FecharComUmNumeroQueNaoVale_PerguntaEFechaSemSalvar() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Escavação", 3);
        a.Clicar("Projeto", "Diário de obra…");
        var tela = a.Ambiente.Dialogos.Diarios[0];
        var janela = new DiarioDeObraWindow(tela);
        janela.Show();
        tela.NovoDiaCommand.Execute(null);
        tela.AdicionarEfetivoCommand.Execute(null);
        tela.Efetivo[0].Funcao = "Pedreiro";
        tela.Efetivo[0].Quantidade = "x";
        Dispatcher.UIThread.RunJobs();

        a.Ambiente.Dialogos.Confirmar = false;
        janela.Close();
        Dispatcher.UIThread.RunJobs();
        janela.IsVisible.Should().BeTrue("ficou para corrigir");

        a.Ambiente.Dialogos.Confirmar = true;
        janela.Close();
        Dispatcher.UIThread.RunJobs();
        janela.IsVisible.Should().BeFalse();
        a.Vm.Cronograma.Diario.Dia(new DateOnly(2026, 9, 28))!.Efetivo.Should().BeEmpty("fechou sem salvar");
    });
}
