using System.Reflection;
using System.Xml.Linq;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 15: a versão do produto fica num lugar só (Directory.Build.props) e aparece no título da janela.
public class VersaoTests
{
    private static readonly string VersaoDoProps = XDocument.Load(Path.Combine(PastaDoRepositorio.Raiz, "Directory.Build.props"))
        .Descendants("Version").Single().Value;

    [Fact]
    public void AVersaoDoProgramaEADoDirectoryBuildProps()
    {
        VersaoDoProps.Should().MatchRegex(@"^\d+\.\d+\.\d+$", "MAJOR.MINOR.PATCH");
        VersaoDoProduto.Atual.Should().Be(VersaoDoProps);
    }

    [Fact]
    public void OExecutavelLevaAMesmaVersaoNosMetadados()
    {
        var app = typeof(VersaoDoProduto).Assembly;

        app.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version.Should().Be(VersaoDoProps + ".0");
        app.GetName().Version!.ToString(3).Should().Be(VersaoDoProps);
        app.GetCustomAttribute<AssemblyProductAttribute>()!.Product.Should().Be("ProjectPlanner");
    }

    [Theory]
    [InlineData("0.15.0+7dc735c1a2b3", "0.15.0")]
    [InlineData("0.15.0", "0.15.0")]
    [InlineData("1.2.3-beta.1+abc", "1.2.3-beta.1")]
    public void Normalizar_TiraOCommitDaVersaoInformativa(string informativa, string esperada)
    {
        VersaoDoProduto.Normalizar(informativa, new Version(9, 9, 9, 9)).Should().Be(esperada);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("+abc")]
    public void Normalizar_SemVersaoInformativa_UsaADoAssembly(string? informativa)
    {
        VersaoDoProduto.Normalizar(informativa, new Version(0, 15, 0, 0)).Should().Be("0.15.0");
    }

    [Fact]
    public void Normalizar_SemNenhumaVersao_ZeroZeroZero()
    {
        VersaoDoProduto.Normalizar(null, null).Should().Be("0.0.0");
    }

    [Fact]
    public void Titulo_ProjetoNovo_NomeDoProjetoEVersao()
    {
        var vm = new Ambiente().CriarViewModel();

        vm.Titulo.Should().Be($"Projeto sem título — ProjectPlanner {VersaoDoProps}");
    }

    [Fact]
    public void Titulo_ProjetoSalvo_ArquivoComExtensao_EAsteriscoQuandoAlterado()
    {
        var ambiente = new Ambiente();
        var vm = ambiente.CriarViewModel();
        ambiente.Dialogos.ArquivoParaSalvar = "/projetos/Obra da sede.pplan";
        vm.SalvarProjetoCommand.Execute(null);

        vm.Titulo.Should().Be($"Obra da sede.pplan — ProjectPlanner {VersaoDoProps}");

        Ambiente.AdicionarTarefa(vm, "Fundação", 3);

        vm.Titulo.Should().Be($"*Obra da sede.pplan — ProjectPlanner {VersaoDoProps}");
    }
}
