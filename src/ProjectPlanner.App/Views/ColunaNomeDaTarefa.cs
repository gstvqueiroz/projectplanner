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
        var alerta = AlertaDeFoto();
        DockPanel.SetDock(alerta, Dock.Right);
        var ordens = MarcaDeOrdens();
        DockPanel.SetDock(ordens, Dock.Right);
        celula.Children.Add(expandir);
        celula.Children.Add(losango);
        celula.Children.Add(fotos);
        celula.Children.Add(alerta);
        celula.Children.Add(ordens);
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

    // Bloco 61: "OS" no fim do nome das tarefas que têm ordem de serviço (laranja se alguma venceu). Clicar abre as OS.
    private static readonly IBrush CorDasOrdens = new SolidColorBrush(Color.Parse("#1E88E5"));
    private static readonly IBrush CorDaOrdemVencida = new SolidColorBrush(Color.Parse("#E65100"));
    private static readonly Avalonia.Data.Converters.FuncValueConverter<bool, IBrush> CorDaMarca = new(vencida => vencida ? CorDaOrdemVencida : CorDasOrdens);

    private static Button MarcaDeOrdens()
    {
        var texto = new TextBlock { Text = "OS", FontSize = 10, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        texto.Bind(TextBlock.ForegroundProperty, new Binding(nameof(TarefaViewModel.OrdemVencida)) { Converter = CorDaMarca });
        var moldura = new Border { Child = texto, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0) };
        moldura.Bind(Border.BorderBrushProperty, new Binding(nameof(TarefaViewModel.OrdemVencida)) { Converter = CorDaMarca });
        var botao = new Button
        {
            Content = moldura,
            Padding = new Thickness(2, 0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        botao.Classes.Add("marcaDeOrdens");
        Avalonia.Automation.AutomationProperties.SetName(botao, "Ordens de serviço da tarefa");
        botao.Bind(ToolTip.TipProperty, new Binding(nameof(TarefaViewModel.DicaDasOrdens)));
        botao.Bind(Visual.IsVisibleProperty, new Binding(nameof(TarefaViewModel.TemOrdens)));
        botao.Bind(Button.CommandProperty, new Binding(nameof(TarefaViewModel.AbrirOrdensCommand)));
        return botao;
    }

    // Bloco 28 (FT5): tarefa em andamento sem foto recente. Um aviso antes da câmera; clicar abre a galeria.
    private static Button AlertaDeFoto()
    {
        var simbolo = new TextBlock { Text = "⚠", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        simbolo.Bind(TextBlock.ForegroundProperty, simbolo.GetResourceObservable("TextoDeAviso"));
        var botao = new Button
        {
            Content = simbolo,
            Padding = new Thickness(3, 0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        botao.Classes.Add("alertaDeFoto");
        Avalonia.Automation.AutomationProperties.SetName(botao, "Tarefa sem foto recente");
        botao.Bind(ToolTip.TipProperty, new Binding(nameof(TarefaViewModel.DicaDoAlertaDeFoto)));
        botao.Bind(Visual.IsVisibleProperty, new Binding(nameof(TarefaViewModel.TemAlertaDeFoto)));
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
