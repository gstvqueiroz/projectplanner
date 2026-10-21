using System.Diagnostics;

namespace ProjectPlanner.App.Services;

/// <summary>
/// 0.47.2: converte uma foto HEIC (a do iPhone) para JPG antes de ir ao assistente. Os níveis com Claude (Padrão e Máximo)
/// não leem HEIC; o JPG vale para todos.
/// </summary>
public interface IConversorDeHeic
{
    /// <returns>O caminho do JPG convertido; nulo se não deu para converter (o original segue como está).</returns>
    string? ParaJpeg(string caminho);
}

/// <summary>No Mac, pelo <c>sips</c>, que vem com o sistema. Os JPGs ficam numa pasta temporária do programa.</summary>
public sealed class ConversorDeHeicPeloSips(string pasta) : IConversorDeHeic
{
    public const string Sips = "/usr/bin/sips";

    public static readonly IReadOnlySet<string> Extensoes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif" };

    /// <summary>O conversor deste sistema: o do Mac; nos outros, nenhum (o HEIC segue como está).</summary>
    public static IConversorDeHeic? DoSistema() =>
        OperatingSystem.IsMacOS() && File.Exists(Sips) ? new ConversorDeHeicPeloSips(Path.Combine(Path.GetTempPath(), "ProjectPlanner-anexos")) : null;

    public string? ParaJpeg(string caminho)
    {
        try
        {
            var destino = Path.Combine(pasta, Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(caminho) + ".jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            var inicio = new ProcessStartInfo(Sips) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argumento in new[] { "-s", "format", "jpeg", "-s", "formatOptions", "85", caminho, "--out", destino })
                inicio.ArgumentList.Add(argumento);
            using var processo = Process.Start(inicio);
            if (processo is null || !processo.WaitForExit(30_000))
            {
                processo?.Kill();
                return null;
            }
            return processo.ExitCode == 0 && File.Exists(destino) && new FileInfo(destino).Length > 0 ? destino : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
