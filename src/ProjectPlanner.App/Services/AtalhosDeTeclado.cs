using Avalonia.Input;

namespace ProjectPlanner.App.Services;

/// <summary>Os comandos que têm atalho de teclado (Bloco 25, P1).</summary>
public enum AcaoComAtalho
{
    Novo,
    Abrir,
    Salvar,
    SalvarComo,
    Recalcular,
    FotosDaTarefa,
    Dividido,
    SomenteTabela,
    SomenteGantt,
    Assistente,
}

/// <param name="Comando">Com a tecla de comando da plataforma: ⌘ no Mac, Ctrl no Windows.</param>
public sealed record Atalho(AcaoComAtalho Acao, Key Tecla, bool Comando = true, bool Shift = false);

/// <summary>
/// Os atalhos de teclado do programa (Bloco 25, P1), num lugar só: quem trata a tecla é a janela principal, e os menus e
/// as dicas da barra mostram o mesmo texto. No Mac a tecla de comando é ⌘; no Windows, Ctrl.
/// </summary>
public static class AtalhosDeTeclado
{
    public static bool EhMac { get; } = OperatingSystem.IsMacOS();

    public static IReadOnlyList<Atalho> Todos { get; } =
    [
        new(AcaoComAtalho.Novo, Key.N),
        new(AcaoComAtalho.Abrir, Key.O),
        new(AcaoComAtalho.Salvar, Key.S),
        new(AcaoComAtalho.SalvarComo, Key.S, Shift: true),
        new(AcaoComAtalho.Recalcular, Key.F9, Comando: false),
        new(AcaoComAtalho.FotosDaTarefa, Key.F, Shift: true),
        new(AcaoComAtalho.Dividido, Key.D1),
        new(AcaoComAtalho.SomenteTabela, Key.D2),
        new(AcaoComAtalho.SomenteGantt, Key.D3),
        new(AcaoComAtalho.Assistente, Key.J),
    ];

    public static Atalho De(AcaoComAtalho acao) => Todos.Single(a => a.Acao == acao);

    public static KeyModifiers Modificadores(Atalho atalho, bool mac) =>
        (atalho.Comando ? (mac ? KeyModifiers.Meta : KeyModifiers.Control) : KeyModifiers.None)
        | (atalho.Shift ? KeyModifiers.Shift : KeyModifiers.None);

    public static KeyGesture Gesto(AcaoComAtalho acao, bool mac)
    {
        var atalho = De(acao);
        return new KeyGesture(atalho.Tecla, Modificadores(atalho, mac));
    }

    /// <summary>"⇧⌘S" no Mac, "Ctrl+Shift+S" no Windows, "F9" sem modificador.</summary>
    public static string Texto(AcaoComAtalho acao, bool mac)
    {
        var atalho = De(acao);
        var tecla = atalho.Tecla switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (atalho.Tecla - Key.D0))).ToString(),
            _ => atalho.Tecla.ToString(),
        };
        if (mac)
            return (atalho.Shift ? "⇧" : "") + (atalho.Comando ? "⌘" : "") + tecla;
        return string.Join("+", new[] { atalho.Comando ? "Ctrl" : null, atalho.Shift ? "Shift" : null, tecla }.OfType<string>());
    }

    /// <summary>"Salvar (⌘S)": a dica de um botão com o atalho dele.</summary>
    public static string Dica(string texto, AcaoComAtalho acao, bool mac) => $"{texto} ({Texto(acao, mac)})";

    /// <summary>A ação da tecla apertada; nula se ela não é um atalho. Os números valem também no teclado numérico.</summary>
    public static AcaoComAtalho? Achar(Key tecla, KeyModifiers modificadores, bool mac)
    {
        var normalizada = tecla is >= Key.NumPad0 and <= Key.NumPad9 ? Key.D0 + (tecla - Key.NumPad0) : tecla;
        return Todos.FirstOrDefault(a => a.Tecla == normalizada && Modificadores(a, mac) == modificadores)?.Acao;
    }
}
