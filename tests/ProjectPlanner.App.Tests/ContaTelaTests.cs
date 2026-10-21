using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Bloco 45: a tela da conta (menu Conta): entrar, criar com o código do e-mail, trocar a senha, Google, plano e uso, sair.
public sealed class ContaTelaTests
{
    private readonly ContaFalsa _conta = new();

    private ContaViewModel Tela() => new(_conta);

    [Fact]
    public async Task Entrar_VaiParaMinhaConta_ComOPlanoEOUso()
    {
        var tela = Tela();
        (tela.Pagina, tela.Titulo, tela.PedeEmail, tela.PedeSenha, tela.EntrarCommand.CanExecute(null)).Should().Be((PaginaDaConta.Entrar, "Entrar", true, true, false));

        (tela.Email, tela.Senha) = ("ana@exemplo.com", ContaFalsa.SenhaCerta);
        tela.EntrarCommand.CanExecute(null).Should().BeTrue();
        await tela.EntrarCommand.ExecuteAsync(null);

        (tela.Pagina, tela.Titulo, tela.NomeDaConta, tela.EmailDaConta, tela.Plano).Should().Be((PaginaDaConta.Perfil, "Minha conta", "Ana", "ana@exemplo.com", "Grátis"));
        tela.TextoDoUso.Should().Be("26 de 300 créditos de IA usados este mês (8,5%). Eles voltam no dia 1º.");
        tela.PercentualDoUso.Should().BeApproximately(8.5, 0.001);
        (tela.Mensagem, tela.MensagemEhErro, tela.Senha, tela.Ocupado).Should().Be(("Você entrou na conta.", false, "", false));
        _conta.Pedidos.Should().Equal("entrar ana@exemplo.com", "uso");
    }

    [Fact]
    public async Task Entrar_SenhaErrada_FicaNaTelaComAMensagem_ESemConfirmar_VaiParaOCodigo()
    {
        var tela = Tela();
        (tela.Email, tela.Senha) = ("ana@exemplo.com", "errada-errada");
        await tela.EntrarCommand.ExecuteAsync(null);
        (tela.Pagina, tela.Mensagem, tela.MensagemEhErro).Should().Be((PaginaDaConta.Entrar, "E-mail ou senha incorretos.", true));

        _conta.PendenteDeConfirmacao = true;
        tela.Senha = ContaFalsa.SenhaCerta;
        await tela.EntrarCommand.ExecuteAsync(null);
        (tela.Pagina, tela.MensagemEhErro, tela.Senha).Should().Be((PaginaDaConta.Confirmar, true, ContaFalsa.SenhaCerta));
        tela.Mensagem.Should().StartWith("Confirme o e-mail antes de entrar");

        tela.Codigo = ContaFalsa.CodigoCerto;
        await tela.ConfirmarCommand.ExecuteAsync(null);
        tela.Pagina.Should().Be(PaginaDaConta.Perfil);
    }

    [Fact]
    public async Task CriarConta_PedeOCodigo_ReenviaEConfirma()
    {
        var tela = Tela();
        tela.IrParaCriarCommand.Execute(null);
        (tela.Pagina, tela.Titulo).Should().Be((PaginaDaConta.Criar, "Criar conta"));
        (tela.Email, tela.Senha) = ("ana@exemplo.com", ContaFalsa.SenhaCerta);
        tela.CriarCommand.CanExecute(null).Should().BeFalse("falta o nome");
        tela.Nome = "Ana";

        await tela.CriarCommand.ExecuteAsync(null);
        (tela.Pagina, tela.MensagemEhErro).Should().Be((PaginaDaConta.Confirmar, false));
        tela.Mensagem.Should().EndWith("Olhe também a caixa de spam.");
        tela.ConfirmarCommand.CanExecute(null).Should().BeFalse("falta o código");

        await tela.ReenviarCodigoCommand.ExecuteAsync(null);
        tela.Codigo = "000000";
        await tela.ConfirmarCommand.ExecuteAsync(null);
        (tela.Pagina, tela.Mensagem).Should().Be((PaginaDaConta.Confirmar, "Código inválido ou vencido. Peça outro."));
        tela.Codigo = $" {ContaFalsa.CodigoCerto} ";
        tela.Codigo = ContaFalsa.CodigoCerto;
        await tela.ConfirmarCommand.ExecuteAsync(null);
        (tela.Pagina, tela.Mensagem).Should().Be((PaginaDaConta.Perfil, "Conta confirmada. Bem-vindo!"));
        _conta.Pedidos.Should().StartWith(["criar Ana ana@exemplo.com", "reenviar ana@exemplo.com", "confirmar ana@exemplo.com 000000"]);
    }

