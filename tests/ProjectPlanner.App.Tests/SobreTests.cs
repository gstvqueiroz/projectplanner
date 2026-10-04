using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (V8): Ajuda > Sobre o ProjectPlanner, com as novidades lidas do CHANGELOG.md embutido.
public class SobreTests
{
    private const string Changelog = """
        # Novidades

        ## 0.25.0

        - Primeira novidade.
        - Segunda, que continua
          na linha de baixo.

        ## 0.24.0

        - Da versão anterior.
        """;

    [Fact]
    public void Novidades_SoAsDaVersao_JuntandoAsLinhasContinuadas()
    {
        NovidadesDaVersao.Ler(Changelog, "0.25.0").Should().Equal("Primeira novidade.", "Segunda, que continua na linha de baixo.");
        NovidadesDaVersao.Ler(Changelog, "0.24.0").Should().Equal("Da versão anterior.");
    }

    [Theory]
    [InlineData("0.99.0")]
    [InlineData("0.25")]
    public void Novidades_VersaoQueNaoEstaNoChangelog_Vazia(string versao)
    {
        NovidadesDaVersao.Ler(Changelog, versao).Should().BeEmpty();
        NovidadesDaVersao.Ler("", versao).Should().BeEmpty();
    }

    [Fact]
    public void Novidades_ChangelogComQuebrasDoWindows()
    {
        NovidadesDaVersao.Ler(Changelog.Replace("\n", "\r\n"), "0.25.0").Should().HaveCount(2);
        var nulo = () => NovidadesDaVersao.Ler(null!, "0.25.0");
        nulo.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ChangelogEmbutido_TemAVersaoAtual()
    {
        var sobre = new SobreViewModel();

        (sobre.Versao, sobre.Titulo).Should().Be((VersaoDoProduto.Atual, $"ProjectPlanner {VersaoDoProduto.Atual}"));
        NovidadesDaVersao.Changelog().Should().Contain($"## {VersaoDoProduto.Atual}");
        (sobre.TemNovidades, sobre.Novidades.Count > 0).Should().Be((true, true), "toda versão publicada tem novidades no CHANGELOG.md");
        sobre.Componentes.Select(c => c.Nome).Should().Contain(n => n.StartsWith("Avalonia")).And.Contain(n => n.StartsWith("MetadataExtractor"));
        sobre.Componentes.Should().OnlyContain(c => c.Licenca.Length > 0);
    }

    [Fact]
    public void Sobre_SemNovidades_EscondeASecao_EArgumentosNulos()
    {
        new SobreViewModel("9.9.9", Changelog).TemNovidades.Should().BeFalse();
        var semVersao = () => new SobreViewModel(null!, Changelog);
        var semChangelog = () => new SobreViewModel("0.25.0", null!);
        semVersao.Should().Throw<ArgumentNullException>();
        semChangelog.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComandoSobre_AbreATela()
    {
        var ambiente = new Ambiente();
        var vm = ambiente.CriarViewModel();

        vm.SobreCommand.Execute(null);

        ambiente.Dialogos.Sobres.Should().ContainSingle().Which.Versao.Should().Be(VersaoDoProduto.Atual);
    }

    [Fact]
    public Task Janela_MostraVersaoNovidadesEComponentes() => Tela.Executar(() =>
    {
        var janela = new SobreWindow(new SobreViewModel("0.25.0", Changelog));
        janela.Show();
        Dispatcher.UIThread.RunJobs();

        janela.FindControl<TextBlock>("Versao")!.Text.Should().Be("ProjectPlanner 0.25.0");
        janela.FindControl<ItemsControl>("Novidades")!.ItemCount.Should().Be(2);
        janela.FindControl<ItemsControl>("Componentes")!.ItemCount.Should().BeGreaterThan(4);
        janela.Close();
    });
}
