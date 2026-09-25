using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ProjectPlanner.App.Views;

public static class Conversores
{
    // Laranja translúcido, para funcionar tanto no tema claro quanto no escuro.
    private static readonly IBrush FundoCritico = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x8C, 0x00));

    /// <summary>Fundo das linhas de tarefas críticas.</summary>
    public static readonly IValueConverter FundoDaTarefaCritica =
        new FuncValueConverter<bool, IBrush?>(critica => critica ? FundoCritico : null);

    public static readonly IValueConverter SimOuVazio =
        new FuncValueConverter<bool, string>(valor => valor ? "Sim" : "");
}