    [Fact]
    public async Task EsqueciASenha_CodigoENovaSenha()
    {
        var tela = Tela();
        tela.IrParaEsqueciCommand.Execute(null);
        (tela.Pagina, tela.PedeEmail, tela.PedeSenha, tela.PedirCodigoDaSenhaCommand.CanExecute(null)).Should().Be((PaginaDaConta.Esqueci, true, false, false));
        tela.Email = "ana@exemplo.com";
        await tela.PedirCodigoDaSenhaCommand.ExecuteAsync(null);
        tela.Pagina.Should().Be(PaginaDaConta.Redefinir);
        tela.RedefinirCommand.CanExecute(null).Should().BeFalse();

        (tela.Codigo, tela.NovaSenha) = (ContaFalsa.CodigoCerto, "senha-nova-456");
        await tela.RedefinirCommand.ExecuteAsync(null);
        (tela.Pagina, tela.Mensagem, tela.NovaSenha).Should().Be((PaginaDaConta.Perfil, "Senha trocada. As outras sessões da conta foram encerradas.", ""));

        tela.IrParaEntrarCommand.Execute(null);
        tela.Pagina.Should().Be(PaginaDaConta.Entrar);
    }

    [Fact]
    public async Task Google_EsperaONavegador_ECancelar()
    {
        var tela = Tela();
        _conta.Segurar = new TaskCompletionSource();
        var google = tela.EntrarComGoogleCommand.ExecuteAsync(null);
        (tela.Ocupado, tela.EntrarCommand.CanExecute(null), tela.EntrarComGoogleCommand.CanExecute(null)).Should().Be((true, false, false));
        tela.Mensagem.Should().StartWith("Continue no navegador");
        tela.CancelarCommand.Execute(null);
        await google;
        (tela.Ocupado, tela.Mensagem, tela.Pagina).Should().Be((false, "Cancelado.", PaginaDaConta.Entrar));

        _conta.Segurar = null;
        await tela.EntrarComGoogleCommand.ExecuteAsync(null);
        (tela.Pagina, tela.NomeDaConta, tela.EntrouComGoogle, tela.Mensagem).Should().Be((PaginaDaConta.Perfil, "Bia", true, "Você entrou com o Google."));
    }

    [Fact]
    public async Task JaConectado_AbreEmMinhaConta_AtualizaESai()
    {
        _conta.Conectar(plano: "profissional");
        _conta.Uso = new UsoDaConta(_conta.Perfil!, 1_200_000, 6_300m, 6_000);
        var tela = Tela();
        (tela.Pagina, tela.Email).Should().Be((PaginaDaConta.Perfil, "ana@exemplo.com"));

        await tela.AbrirAsync();
        (tela.Plano, tela.PercentualDoUso).Should().Be(("Profissional", 100), "a barra para em 100% mesmo passando da cota");

        _conta.Erro = new ErroDaContaException("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.", 0);
        await tela.AtualizarUsoCommand.ExecuteAsync(null);
        (tela.Mensagem, tela.MensagemEhErro).Should().Be(("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.", true));

        await tela.SairCommand.ExecuteAsync(null);
        (tela.Pagina, tela.Plano, tela.TextoDoUso, tela.Mensagem, _conta.Conectada).Should().Be((PaginaDaConta.Entrar, "", "", "Você saiu da conta neste computador.", false));
    }

    [Fact]
    public async Task Fechando_CancelaOQueEstaEmAndamento()
    {
        var tela = Tela();
        _conta.Segurar = new TaskCompletionSource();
        (tela.Email, tela.Senha) = ("ana@exemplo.com", ContaFalsa.SenhaCerta);
        var entrando = tela.EntrarCommand.ExecuteAsync(null);
        tela.Fechando();
        await entrando;
        (tela.Ocupado, tela.Mensagem, _conta.Conectada).Should().Be((false, "Cancelado.", false));
    }

    [Theory]
    [InlineData("gratis", "Grátis")]
    [InlineData("profissional", "Profissional")]
    [InlineData("ouro", "ouro")]
    public void NomeDoPlano(string plano, string nome) => ContaViewModel.NomeDoPlano(plano).Should().Be(nome);

    [Fact]
    public async Task SemConta_EntrarFalhaComAMensagem_EAIAAvisa()
    {
        var sem = new SemContaProjectPlanner();
        (sem.Conectada, sem.Perfil, sem.ProblemaNoCofre).Should().Be((false, null, null));
        (await FluentActions.Invoking(() => sem.EntrarAsync("a", "b", default)).Should().ThrowAsync<ErroDaContaException>())
            .Which.Message.Should().Be("A conta ProjectPlanner não está disponível.");
        (await FluentActions.Invoking(() => sem.AcessoAsync(false, default)).Should().ThrowAsync<IA.ErroDoAssistenteException>())
            .Which.Message.Should().Be(ContaProjectPlanner.SemConta);
        await sem.SairAsync(default);
    }
}

// Bloco 47: plano e pagamento na tela da conta (o pagamento em si é no Mercado Pago, pelo navegador).
public sealed class ContaPagamentosTests
{
    private readonly ContaFalsa _conta = new();

