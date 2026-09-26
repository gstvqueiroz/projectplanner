using System.Xml.Linq;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

// A associação dos arquivos .pplan fica fora do código (Info.plist do macOS, instalador NSIS do Windows).
// Estes testes garantem que ela continua batendo com a extensão que o programa usa e com os ícones do repositório.
public class EmpacotamentoTests
{
    private static readonly string Raiz = RaizDoRepositorio();
    private static readonly string PastaDoApp = Path.Combine(Raiz, "src", "ProjectPlanner.App");

    private static string RaizDoRepositorio()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
            if (File.Exists(Path.Combine(pasta.FullName, "ProjectPlanner.slnx")))
                return pasta.FullName;
        throw new InvalidOperationException("Não achei a raiz do repositório (ProjectPlanner.slnx).");
    }

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

    [Fact]
    public void InstaladorWindows_EmUtf8ComBom_ParaOMakensisLerOsAcentos()
    {
        File.ReadAllBytes(Path.Combine(PastaDoApp, "Windows", "ProjectPlanner.nsi")).Take(3)
            .Should().Equal(0xEF, 0xBB, 0xBF);
    }
}
