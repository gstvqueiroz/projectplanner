using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

/// <summary>Bloco 45: o servidor de contas nos testes do programa: responde cada rota como o de verdade, sem rede.</summary>
public sealed class ServidorDeContasFalso : HttpMessageHandler
{
    public List<(string Rota, string Corpo)> Pedidos { get; } = [];
    public Func<string, string, (HttpStatusCode, string)?>? Responder { get; set; }
    public bool Fora { get; set; }
    public string? ClienteDoGoogle { get; set; }
    private int _sessoes;

    public static string Sessao(int numero, DateTime expira, string plano = "gratis") => JsonSerializer.Serialize(new
    {
        acesso = $"acesso-{numero}", acessoExpiraEm = expira, renovacao = $"renovacao-{numero}", renovacaoExpiraEm = expira.AddDays(30),
        usuario = new { email = "ana@exemplo.com", nome = "Ana", plano, temSenha = true, google = false },
    });

    public DateTime Agora { get; set; } = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        var rota = $"{pedido.Method} {pedido.RequestUri!.AbsolutePath}";
        var corpo = pedido.Content is null ? "" : await pedido.Content.ReadAsStringAsync(cancelamento);
        Pedidos.Add((rota, corpo + (pedido.Headers.Authorization is { } a ? $" [{a.Parameter}]" : "")));
        if (Fora)
            throw new HttpRequestException("sem rede");
        var (status, resposta) = Responder?.Invoke(rota, corpo) ?? Padrao(rota, corpo);
        return new HttpResponseMessage(status) { Content = new StringContent(resposta, Encoding.UTF8, "application/json") };
    }

    private (HttpStatusCode, string) Padrao(string rota, string corpo) => rota switch
    {
        "POST /v1/sessoes" when corpo.Contains("senha-boa-123") => (HttpStatusCode.OK, Sessao(++_sessoes, Agora.AddMinutes(15))),
        "POST /v1/sessoes" => (HttpStatusCode.Unauthorized, """{"erro":"E-mail ou senha incorretos."}"""),
        "POST /v1/sessoes/renovar" => (HttpStatusCode.OK, Sessao(++_sessoes, Agora.AddMinutes(15))),
        "POST /v1/sessoes/sair" => (HttpStatusCode.NoContent, ""),
        "POST /v1/sessoes/google/codigo" => (HttpStatusCode.OK, Sessao(++_sessoes, Agora.AddMinutes(15))),
        "GET /v1/google" when ClienteDoGoogle is { } id => (HttpStatusCode.OK, $$"""{"clientId":"{{id}}"}"""),
        "GET /v1/google" => (HttpStatusCode.NotFound, """{"erro":"O login com Google ainda não está disponível. Use e-mail e senha."}"""),
        "GET /v1/eu" => (HttpStatusCode.OK,
            """{"perfil":{"email":"ana@exemplo.com","nome":"Ana","plano":"profissional","temSenha":true,"google":false},"tokensUsadosNoMes":1200,"creditosUsadosNoMes":250.5,"creditosDoPlano":6000}"""),
        _ => (HttpStatusCode.NotFound, """{"erro":"rota desconhecida"}"""),
    };
}

/// <summary>Bloco 45: um relógio que os testes fazem andar (o acesso da conta vence).</summary>
public sealed class RelogioQueAnda(DateTimeOffset inicio) : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = inicio;
    public override DateTimeOffset GetUtcNow() => Agora;
}

/// <summary>Bloco 45: um cofre que só falha ao gravar (para a sessão que não deu para guardar).</summary>
public sealed class CofreQueNaoGrava(CofreEmMemoria cofre) : ICofreDeSegredos
{
    public string Nome => cofre.Nome;
    public bool Disponivel => true;
    public string? Ler(string nome) => cofre.Ler(nome);
    public void Gravar(string nome, string segredo) => throw new CofreException("O cofre está bloqueado.");
    public void Remover(string nome) => cofre.Remover(nome);
}

