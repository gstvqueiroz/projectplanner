namespace ProjectPlanner.App.Tests;

/// <summary>A raiz do repositório, para os testes que conferem arquivos fora do código (scripts, plist, props).</summary>
public static class PastaDoRepositorio
{
    public static string Raiz { get; } = Achar();

    private static string Achar()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
            if (File.Exists(Path.Combine(pasta.FullName, "ProjectPlanner.slnx")))
                return pasta.FullName;
        throw new InvalidOperationException("Não achei a raiz do repositório (ProjectPlanner.slnx).");
    }
}
