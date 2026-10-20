using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectPlanner.IA.Conta;

/// <summary>Bloco 45: quem dá o token de acesso da conta ProjectPlanner (renovando a sessão quando preciso).</summary>
public interface ISessaoDaConta
{
    /// <summary>Um token de acesso válido. <paramref name="renovar"/>: o anterior foi recusado, renove antes.</summary>
    /// <exception cref="ErroDoAssistenteException">Sem conta conectada, ou a sessão acabou (é preciso entrar de novo).</exception>
    Task<string> AcessoAsync(bool renovar, CancellationToken cancelamento);

    /// <summary>Bloco 46: o servidor disse quantos créditos restam no mês (depois de um pedido de IA).</summary>
    void AtualizarSaldo(decimal creditosRestantes)
    {
    }
}

/// <summary>O dono da conta, como o servidor descreve.</summary>
public sealed record PerfilDaConta(string Email, string Nome, string Plano, bool TemSenha, bool Google);

/// <summary>Uma sessão: o token de acesso (curto, 15 min) e o de renovação (longo, 30 dias), com o perfil.</summary>
public sealed record SessaoDaConta(string Acesso, DateTime AcessoExpiraEm, string Renovacao, DateTime RenovacaoExpiraEm, PerfilDaConta Usuario);

/// <summary>O plano e o uso de IA do mês (Bloco 46: em créditos; cada pedido gasta o custo real dele).</summary>
public sealed record UsoDaConta(PerfilDaConta Perfil, long TokensUsadosNoMes, decimal CreditosUsadosNoMes, long CreditosDoPlano)
{
    public decimal CreditosRestantes => Math.Max(0, CreditosDoPlano - CreditosUsadosNoMes);
}

/// <summary>Bloco 46: um modelo do servidor: o nível (padrao, avancado, maximo), se o plano libera e o gasto de um pedido típico.</summary>
public sealed record ModeloDaConta(string Id, string Nivel, string Nome, bool Liberado, bool Disponivel, int CreditosPorPedido)
{
    public string NomeDoNivel => Nivel switch
    {
        "avancado" => "Avançado",
        "maximo" => "Máximo",
        _ => "Padrão",
    };
}

/// <summary>Bloco 49: uma obra marcada para o Telegram e quantos itens esperam na caixa dela.</summary>
public sealed record ObraNoTelegram(Guid Projeto, string Nome, DateTime AtualizadoEm, int Pendentes);

/// <summary>Bloco 49: uma tarefa do retrato do momento em que a mensagem chegou (o Nº que a IA usou e o id).</summary>
public sealed record TarefaDoTelegram(int Numero, Guid Id);

/// <summary>Bloco 49: o que chegou pelo Telegram: atualizações (para o cartão) ou uma foto.</summary>
public sealed record ItemDoTelegram(Guid Id, string Tipo, DateTime CriadoEm, string Texto, string? NomeDaFoto,
    IReadOnlyList<AtualizacaoDaObra>? Atualizacoes, IReadOnlyList<TarefaDoTelegram>? Tarefas)
{
    public const string DeAtualizacoes = "atualizacoes";
    public const string DeFoto = "foto";
}

/// <summary>Bloco 49: se o servidor tem o Telegram e se esta conta está ligada a um chat.</summary>
public sealed record SituacaoDoTelegram(bool Disponivel, bool Ligado, string? Nome, string? Bot);

/// <summary>Bloco 49: o código para ligar o Telegram (vale alguns minutos) e o link que abre o bot com ele.</summary>
public sealed record CodigoParaOTelegram(string Codigo, string? Link, string? Bot, int Minutos);

/// <summary>Uma falha numa rota de conta, com a mensagem para o usuário. <see cref="Status"/> 0: sem resposta do servidor.</summary>
public sealed class ErroDaContaException(string mensagem, int status, Exception? causa = null) : Exception(mensagem, causa)
{
    public int Status { get; } = status;

    /// <summary>O servidor recusou a sessão (ou as credenciais): é preciso entrar de novo.</summary>
    public bool SessaoRecusada => Status == 401;
}

/// <summary>
/// Bloco 45: as rotas de conta do servidor do ProjectPlanner (Bloco 44): cadastro, código por e-mail, login com senha ou
/// com o Google, renovação, saída, troca de senha e o uso do mês. Só fala com o servidor: guardar a sessão é com quem chama.
/// </summary>
public sealed class ClienteDaConta
{
    /// <summary>O servidor publicado (ver docs/SERVIDOR.md).</summary>
    public static readonly Uri ServidorPadrao = new("https://projectplanner-servidor.onrender.com/");

