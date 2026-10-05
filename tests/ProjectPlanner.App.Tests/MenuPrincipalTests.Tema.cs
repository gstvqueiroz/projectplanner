using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (B2, V3), na janela de verdade: Exibir > Tema e as cores do tema escuro. O tema é do Application (vale
// para todas as janelas): cada teste que o troca volta ao padrão no fim, para não vazar para os outros.
public partial class MenuPrincipalTests
{
    // As cores fixas de antes do Bloco 25: no tema claro, cada chave continua com o mesmo valor (o claro fica idêntico).
    private static readonly Dictionary<string, string> CoresDoClaroDeAntes = new()
    {
        ["FundoDoBotaoAtivo"] = "#D6E4F0",
        ["FundoDoControleDeZoom"] = "#E6FFFFFF",
        ["BordaDoControleDeZoom"] = "#33000000",
        ["TextoDeErro"] = "#C62828",
        ["TextoDeErroForte"] = "#B3261E",
        ["TextoDeAviso"] = "#8A5300",
        ["FundoDeAviso"] = "#FFF4E5",
        ["TextoDeLink"] = "#0B57D0",
        ["FundoSutil"] = "#14000000",
        ["FundoDeSucesso"] = "#E6F4EA",
        ["FundoDoBalaoDoUsuario"] = "#DCEBFA",
        ["FundoDoBalaoDoAssistente"] = "#F0F0F0",
        ["FundoDoBalaoDeErro"] = "#FDECEA",
        ["FundoDosFiltros"] = "#F4F6F8",
        ["FundoDaMiniatura"] = "#F4F6F8",
        ["FundoDaMiniaturaSelecionada"] = "#E3ECF5",
        ["FundoDaImagemDaMiniatura"] = "#CFD8DC",
        ["FundoDaFotoNosDetalhes"] = "#ECEFF1",
        ["FundoDeOcupado"] = "#CCFFFFFF",
        ["GanttMarco"] = "#212121",
        ["GanttResumo"] = "#37474F",
        ["GanttContornoDaSelecao"] = "#263238",
    };

    private static void VoltarAoTemaPadrao() => Application.Current!.RequestedThemeVariant = ThemeVariant.Default;

    private static Color Cor(string chave, ThemeVariant tema) =>
        Application.Current!.TryGetResource(chave, tema, out var valor) && valor is ISolidColorBrush pincel
            ? pincel.Color
            : throw new InvalidOperationException($"Sem a cor \"{chave}\" no tema {tema}.");

    // Contraste do WCAG 2 entre o texto e o fundo (o fundo translúcido sobre a base, o texto sobre o resultado).
    private static double Contraste(Color texto, Color fundo, Color baseDoFundo)
    {
        static Color Sobre(Color cima, Color baixo)
        {
            var a = cima.A / 255.0;
            byte Canal(byte c, byte b) => (byte)Math.Round(a * c + (1 - a) * b);
            return Color.FromRgb(Canal(cima.R, baixo.R), Canal(cima.G, baixo.G), Canal(cima.B, baixo.B));
        }
        static double Luminancia(Color c)
        {
            static double Linear(byte canal)
            {
                var v = canal / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        var fundoFinal = Sobre(fundo, baseDoFundo);
        var (l1, l2) = (Luminancia(Sobre(texto, fundoFinal)), Luminancia(fundoFinal));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    [Fact]
    public Task Tema_PeloMenu_TrocaOProgramaInteiro_EMarcaOItem() => Tela.Executar(() =>
    {
        try
        {
            var a = Abrir();
            Titulos(a.Item("Exibir", "Tema")).Should().Equal("Igual ao sistema", "Claro", "Escuro");
            a.Item("Exibir", "Tema", "Igual ao sistema").IsChecked.Should().BeTrue();
            (Application.Current!.RequestedThemeVariant, a.Janela.ActualThemeVariant)
                .Should().Be((ThemeVariant.Default, ThemeVariant.Light), "sem escolha, o do sistema (claro na tela de testes)");

            a.Clicar("Exibir", "Tema", "Escuro");
            (a.Vm.Tema, Application.Current.RequestedThemeVariant, a.Janela.ActualThemeVariant)
                .Should().Be((TemaDaInterface.Escuro, ThemeVariant.Dark, ThemeVariant.Dark));
            new[] { "Igual ao sistema", "Claro", "Escuro" }.Select(t => a.Item("Exibir", "Tema", t).IsChecked)
                .Should().Equal(false, false, true);
            a.Ambiente.PreferenciasInterface.Salvo!.Tema.Should().Be(TemaDaInterface.Escuro);

            a.Clicar("Exibir", "Tema", "Claro");
            (a.Vm.Tema, Application.Current.RequestedThemeVariant).Should().Be((TemaDaInterface.Claro, ThemeVariant.Light));

            a.Clicar("Exibir", "Tema", "Igual ao sistema");
            (a.Vm.Tema, Application.Current.RequestedThemeVariant).Should().Be((TemaDaInterface.Sistema, ThemeVariant.Default));
        }
        finally
        {
            VoltarAoTemaPadrao();
        }
    });

    [Fact]
    public Task Tema_DaSessaoAnterior_AplicadoAoAbrir_TambemNasOutrasJanelas() => Tela.Executar(() =>
    {
        try
        {
            var a = Abrir(ambiente => ambiente.PreferenciasInterface.Salvo = new PreferenciasInterface(false, TemaDaInterface.Escuro));

            a.Janela.ActualThemeVariant.Should().Be(ThemeVariant.Dark);
            new Views.SobreWindow().ActualThemeVariant.Should().Be(ThemeVariant.Dark, "o tema é do programa inteiro");
        }
        finally
        {
            VoltarAoTemaPadrao();
        }
    });

    [Fact]
    public Task TemaClaro_AsCoresDeAntes_EOEscuroTemTodasAsChaves() => Tela.Executar(() =>
    {
        foreach (var (chave, cor) in CoresDoClaroDeAntes)
        {
            Cor(chave, ThemeVariant.Light).Should().Be(Color.Parse(cor), $"{chave} no tema claro não muda");
            Cor(chave, ThemeVariant.Dark).Should().NotBe(Color.Parse(cor), $"{chave} tem uma versão para o escuro");
        }
        var claras = ((ResourceDictionary)Application.Current!.Resources.ThemeDictionaries[ThemeVariant.Light]).Keys;
        var escuras = ((ResourceDictionary)Application.Current.Resources.ThemeDictionaries[ThemeVariant.Dark]).Keys;
        claras.Should().BeEquivalentTo(escuras).And.BeEquivalentTo(CoresDoClaroDeAntes.Keys);
    });

    [Fact]
    public Task TemaEscuro_TextosLegiveis_NosFundosDoPrograma() => Tela.Executar(() =>
    {
        var escuro = ThemeVariant.Dark;
        var texto = Application.Current!.TryGetResource("SystemControlForegroundBaseHighBrush", escuro, out var valor)
            ? ((ISolidColorBrush)valor!).Color
            : Colors.White;
        var preto = Colors.Black;

        foreach (var fundo in new[] { "FundoDoBotaoAtivo", "FundoDoControleDeZoom", "FundoDeAviso", "FundoSutil", "FundoDeSucesso",
                     "FundoDoBalaoDoUsuario", "FundoDoBalaoDoAssistente", "FundoDoBalaoDeErro", "FundoDosFiltros", "FundoDaMiniatura",
                     "FundoDaMiniaturaSelecionada" })
            Contraste(texto, Cor(fundo, escuro), preto).Should().BeGreaterThanOrEqualTo(4.5, $"texto do tema sobre {fundo}");
        Contraste(Cor("TextoDeAviso", escuro), Cor("FundoDeAviso", escuro), preto).Should().BeGreaterThanOrEqualTo(4.5);
        Contraste(Cor("TextoDeLink", escuro), Cor("FundoDeAviso", escuro), preto).Should().BeGreaterThanOrEqualTo(4.5);
        Contraste(Cor("TextoDeErro", escuro), preto, preto).Should().BeGreaterThanOrEqualTo(4.5);
        Contraste(Cor("TextoDeErro", escuro), Cor("FundoDosFiltros", escuro), preto).Should().BeGreaterThanOrEqualTo(4.5);
        Contraste(Cor("TextoDeErroForte", escuro), Cor("FundoSutil", escuro), preto).Should().BeGreaterThanOrEqualTo(4.5);
        Contraste(Cor("TextoDeAviso", escuro), preto, preto).Should().BeGreaterThanOrEqualTo(4.5);
        Contraste(Cor("GanttMarco", escuro), preto, preto).Should().BeGreaterThanOrEqualTo(3, "o losango precisa aparecer");
        Contraste(Cor("GanttResumo", escuro), preto, preto).Should().BeGreaterThanOrEqualTo(3, "o chapéu precisa aparecer");
    });

    [Fact]
    public Task TemaEscuro_ZoomLegivel_ENoClaroOFundoDeSempre() => Tela.Executar(() =>
    {
        try
        {
            var a = Abrir();
            Pintar(a);
            var zoom = a.Janela.ControleDeZoom;
            ((ISolidColorBrush)zoom.Background!).Color.Should().Be(Color.Parse("#E6FFFFFF"));

            a.Vm.DefinirTemaCommand.Execute(TemaDaInterface.Escuro);
            Pintar(a);

            var fundo = ((ISolidColorBrush)zoom.Background!).Color;
            fundo.A.Should().BeInRange((byte)128, (byte)254, "continua semitransparente");
            var escala = zoom.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text?.EndsWith("px/dia") == true);
            Contraste(((ISolidColorBrush)escala.Foreground!).Color, fundo, Colors.Black).Should().BeGreaterThanOrEqualTo(4.5);
        }
        finally
        {
            VoltarAoTemaPadrao();
        }
    });

    [Fact]
    public Task TemaEscuro_Gantt_RedesenhadoComMarcoResumoESelecaoClaros() => Tela.Executar(() =>
    {
        try
        {
            var a = Abrir();
            a.Vm.Gantt.MostrarCaminhoCritico = false;
            a.Vm.AdicionarResumoCommand.Execute(null);
            a.Vm.AdicionarMarcoCommand.Execute(null);
            var tarefa = a.Vm.Tarefas.First(t => !t.EhResumo && !t.EhMarco);
            a.Vm.TarefaSelecionada = tarefa;
            Pintar(a);
            var corpo = a.Janela.Gantt.FindControl<Canvas>("Corpo")!;
            Shape Forma(ViewModels.TarefaViewModel linha) =>
                corpo.Children.OfType<Shape>().Single(f => f.Tag is Guid id && id == linha.Tarefa.Id);
            (Color Marco, Color Resumo, Color Selecao) Cores() =>
                (((ISolidColorBrush)Forma(a.Vm.Tarefas.Single(t => t.EhMarco)).Fill!).Color,
                 ((ISolidColorBrush)Forma(a.Vm.Tarefas.Single(t => t.EhResumo)).Fill!).Color,
                 ((ISolidColorBrush)Forma(tarefa).Stroke!).Color);

            Cores().Should().Be((Color.Parse("#212121"), Color.Parse("#37474F"), Color.Parse("#263238")));

            a.Vm.DefinirTemaCommand.Execute(TemaDaInterface.Escuro);
            Pintar(a);
            Cores().Should().Be((Cor("GanttMarco", ThemeVariant.Dark), Cor("GanttResumo", ThemeVariant.Dark),
                Cor("GanttContornoDaSelecao", ThemeVariant.Dark)), "trocar o tema redesenha o Gantt");

            a.Vm.DefinirTemaCommand.Execute(TemaDaInterface.Claro);
            Pintar(a);
            Cores().Should().Be((Color.Parse("#212121"), Color.Parse("#37474F"), Color.Parse("#263238")));
        }
        finally
        {
            VoltarAoTemaPadrao();
        }
    });

    // Cor fixa numa tela some (ou ofusca) num dos temas: as cores ficam no App.axaml, uma por tema. Ficam de fora só as
    // que funcionam nos dois: o contorno da seleção, a sombra do zoom e o fundo escuro do visualizador de fotos.
    [Fact]
    public void Telas_SemCoresFixas_ForaDasQueServemNosDoisTemas()
    {
        var permitidas = new[] { "#6A8CAF", "#26000000", "#202124" };
        var pasta = System.IO.Path.Combine(PastaDoRepositorio.Raiz, "src", "ProjectPlanner.App", "Views");

        var fixas = Directory.GetFiles(pasta, "*.axaml")
            .SelectMany(arquivo => Regex.Matches(File.ReadAllText(arquivo), "#[0-9A-Fa-f]{6,8}\\b")
                .Select(m => $"{System.IO.Path.GetFileName(arquivo)} {m.Value}"))
            .Where(achado => !permitidas.Any(cor => achado.EndsWith(" " + cor, StringComparison.OrdinalIgnoreCase)));

        fixas.Should().BeEmpty();
    }
}
