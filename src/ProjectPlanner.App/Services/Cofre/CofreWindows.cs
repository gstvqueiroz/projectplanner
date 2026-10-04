using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace ProjectPlanner.App.Services.Cofre;

/// <summary>
/// Gerenciador de Credenciais do Windows (Bloco 23): uma credencial genérica "ProjectPlanner:&lt;nome&gt;", guardada
/// para o usuário nesta máquina (não vai para outras máquinas do mesmo usuário). Usa a API do advapi32 diretamente.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CofreWindows : ICofreDeSegredos
{
    private const int Generica = 1; // CRED_TYPE_GENERIC
    private const int NestaMaquina = 2; // CRED_PERSIST_LOCAL_MACHINE
    private const int NaoEncontrada = 1168; // ERROR_NOT_FOUND
    private const int TamanhoMaximo = 5 * 512; // CRED_MAX_CREDENTIAL_BLOB_SIZE

    private readonly string _prefixo;

    public CofreWindows(string prefixo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixo);
        _prefixo = prefixo;
    }

    public string Nome => "Gerenciador de Credenciais do Windows";

    public bool Disponivel => true;

    public string? Ler(string nome)
    {
        if (!CredRead(Alvo(nome), Generica, 0, out var credencial))
        {
            var erro = Marshal.GetLastWin32Error();
            if (erro == NaoEncontrada)
                return null;
            throw Falha("ler a chave", erro);
        }
        try
        {
            var dados = Marshal.PtrToStructure<Credencial>(credencial);
            var bytes = new byte[dados.CredentialBlobSize];
            Marshal.Copy(dados.CredentialBlob, bytes, 0, bytes.Length);
            var segredo = Encoding.UTF8.GetString(bytes);
            Array.Clear(bytes);
            return segredo;
        }
        finally
        {
            CredFree(credencial);
        }
    }

    public void Gravar(string nome, string segredo)
    {
        ArgumentException.ThrowIfNullOrEmpty(segredo);
        var bytes = Encoding.UTF8.GetBytes(segredo);
        if (bytes.Length > TamanhoMaximo)
            throw new CofreException("A chave é grande demais para o Gerenciador de Credenciais do Windows.");
        var blob = Marshal.AllocHGlobal(bytes.Length);
        var alvo = Marshal.StringToHGlobalUni(Alvo(nome));
        var usuario = Marshal.StringToHGlobalUni(nome);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credencial = new Credencial
            {
                Type = Generica,
                TargetName = alvo,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = NestaMaquina,
                UserName = usuario,
            };
            if (!CredWrite(ref credencial, 0))
                throw Falha("salvar a chave", Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(alvo);
            Marshal.FreeHGlobal(usuario);
        }
    }

    public void Remover(string nome)
    {
        if (!CredDelete(Alvo(nome), Generica, 0))
        {
            var erro = Marshal.GetLastWin32Error();
            if (erro != NaoEncontrada)
                throw Falha("remover a chave", erro);
        }
    }

    private string Alvo(string nome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        return $"{_prefixo}:{nome}";
    }

    private static CofreException Falha(string acao, int erro) =>
        new($"Não foi possível {acao} no Gerenciador de Credenciais do Windows ({new Win32Exception(erro).Message}).");

    [StructLayout(LayoutKind.Sequential)]
    private struct Credencial
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string alvo, int tipo, int opcoes, out IntPtr credencial);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credencial credencial, int opcoes);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string alvo, int tipo, int opcoes);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
