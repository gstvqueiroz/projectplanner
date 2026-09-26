using System.Reflection;

namespace ProjectPlanner.App.Services;

/// <summary>
/// A versão do ProjectPlanner, lida do próprio programa. A fonte é o <c>&lt;Version&gt;</c> do
/// Directory.Build.props, que o build grava no assembly; aqui ela só é lida, para o título da janela.
/// </summary>
public static class VersaoDoProduto
{
    /// <summary>A versão em uso, como "0.15.0".</summary>
    public static string Atual { get; } = Ler(typeof(VersaoDoProduto).Assembly);

    internal static string Ler(Assembly assembly) =>
        Normalizar(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version);

    /// <summary>
    /// A versão para exibir: a informativa sem o sufixo do commit que o SDK acrescenta ("0.15.0+3f2a…");
    /// sem ela, os três primeiros números da versão do assembly; sem nenhuma das duas, "0.0.0".
    /// </summary>
    internal static string Normalizar(string? informativa, Version? doAssembly)
    {
        var semCommit = informativa?.Split('+')[0].Trim();
        if (!string.IsNullOrEmpty(semCommit))
            return semCommit;
        return doAssembly is null ? "0.0.0" : doAssembly.ToString(3);
    }
}
