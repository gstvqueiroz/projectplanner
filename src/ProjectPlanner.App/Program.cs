using Avalonia;
using System;

namespace ProjectPlanner.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Bloco 68: o programa é em português do Brasil, qualquer que seja o idioma do computador (datas dd/MM/aaaa, vírgula decimal).
        var brasil = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = brasil;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = brasil;
        // Bloco 25 (R1): erros que ninguém tratou vão para o registro local, e os da interface viram um aviso.
        new Services.TratamentoDeErros(new Services.RegistroDeErros(Services.RegistroDeErros.PastaPadrao(), TimeProvider.System),
            Views.AvisoDeErroWindow.Mostrar).Instalar();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
