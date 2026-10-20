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
        uso.Should().Be(new UsoDaConta(login.Usuario, 0, 0, 300));
        uso.CreditosRestantes.Should().Be(300);
        (await _cliente.ModelosAsync(login.Acesso, default)).Select(m => (m.Id, m.Nivel, m.Liberado, m.CreditosPorPedido)).Should().Equal(
            ("gemini-2.5-flash", "padrao", true, 14), ("gemini-3.1-pro-preview", "avancado", false, 76), ("claude-opus-5-5", "maximo", false, 140));

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
        modelo.Which.Message.Should().StartWith("O modelo \"gemini-velho\" não está disponível.");

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

    [Fact]
    public async Task Telegram_OProgramaMarcaAObra_EOQueChegaPeloBotVoltaComOsMesmosTipos()
    {
        var sessao = await CriarConta();
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", "profissional", Servidor.ChaveDeAdmin);
        var projeto = Guid.NewGuid();
        var alvenaria = Guid.NewGuid();
        var retrato = new ContextoDoProjeto("Casa da Ana", new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 1), new DateOnly(2026, 10, 18),
            [new TarefaNoContexto(1, "1", "Alvenaria", 0, TipoNoContexto.Tarefa, 10, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14), 40, "", 0, true, null, 0, alvenaria)],
            Obra: ["Prazo: IDP 0,95"]);
        await _cliente.MarcarObraAsync(sessao.Acesso, projeto, "Casa da Ana", retrato, default);
        (await _cliente.ObrasAsync(sessao.Acesso, default)).Single().Should().Match<ObraNoTelegram>(o => o.Projeto == projeto && o.Nome == "Casa da Ana" && o.Pendentes == 0);
        var codigo = await _cliente.CodigoDoTelegramAsync(sessao.Acesso, default);
        codigo.Link.Should().Be($"https://t.me/ProjectPlannerTesteBot?start={codigo.Codigo}");

        async Task Mandar(string texto, string? foto = null)
        {
            var mensagem = new System.Text.Json.Nodes.JsonObject { ["chat"] = new System.Text.Json.Nodes.JsonObject { ["id"] = 5 } };
            if (foto is null)
                mensagem["text"] = texto;
            else
                (mensagem["caption"], mensagem["photo"]) = (texto, new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["file_id"] = foto }));
            using var escopo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_s.Services);
            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Servicos.AtendenteDoTelegram>(escopo.ServiceProvider)
                .AtenderAsync(System.Text.Json.JsonDocument.Parse(new System.Text.Json.Nodes.JsonObject { ["message"] = mensagem }.ToJsonString()).RootElement, default);
        }
        await Mandar($"/start {codigo.Codigo}");
        (await _cliente.TelegramAsync(sessao.Acesso, default)).Ligado.Should().BeTrue();

        _s.Gemini.Resposta = """
            {"candidates":[{"content":{"parts":[{"text":"{\"mensagem\":\"Anotado.\",\"alteracoes\":[],\"atualizacoes\":[{\"tipo\":\"percentual\",\"tarefa\":\"1\",\"percentual\":70}]}"}]},"finishReason":"STOP"}]}
            """;
        await Mandar("alvenaria 70%");
        _s.Telegram.Arquivos["f1"] = [9, 8, 7];
        await Mandar("fachada", foto: "f1");

        var caixa = await _cliente.CaixaAsync(sessao.Acesso, projeto, default);
        caixa.Select(i => i.Tipo).Should().Equal(ItemDoTelegram.DeAtualizacoes, ItemDoTelegram.DeFoto);
        caixa[0].Atualizacoes!.Single().Should().Be(new AtualizacaoDaObra("percentual", "1", Percentual: 70));
        caixa[0].Tarefas!.Single().Should().Be(new TarefaDoTelegram(1, alvenaria));
        (await _cliente.FotoDaCaixaAsync(sessao.Acesso, caixa[1].Id, default)).Should().Equal(9, 8, 7);
        foreach (var item in caixa)
            await _cliente.LevarDaCaixaAsync(sessao.Acesso, item.Id, default);
        (await _cliente.CaixaAsync(sessao.Acesso, projeto, default)).Should().BeEmpty();
        await _cliente.DesmarcarObraAsync(sessao.Acesso, projeto, default);
        await _cliente.DesligarTelegramAsync(sessao.Acesso, default);
        ((await _cliente.ObrasAsync(sessao.Acesso, default)).Count, (await _cliente.TelegramAsync(sessao.Acesso, default)).Ligado).Should().Be((0, false));
    }
}
