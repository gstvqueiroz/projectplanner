using System.Security.Cryptography;
using System.Text;

namespace ProjectPlanner.App.Services;

/// <summary>
/// As cópias de segurança dos projetos (Bloco 25, R2): antes de gravar por cima de um .pplan, a versão anterior vai para
/// <c>backups/&lt;projeto&gt;-&lt;código do caminho&gt;/</c>, na pasta de preferências do programa, e só as
/// <see cref="Quantidade"/> mais recentes de cada projeto ficam. Uma cópia de segurança é um .pplan comum: para
/// recuperar, basta abri-la.
/// </summary>
public sealed class BackupsDeProjetos
{
    public const int Quantidade = 5;

    private readonly TimeProvider _relogio;

    public BackupsDeProjetos(string pasta, TimeProvider relogio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        ArgumentNullException.ThrowIfNull(relogio);
        (Pasta, _relogio) = (pasta, relogio);
    }

    public static string PastaPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "backups");

    public string Pasta { get; }

    /// <summary>
    /// A pasta das cópias de um projeto: o nome do arquivo e um código do caminho inteiro, para dois "obra.pplan" de
    /// pastas diferentes não se misturarem.
    /// </summary>
    public string PastaDo(string caminhoDoProjeto)
    {
        var completo = Path.GetFullPath(caminhoDoProjeto);
        var codigo = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(completo)))[..8];
        return Path.Combine(Pasta, $"{Path.GetFileNameWithoutExtension(completo)}-{codigo}");
    }

    /// <summary>As cópias do projeto, da mais recente para a mais antiga.</summary>
    public IReadOnlyList<string> CopiasDo(string caminhoDoProjeto)
    {
        var pasta = PastaDo(caminhoDoProjeto);
        return Directory.Exists(pasta)
            ? Directory.GetFiles(pasta, "*.pplan").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];
    }

    /// <summary>Copia o arquivo como está agora (se existe) e apaga as cópias além das mais recentes.</summary>
    /// <returns>O caminho da cópia, ou nulo se não havia o que copiar ou não deu para copiar.</returns>
    public string? Guardar(string caminhoDoProjeto)
    {
        try
        {
            if (!File.Exists(caminhoDoProjeto))
                return null;
            var pasta = PastaDo(caminhoDoProjeto);
            Directory.CreateDirectory(pasta);
            var copia = Path.Combine(pasta,
                $"{Path.GetFileNameWithoutExtension(caminhoDoProjeto)}-{_relogio.GetLocalNow():yyyyMMdd-HHmmss-fff}.pplan");
            File.Copy(caminhoDoProjeto, copia, overwrite: true);
            foreach (var antiga in CopiasDo(caminhoDoProjeto).Skip(Quantidade))
                File.Delete(antiga);
            return copia;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null; // a cópia de segurança nunca impede de salvar
        }
    }
}