    private static SituacaoDosPagamentos Situacao(string plano = "gratis", string? assinatura = null, DateTime? ate = null, bool teste = false) =>
        new(true, teste, plano, ate, 59m, 6000, 3000, 19m, 0, assinatura, assinatura == "authorized" ? new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc) : null);

    private async Task<ContaViewModel> Aberta(SituacaoDosPagamentos? pagamentos, UsoDaConta? uso = null)
    {
        _conta.Conectar(plano: pagamentos?.Plano ?? "gratis");
        _conta.Pagamentos = pagamentos;
        _conta.Uso = uso;
        var tela = new ContaViewModel(_conta);
        await tela.AbrirAsync();
        return tela;
    }

    [Fact]
    public async Task Gratis_OfereceAssinarEComprar_ComOsPrecos_EAbreONavegador()
    {
        var tela = await Aberta(Situacao(teste: true));

        (tela.MostraPagamentos, tela.PodeAssinarPlano, tela.TemAssinatura, tela.EmModoDeTeste, tela.TextoDoPlanoPago).Should().Be((true, true, false, true, ""));
        (tela.TextoDoAssinar, tela.TextoDoPacote).Should().Be(("Assinar o Profissional: R$ 59,00/mês (6.000 créditos por mês)", "Comprar 3.000 créditos: R$ 19,00"));

        await tela.AssinarCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Conclua a assinatura no navegador (Pix ou cartão). Depois de pagar, clique em Atualizar.");
        await tela.ComprarCreditosCommand.ExecuteAsync(null);
        tela.Mensagem.Should().StartWith("Conclua a compra no navegador.");
        _conta.Pedidos.Should().Equal("uso", "pagamentos", "assinar", "comprar créditos");
    }

    [Fact]
    public async Task Assinando_MostraAProximaCobranca_ECancelarPedeConfirmacao()
    {
        var tela = await Aberta(Situacao("profissional", "authorized"));

        (tela.PodeAssinarPlano, tela.TemAssinatura, tela.TextoDoPlanoPago).Should().Be((false, true, "Assinatura ativa: próxima cobrança em " +
            new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy") + "."));
        tela.AssinarCommand.CanExecute(null).Should().BeFalse();

        tela.CancelarAssinaturaCommand.Execute(null);
        (tela.ConfirmandoCancelamento, tela.Mensagem).Should().Be((true,
            "Cancelar a assinatura? O Profissional continua valendo até o fim do mês já pago, e nada mais é cobrado."));
        tela.ManterAssinaturaCommand.Execute(null);
        (tela.ConfirmandoCancelamento, tela.Mensagem).Should().Be((false, ""));
        _conta.Pedidos.Should().NotContain("cancelar assinatura");

        tela.CancelarAssinaturaCommand.Execute(null);
        await tela.ConfirmarCancelamentoCommand.ExecuteAsync(null);

        var data = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy");
        (tela.ConfirmandoCancelamento, tela.Mensagem, tela.MensagemEhErro).Should().Be((false, $"Assinatura cancelada. O Profissional vale até {data}.", false));
        (tela.TemAssinatura, tela.PodeAssinarPlano, tela.TextoDoPlanoPago).Should().Be((false, true, $"O Profissional vale até {data} (assinatura cancelada)."));
    }

    [Fact]
    public async Task SemPagamentosNoServidor_ATelaSegueSemEles_EOsAvulsosEntramNoUso()
    {
        var tela = await Aberta(null, new UsoDaConta(new PerfilDaConta("ana@exemplo.com", "Ana", "gratis", true, false), 0, 300m, 300, 2500m));

        (tela.MostraPagamentos, tela.PodeAssinarPlano, tela.Mensagem, tela.MensagemEhErro).Should().Be((false, false, "", false));
        tela.ComprarCreditosCommand.CanExecute(null).Should().BeFalse();
        tela.TextoDoUso.Should().Be("300 de 300 créditos de IA usados este mês (100%). Eles voltam no dia 1º. Mais 2.500 créditos avulsos, que não vencem.");
        _conta.CreditosRestantes.Should().Be(2500m, "os do plano acabaram, os avulsos não");

        // Uma falha do servidor ao abrir o pagamento aparece como erro.
        _conta.Pagamentos = Situacao();
        await tela.AtualizarUsoCommand.ExecuteAsync(null);
        _conta.Erro = new ErroDaContaException("O Mercado Pago não respondeu. Tente de novo em instantes.", 502);
        await tela.AssinarCommand.ExecuteAsync(null);
        (tela.Mensagem, tela.MensagemEhErro).Should().Be(("O Mercado Pago não respondeu. Tente de novo em instantes.", true));

        await tela.SairCommand.ExecuteAsync(null);
        (tela.Pagamentos, tela.MostraPagamentos).Should().Be((null, false));
    }
}
