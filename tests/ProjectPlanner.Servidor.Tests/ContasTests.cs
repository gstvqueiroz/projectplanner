using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 44: cadastro com código por e-mail, login, bloqueio, sessões (renovar e sair) e troca de senha.
public sealed class ContasTests : IDisposable
{
    private readonly Servidor _s = new();

    public void Dispose() => _s.Dispose();

    private static async Task<Sessao> Sessao(HttpResponseMessage r)
    {
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<Sessao>())!;
    }

    [Fact]
    public async Task Cadastro_ConfirmaPeloCodigo_EntraEVeOPerfil()
    {
        var cadastro = await _s.Post("/v1/contas", new { email = " Ana@Exemplo.com ", senha = "senha-boa-123", nome = "Ana Souza" });

        cadastro.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await cadastro.Content.ReadFromJsonAsync<Resposta>())!.Mensagem.Should().Be(Contas.MensagemDoCadastro);
        var email = _s.Emails.Enviados.Single();
        (email.Para, email.Assunto, email.Codigo).Should().Match<(string, string, string?)>(e => e.Item1 == "Ana@Exemplo.com"
            && e.Item2 == "Seu código do ProjectPlanner" && e.Item3!.Length == 6);
        _s.NoBanco(b => b.Usuarios.Single()).Should().Match<Usuario>(u => !u.EmailConfirmado && u.SenhaHash == null && u.EmailNormalizado == "ana@exemplo.com");
        _s.NoBanco(b => b.CodigosDeEmail.Single().Hash).Should().NotBe(email.Codigo, "só o hash do código fica no banco");

        var sessao = await Sessao(await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = email.Codigo, senha = "senha-boa-123" }));
        sessao.Usuario.Should().Be(new PerfilDoUsuario("Ana@Exemplo.com", "Ana Souza", Planos.Gratis, true, false));
        (sessao.AcessoExpiraEm, sessao.RenovacaoExpiraEm).Should().Be((_s.Relogio.Agora.UtcDateTime.AddMinutes(15), _s.Relogio.Agora.UtcDateTime.AddDays(30)));
        _s.NoBanco(b => b.TokensDeRenovacao.Single().Hash).Should().Be(Segredos.Hash(sessao.Renovacao));

        var eu = await _s.Get("/v1/eu", sessao.Acesso);
        eu.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await eu.Content.ReadAsStringAsync()).RootElement;
        (json.GetProperty("perfil").GetProperty("nome").GetString(), json.GetProperty("tokensUsadosNoMes").GetInt64(),
                json.GetProperty("creditosUsadosNoMes").GetDecimal(), json.GetProperty("creditosDoPlano").GetInt64())
            .Should().Be(("Ana Souza", 0L, 0m, 300L));

        var login = await Sessao(await _s.Post("/v1/sessoes", new { email = "ANA@exemplo.com", senha = "senha-boa-123" }));
        login.Renovacao.Should().NotBe(sessao.Renovacao);
    }

    [Theory]
    [InlineData("", "senha-boa-123", "Ana", "Informe um e-mail válido.")]
    [InlineData("ana@", "senha-boa-123", "Ana", "Informe um e-mail válido.")]
    [InlineData("ana@localhost", "senha-boa-123", "Ana", "Informe um e-mail válido.")]
    [InlineData("ana@exemplo.com", "curta", "Ana", "A senha precisa ter pelo menos 10 caracteres.")]
    [InlineData("ana@exemplo.com", "senha-boa-123", " ", "Informe o nome (até 120 caracteres).")]
    public async Task Cadastro_DadosInvalidos_400SemEmail(string email, string senha, string nome, string erro)
    {
        var r = await _s.Post("/v1/contas", new { email, senha, nome });

        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.BadRequest, erro));
        _s.Emails.Enviados.Should().BeEmpty();
        (await Servidor.Erro(await _s.Post("/v1/contas", new { email = "a@b.com", senha = new string('x', 201), nome = "A" })))
            .Should().Be("A senha pode ter até 200 caracteres.");
    }

    [Fact]
    public async Task Cadastro_DeEmailQueJaTemConta_RespondeIgual_AvisaODono_ENaoMudaASenha()
    {
        await _s.CriarConta();
        _s.Emails.Enviados.Clear();

        var r = await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-do-intruso", nome = "Intruso" });

        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await r.Content.ReadFromJsonAsync<Resposta>())!.Mensagem.Should().Be(Contas.MensagemDoCadastro, "não revela que a conta existe");
        var aviso = _s.Emails.Enviados.Single();
        (aviso.Assunto, aviso.Codigo).Should().Be(("Você já tem uma conta no ProjectPlanner", null));
        aviso.Texto.Should().Contain("Esqueci a senha");
        (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-do-intruso" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" })).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.NoBanco(b => b.Usuarios.Single().Nome).Should().Be("Ana");
    }

    [Fact]
    public async Task Confirmar_ExigeASenhaDoCadastroDoCodigo_UmCadastroDeOutroNaoViraASenha()
    {
        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-da-ana-1", nome = "Ana" });
        var codigoDaAna = _s.Emails.Ultimo.Codigo;
        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-do-intruso", nome = "Intruso" });
        var codigoDoIntruso = _s.Emails.Ultimo.Codigo; // chega à Ana, não ao intruso

        (await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = codigoDaAna, senha = "senha-da-ana-1" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "um cadastro novo invalida o código anterior");
        var r = await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = codigoDoIntruso, senha = "senha-da-ana-1" });
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.BadRequest, Contas.CodigoInvalido), "o código é de um cadastro com outra senha");

        // A Ana cadastra de novo e confirma: vale a senha dela e o nome dela.
        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-da-ana-1", nome = "Ana" });
        var sessao = await Sessao(await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = _s.Emails.Ultimo.Codigo, senha = "senha-da-ana-1" }));
        sessao.Usuario.Nome.Should().Be("Ana");
        (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-do-intruso" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = _s.Emails.Ultimo.Codigo, senha = "senha-da-ana-1" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "o código é de uso único e a conta já está confirmada");
    }

    [Fact]
    public async Task Codigo_Vencido_OuErradoCincoVezes_NaoVale_EReenviarMandaOutroComAMesmaSenha()
    {
        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-boa-123", nome = "Ana" });
        var codigo = _s.Emails.Ultimo.Codigo!;
        var errado = codigo == "000000" ? "111111" : "000000";
        for (var i = 0; i < Contas.TentativasDoCodigo; i++)
            (await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = errado, senha = "senha-boa-123" })).StatusCode
                .Should().Be(HttpStatusCode.BadRequest);
        (await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo, senha = "senha-boa-123" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "depois de 5 erros nem o certo vale");

        (await _s.Post("/v1/contas/reenviar", new { email = "ana@exemplo.com" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var novo = _s.Emails.Ultimo.Codigo;
        _s.Relogio.Andar(TimeSpan.FromMinutes(Contas.MinutosDoCodigo + 1));
        (await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = novo, senha = "senha-boa-123" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "venceu");

        await _s.Post("/v1/contas/reenviar", new { email = "ana@exemplo.com" });
        await Sessao(await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo = _s.Emails.Ultimo.Codigo, senha = "senha-boa-123" }));

        _s.Emails.Enviados.Clear();
        (await _s.Post("/v1/contas/reenviar", new { email = "ana@exemplo.com" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await _s.Post("/v1/contas/reenviar", new { email = "ninguem@exemplo.com" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        _s.Emails.Enviados.Should().BeEmpty("conta confirmada ou inexistente: nada a mandar, e a resposta é a mesma");
    }

    [Fact]
    public async Task Codigos_NoMaximoCincoPorHora_PorConta()
    {
        for (var i = 0; i < Contas.CodigosPorHora + 2; i++)
            (await _s.Post("/v1/contas/reenviar", new { email = "x" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-boa-123", nome = "Ana" });
        for (var i = 0; i < Contas.CodigosPorHora + 2; i++)
            await _s.Post("/v1/contas/reenviar", new { email = "ana@exemplo.com" });

        _s.Emails.Enviados.Should().HaveCount(Contas.CodigosPorHora);
        _s.Relogio.Andar(TimeSpan.FromMinutes(61));
        await _s.Post("/v1/contas/reenviar", new { email = "ana@exemplo.com" });
        _s.Emails.Enviados.Should().HaveCount(Contas.CodigosPorHora + 1, "passada a hora, volta a mandar");
    }

    [Fact]
    public async Task Login_MensagemGenerica_ESemConfirmar403SoComASenhaCerta()
    {
        var semConta = await _s.Post("/v1/sessoes", new { email = "ninguem@exemplo.com", senha = "qualquer-coisa" });
        (semConta.StatusCode, await Servidor.Erro(semConta)).Should().Be((HttpStatusCode.Unauthorized, Contas.LoginInvalido));

        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-boa-123", nome = "Ana" });
        var pendente = await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" });
        (pendente.StatusCode, await Servidor.Erro(pendente)).Should().Be((HttpStatusCode.Forbidden,
            "Confirme o e-mail antes de entrar: digite o código que mandamos (ou peça outro)."));
        (await Servidor.Erro(await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-errada-1" }))).Should().Be(Contas.LoginInvalido);
        (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_CincoSenhasErradas_BloqueiaQuinzeMinutos()
    {
        await _s.CriarConta();
        for (var i = 0; i < 5; i++)
            (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-errada-" + i })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var bloqueado = await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" });
        (bloqueado.StatusCode, await Servidor.Erro(bloqueado)).Should().Be(((HttpStatusCode)429,
            "Muitas tentativas erradas. Tente de novo em 15 minuto(s) ou troque a senha."));

        _s.Relogio.Andar(TimeSpan.FromMinutes(15.5));
        await Sessao(await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" }));
        _s.NoBanco(b => b.Usuarios.Single()).Should().Match<Usuario>(u => u.TentativasFalhas == 0 && u.BloqueadoAte == null);
    }

    [Fact]
    public async Task Renovar_TrocaOToken_EReusarUmVelhoDerrubaAFamilia()
    {
        var primeira = await _s.CriarConta();
        var segunda = await Sessao(await _s.Post("/v1/sessoes/renovar", new { renovacao = primeira.Renovacao }));
        var terceira = await Sessao(await _s.Post("/v1/sessoes/renovar", new { renovacao = segunda.Renovacao }));
        (await _s.Get("/v1/eu", terceira.Acesso)).StatusCode.Should().Be(HttpStatusCode.OK);
        var outroAparelho = await Sessao(await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" }));

        // Alguém reusa o primeiro (já trocado): pode ter sido copiado, então a família toda cai.
        var reuso = await _s.Post("/v1/sessoes/renovar", new { renovacao = primeira.Renovacao });
        (reuso.StatusCode, await Servidor.Erro(reuso)).Should().Be((HttpStatusCode.Unauthorized, "Sessão vencida: entre de novo."));
        (await _s.Post("/v1/sessoes/renovar", new { renovacao = terceira.Renovacao })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Sessao(await _s.Post("/v1/sessoes/renovar", new { renovacao = outroAparelho.Renovacao }));

        (await _s.Post("/v1/sessoes/renovar", new { renovacao = "inventado" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.Post("/v1/sessoes/renovar", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sessoes_VencemNoPrazo_ESairDerrubaARenovacao()
    {
        var sessao = await _s.CriarConta();
        _s.Relogio.Andar(TimeSpan.FromMinutes(16));
        var vencido = await _s.Get("/v1/eu", sessao.Acesso);
        (vencido.StatusCode, await Servidor.Erro(vencido)).Should().Be((HttpStatusCode.Unauthorized,
            "Entre na sua conta ProjectPlanner (ou a sessão venceu: entre de novo)."));
        (await _s.Get("/v1/eu")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.Get("/v1/eu", "nao-e-um-jwt")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var renovada = await Sessao(await _s.Post("/v1/sessoes/renovar", new { renovacao = sessao.Renovacao }));
        (await _s.Post("/v1/sessoes/sair", new { renovacao = renovada.Renovacao })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _s.Post("/v1/sessoes/renovar", new { renovacao = renovada.Renovacao })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.Post("/v1/sessoes/sair", new { renovacao = "inventado" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var outra = await Sessao(await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" }));
        _s.Relogio.Andar(TimeSpan.FromDays(31));
        (await _s.Post("/v1/sessoes/renovar", new { renovacao = outra.Renovacao })).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "30 dias");
    }

    [Fact]
    public async Task EsqueciASenha_RedefineComOCodigo_DerrubaAsOutrasSessoes_EDesbloqueia()
    {
        var antiga = await _s.CriarConta();
        for (var i = 0; i < 5; i++)
            await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-errada-" + i });
        _s.Emails.Enviados.Clear();

        (await _s.Post("/v1/senha/esqueci", new { email = "ninguem@exemplo.com" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        _s.Emails.Enviados.Should().BeEmpty();
        var pedido = await _s.Post("/v1/senha/esqueci", new { email = "ana@exemplo.com" });
        (await pedido.Content.ReadFromJsonAsync<Resposta>())!.Mensagem.Should().Be(Contas.MensagemDaSenha);
        var email = _s.Emails.Enviados.Single();
        email.Assunto.Should().Be("Trocar a senha do ProjectPlanner");

        (await _s.Post("/v1/senha/redefinir", new { email = "ana@exemplo.com", codigo = email.Codigo, novaSenha = "curta" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        var errado = await _s.Post("/v1/senha/redefinir", new { email = "ana@exemplo.com", codigo = "x", novaSenha = "senha-nova-456" });
        (await Servidor.Erro(errado)).Should().Be(Contas.CodigoInvalido);

        var nova = await Sessao(await _s.Post("/v1/senha/redefinir", new { email = "ana@exemplo.com", codigo = email.Codigo, novaSenha = "senha-nova-456" }));
        (await _s.Post("/v1/sessoes/renovar", new { renovacao = antiga.Renovacao })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Sessao(await _s.Post("/v1/sessoes/renovar", new { renovacao = nova.Renovacao }));
        (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Sessao(await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-nova-456" }));
        (await _s.Post("/v1/senha/redefinir", new { email = "ana@exemplo.com", codigo = email.Codigo, novaSenha = "senha-outra-789" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "o código já foi usado");
    }

    [Fact]
    public async Task EsqueciASenha_NumCadastroNaoConfirmado_ConfirmaOEmail()
    {
        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-esquecida", nome = "Ana" });
        await _s.Post("/v1/senha/esqueci", new { email = "ana@exemplo.com" });

        var sessao = await Sessao(await _s.Post("/v1/senha/redefinir", new { email = "ana@exemplo.com", codigo = _s.Emails.Ultimo.Codigo, novaSenha = "senha-nova-456" }));

        sessao.Usuario.TemSenha.Should().BeTrue();
        _s.NoBanco(b => b.Usuarios.Single().EmailConfirmado).Should().BeTrue();
    }

    [Fact]
    public async Task Google_CriaVinculaERecusa()
    {
        _s.Google.Tokens["token-novo"] = new ContaGoogle("g-1", "Bia@Gmail.com", true, "Bia");
        _s.Google.Tokens["token-da-ana"] = new ContaGoogle("g-2", "ana@exemplo.com", true, "Ana G.");
        _s.Google.Tokens["nao-verificado"] = new ContaGoogle("g-3", "caio@exemplo.com", false, "Caio");

        var invalido = await _s.Post("/v1/sessoes/google", new { idToken = "falso" });
        (invalido.StatusCode, await Servidor.Erro(invalido)).Should().Be((HttpStatusCode.Unauthorized, "Não foi possível confirmar a conta Google. Tente de novo."));
        (await _s.Post("/v1/sessoes/google", new { idToken = "" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.Post("/v1/sessoes/google", new { idToken = "nao-verificado" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var bia = await Sessao(await _s.Post("/v1/sessoes/google", new { idToken = "token-novo" }));
        bia.Usuario.Should().Be(new PerfilDoUsuario("Bia@Gmail.com", "Bia", Planos.Gratis, false, true));
        (await _s.Post("/v1/sessoes", new { email = "bia@gmail.com", senha = "qualquer-senha" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Sessao(await _s.Post("/v1/sessoes/google", new { idToken = "token-novo" }));
        _s.NoBanco(b => b.Usuarios.Count()).Should().Be(1);

        // A conta com senha e o mesmo e-mail é vinculada (as duas formas de entrar valem).
        await _s.CriarConta();
        var ana = await Sessao(await _s.Post("/v1/sessoes/google", new { idToken = "token-da-ana" }));
        (ana.Usuario.Nome, ana.Usuario.TemSenha, ana.Usuario.Google).Should().Be(("Ana", true, true));
        await Sessao(await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-boa-123" }));

        // Quem tem só o Google e tenta cadastrar com senha recebe o aviso de usar o Google.
        _s.Emails.Enviados.Clear();
        await _s.Post("/v1/contas", new { email = "bia@gmail.com", senha = "senha-boa-123", nome = "Bia" });
        _s.Emails.Ultimo.Texto.Should().Contain("Entrar com Google");
    }

    [Fact]
    public async Task Google_NumCadastroNaoConfirmado_ConfirmaSemASenhaPendente()
    {
        await _s.Post("/v1/contas", new { email = "ana@exemplo.com", senha = "senha-do-intruso", nome = "Intruso" });
        var codigo = _s.Emails.Ultimo.Codigo;
        _s.Google.Tokens["t"] = new ContaGoogle("g-2", "ana@exemplo.com", true, "Ana");

        var sessao = await Sessao(await _s.Post("/v1/sessoes/google", new { idToken = "t" }));

        (sessao.Usuario.TemSenha, sessao.Usuario.Google).Should().Be((false, true));
        (await _s.Post("/v1/contas/confirmar", new { email = "ana@exemplo.com", codigo, senha = "senha-do-intruso" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "a conta já está confirmada pelo Google");
        (await _s.Post("/v1/sessoes", new { email = "ana@exemplo.com", senha = "senha-do-intruso" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Eu_ContaApagada_401()
    {
        var sessao = await _s.CriarConta();
        _s.NoBanco(b => b.Usuarios.ExecuteDelete());

        (await _s.Get("/v1/eu", sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
