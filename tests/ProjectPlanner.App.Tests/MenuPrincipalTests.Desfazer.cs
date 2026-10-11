using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

// Bloco 30, na janela de verdade: menu Editar, botões Desfazer e Refazer, e os atalhos (que num campo de texto continuam
// sendo do texto).
public partial class MenuPrincipalTests
{
    [Fact]
    public Task Desfazer_AtalhosForaDoTexto_EMenuComONome() => Tela.Executar(() =>
    {
        var a = Abrir();
        var linha = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        a.Vm.TarefaSelecionada = linha;
        a.Janela.Grade.Focus();
        Pintar(a);

        a.Janela.FindControl<MenuItem>("ItemDesfazer")!.Header.Should().Be("Desfazer Duração de \"Fundação\"");
        ToolTip.GetTip(a.Janela.FindControl<Button>("BotaoDesfazer")!).Should()
            .Be($"Desfazer Duração de \"Fundação\" ({AtalhosDeTeclado.Texto(AcaoComAtalho.Desfazer, AtalhosDeTeclado.EhMac)})");

        Apertar(a, Key.D, Comando, PhysicalKey.D);
        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("Fundação", "Fundação");
        Apertar(a, Key.Z, Comando, PhysicalKey.Z);
        a.Vm.Tarefas.Should().ContainSingle();
        Apertar(a, Key.Z, Comando | RawInputModifiers.Shift, PhysicalKey.Z);
        a.Vm.Tarefas.Should().HaveCount(2);

        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        a.Janela.Grade.Focus();
        Apertar(a, Key.C, Comando, PhysicalKey.C);
        Apertar(a, Key.V, Comando, PhysicalKey.V);
        a.Vm.Tarefas.Should().HaveCount(3);
        a.Janela.FindControl<MenuItem>("ItemColar")!.InputGesture.Should().Be(AtalhosDeTeclado.Gesto(AcaoComAtalho.Colar, AtalhosDeTeclado.EhMac));
    });

    [Fact]
    public Task Desfazer_NumCampoDeTexto_ATeclaEDoTexto() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var nome = a.Janela.GetLogicalDescendants().OfType<TextBox>().First(t => t.Width == 320);
        nome.Focus();
        Pintar(a);

        Apertar(a, Key.Z, Comando, PhysicalKey.Z);
        Apertar(a, Key.V, Comando, PhysicalKey.V);

        a.Vm.Tarefas.Single().Duracao.Should().Be(3, "o Desfazer do cronograma não foi acionado");
        a.Vm.TextoDesfazer.Should().Be("Desfazer Duração de \"Fundação\"");
    });
}
