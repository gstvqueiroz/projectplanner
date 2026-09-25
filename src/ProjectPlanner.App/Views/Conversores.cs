using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ProjectPlanner.App.Views;

public static class Conversores
{
    // Vermelho suave translúcido (#2EE53935, ≈ #FADBDA sobre branco): a mesma família do vermelho das
    // barras críticas no Gantt, bem mais claro, e translúcido para funcionar também no tema escuro.
    private static readonly IBrush FundoCritico = new SolidColorBrush(Color.FromArgb(0x2E, 0xE5, 0x39, 0x35));

    /// <summary>Fundo das linhas de tarefas críticas (quando o destaque do caminho crítico está ligado).</summary>
    public static readonly IValueConverter FundoDaTarefaCritica =
        new FuncValueConverter<bool, IBrush?>(critica => critica ? FundoCritico : null);

    public static readonly IValueConverter SimOuVazio =
        new FuncValueConverter<bool, string>(valor => valor ? "Sim" : "");
}
