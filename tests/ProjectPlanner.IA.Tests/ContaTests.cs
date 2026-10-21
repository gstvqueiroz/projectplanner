using System.Net;
using ProjectPlanner.IA.Conta;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 45: a IA pela conta ProjectPlanner (o mesmo corpo do Gemini, pelo servidor, com o token) e o cliente das rotas de conta.
public sealed class ContaTests
{
    private readonly ManipuladorFalso _servidor = new();

    private sealed class SessaoFixa : ISessaoDaConta
    {
        public List<bool> Pedidos { get; } = [];
        public List<decimal> Saldos { get; } = [];
        public void AtualizarSaldo(decimal creditosRestantes) => Saldos.Add(creditosRestantes);
        public Task<string> AcessoAsync(bool renovar, CancellationToken cancelamento)
        {
            Pedidos.Add(renovar);
            return Task.FromResult(renovar ? "acesso-novo" : "acesso-velho");
        }
    }

    private AssistenteGemini Assistente(SessaoFixa sessao) =>
        new(new HttpClient(_servidor), new AcessoPelaConta(new Uri("https://servidor.teste/base"), sessao), "gemini-2.5-flash");

    [Fact]
    public async Task PelaConta_MesmoCorpo_ComOToken_ESemAChaveDoGoogle()
    {
        var sessao = new SessaoFixa();

        var resposta = await Assistente(sessao).EnviarAsync(new PedidoIA("Oi", [], []), default);

        resposta.Texto.Should().Be("Olá!");
        var (pedido, corpo) = _servidor.Pedidos.Single();
        pedido.RequestUri.Should().Be(new Uri("https://servidor.teste/base/v1/ia/models/gemini-2.5-flash:generateContent"));
        (pedido.Headers.Authorization!.Scheme, pedido.Headers.Authorization.Parameter, pedido.Headers.Contains("x-goog-api-key"))
            .Should().Be(("Bearer", "acesso-velho", false));
        corpo.Should().Contain("\"contents\"");
        sessao.Pedidos.Should().Equal(false);
    }