// Bloco 45: a conta no programa. A sessão (o token de renovação e o perfil) fica no cofre; o acesso, só na memória.
public sealed class ContaProjectPlannerTests
{
    private readonly ServidorDeContasFalso _servidor = new();
    private readonly CofreEmMemoria _cofre = new();
    private readonly RelogioQueAnda _relogio = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly List<Uri> _abertos = [];
    private int _mudancas;

    private ContaProjectPlanner Nova(ICofreDeSegredos? cofre = null, Func<Uri, Task>? navegador = null)
    {
        var conta = new ContaProjectPlanner(new ClienteDaConta(new HttpClient(_servidor), new Uri("https://servidor.teste")), cofre ?? _cofre, _relogio,
            navegador ?? (u =>
            {
                _abertos.Add(u);
                return Task.CompletedTask;
            }));
        conta.Mudou += (_, _) => _mudancas++;
        return conta;
    }

    private string? Guardada => _cofre.Segredos.GetValueOrDefault(ContaProjectPlanner.NomeNoCofre);

    [Fact]
    public async Task Entrar_GuardaARenovacaoNoCofre_ENaoOAcesso_EOutraAberturaJaEstaConectada()
    {
        var conta = Nova();
        (conta.Conectada, conta.Perfil).Should().Be((false, null));

        await conta.EntrarAsync(" ana@exemplo.com ", "senha-boa-123", default);

        (conta.Conectada, conta.Perfil!.Nome, _mudancas).Should().Be((true, "Ana", 1));
        Guardada.Should().Contain("renovacao-1").And.NotContain("acesso-1");
        _servidor.Pedidos.Single().Corpo.Should().Contain("\"email\":\"ana@exemplo.com\"", "o e-mail vai sem os espaços");
        (await conta.AcessoAsync(false, default)).Should().Be("acesso-1", "o acesso da memória vale por 15 min");

        var reaberta = Nova();
        (reaberta.Conectada, reaberta.Perfil!.Email).Should().Be((true, "ana@exemplo.com"));
        (await reaberta.AcessoAsync(false, default)).Should().Be("acesso-2", "sem acesso na memória, renova com o do cofre");
        _servidor.Pedidos[^1].Corpo.Should().Contain("renovacao-1");
        Guardada.Should().Contain("renovacao-2", "a renovação troca o token, e o novo substitui o velho no cofre");
    }

    [Fact]
    public async Task Acesso_RenovaPertoDeVencerOuQuandoPedem()
    {
        var conta = Nova();
        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);

