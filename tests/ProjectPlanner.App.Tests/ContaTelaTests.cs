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
        tela.TextoDoUso.Should().Be("1.500 de 300.000 tokens de IA usados este mês (0,5%). A cota volta no dia 1º.");
        tela.PercentualDoUso.Should().BeApproximately(0.5, 0.001);
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
        _conta.Uso = new UsoDaConta(_conta.Perfil!, 12_000_000, 10_000_000);
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
