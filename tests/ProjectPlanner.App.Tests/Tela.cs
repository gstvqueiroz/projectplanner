using Avalonia;
using Avalonia.Headless;

namespace ProjectPlanner.App.Tests;

/// <summary>
/// Para os testes que abrem a janela de verdade, sem tela (Avalonia headless). Todos rodam na mesma
/// thread de interface, criada uma vez por esta sessão. (O Avalonia.Headless.XUnit 12 exige xunit v3,
/// e estes testes usam o xunit 2; por isso a sessão é usada diretamente.)
/// </summary>
public static class Tela
{
    private static readonly HeadlessUnitTestSession Sessao = HeadlessUnitTestSession.StartNew(typeof(Tela));

    /// <summary>A sessão monta o App por aqui: o App de verdade (estilos e temas), na plataforma headless.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public static Task Executar(Action teste) => Sessao.Dispatch(teste, CancellationToken.None);
}
