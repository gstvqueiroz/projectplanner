using Avalonia;
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

    /// <summary>Recuo do nome na grid conforme o nível da tarefa na estrutura.</summary>
    public static readonly IValueConverter RecuoDoNivel =
        new FuncValueConverter<int, Thickness>(nivel => new Thickness(nivel * ColunaNomeDaTarefa.RecuoPorNivel, 0, 0, 0));

    /// <summary>Recuo do nome em edição: o do nível mais o espaço do ▾/▸, para o texto não sair do lugar.</summary>
    public static readonly IValueConverter RecuoDaEdicao =
        new FuncValueConverter<int, Thickness>(nivel =>
            new Thickness(nivel * ColunaNomeDaTarefa.RecuoPorNivel + ColunaNomeDaTarefa.LarguraDoSimbolo, 0, 0, 0));

    public static readonly IValueConverter OpacoSeVerdadeiro = new FuncValueConverter<bool, double>(valor => valor ? 1 : 0);

    /// <summary>Linhas de resumo em negrito, como no MS Project.</summary>
    public static readonly IValueConverter NegritoSeVerdadeiro =
        new FuncValueConverter<bool, FontWeight>(valor => valor ? FontWeight.SemiBold : FontWeight.Normal);
}
