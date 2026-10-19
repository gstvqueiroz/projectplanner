using System.Net;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 45, de ponta a ponta: o cliente da conta e as classes do Gemini do programa, falando com o servidor de verdade
// (em memória). Confere que o caminho espelhado funciona: o programa monta o pedido como para o Google e o servidor repassa.
public sealed class ProgramaComOServidorTests : IDisposable
{
    private readonly Servidor _s = new();
    private readonly ClienteDaConta _cliente;
    private readonly HttpClient _http;

    public ProgramaComOServidorTests()
    {
        _http = _s.CreateClient();
        _cliente = new ClienteDaConta(_http, _http.BaseAddress!);
    }

    public void Dispose()
    {
        _http.Dispose();
        _s.Dispose();
    }

    /// <summary>A sessão como o programa guarda: renova quando pedem (e conta quantas vezes).</summary>
    private sealed class SessaoDeTeste(ClienteDaConta cliente, SessaoDaConta inicial) : ISessaoDaConta
    {
        public SessaoDaConta Atual { get; private set; } = inicial;
        public int Renovacoes { get; private set; }
        public string? AcessoForcado { get; set; }

        public async Task<string> AcessoAsync(bool renovar, CancellationToken cancelamento)
        {
            if (renovar)
            {
                Atual = await cliente.RenovarAsync(Atual.Renovacao, cancelamento);
                Renovacoes++;
                AcessoForcado = null;
            }
            return AcessoForcado ?? Atual.Acesso;
        }
    }

    private async Task<SessaoDaConta> CriarConta()
    {
        (await _cliente.CadastrarAsync("ana@exemplo.com", "senha-boa-123", "Ana", default)).Should().Be(Rotas.Contas.MensagemDoCadastro);
        return await _cliente.ConfirmarAsync("ana@exemplo.com", _s.Emails.Ultimo.Codigo!, "senha-boa-123", default);
    }

    [Fact]
    public async Task Conta_CadastroLoginUsoRenovacaoESaida()
    {
        var sessao = await CriarConta();
        sessao.Usuario.Should().Be(new PerfilDaConta("ana@exemplo.com", "Ana", "gratis", true, false));

        var login = await _cliente.EntrarAsync("ana@exemplo.com", "senha-boa-123", default);
        var uso = await _cliente.UsoAsync(login.Acesso, default);
        uso.Should().Be(new UsoDaConta(login.Usuario, 0, 300_000));

        var renovada = await _cliente.RenovarAsync(login.Renovacao, default);
        renovada.Renovacao.Should().NotBe(login.Renovacao);
        await _cliente.SairAsync(renovada.Renovacao, default);
        var erro = await FluentActions.Invoking(() => _cliente.RenovarAsync(renovada.Renovacao, default)).Should().ThrowAsync<ErroDaContaException>();
        (erro.Which.Message, erro.Which.SessaoRecusada).Should().Be(("Sessão vencida: entre de novo.", true));

        (await _cliente.EsqueciASenhaAsync("ana@exemplo.com", default)).Should().Be(Rotas.Contas.MensagemDaSenha);
        var nova = await _cliente.RedefinirSenhaAsync("ana@exemplo.com", _s.Emails.Ultimo.Codigo!, "senha-nova-456", default);
        nova.Usuario.Email.Should().Be("ana@exemplo.com");
        (await _cliente.ReenviarCodigoAsync("ana@exemplo.com", default)).Should().Be(Rotas.Contas.MensagemDoCadastro);
        (await _cliente.ClienteDoGoogleAsync(default)).Should().BeNull("o servidor dos testes não tem o Google do app");
    }

    [Fact]
    public async Task Conta_ErrosComAMensagemDoServidor()
    {
        var curta = await FluentActions.Invoking(() => _cliente.CadastrarAsync("ana@exemplo.com", "curta", "Ana", default))
            .Should().ThrowAsync<ErroDaContaException>();
        (curta.Which.Message, curta.Which.Status).Should().Be(("A senha precisa ter pelo menos 10 caracteres.", 400));
        var login = await FluentActions.Invoking(() => _cliente.EntrarAsync("ninguem@exemplo.com", "qualquer-coisa", default))
            .Should().ThrowAsync<ErroDaContaException>();
        login.Which.Message.Should().Be(Rotas.Contas.LoginInvalido);
    }

    [Fact]
    public async Task Assistente_PelaConta_UsaAsMesmasClassesDoGemini_ERenovaOAcessoVencido()
    {
        var sessao = new SessaoDeTeste(_cliente, await CriarConta());
        var provedor = new ProvedorDaConta(_http, _http.BaseAddress!, sessao);

        await provedor.TestarAsync(ProvedorDaConta.Credencial, "gemini-2.5-flash", default);
        _s.Gemini.Pedidos.Should().BeEmpty("testar a conexão não vai ao Gemini");

        var resposta = await provedor.CriarAssistente(ProvedorDaConta.Credencial, "gemini-2.5-flash")
            .EnviarAsync(new PedidoIA("Oi", [], []), default);
        resposta.Texto.Should().Be("Olá!");
        var enviado = _s.Gemini.Pedidos.Single();
        (enviado.Endereco.AbsolutePath, enviado.Chave).Should().Be(("/v1beta/models/gemini-2.5-flash:generateContent", Servidor.ChaveDoGemini));
        _s.NoBanco(b => b.UsosDeIa.Single().TokensTotais).Should().Be(150);

        // O acesso venceu (15 min): o programa renova sozinho e manda de novo.
        _s.Relogio.Andar(TimeSpan.FromMinutes(20));
        await provedor.TestarAsync(ProvedorDaConta.Credencial, "gemini-2.5-flash", default);
        sessao.Renovacoes.Should().Be(1);
    }

    [Fact]
    public async Task Assistente_PelaConta_ErrosDoServidorViramMensagens()
    {
        var sessao = new SessaoDeTeste(_cliente, await CriarConta());
        var provedor = new ProvedorDaConta(_http, _http.BaseAddress!, sessao);

        var modelo = await FluentActions.Invoking(() => provedor.TestarAsync(ProvedorDaConta.Credencial, "gemini-velho", default))
            .Should().ThrowAsync<ErroDoAssistenteException>();
        modelo.Which.Message.Should().StartWith("O modelo \"gemini-velho\" não está liberado.");

        (_s.Gemini.Status, _s.Gemini.Resposta) = (HttpStatusCode.TooManyRequests, "{}");
        var ocupado = await FluentActions.Invoking(() => provedor.CriarAnalistaDaObra(ProvedorDaConta.Credencial, "gemini-2.5-flash")
                .AnalisarAsync(new PedidoDeAnaliseDaObra("Obra", new DateOnly(2026, 10, 19), [], []), default))
            .Should().ThrowAsync<ErroDoAssistenteException>();
        ocupado.Which.Message.Should().Be("A IA está muito procurada agora. Espere um pouco e tente de novo.");

        // A renovação também foi recusada (a pessoa saiu em outro lugar): a mensagem manda entrar de novo.
        await _cliente.SairAsync(sessao.Atual.Renovacao, default);
        sessao.AcessoForcado = "token-invalido";
        var sessaoAcabou = await FluentActions.Invoking(() => provedor.TestarAsync(ProvedorDaConta.Credencial, "gemini-2.5-flash", default))
            .Should().ThrowAsync<ErroDaContaException>();
        sessaoAcabou.Which.SessaoRecusada.Should().BeTrue();
    }
}
