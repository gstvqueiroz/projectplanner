namespace ProjectPlanner.App.Services;

/// <summary>
/// O registro local de erros (Bloco 25, R1): um arquivo por mês em <c>logs</c>, na pasta de preferências do programa
/// (ao lado do colunas.json), com a data, a versão, de onde veio e os detalhes do erro. Nunca lança: se não der para
/// gravar, o programa segue.
/// </summary>
public sealed class RegistroDeErros
{
    private readonly TimeProvider _relogio;
    private readonly Lock _trava = new();

    public RegistroDeErros(string pasta, TimeProvider relogio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        ArgumentNullException.ThrowIfNull(relogio);
        (Pasta, _relogio) = (pasta, relogio);
    }

    public static string PastaPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "logs");

    public string Pasta { get; }

    /// <summary>O arquivo do mês atual: erros-AAAA-MM.log.</summary>
    public string ArquivoAtual => Path.Combine(Pasta, $"erros-{_relogio.GetLocalNow():yyyy-MM}.log");

    /// <returns>Se gravou.</returns>
    public bool Registrar(Exception erro, string origem)
    {
        ArgumentNullException.ThrowIfNull(erro);
        try
        {
            lock (_trava)
            {
                Directory.CreateDirectory(Pasta);
                File.AppendAllText(ArquivoAtual, Formatar(_relogio.GetLocalNow(), VersaoDoProduto.Atual, origem, erro));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    internal static string Formatar(DateTimeOffset quando, string versao, string origem, Exception erro) =>
        $"[{quando:yyyy-MM-dd HH:mm:ss zzz}] ProjectPlanner {versao} — {origem}{Environment.NewLine}{erro}{Environment.NewLine}{Environment.NewLine}";
}