    /// <summary>Quanto esperar por uma rota de conta. No plano grátis o servidor "dorme" e leva cerca de 1 minuto para acordar.</summary>
    public static readonly TimeSpan TempoLimite = TimeSpan.FromSeconds(100);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ClienteDaConta(HttpClient http, Uri servidor)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(servidor);
        (_http, Servidor) = (http, ComBarra(servidor));
    }

    public Uri Servidor { get; }

    /// <summary>O endereço com a barra no fim, para os caminhos relativos se juntarem a ele.</summary>
    public static Uri ComBarra(Uri endereco) => endereco.AbsoluteUri.EndsWith('/') ? endereco : new Uri(endereco.AbsoluteUri + "/");

    /// <returns>A mensagem do servidor (o código foi mandado por e-mail).</returns>
    public Task<string> CadastrarAsync(string email, string senha, string nome, CancellationToken cancelamento) =>
        MensagemAsync("v1/contas", new { email, senha, nome }, cancelamento);

    public Task<SessaoDaConta> ConfirmarAsync(string email, string codigo, string senha, CancellationToken cancelamento) =>
        PedirAsync<SessaoDaConta>(HttpMethod.Post, "v1/contas/confirmar", new { email, codigo, senha }, null, cancelamento);

    public Task<string> ReenviarCodigoAsync(string email, CancellationToken cancelamento) =>
        MensagemAsync("v1/contas/reenviar", new { email }, cancelamento);

    public Task<SessaoDaConta> EntrarAsync(string email, string senha, CancellationToken cancelamento) =>
        PedirAsync<SessaoDaConta>(HttpMethod.Post, "v1/sessoes", new { email, senha }, null, cancelamento);

    /// <summary>O client ID do Google do app (para abrir o login no navegador); nulo se o servidor não tem o Google configurado.</summary>
    public async Task<string?> ClienteDoGoogleAsync(CancellationToken cancelamento)
    {
        try
        {
            var resposta = await PedirAsync<JsonElement>(HttpMethod.Get, "v1/google", null, null, cancelamento);
            return resposta.TryGetProperty("clientId", out var id) && id.GetString() is { Length: > 0 } texto ? texto : null;
        }
        catch (ErroDaContaException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Troca o código que o Google mandou ao navegador por uma sessão (o servidor tem o segredo do Google).</summary>
    public Task<SessaoDaConta> EntrarComGoogleAsync(string codigo, string verificador, Uri redirecionamento, CancellationToken cancelamento) =>
        PedirAsync<SessaoDaConta>(HttpMethod.Post, "v1/sessoes/google/codigo",
            new { codigo, verificador, redirecionamento = redirecionamento.AbsoluteUri }, null, cancelamento);

    public Task<SessaoDaConta> RenovarAsync(string renovacao, CancellationToken cancelamento) =>
        PedirAsync<SessaoDaConta>(HttpMethod.Post, "v1/sessoes/renovar", new { renovacao }, null, cancelamento);

    public async Task SairAsync(string renovacao, CancellationToken cancelamento) =>
        await PedirAsync<JsonElement?>(HttpMethod.Post, "v1/sessoes/sair", new { renovacao }, null, cancelamento);

    public Task<string> EsqueciASenhaAsync(string email, CancellationToken cancelamento) =>
        MensagemAsync("v1/senha/esqueci", new { email }, cancelamento);

    public Task<SessaoDaConta> RedefinirSenhaAsync(string email, string codigo, string novaSenha, CancellationToken cancelamento) =>
        PedirAsync<SessaoDaConta>(HttpMethod.Post, "v1/senha/redefinir", new { email, codigo, novaSenha }, null, cancelamento);

    public Task<UsoDaConta> UsoAsync(string acesso, CancellationToken cancelamento) =>
        PedirAsync<UsoDaConta>(HttpMethod.Get, "v1/eu", null, acesso, cancelamento);

    // ---- Bloco 49: o Telegram ----

    public Task<SituacaoDoTelegram> TelegramAsync(string acesso, CancellationToken c) =>
        PedirAsync<SituacaoDoTelegram>(HttpMethod.Get, "v1/telegram", null, acesso, c);

    public Task<CodigoParaOTelegram> CodigoDoTelegramAsync(string acesso, CancellationToken c) =>
        PedirAsync<CodigoParaOTelegram>(HttpMethod.Post, "v1/telegram/codigo", null, acesso, c);

    public async Task DesligarTelegramAsync(string acesso, CancellationToken c) =>
        await PedirAsync<JsonElement?>(HttpMethod.Delete, "v1/telegram", null, acesso, c);

    public async Task<IReadOnlyList<ObraNoTelegram>> ObrasAsync(string acesso, CancellationToken c) =>
        await PedirAsync<List<ObraNoTelegram>>(HttpMethod.Get, "v1/obras", null, acesso, c);

    /// <summary>Marca a obra (ou atualiza o retrato dela) para o Telegram.</summary>
    public async Task MarcarObraAsync(string acesso, Guid projeto, string nome, ContextoDoProjeto retrato, CancellationToken c) =>
        await PedirAsync<JsonElement>(HttpMethod.Put, $"v1/obras/{projeto}", new { nome, retrato }, acesso, c);

    public async Task DesmarcarObraAsync(string acesso, Guid projeto, CancellationToken c) =>
        await PedirAsync<JsonElement?>(HttpMethod.Delete, $"v1/obras/{projeto}", null, acesso, c);

    public async Task<IReadOnlyList<ItemDoTelegram>> CaixaAsync(string acesso, Guid projeto, CancellationToken c) =>
        await PedirAsync<List<ItemDoTelegram>>(HttpMethod.Get, $"v1/obras/{projeto}/caixa", null, acesso, c);

    public async Task<byte[]> FotoDaCaixaAsync(string acesso, Guid item, CancellationToken c)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, new Uri(Servidor, $"v1/caixa/{item}/foto"));
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        using var resposta = await _http.SendAsync(pedido, c);
        if (!resposta.IsSuccessStatusCode)
            throw new ErroDaContaException(LerErro(await resposta.Content.ReadAsStringAsync(c)) ?? "Não deu para baixar a foto do Telegram.", (int)resposta.StatusCode);
        return await resposta.Content.ReadAsByteArrayAsync(c);
    }

    /// <summary>O programa levou o item: ele some do servidor.</summary>
    public async Task LevarDaCaixaAsync(string acesso, Guid item, CancellationToken c) =>
        await PedirAsync<JsonElement?>(HttpMethod.Delete, $"v1/caixa/{item}", null, acesso, c);

    /// <summary>Bloco 46: os modelos de cada nível para esta conta.</summary>
    public async Task<IReadOnlyList<ModeloDaConta>> ModelosAsync(string acesso, CancellationToken cancelamento) =>
        await PedirAsync<List<ModeloDaConta>>(HttpMethod.Get, "v1/modelos", null, acesso, cancelamento);

    private async Task<string> MensagemAsync(string caminho, object corpo, CancellationToken cancelamento)
    {
        var resposta = await PedirAsync<JsonElement>(HttpMethod.Post, caminho, corpo, null, cancelamento);
        return resposta.TryGetProperty("mensagem", out var m) && m.GetString() is { } texto ? texto : "";
    }

    private async Task<T> PedirAsync<T>(HttpMethod metodo, string caminho, object? corpo, string? acesso, CancellationToken cancelamento)
    {
        using var pedido = new HttpRequestMessage(metodo, new Uri(Servidor, caminho));
        if (corpo is not null)
            pedido.Content = JsonContent.Create(corpo, options: Json);
        if (acesso is not null)
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso);
        using var tempo = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        tempo.CancelAfter(TempoLimite);
        HttpResponseMessage resposta;
        try
        {
            resposta = await _http.SendAsync(pedido, tempo.Token);
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new ErroDaContaException("O servidor do ProjectPlanner demorou demais para responder. Tente de novo em instantes.", 0, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ErroDaContaException("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.", 0, ex);
        }
        using (resposta)
        {
            var texto = await resposta.Content.ReadAsStringAsync(cancelamento);
            var status = (int)resposta.StatusCode;
            if (!resposta.IsSuccessStatusCode)
                throw new ErroDaContaException(LerErro(texto) ?? status switch
                {
                    401 => "Sessão vencida: entre de novo.",
                    404 => "O servidor do ProjectPlanner não reconheceu o pedido. Atualize o programa.",
                    429 => "Muitos pedidos seguidos. Espere um minuto e tente de novo.",
                    >= 500 => $"O servidor do ProjectPlanner está com problemas no momento (erro {status}). Tente de novo em instantes.",
                    _ => $"O servidor do ProjectPlanner respondeu com erro {status}.",
                }, status);
            if (resposta.StatusCode == HttpStatusCode.NoContent || texto.Length == 0)
                return default!;
            try
            {
                return JsonSerializer.Deserialize<T>(texto, Json) ?? throw new JsonException();
            }
            catch (JsonException ex)
            {
                throw new ErroDaContaException("O servidor do ProjectPlanner mandou uma resposta que não deu para entender. Atualize o programa.", status, ex);
            }
        }
    }

    /// <summary>A mensagem de um erro do servidor (<c>{"erro": "..."}</c>); nula se o corpo não tem esse formato.</summary>
    public static string? LerErro(string corpo)
    {
        try
        {
            using var json = JsonDocument.Parse(corpo);
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("erro", out var e)
                   && e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } texto
                ? texto
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
