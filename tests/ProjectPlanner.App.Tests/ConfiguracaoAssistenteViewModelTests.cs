using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Blocos 23 e 46: a tela Configurações do assistente: a conta, o nível do modelo (com o gasto em créditos), o modo e o teste.
public class ConfiguracaoAssistenteViewModelTests
{
    private readonly ConfiguracaoAssistenteFalsa _configuracao = new();
    private readonly DialogosFalsos _dialogos = new();
    private readonly ContaFalsa _conta = new();

    private static readonly IReadOnlyList<ModeloDaConta> DoProfissional =
        [.. ContaFalsa.ModelosDoGratis.Select(m => m with { Liberado = true })];

    private ConfiguracaoAssistenteViewModel Tela() => new(_configuracao, _dialogos, _conta);

    [Fact]
    public void SemConta_ConvidaAEntrar_SemNiveis_ETestarDesligado()
    {
        var tela = Tela();

        (tela.ContaConectada, tela.TextoDoBotaoDaConta, tela.TemNiveis, tela.ChaveDeDesenvolvimento, tela.TestarCommand.CanExecute(null))
            .Should().Be((false, "Entrar ou criar conta…", false, false, false));
        tela.SituacaoDaConta.Should().Be("Entre na sua conta ProjectPlanner para usar o assistente (o plano Grátis tem 300 créditos por mês).");
        ConfiguracaoAssistenteViewModel.AvisoDePrivacidade.Should().Contain("servidor do ProjectPlanner").And.Contain("não guarda o conteúdo");
    }

    [Fact]
    public async Task ComConta_CarregaOsNiveis_ForaDoPlanoDizOPorque()
    {
        _conta.Conectar();
        var tela = Tela();
        await tela.CarregarAsync();

        (tela.SituacaoDaConta, tela.TextoDoBotaoDaConta, tela.TemNiveis).Should().Be(("Conectado como Ana (ana@exemplo.com), plano Grátis.", "Minha conta…", true));
        tela.Niveis.Select(n => (n.Titulo, n.Detalhe, n.Habilitado)).Should().Equal(
            ("Padrão: Gemini 2.5 Flash", "cerca de 14 créditos por pedido", true),
            ("Avançado: Gemini 2.5 Pro", "do plano Profissional", false),
            ("Máximo: Claude Opus 5.5", "do plano Profissional", false));
        (tela.NivelEscolhido!.Id, tela.Mensagem).Should().Be(("gemini-2.5-flash", ""));

        tela.NivelEscolhido = tela.Niveis[2];
        (tela.NivelEscolhido!.Id, _configuracao.Modelo, tela.MensagemEhErro).Should().Be(("gemini-2.5-flash", "gemini-2.5-flash", true));
        tela.Mensagem.Should().Be("O nível Máximo (Claude Opus 5.5) é do plano Profissional.");
    }

    [Fact]
    public async Task Profissional_EscolherONivel_SalvaNaHora()
    {
        _conta.Conectar(plano: "profissional");
        _conta.ModelosDoServidor = DoProfissional;
        var tela = Tela();
        await tela.CarregarAsync();

        tela.NivelEscolhido = tela.Niveis[2];

        (_configuracao.Modelo, tela.MensagemEhErro).Should().Be(("claude-opus-5-5", false));
        tela.Mensagem.Should().Be("O assistente passa a usar o nível Máximo (Claude Opus 5.5), cerca de 140 créditos por pedido.");
        Tela().NivelEscolhido!.Id.Should().Be("claude-opus-5-5", "a conta já tem os níveis: a tela abre com a escolha");
    }

    [Fact]
    public async Task ModeloSalvoForaDoPlanoOuDaLista_VoltaAoPadrao()
    {
        _conta.Conectar();
        _configuracao.Modelo = "claude-opus-5-5"; // era Profissional e voltou ao Grátis
        var tela = Tela();
        await tela.CarregarAsync();
        (tela.NivelEscolhido!.Id, _configuracao.Modelo).Should().Be(("gemini-2.5-flash", "gemini-2.5-flash"));

        _configuracao.Modelo = "gemini-2.5-flash-lite"; // um modelo que saiu da lista
        await tela.CarregarAsync();
        _configuracao.Modelo.Should().Be("gemini-2.5-flash");
    }

    [Fact]
    public async Task AbrirAConta_EntraEOsNiveisAparecem()
    {
        _dialogos.AoMostrarConta = conta => conta.EntrarComGoogleCommand.ExecuteAsync(null);
        var tela = Tela();

        await tela.AbrirContaCommand.ExecuteAsync(null);

        _dialogos.Contas.Should().ContainSingle();
        (tela.ContaConectada, tela.SituacaoDaConta, tela.TemNiveis, tela.TestarCommand.CanExecute(null))
            .Should().Be((true, "Conectado como Bia (bia@gmail.com), plano Grátis.", true, true));
    }

    [Fact]
    public async Task CarregarSemServidor_MostraOMotivo()
    {
        _conta.Conectar();
        _conta.Erro = new ErroDaContaException("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.", 0);
        var tela = Tela();

        await tela.CarregarAsync();

        (tela.TemNiveis, tela.Mensagem, tela.MensagemEhErro).Should().Be((false,
            "Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.", true));
    }

    [Fact]
    public async Task Testar_ComONivelEscolhido_OkOuOErroDoServidor()
    {
        _conta.Conectar();
        var tela = Tela();
        await tela.CarregarAsync();

        await tela.TestarCommand.ExecuteAsync(null);
        (_configuracao.Testes.Single(), tela.Mensagem, tela.MensagemEhErro, tela.Ocupado)
            .Should().Be(("gemini-2.5-flash", "Conexão OK: a conta funciona com o nível Padrão (Gemini 2.5 Flash).", false, false));

        _configuracao.ErroNoTeste = new ErroDoAssistenteException("A sessão da sua conta ProjectPlanner acabou. Entre de novo (menu Conta).");
        await tela.TestarCommand.ExecuteAsync(null);
        (tela.Mensagem, tela.MensagemEhErro).Should().Be(("A sessão da sua conta ProjectPlanner acabou. Entre de novo (menu Conta).", true));
    }

    [Fact]
    public async Task ChaveDeDesenvolvimento_Explica_ETestaSemConta()
    {
        _configuracao.ChaveDeDesenvolvimento = true;
        var tela = Tela();

        (tela.ChaveDeDesenvolvimento, tela.TestarCommand.CanExecute(null)).Should().Be((true, true));
        tela.TextoDaChaveDeDesenvolvimento.Should().StartWith("Usando a chave da variável de ambiente GEMINI_API_KEY (desenvolvimento)");
        await tela.TestarCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Conexão OK com o modelo gemini-2.5-flash.");
    }

    [Fact]
    public void ArgumentosNulos_Recusados()
    {
        FluentActions.Invoking(() => new ConfiguracaoAssistenteViewModel(null!, _dialogos)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ConfiguracaoAssistenteViewModel(_configuracao, null!)).Should().Throw<ArgumentNullException>();
        new ConfiguracaoAssistenteViewModel(_configuracao, _dialogos).ContaConectada.Should().BeFalse("sem conta passada");
    }
}