    [Fact]
    public async Task PelaConta_CadaPedidoDizARotina_PelaChaveNao()
    {
        // Bloco 50: o servidor guarda a rotina de cada pedido, para o painel mostrar quanto custa cada uma.
        var sessao = new SessaoFixa();
        await Assistente(sessao).EnviarAsync(new PedidoIA("Oi", [], []), default);
        await Assistente(sessao).EnviarAsync(new PedidoIA("Oi", [], []) { Canal = CanaisDaConversa.Telegram }, default);
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("""{"legendas":[]}""")));
        var fotos = new PedidoDeLegendas("Casa", [new FotoParaIA("f1", [1, 2, 3], "Alvenaria", new DateOnly(2026, 10, 21))]);
        await new AnalistaGemini(new HttpClient(_servidor), new AcessoPelaConta(new Uri("https://servidor.teste/"), sessao), "m")
            .SugerirLegendasAsync(fotos, default);
        await new AnalistaGemini(new HttpClient(_servidor), "AIzaChaveDeTesteSoParaOsTestes123456789", "m").SugerirLegendasAsync(fotos, default);

        _servidor.Pedidos.Select(p => p.Pedido.Headers.TryGetValues(RotinasDeIa.Cabecalho, out var v) ? v.Single() : null)
            .Should().Equal(RotinasDeIa.Chat, RotinasDeIa.Telegram, RotinasDeIa.Legendas, null);
        (RotinasDeIa.Normalizar(" Classificar-Fotos "), RotinasDeIa.Normalizar("nova-rotina"), RotinasDeIa.Normalizar(null))
            .Should().Be((RotinasDeIa.ClassificarFotos, RotinasDeIa.Outra, RotinasDeIa.Outra));
        RotinasDeIa.Todas.Select(RotinasDeIa.Nome).Should().OnlyHaveUniqueItems().And.NotContain("Outras", "toda rotina conhecida tem nome próprio");
    }

    [Fact]
    public async Task PelaConta_OSaldoDoCabecalhoVaiParaASessao()
    {
        var sessao = new SessaoFixa();
        _servidor.Responder = (_, _) =>
        {
            var resposta = ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("Olá"));
            resposta.Headers.Add(AcessoPelaConta.CabecalhoDoSaldo, "5870.5");
            return Task.FromResult(resposta);
        };
        await Assistente(sessao).EnviarAsync(new PedidoIA("Oi", [], []), default);
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("Sem saldo no cabeçalho")));
        await Assistente(sessao).EnviarAsync(new PedidoIA("Oi", [], []), default);

        sessao.Saldos.Should().Equal(5870.5m);
    }

    [Fact]
    public async Task PelaConta_401_RenovaUmaVez_EDepoisDesiste()
    {
        var sessao = new SessaoFixa();
        var vezes = 0;
        _servidor.Responder = (_, _) => Task.FromResult(++vezes == 1
            ? ManipuladorFalso.Json(401, """{"erro":"Entre na sua conta"}""")
            : ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("Olá de novo")));
        (await Assistente(sessao).EnviarAsync(new PedidoIA("Oi", [], []), default)).Texto.Should().Be("Olá de novo");
        (sessao.Pedidos, _servidor.Pedidos[^1].Pedido.Headers.Authorization!.Parameter).Should().BeEquivalentTo((new[] { false, true }, "acesso-novo"));

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(401, """{"erro":"x"}"""));
        (await FluentActions.Invoking(() => Assistente(new SessaoFixa()).EnviarAsync(new PedidoIA("Oi", [], []), default))
            .Should().ThrowAsync<ErroDoAssistenteException>()).Which.Message.Should().Be("A sessão da sua conta ProjectPlanner acabou. Entre de novo (menu Conta).");
    }

    [Theory]
    [InlineData(402, """{"erro":"A cota de IA do seu plano acabou neste mês."}""", "A cota de IA do seu plano acabou neste mês.")]
    [InlineData(503, """{"erro":"A IA está muito procurada agora."}""", "A IA está muito procurada agora.")]
    [InlineData(400, """{"error":{"code":400,"message":"Invalid JSON payload"}}""", "O Gemini recusou o pedido: Invalid JSON payload")]
    [InlineData(502, "", "O servidor do ProjectPlanner está com problemas no momento (erro 502). Tente de novo em instantes.")]
    [InlineData(404, "<html>", "O servidor do ProjectPlanner não reconheceu o pedido. Atualize o programa.")]
    [InlineData(418, "", "O servidor do ProjectPlanner respondeu com erro 418.")]
    public async Task PelaConta_ErrosDoServidor(int status, string corpo, string mensagem)
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(status, corpo));

        (await FluentActions.Invoking(() => Assistente(new SessaoFixa()).EnviarAsync(new PedidoIA("Oi", [], []), default))
            .Should().ThrowAsync<ErroDoAssistenteException>()).Which.Message.Should().Be(mensagem);
    }

    [Fact]
    public async Task PelaConta_SemConexao_FalaDoServidor()
    {
        _servidor.Responder = (_, _) => throw new HttpRequestException("sem rede");

        (await FluentActions.Invoking(() => Assistente(new SessaoFixa()).EnviarAsync(new PedidoIA("Oi", [], []), default))
            .Should().ThrowAsync<ErroDoAssistenteException>()).Which.Message.Should().Be("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.");
    }

    [Fact]
    public async Task Cliente_FalhasSemRespostaOuComRespostaEstranha()
    {
        var cliente = new ClienteDaConta(new HttpClient(_servidor), new Uri("https://servidor.teste"));
        cliente.Servidor.Should().Be(new Uri("https://servidor.teste/"));

        _servidor.Responder = (_, _) => throw new HttpRequestException("sem rede");
        var semRede = await FluentActions.Invoking(() => cliente.EntrarAsync("a@b.com", "x", default)).Should().ThrowAsync<ErroDaContaException>();
        (semRede.Which.Message, semRede.Which.Status, semRede.Which.SessaoRecusada)
            .Should().Be(("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.", 0, false));

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, "não é json"));
        (await FluentActions.Invoking(() => cliente.EntrarAsync("a@b.com", "x", default)).Should().ThrowAsync<ErroDaContaException>())
            .Which.Message.Should().Be("O servidor do ProjectPlanner mandou uma resposta que não deu para entender. Atualize o programa.");

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(429, ""));
        (await FluentActions.Invoking(() => cliente.EsqueciASenhaAsync("a@b.com", default)).Should().ThrowAsync<ErroDaContaException>())
            .Which.Message.Should().Be("Muitos pedidos seguidos. Espere um minuto e tente de novo.");

        _servidor.Responder = (_, c) => Task.Delay(Timeout.Infinite, c).ContinueWith(_ => ManipuladorFalso.Json(200, "{}"), c);
        using var cancelar = new CancellationTokenSource(50);
        await FluentActions.Invoking(() => cliente.EntrarAsync("a@b.com", "x", cancelar.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("""{"erro":"Mensagem"}""", "Mensagem")]
    [InlineData("""{"erro":""}""", null)]
    [InlineData("""{"error":{"message":"x"}}""", null)]
    [InlineData("[1]", null)]
    [InlineData("texto", null)]
    public void LerErro(string corpo, string? esperado) => ClienteDaConta.LerErro(corpo).Should().Be(esperado);
}
