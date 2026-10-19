using System.Net;
using System.Net.Http.Headers;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// Bloco 45: por onde os pedidos chegam ao Gemini. Com a chave própria, direto ao Google (cabeçalho <c>x-goog-api-key</c>);
/// com a conta ProjectPlanner, pelo servidor (<see cref="AcessoPelaConta"/>), que espelha o mesmo caminho
/// (<c>models/{modelo}:generateContent</c>). O corpo dos pedidos e das respostas é o mesmo nos dois.
/// </summary>
public abstract class AcessoAoGemini
{
    private protected AcessoAoGemini()
    {
    }

    /// <summary>O endereço a que se juntam os caminhos ("models/…").</summary>
    internal abstract Uri Endereco { get; }

    /// <summary>"o Gemini" ou "o servidor do ProjectPlanner", para as mensagens de conexão.</summary>
    internal abstract string Destino { get; }

    /// <summary>Põe a credencial no pedido. <paramref name="renovar"/>: a anterior foi recusada, pegue outra.</summary>
    /// <exception cref="ErroDoAssistenteException">Sem credencial (por exemplo, a sessão da conta acabou).</exception>
    internal abstract Task AutenticarAsync(HttpRequestMessage pedido, bool renovar, CancellationToken cancelamento);

    /// <summary>Vale pedir de novo com uma credencial renovada depois desta resposta?</summary>
    internal virtual bool RenovarDepoisDe(HttpStatusCode status) => false;

    /// <summary>A mensagem para o usuário de uma resposta de erro (sem segredos).</summary>
    internal abstract ErroDoAssistenteException Traduzir(HttpStatusCode status, string corpo, string modelo);
}

/// <summary>Direto ao Google, com a chave de API do usuário (Bloco 23).</summary>
internal sealed class AcessoPorChave : AcessoAoGemini
{
    private readonly string _chave;

    public AcessoPorChave(string chave)
    {
        ChamadaGemini.ValidarChave(chave);
        _chave = chave;
    }

    internal override Uri Endereco => ProvedorGemini.Endereco;

    internal override string Destino => "o Gemini";

    internal override Task AutenticarAsync(HttpRequestMessage pedido, bool renovar, CancellationToken cancelamento)
    {
        pedido.Headers.Remove("x-goog-api-key");
        pedido.Headers.Add("x-goog-api-key", _chave);
        return Task.CompletedTask;
    }

    internal override ErroDoAssistenteException Traduzir(HttpStatusCode status, string corpo, string modelo) =>
        ChamadaGemini.Traduzir(status, corpo, _chave, modelo);
}

/// <summary>
/// Bloco 45: pelo servidor do ProjectPlanner, com a conta do usuário (<c>Authorization: Bearer</c>). O servidor usa a chave
/// dele, confere a cota do plano e responde os erros como <c>{"erro": "mensagem"}</c>.
/// </summary>
public sealed class AcessoPelaConta : AcessoAoGemini
{
    private readonly ISessaoDaConta _sessao;

    /// <param name="servidor">O endereço do servidor, como <c>https://projectplanner-servidor.onrender.com/</c>.</param>
    public AcessoPelaConta(Uri servidor, ISessaoDaConta sessao)
    {
        ArgumentNullException.ThrowIfNull(servidor);
        ArgumentNullException.ThrowIfNull(sessao);
        Endereco = new Uri(ClienteDaConta.ComBarra(servidor), "v1/ia/");
        _sessao = sessao;
    }

    internal override Uri Endereco { get; }

    internal override string Destino => "o servidor do ProjectPlanner";

    internal override async Task AutenticarAsync(HttpRequestMessage pedido, bool renovar, CancellationToken cancelamento) =>
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _sessao.AcessoAsync(renovar, cancelamento));

    internal override bool RenovarDepoisDe(HttpStatusCode status) => status == HttpStatusCode.Unauthorized;

    internal override ErroDoAssistenteException Traduzir(HttpStatusCode status, string corpo, string modelo)
    {
        var codigo = (int)status;
        // Um 400 que o servidor repassou do Gemini vem no formato do Google; os do servidor, como {"erro": "..."}.
        if (ClienteDaConta.LerErro(corpo) is { } mensagem)
            return new ErroDoAssistenteException(codigo == 401
                ? "A sessão da sua conta ProjectPlanner acabou. Entre de novo (menu Conta)."
                : mensagem);
        if (codigo == 400)
            return ChamadaGemini.Traduzir(status, corpo, "", modelo);
        return new ErroDoAssistenteException(codigo switch
        {
            401 => "A sessão da sua conta ProjectPlanner acabou. Entre de novo (menu Conta).",
            404 => "O servidor do ProjectPlanner não reconheceu o pedido. Atualize o programa.",
            >= 500 => $"O servidor do ProjectPlanner está com problemas no momento (erro {codigo}). Tente de novo em instantes.",
            _ => $"O servidor do ProjectPlanner respondeu com erro {codigo}.",
        });
    }
}
