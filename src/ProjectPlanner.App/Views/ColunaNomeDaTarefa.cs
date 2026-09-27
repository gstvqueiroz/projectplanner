using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

/// <summary>
/// A coluna Nome da grid: o nome recuado conforme o nível na estrutura, com ▾/▸ para recolher e
/// expandir os resumos e ◆ antes dos marcos. A edição é a da coluna de texto comum (o mesmo TextBox,
/// só que recuado), para manter a confirmação e o Esc da grid como sempre foram.
/// </summary>
public sealed class ColunaNomeDaTarefa : DataGridTextColumn
{
    /// <summary>Recuo de cada nível da estrutura.</summary>
    public const double RecuoPorNivel = 16;

    /// <summary>Espaço do ▾/▸, reservado em todas as linhas para alinhar os nomes do mesmo nível.</summary>
    public const double LarguraDoSimbolo = 18;

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        var nome = base.GenerateElement(cell, dataItem);
        if (nome is TextBlock texto)
            texto.TextTrimming = TextTrimming.CharacterEllipsis;

        var expandir = new Button
        {
            Width = LarguraDoSimbolo,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(expandir, "Recolher ou expandir as subtarefas");
        expandir.Bind(ContentControl.ContentProperty, new Binding(nameof(TarefaViewModel.SimboloExpansao)));
        expandir.Bind(Button.CommandProperty, new Binding(nameof(TarefaViewModel.AlternarExpansaoCommand)));
        expandir.Bind(Visual.OpacityProperty,
            new Binding(nameof(TarefaViewModel.EhResumo)) { Converter = Conversores.OpacoSeVerdadeiro });
        expandir.Bind(InputElement.IsHitTestVisibleProperty, new Binding(nameof(TarefaViewModel.EhResumo)));

        var losango = new TextBlock
        {
            Text = "◆",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        losango.Bind(Visual.IsVisibleProperty, new Binding(nameof(TarefaViewModel.EhMarco)));

        var celula = new DockPanel();
        DockPanel.SetDock(expandir, Dock.Left);
        DockPanel.SetDock(losango, Dock.Left);
        var fotos = IndicadorDeFotos();
        DockPanel.SetDock(fotos, Dock.Right);
        celula.Children.Add(expandir);
        celula.Children.Add(losango);
        celula.Children.Add(fotos);
        celula.Children.Add(nome);
        celula.Bind(Layoutable.MarginProperty,
            new Binding(nameof(TarefaViewModel.Nivel)) { Converter = Conversores.RecuoDoNivel });
        return celula;
    }

    /// <summary>A cor da câmera, a mesma do ícone de fotos do Gantt.</summary>
    public static readonly IBrush CorDaCamera = new SolidColorBrush(Color.Parse("#90A4AE"));

    // Bloco 17: câmera com o número de fotos no fim do nome, só nas tarefas com fotos. Clicar abre a galeria.
    private static Button IndicadorDeFotos()
    {
        var camera = new Avalonia.Controls.Shapes.Path
        {
            // Corpo com o visor em cima e a lente vazada.
            Data = Geometry.Parse("M1,4 H4 L5,2.5 H9 L10,4 H13 V11.5 H1 Z M7,5.5 A2.2,2.2 0 1 0 7,9.9 A2.2,2.2 0 1 0 7,5.5 Z"),
            Fill = CorDaCamera,
            Width = 14,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var quantidade = new TextBlock { FontSize = 11, Foreground = CorDaCamera, VerticalAlignment = VerticalAlignment.Center };
        quantidade.Bind(TextBlock.TextProperty, new Binding(nameof(TarefaViewModel.QuantidadeDeFotos)));

        var botao = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { camera, quantidade } },
            Padding = new Thickness(4, 0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        botao.Classes.Add("indicadorDeFotos");
        Avalonia.Automation.AutomationProperties.SetName(botao, "Fotos da tarefa");
        botao.Bind(ToolTip.TipProperty, new Binding(nameof(TarefaViewModel.DicaDasFotos)));
        botao.Bind(Visual.IsVisibleProperty, new Binding(nameof(TarefaViewModel.TemFotos)));
        botao.Bind(Button.CommandProperty, new Binding(nameof(TarefaViewModel.GerenciarFotosCommand)));
        return botao;
    }

    protected override Control GenerateEditingElementDirect(DataGridCell cell, object dataItem)
    {
        var caixa = base.GenerateEditingElementDirect(cell, dataItem);
        caixa.Bind(Layoutable.MarginProperty,
            new Binding(nameof(TarefaViewModel.Nivel)) { Converter = Conversores.RecuoDaEdicao });
        return caixa;
    }
}
