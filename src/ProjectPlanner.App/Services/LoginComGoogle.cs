using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Text;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Services;

/// <summary>O que o Google devolveu ao navegador, para o servidor trocar pela sessão.</summary>
public sealed record CodigoDoGoogle(string Codigo, string Verificador, Uri Redirecionamento);

/// <summary>
/// Bloco 45: o "Entrar com Google" de um programa de computador (OAuth com PKCE e volta para o próprio computador). Abre o
/// login do Google no navegador e espera a volta em <c>http://127.0.0.1:porta/</c>, só neste computador. O código que
/// volta só vale com o verificador, que nunca sai do programa até ir ao servidor do ProjectPlanner.
/// </summary>
public static class LoginComGoogle
{
    public static readonly Uri EnderecoDoGoogle = new("https://accounts.google.com/o/oauth2/v2/auth");

    /// <summary>Quanto esperar a pessoa terminar o login no navegador.</summary>
    public static readonly TimeSpan TempoLimite = TimeSpan.FromMinutes(5);

    public const string PaginaDeSucesso =
        "<!doctype html><meta charset=\"utf-8\"><title>ProjectPlanner</title><body style=\"font-family:sans-serif;margin:3em\">" +
        "<h2>Pronto!</h2><p>Você entrou com o Google. Pode fechar esta aba e voltar ao ProjectPlanner.</p></body>";

    public const string PaginaDeCancelamento =
        "<!doctype html><meta charset=\"utf-8\"><title>ProjectPlanner</title><body style=\"font-family:sans-serif;margin:3em\">" +
        "<h2>Login não concluído</h2><p>Pode fechar esta aba e voltar ao ProjectPlanner.</p></body>";

    /// <exception cref="ErroDaContaException">A pessoa cancelou no Google, ou o tempo acabou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado no programa.</exception>
    // 0.64.0: no macOS, fechar o ouvinte já parado às vezes lança "Address already in use" (no HttpListener do .NET): isso não
    // pode esconder o que de fato aconteceu (o cancelamento, o código que chegou).
    private sealed class FechamentoSeguro(HttpListener ouvinte) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                ouvinte.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // nada a fazer: a porta é liberada com o processo
            }
        }
    }

    // 0.64.0: a porta livre pode ser tomada entre achá-la e abrir o ouvinte (outro programa, ou outro login ao mesmo tempo):
    // tenta outra porta algumas vezes.
    private static (HttpListener Ouvinte, Uri Volta) Ouvir()
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var volta = new Uri($"http://127.0.0.1:{PortaLivre()}/");
            var ouvinte = new HttpListener();
            ouvinte.Prefixes.Add(volta.AbsoluteUri);
            try
            {
                ouvinte.Start();
                return (ouvinte, volta);
            }
            catch (HttpListenerException) when (tentativa < 5)
            {
                ouvinte.Close();
            }
        }
    }

    public static async Task<CodigoDoGoogle> ObterCodigoAsync(string clientId, Func<Uri, Task> abrirNavegador, CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(abrirNavegador);
        var (ouvinte, volta) = Ouvir();
        using var fechar = new FechamentoSeguro(ouvinte);

        var verificador = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var estado = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        await abrirNavegador(EnderecoDeLogin(clientId, volta, verificador, estado));

        using var tempo = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        tempo.CancelAfter(TempoLimite);
        using var parar = tempo.Token.Register(ouvinte.Stop);
        while (true)
        {
            HttpListenerContext contexto;
            try
            {
                contexto = await ouvinte.GetContextAsync();
            }
            catch (Exception) when (tempo.IsCancellationRequested)
            {
                cancelamento.ThrowIfCancellationRequested();
                throw new ErroDaContaException("O login com Google não foi concluído a tempo. Tente de novo.", 0);
            }
            var consulta = contexto.Request.QueryString;
            if (consulta["state"] != estado)
            {
                Responder(contexto, HttpStatusCode.NotFound, ""); // outro pedido do navegador (o ícone da aba, por exemplo)
                continue;
            }
            if (consulta["error"] is { } erro)
            {
                Responder(contexto, HttpStatusCode.OK, PaginaDeCancelamento);
                throw new ErroDaContaException(erro == "access_denied"
                    ? "Login com Google cancelado."
                    : $"O Google não concluiu o login ({erro}). Tente de novo.", 0);
            }
            if (consulta["code"] is { Length: > 0 } codigo)
            {
                Responder(contexto, HttpStatusCode.OK, PaginaDeSucesso);
                return new CodigoDoGoogle(codigo, verificador, volta);
            }
            Responder(contexto, HttpStatusCode.BadRequest, PaginaDeCancelamento);
        }
    }

    /// <summary>O endereço do login do Google (com o desafio PKCE do verificador).</summary>
    public static Uri EnderecoDeLogin(string clientId, Uri volta, string verificador, string estado)
    {
        var desafio = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verificador)));
        string E(string valor) => Uri.EscapeDataString(valor);
        return new Uri($"{EnderecoDoGoogle}?client_id={E(clientId)}&redirect_uri={E(volta.AbsoluteUri)}&response_type=code" +
                       $"&scope={E("openid email profile")}&code_challenge={desafio}&code_challenge_method=S256&state={E(estado)}" +
                       "&prompt=select_account");
    }

    private static int PortaLivre()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static void Responder(HttpListenerContext contexto, HttpStatusCode status, string html)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            contexto.Response.StatusCode = (int)status;
            contexto.Response.ContentType = "text/html; charset=utf-8";
            contexto.Response.ContentLength64 = bytes.Length;
            contexto.Response.OutputStream.Write(bytes);
            contexto.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // o navegador fechou a aba antes da resposta: não faz diferença
        }
    }
}
