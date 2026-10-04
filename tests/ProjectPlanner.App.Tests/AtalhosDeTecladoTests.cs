using Avalonia.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (P1): a tabela de atalhos, no Mac (⌘) e no Windows (Ctrl).
public class AtalhosDeTecladoTests
{
    [Theory]
    [InlineData(AcaoComAtalho.Salvar, true, "⌘S")]
    [InlineData(AcaoComAtalho.SalvarComo, true, "⇧⌘S")]
    [InlineData(AcaoComAtalho.FotosDaTarefa, true, "⇧⌘F")]
    [InlineData(AcaoComAtalho.SomenteTabela, true, "⌘2")]
    [InlineData(AcaoComAtalho.Recalcular, true, "F9")]
    [InlineData(AcaoComAtalho.Salvar, false, "Ctrl+S")]
    [InlineData(AcaoComAtalho.SalvarComo, false, "Ctrl+Shift+S")]
    [InlineData(AcaoComAtalho.Dividido, false, "Ctrl+1")]
    [InlineData(AcaoComAtalho.Recalcular, false, "F9")]
    public void Texto_DeCadaPlataforma(AcaoComAtalho acao, bool mac, string texto)
    {
        AtalhosDeTeclado.Texto(acao, mac).Should().Be(texto);
    }

    [Theory]
    [InlineData(Key.S, KeyModifiers.Meta, true, AcaoComAtalho.Salvar)]
    [InlineData(Key.S, KeyModifiers.Control, false, AcaoComAtalho.Salvar)]
    [InlineData(Key.S, KeyModifiers.Meta | KeyModifiers.Shift, true, AcaoComAtalho.SalvarComo)]
    [InlineData(Key.D3, KeyModifiers.Control, false, AcaoComAtalho.SomenteGantt)]
    [InlineData(Key.NumPad3, KeyModifiers.Control, false, AcaoComAtalho.SomenteGantt)]
    [InlineData(Key.F9, KeyModifiers.None, true, AcaoComAtalho.Recalcular)]
    [InlineData(Key.J, KeyModifiers.Meta, true, AcaoComAtalho.Assistente)]
    public void Achar_ATeclaCerta(Key tecla, KeyModifiers modificadores, bool mac, AcaoComAtalho acao)
    {
        AtalhosDeTeclado.Achar(tecla, modificadores, mac).Should().Be(acao);
    }

    [Theory]
    [InlineData(Key.S, KeyModifiers.Control, true)] // Ctrl no Mac não é o comando
    [InlineData(Key.S, KeyModifiers.Meta, false)] // ⌘ (Windows) no Windows não é o comando
    [InlineData(Key.S, KeyModifiers.None, true)] // letra solta: é texto
    [InlineData(Key.F9, KeyModifiers.Shift, true)]
    [InlineData(Key.Q, KeyModifiers.Meta, true)] // ⌘Q é do sistema
    public void Achar_QueNaoEAtalho_Nulo(Key tecla, KeyModifiers modificadores, bool mac)
    {
        AtalhosDeTeclado.Achar(tecla, modificadores, mac).Should().BeNull();
    }

    [Fact]
    public void Tabela_SemRepetidos_ECadaAcaoUmaVez()
    {
        foreach (var mac in new[] { true, false })
            AtalhosDeTeclado.Todos.Select(a => (a.Tecla, AtalhosDeTeclado.Modificadores(a, mac))).Should().OnlyHaveUniqueItems();
        AtalhosDeTeclado.Todos.Select(a => a.Acao).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Enum.GetValues<AcaoComAtalho>());
        AtalhosDeTeclado.Dica("Salvar", AcaoComAtalho.Salvar, true).Should().Be("Salvar (⌘S)");
        AtalhosDeTeclado.Gesto(AcaoComAtalho.SalvarComo, false).Should().Be(new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift));
    }
}