        (await conta.AcessoAsync(true, default)).Should().Be("acesso-2", "o servidor recusou o anterior");
        _relogio.Agora = _relogio.Agora.AddMinutes(14.5);
        (await conta.AcessoAsync(false, default)).Should().Be("acesso-3", "faltando menos de 1 minuto, renova antes");
    }

    [Fact]
    public async Task Renovacao_Recusada_SaiDaConta_EAvisaParaEntrarDeNovo()
    {
        var conta = Nova();
        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);
        _servidor.Responder = (rota, _) => rota == "POST /v1/sessoes/renovar" ? (HttpStatusCode.Unauthorized, """{"erro":"Sessão vencida: entre de novo."}""") : null;

        var erro = await FluentActions.Invoking(() => conta.AcessoAsync(true, default)).Should().ThrowAsync<ErroDoAssistenteException>();

        erro.Which.Message.Should().Be(ContaProjectPlanner.SessaoAcabou);
        (conta.Conectada, Guardada, _mudancas).Should().Be((false, null, 2));
        (await FluentActions.Invoking(() => conta.AcessoAsync(false, default)).Should().ThrowAsync<ErroDoAssistenteException>())
            .Which.Message.Should().Be(ContaProjectPlanner.SemConta);
    }

    [Fact]
    public async Task Renovacao_SemInternet_NaoSaiDaConta()
    {
        var conta = Nova();
        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);
        _servidor.Fora = true;

        (await FluentActions.Invoking(() => conta.AcessoAsync(true, default)).Should().ThrowAsync<ErroDoAssistenteException>())
            .Which.Message.Should().StartWith("Sem conexão com o servidor do ProjectPlanner");
        conta.Conectada.Should().BeTrue("sem internet, a sessão continua guardada");
    }

    [Fact]
    public async Task Entrar_Errado_MensagemDoServidor_ENadaGuardado()
    {
        var conta = Nova();

        (await FluentActions.Invoking(() => conta.EntrarAsync("ana@exemplo.com", "errada", default)).Should().ThrowAsync<ErroDaContaException>())
            .Which.Message.Should().Be("E-mail ou senha incorretos.");
        (conta.Conectada, Guardada, _mudancas).Should().Be((false, null, 0));
    }

    [Fact]
    public async Task Sair_AvisaOServidor_EApagaDoCofre_MesmoSemInternet()
    {
        var conta = Nova();
        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);
        await conta.SairAsync(default);
        (_servidor.Pedidos[^1].Rota, conta.Conectada, Guardada).Should().Be(("POST /v1/sessoes/sair", false, null));

        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);
        _servidor.Fora = true;
        await conta.SairAsync(default);
        (conta.Conectada, Guardada).Should().Be((false, null));
        await conta.SairAsync(default); // sem conta: nada a fazer
    }

    [Fact]
    public async Task Uso_AtualizaOPlanoDoPerfil()
    {
        var conta = Nova();
        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);

        var uso = await conta.UsoAsync(default);

        (uso.TokensUsadosNoMes, uso.CreditosUsadosNoMes, uso.CreditosDoPlano, conta.Perfil!.Plano, conta.CreditosRestantes, _mudancas)
            .Should().Be((1200L, 250.5m, 6000L, "profissional", 5749.5m, 3), "entrou, mudou o plano e chegou o saldo");
        Guardada.Should().Contain("profissional");
        _servidor.Pedidos[^1].Corpo.Should().EndWith("[acesso-1]");
    }

    [Fact]
    public async Task Uso_AcessoRecusado_RenovaETentaDeNovo()
    {
        var conta = Nova();
        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);
        var primeira = true;
        _servidor.Responder = (rota, _) => rota == "GET /v1/eu" && primeira ? (primeira = false) is false ? (HttpStatusCode.Unauthorized, "{}") : null : null;

        (await conta.UsoAsync(default)).TokensUsadosNoMes.Should().Be(1200);
        _servidor.Pedidos.Select(p => p.Rota).Should().Equal("POST /v1/sessoes", "GET /v1/eu", "POST /v1/sessoes/renovar", "GET /v1/eu");
    }

    [Fact]
    public async Task Cofre_QueNaoGrava_SessaoValeAteFechar_ECofreIlegivel_ValeComoSemConta()
    {
        var conta = Nova(new CofreQueNaoGrava(_cofre));
        await conta.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);
        (conta.Conectada, conta.ProblemaNoCofre).Should().Be((true, "O cofre está bloqueado."));

        _cofre.Segredos[ContaProjectPlanner.NomeNoCofre] = "isto não é json";
        Nova().Conectada.Should().BeFalse();
        _cofre.Falha = new CofreException("Acesso negado.");
        var bloqueado = Nova();
        (bloqueado.Conectada, bloqueado.ProblemaNoCofre).Should().Be((false, "Acesso negado."));
    }

    [Fact]
    public void Servidor_PelaVariavel_OuOPublicado()
    {
        ContaProjectPlanner.Servidor(_ => "http://localhost:5199").Should().Be(new Uri("http://localhost:5199/"));
        ContaProjectPlanner.Servidor(_ => null).Should().Be(ClienteDaConta.ServidorPadrao);
        ContaProjectPlanner.Servidor(_ => "ftp://x").Should().Be(ClienteDaConta.ServidorPadrao);
        ContaProjectPlanner.Servidor(_ => "não é endereço").Should().Be(ClienteDaConta.ServidorPadrao);
        ClienteDaConta.ServidorPadrao.Should().Be(new Uri("https://projectplanner-servidor.onrender.com/"));
    }

    [Fact]
    public async Task Google_SemGoogleNoServidor_AvisaSemAbrirONavegador()
    {
        var conta = Nova();

        (await FluentActions.Invoking(() => conta.EntrarComGoogleAsync(default)).Should().ThrowAsync<ErroDaContaException>())
            .Which.Message.Should().Be("O login com Google ainda não está disponível. Use e-mail e senha.");
        _abertos.Should().BeEmpty();
    }

    [Fact]
    public async Task Google_AbreONavegador_RecebeOCodigoNaVolta_EOServidorTrocaPelaSessao()
    {
        _servidor.ClienteDoGoogle = "app-123.apps.googleusercontent.com";
        using var navegador = new HttpClient();
        Uri? login = null;
        var conta = Nova(navegador: u =>
        {
            login = u;
            var q = HttpUtility.ParseQueryString(u.Query);
            // O "Google" volta ao programa: primeiro o ícone da aba (ignorado), depois o código.
            _ = Task.Run(async () =>
            {
                await navegador.GetAsync(new Uri(new Uri(q["redirect_uri"]!), "favicon.ico"));
                await navegador.GetAsync($"{q["redirect_uri"]}?code=codigo-do-google&state={Uri.EscapeDataString(q["state"]!)}&scope=email");
            });
            return Task.CompletedTask;
        });

        await conta.EntrarComGoogleAsync(default);

        conta.Conectada.Should().BeTrue();
        var consulta = HttpUtility.ParseQueryString(login!.Query);
        (login.GetLeftPart(UriPartial.Path), consulta["client_id"], consulta["response_type"], consulta["scope"], consulta["code_challenge_method"])
            .Should().Be(("https://accounts.google.com/o/oauth2/v2/auth", "app-123.apps.googleusercontent.com", "code", "openid email profile", "S256"));
        consulta["redirect_uri"].Should().MatchRegex(@"^http://127\.0\.0\.1:\d+/$");
        var troca = JsonDocument.Parse(_servidor.Pedidos.Single(p => p.Rota == "POST /v1/sessoes/google/codigo").Corpo).RootElement;
        (troca.GetProperty("codigo").GetString(), troca.GetProperty("redirecionamento").GetString()).Should().Be(("codigo-do-google", consulta["redirect_uri"]));
        var desafio = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(troca.GetProperty("verificador").GetString()!)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        consulta["code_challenge"].Should().Be(desafio, "o verificador que vai ao servidor é o do desafio que foi ao Google");
    }

    [Fact]
    public async Task Google_CanceladoNoGoogle_OuNoPrograma()
    {
        _servidor.ClienteDoGoogle = "app-123";
        using var navegador = new HttpClient();
        var conta = Nova(navegador: u =>
        {
            var q = HttpUtility.ParseQueryString(u.Query);
            _ = navegador.GetAsync($"{q["redirect_uri"]}?error=access_denied&state={Uri.EscapeDataString(q["state"]!)}");
            return Task.CompletedTask;
        });
        (await FluentActions.Invoking(() => conta.EntrarComGoogleAsync(default)).Should().ThrowAsync<ErroDaContaException>())
            .Which.Message.Should().Be("Login com Google cancelado.");

        using var cancelar = new CancellationTokenSource();
        var esperando = Nova(navegador: _ =>
        {
            cancelar.CancelAfter(200);
            return Task.CompletedTask;
        });
        await FluentActions.Invoking(() => esperando.EntrarComGoogleAsync(cancelar.Token)).Should().ThrowAsync<OperationCanceledException>();
        esperando.Conectada.Should().BeFalse();
    }
}
