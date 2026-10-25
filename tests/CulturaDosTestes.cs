using System.Globalization;
using System.Runtime.CompilerServices;

namespace ProjectPlanner.Testes;

/// <summary>
/// Bloco 68: os testes rodam em português do Brasil, como o programa e o servidor (que fixam o idioma ao abrir), qualquer que
/// seja o idioma do computador (o CI roda em inglês).
/// </summary>
internal static class CulturaDosTestes
{
    [ModuleInitializer]
    internal static void Fixar()
    {
        var brasil = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = brasil;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = brasil;
    }
}
