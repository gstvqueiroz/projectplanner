using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ProjectPlanner.App.Services.Cofre;

/// <summary>
/// Chaves do macOS (Bloco 23): um item "senha genérica" no chaveiro de login, com o serviço "ProjectPlanner" e o nome
/// do segredo como conta. Usa a API SecItem do Security.framework diretamente (sem pacotes de terceiros).
/// </summary>
/// <remarks>
/// O macOS liga o item ao programa que o criou. Um programa com outra assinatura (uma versão nova publicada sem
/// assinatura de desenvolvedor, ou o <c>dotnet run</c>) pode ver o aviso do sistema pedindo para usar o item; com
/// "Permitir sempre" ele não aparece mais. Ver docs/ASSISTENTE-IA.md.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class CofreMacOS : ICofreDeSegredos
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    private const int Sucesso = 0;
    private const int ItemNaoEncontrado = -25300; // errSecItemNotFound
    private const int ItemDuplicado = -25299; // errSecDuplicateItem
    private const int CanceladoPeloUsuario = -128; // errSecUserCanceled
    private const int AutenticacaoFalhou = -25293; // errSecAuthFailed
    private const int InteracaoNaoPermitida = -25308; // errSecInteractionNotAllowed

    private readonly string _servico;

    public CofreMacOS(string servico)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servico);
        _servico = servico;
    }

    public string Nome => "Chaves do macOS";

    public bool Disponivel => true;

    public string? Ler(string nome)
    {
        using var consulta = Consulta(nome);
        consulta.Por(Constantes.ReturnData, Constantes.True);
        consulta.Por(Constantes.MatchLimit, Constantes.MatchLimitOne);
        var status = SecItemCopyMatching(consulta.Ponteiro, out var dados);
        if (status == ItemNaoEncontrado)
            return null;
        Conferir(status, "ler a chave");
        try
        {
            var tamanho = (int)CFDataGetLength(dados);
            var bytes = new byte[tamanho];
            Marshal.Copy(CFDataGetBytePtr(dados), bytes, 0, tamanho);
            var segredo = Encoding.UTF8.GetString(bytes);
            Array.Clear(bytes);
            return segredo;
        }
        finally
        {
            CFRelease(dados);
        }
    }

    public void Gravar(string nome, string segredo)
    {
        ArgumentException.ThrowIfNullOrEmpty(segredo);
        var bytes = Encoding.UTF8.GetBytes(segredo);
        try
        {
            using var valor = new ObjetoCF(CFDataCreate(IntPtr.Zero, bytes, bytes.Length));
            using var novo = Consulta(nome);
            novo.Por(Constantes.ValueData, valor.Ponteiro);
            novo.Por(Constantes.Label, novo.Guardar(Texto($"{_servico} — {nome}")));
            var status = SecItemAdd(novo.Ponteiro, IntPtr.Zero);
            if (status == ItemDuplicado)
            {
                using var existente = Consulta(nome);
                using var mudanca = new Dicionario();
                mudanca.Por(Constantes.ValueData, valor.Ponteiro);
                status = SecItemUpdate(existente.Ponteiro, mudanca.Ponteiro);
            }
            Conferir(status, "salvar a chave");
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public void Remover(string nome)
    {
        using var consulta = Consulta(nome);
        var status = SecItemDelete(consulta.Ponteiro);
        if (status != ItemNaoEncontrado)
            Conferir(status, "remover a chave");
    }

    private Dicionario Consulta(string nome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        var consulta = new Dicionario();
        consulta.Por(Constantes.Class, Constantes.ClassGenericPassword);
        consulta.Por(Constantes.AttrService, consulta.Guardar(Texto(_servico)));
        consulta.Por(Constantes.AttrAccount, consulta.Guardar(Texto(nome)));
        return consulta;
    }

    private static void Conferir(int status, string acao)
    {
        if (status == Sucesso)
            return;
        throw new CofreException(status switch
        {
            CanceladoPeloUsuario => $"Não foi possível {acao}: o acesso às Chaves do macOS foi negado.",
            AutenticacaoFalhou or InteracaoNaoPermitida =>
                $"Não foi possível {acao}: as Chaves do macOS estão bloqueadas ou recusaram o acesso.",
            _ => $"Não foi possível {acao} nas Chaves do macOS (erro {status}).",
        });
    }

    private static IntPtr Texto(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto + "\0");
        return CFStringCreateWithCString(IntPtr.Zero, bytes, Utf8);
    }

    /// <summary>Um CFMutableDictionary que solta, ao ser descartado, ele mesmo e os textos criados para ele.</summary>
    private sealed class Dicionario : IDisposable
    {
        private readonly List<IntPtr> _guardados = [];

        public Dicionario() =>
            Ponteiro = CFDictionaryCreateMutable(IntPtr.Zero, 0, Constantes.ChavesCallBacks, Constantes.ValoresCallBacks);

        public IntPtr Ponteiro { get; }

        public void Por(IntPtr chave, IntPtr valor) => CFDictionaryAddValue(Ponteiro, chave, valor);

        /// <summary>Solta o objeto junto com o dicionário (o dicionário tem a própria referência).</summary>
        public IntPtr Guardar(IntPtr objeto)
        {
            _guardados.Add(objeto);
            return objeto;
        }

        public void Dispose()
        {
            foreach (var objeto in _guardados)
                CFRelease(objeto);
            CFRelease(Ponteiro);
        }
    }

    private sealed class ObjetoCF(IntPtr ponteiro) : IDisposable
    {
        public IntPtr Ponteiro { get; } = ponteiro;

        public void Dispose() => CFRelease(Ponteiro);
    }

    /// <summary>As constantes exportadas pelos frameworks (kSecClass, kCFBooleanTrue...).</summary>
    private static class Constantes
    {
        private static readonly IntPtr Seguranca = NativeLibrary.Load(Security);
        private static readonly IntPtr Fundacao = NativeLibrary.Load(CoreFoundation);

        public static readonly IntPtr Class = Valor(Seguranca, "kSecClass");
        public static readonly IntPtr ClassGenericPassword = Valor(Seguranca, "kSecClassGenericPassword");
        public static readonly IntPtr AttrService = Valor(Seguranca, "kSecAttrService");
        public static readonly IntPtr AttrAccount = Valor(Seguranca, "kSecAttrAccount");
        public static readonly IntPtr Label = Valor(Seguranca, "kSecAttrLabel");
        public static readonly IntPtr ValueData = Valor(Seguranca, "kSecValueData");
        public static readonly IntPtr ReturnData = Valor(Seguranca, "kSecReturnData");
        public static readonly IntPtr MatchLimit = Valor(Seguranca, "kSecMatchLimit");
        public static readonly IntPtr MatchLimitOne = Valor(Seguranca, "kSecMatchLimitOne");
        public static readonly IntPtr True = Valor(Fundacao, "kCFBooleanTrue");

        // As callbacks são estruturas: o que se passa é o endereço delas, não o conteúdo.
        public static readonly IntPtr ChavesCallBacks = NativeLibrary.GetExport(Fundacao, "kCFTypeDictionaryKeyCallBacks");
        public static readonly IntPtr ValoresCallBacks = NativeLibrary.GetExport(Fundacao, "kCFTypeDictionaryValueCallBacks");

        private static IntPtr Valor(IntPtr biblioteca, string simbolo) => Marshal.ReadIntPtr(NativeLibrary.GetExport(biblioteca, simbolo));
    }

    [DllImport(Security)]
    private static extern int SecItemCopyMatching(IntPtr consulta, out IntPtr resultado);

    [DllImport(Security)]
    private static extern int SecItemAdd(IntPtr atributos, IntPtr resultado);

    [DllImport(Security)]
    private static extern int SecItemUpdate(IntPtr consulta, IntPtr atributos);

    [DllImport(Security)]
    private static extern int SecItemDelete(IntPtr consulta);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr alocador, nint capacidade, IntPtr chaves, IntPtr valores);

    [DllImport(CoreFoundation)]
    private static extern void CFDictionaryAddValue(IntPtr dicionario, IntPtr chave, IntPtr valor);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alocador, byte[] texto, uint codificacao);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr alocador, byte[] bytes, nint tamanho);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr dados);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(IntPtr dados);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr objeto);
}
