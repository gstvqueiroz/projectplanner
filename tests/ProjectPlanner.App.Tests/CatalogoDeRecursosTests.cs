using ProjectPlanner.App.Services;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.Tests;

// Bloco 32: o catálogo pessoal de recursos em recursos.json.
public sealed class CatalogoDeRecursosTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-catalogo-" + Guid.NewGuid().ToString("N"));

    private string Caminho => Path.Combine(_pasta, "sub", "recursos.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void GravaELe_ESemArquivoEVazio()
    {
        var catalogo = new ArquivoCatalogoDeRecursos(Caminho);
        catalogo.Carregar().Should().BeEmpty();
        var recursos = new[]
        {
            new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, null, 280, 2, "Equipe"),
            new Recurso(Guid.NewGuid(), "Cimento", TipoDeRecurso.Material, "sc", 38.9m),
        };

        catalogo.Salvar(recursos);

        new ArquivoCatalogoDeRecursos(Caminho).Carregar().Should().Equal(recursos);
        File.ReadAllText(Caminho).Should().Contain("\"tipo\": \"Profissional\"");
    }

    [Fact]
    public void ArquivoComDefeito_FicaVazio_ERecursoInvalidoFicaDeFora()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        File.WriteAllText(Caminho, "{ ruim");
        new ArquivoCatalogoDeRecursos(Caminho).Carregar().Should().BeEmpty();

        File.WriteAllText(Caminho, """
            {"versao":1,"recursos":[{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","nome":"","tipo":"Material","unidade":"sc","custoUnitario":1},
            {"id":"7f9619ff-8b86-d011-b42d-00cf4fc964ff","nome":"Areia","tipo":"Material","unidade":"m³","custoUnitario":120}]}
            """);
        new ArquivoCatalogoDeRecursos(Caminho).Carregar().Should().ContainSingle().Which.Nome.Should().Be("Areia");
    }
}
