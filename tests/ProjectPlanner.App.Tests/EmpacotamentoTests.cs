using System.Xml.Linq;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

// A associação dos arquivos .pplan fica fora do código (Info.plist do macOS, instalador NSIS do Windows).
// Estes testes garantem que ela continua batendo com a extensão que o programa usa e com os ícones do repositório.
public class EmpacotamentoTests
{
    private static readonly string Raiz = PastaDoRepositorio.Raiz;
    private static readonly string PastaDoApp = Path.Combine(Raiz, "src", "ProjectPlanner.App");

    // Num plist, cada valor de um <dict> vem logo depois do seu <key>.
    private static XElement Valor(XElement dict, string chave) =>
        dict.Elements("key").Single(k => k.Value == chave).ElementsAfterSelf().First();

    [Fact]
    public void InfoPlist_DeclaraOTipoPplanComOAppComoDonoEEditor()
    {
        var plist = XDocument.Load(Path.Combine(PastaDoApp, "macOS", "Info.plist")).Root!.Element("dict")!;
        var documento = Valor(plist, "CFBundleDocumentTypes").Elements("dict").Single();
        var tipo = Valor(plist, "UTExportedTypeDeclarations").Elements("dict").Single();

        var uti = Valor(tipo, "UTTypeIdentifier").Value;
        Valor(documento, "LSItemContentTypes").Elements("string").Select(e => e.Value).Should().Equal(uti);
        (Valor(documento, "CFBundleTypeRole").Value, Valor(documento, "LSHandlerRank").Value).Should().Be(("Editor", "Owner"));
        Valor(Valor(tipo, "UTTypeTagSpecification"), "public.filename-extension").Elements("string").Select(e => "." + e.Value)
            .Should().Equal(ArquivosPedidos.Extensao);
        var icone = Valor(documento, "CFBundleTypeIconFile").Value;
        Valor(tipo, "UTTypeIconFile").Value.Should().Be(icone);
        File.Exists(Path.Combine(PastaDoApp, "macOS", icone + ".icns")).Should().BeTrue();
    }

    [Fact]
    public void Publicacao_ColocaOIconeDosProjetosDentroDoApp()
    {
        File.ReadAllText(Path.Combine(Raiz, "scripts", "publicar.sh")).Should().Contain("macOS/ProjetoPplan.icns");
    }

    [Fact]
    public void InstaladorWindows_RegistraAExtensaoParaOUsuarioComOArquivoClicadoNoComando()
    {
        var script = File.ReadAllText(Path.Combine(PastaDoApp, "Windows", "ProjectPlanner.nsi"));

        script.Should().Contain($"!define EXTENSAO \"{ArquivosPedidos.Extensao}\"")
            .And.Contain("RequestExecutionLevel user")
            .And.Contain("WriteRegStr HKCU \"Software\\Classes\\${PROGID}\\shell\\open\\command\" \"\" '\"$INSTDIR\\ProjectPlanner.exe\" \"%1\"'")
            .And.Contain("WriteRegStr HKCU \"Software\\Classes\\${PROGID}\\DefaultIcon\" \"\" \"$INSTDIR\\ProjetoPplan.ico\"")
            .And.Contain("DeleteRegKey HKCU \"Software\\Classes\\${PROGID}\"");
        File.Exists(Path.Combine(PastaDoApp, "Windows", "ProjetoPplan.ico")).Should().BeTrue();
    }

    // Bloco 15: nada é publicado sem confirmar a versão. Estes casos param antes de compilar.
    private static (int Codigo, string Saida) Publicar(params string[] argumentos)
    {
        var inicio = new System.Diagnostics.ProcessStartInfo("bash")
        {
            WorkingDirectory = Raiz,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // sem terminal: o script não pode perguntar
        };
        inicio.ArgumentList.Add(Path.Combine("scripts", "publicar.sh"));
        foreach (var argumento in argumentos)
            inicio.ArgumentList.Add(argumento);
        using var processo = System.Diagnostics.Process.Start(inicio)!;
        processo.StandardInput.Close();
        var saida = processo.StandardOutput.ReadToEnd() + processo.StandardError.ReadToEnd();
        processo.WaitForExit();
        return (processo.ExitCode, saida);
    }

    [Fact]
    public void Publicar_ComVersaoDiferenteDaDoProps_ParaSemGerarNada()
    {
        if (OperatingSystem.IsWindows())
            return; // o script é bash (Mac/Linux)

        var (codigo, saida) = Publicar("--versao", "9.9.9", "osx-arm64");

        codigo.Should().Be(1);
        saida.Should().Contain($"A versão confirmada (9.9.9) não é a do Directory.Build.props ({VersaoDoProduto.Atual}). Nada foi publicado.");
    }

    [Fact]
    public void Publicar_SemTerminalESemConfirmar_PedeAVersaoNaLinhaDeComando()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (codigo, saida) = Publicar("osx-arm64");

        codigo.Should().Be(1);
        saida.Should().Contain($"Versão no Directory.Build.props: {VersaoDoProduto.Atual}")
            .And.Contain($"--versao {VersaoDoProduto.Atual} osx-arm64");
    }

    [Fact]
    public void Publicacao_DocumentaAConfirmacaoDaVersao()
    {
        File.ReadAllText(Path.Combine(Raiz, "PUBLISHING.md"))
            .Should().Contain("toda publicação confirma a versão antes de gerar os executáveis")
            .And.Contain("scripts/publicar.sh --versao");
    }

    [Fact]
    public void InstaladorWindows_EmUtf8ComBom_ParaOMakensisLerOsAcentos()
    {
        File.ReadAllBytes(Path.Combine(PastaDoApp, "Windows", "ProjectPlanner.nsi")).Take(3)
            .Should().Equal(0xEF, 0xBB, 0xBF);
    }
}
