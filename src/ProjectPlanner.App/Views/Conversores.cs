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

    /// <summary>A linha sendo arrastada na grid (Bloco 18) fica meio transparente.</summary>
    public static readonly IValueConverter MeiaOpacidadeSeVerdadeiro = new FuncValueConverter<bool, double>(valor => valor ? 0.5 : 1);

    /// <summary>Bytes de uma imagem (JPEG das fotos) para o controle Image; bytes inválidos viram nada.</summary>
    public static readonly IValueConverter Imagem = new FuncValueConverter<byte[]?, Avalonia.Media.Imaging.Bitmap?>(bytes =>
    {
        if (bytes is not { Length: > 0 })
            return null;
        try
        {
            return new Avalonia.Media.Imaging.Bitmap(new MemoryStream(bytes));
        }
        catch (Exception)
        {
            return null;
        }
    });

    /// <summary>Bloco 75: "#RRGGBB" (a cor de uma coluna ou etiqueta do quadro) para o pincel; vazia ou inválida, cinza.</summary>
    public static readonly IValueConverter Cor = new FuncValueConverter<string?, IBrush>(cor =>
        cor is { Length: > 0 } && Color.TryParse(cor, out var c) ? new SolidColorBrush(c) : new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98)));

    /// <summary>
    /// Bloco 77: o texto sobre uma cor de etiqueta: branco, menos nas cores claras (o amarelo), onde o branco não se lê.
    /// </summary>
    public static readonly IValueConverter TextoSobreACor = new FuncValueConverter<string?, IBrush>(cor =>
    {
        if (cor is not { Length: > 0 } || !Color.TryParse(cor, out var c))
            return Brushes.White;
        static double Linear(byte canal)
        {
            var v = canal / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        var luminancia = 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        return luminancia > 0.4 ? new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)) : Brushes.White;
    });

    public static readonly IValueConverter OpacoSeVerdadeiro = new FuncValueConverter<bool, double>(valor => valor ? 1 : 0);

    /// <summary>Linhas de resumo em negrito, como no MS Project.</summary>
    public static readonly IValueConverter NegritoSeVerdadeiro =
        new FuncValueConverter<bool, FontWeight>(valor => valor ? FontWeight.SemiBold : FontWeight.Normal);
}
