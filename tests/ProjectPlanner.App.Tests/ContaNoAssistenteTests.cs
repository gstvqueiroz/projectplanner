using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Bloco 45: a escolha entre a chave própria e a conta ProjectPlanner (configuração, arquivo e tela de configurações).
public sealed class ContaNoAssistenteTests
{
    private const string Chave = "AIzaChaveDoCofreParaTestes0000000001234";

    private readonly CofreEmMemoria _cofre = new();
    private readonly PreferenciasAssistenteEmMemoria _preferencias = new();
    private readonly ProvedorFalso _provedor = new();
    private readonly ContaFalsa _conta = new();

    private ConfiguracaoDoAssistente Nova() => new(_cofre, _preferencias, _provedor, _ => null, _conta);

    [Fact]
    public void SemEscolha_ChaveTemPrioridade_SemChaveVaiPelaConta()
    {
        var configuracao = Nova();
        (configuracao.Provedor, configuracao.ChaveApi).Should().Be((ProvedorDoAssistente.ChavePropria, null));

        _conta.Conectar();
        (configuracao.Provedor, configuracao.ChaveApi).Should().Be((ProvedorDoAssistente.Conta, ProvedorDaConta.Credencial));

        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre] = Chave;
        var comChave = Nova();
        (comChave.Provedor, comChave.ChaveApi, comChave.FinalDaChave).Should().Be((ProvedorDoAssistente.ChavePropria, Chave, "…1234"),
            "quem já usa a chave continua igual depois de entrar na conta");
    }

    [Fact]
    public void Escolha_Salva_ValeMesmoComAsDuas_ESemContaNaoHaCredencial()
    {
        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre] = Chave;
        var configuracao = Nova();

        configuracao.SalvarProvedor(ProvedorDoAssistente.Conta);
        (_preferencias.Salvas!.Provedor, configuracao.Provedor, configuracao.ChaveApi).Should().Be((ProvedorDoAssistente.Conta, ProvedorDoAssistente.Conta, null),
            "escolheu a conta mas não entrou: o assistente fica sem IA (respostas simuladas)");
        (configuracao.Origem, configuracao.FinalDaChave).Should().Be((OrigemDaChave.Cofre, "…1234"), "a chave própria continua guardada");
        _conta.Conectar();
        configuracao.ChaveApi.Should().Be(ProvedorDaConta.Credencial);

        configuracao.SalvarProvedor(ProvedorDoAssistente.ChavePropria);
        configuracao.ChaveApi.Should().Be(Chave);
        FluentActions.Invoking(() => configuracao.SalvarProvedor((ProvedorDoAssistente)9)).Should().Throw<ArgumentOutOfRangeException>();
        _preferencias.Salvas.Modelo.Should().Be("gemini-2.5-flash");
    }

    [Fact]
    public async Task Testar_PelaConta()
    {
        var configuracao = Nova();
        configuracao.SalvarProvedor(ProvedorDoAssistente.Conta);
        (await FluentActions.Invoking(() => configuracao.TestarAsync(null, "gemini-2.5-flash", default)).Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Be("Entre na sua conta ProjectPlanner (menu Conta) para testar.");

        _conta.Conectar();
        await configuracao.TestarAsync("", "gemini-2.5-pro", default);
        await configuracao.TestarAsync(Chave, "gemini-2.5-pro", default);
        _provedor.Testes.Should().Equal((ProvedorDaConta.Credencial, "gemini-2.5-pro"), (Chave, "gemini-2.5-pro"));
    }

    [Fact]
    public void Arquivo_GuardaOProvedor_EOAntigoSemEleContinuaValendo()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "pp-provedor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var arquivo = new ArquivoPreferenciasAssistente(Path.Combine(pasta, "assistente.json"));
            arquivo.Salvar(new PreferenciasAssistente("gemini-2.5-flash", PreferenciaDeModo.Direto, ProvedorDoAssistente.Conta));
            File.ReadAllText(arquivo.Caminho).Should().Contain("\"provedor\": \"conta\"");
            arquivo.Carregar().Should().Be(new PreferenciasAssistente("gemini-2.5-flash", PreferenciaDeModo.Direto, ProvedorDoAssistente.Conta));

            File.WriteAllText(arquivo.Caminho, """{"versao":1,"modelo":"gemini-2.5-pro","modo":"estudo"}""");
            arquivo.Carregar().Should().Be(new PreferenciasAssistente("gemini-2.5-pro", PreferenciaDeModo.Estudo, null));
            File.WriteAllText(arquivo.Caminho, """{"versao":1,"modelo":"gemini-2.5-pro","provedor":"outro"}""");
            arquivo.Carregar()!.Provedor.Should().BeNull();
            arquivo.Salvar(new PreferenciasAssistente("gemini-2.5-pro", Provedor: ProvedorDoAssistente.ChavePropria));
            arquivo.Carregar()!.Provedor.Should().Be(ProvedorDoAssistente.ChavePropria);
        }
        finally
        {
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public async Task TelaDeConfiguracoes_EscolheAConta_AbreATelaDaConta_ETestaPorEla()
    {
        var configuracao = new ConfiguracaoAssistenteFalsa();
        var dialogos = new DialogosFalsos { AoMostrarConta = c => c.EntrarComGoogleCommand.ExecuteAsync(null) };
        var tela = new ConfiguracaoAssistenteViewModel(configuracao, dialogos, _conta);
        (tela.UsarConta, tela.UsarChavePropria, tela.TestarCommand.CanExecute(null)).Should().Be((false, true, false));
        (tela.TextoDoProvedor, tela.Aviso).Should().Be(("Gemini (Google)", ConfiguracaoAssistenteViewModel.AvisoDePrivacidade));

        tela.UsarConta = true;
        (configuracao.Provedor, tela.Mensagem).Should().Be((ProvedorDoAssistente.Conta,
            "O assistente vai usar a conta ProjectPlanner: entre na conta para ele funcionar."));
        (tela.TextoDoProvedor, tela.Aviso).Should().Be(("Conta ProjectPlanner (Gemini pelo servidor)", ConfiguracaoAssistenteViewModel.AvisoDaConta));
        (tela.SituacaoDaConta, tela.TextoDoBotaoDaConta, tela.TestarCommand.CanExecute(null))
            .Should().Be(("Você não entrou na conta ProjectPlanner.", "Entrar ou criar conta…", false));

        await tela.AbrirContaCommand.ExecuteAsync(null);
        dialogos.Contas.Should().ContainSingle();
        (tela.ContaConectada, tela.SituacaoDaConta, tela.TextoDoBotaoDaConta, tela.TestarCommand.CanExecute(null))
            .Should().Be((true, "Conectado como Bia (bia@gmail.com), plano Grátis.", "Minha conta…", true));

        await tela.TestarCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Conexão OK: a conta ProjectPlanner funciona com o modelo gemini-2.5-flash.");

        tela.UsarChavePropria = true;
        (configuracao.Provedor, tela.Mensagem).Should().Be((ProvedorDoAssistente.ChavePropria, "O assistente vai usar a sua chave do Gemini: cole a chave e salve."));
        configuracao.Chave = Chave;
        tela.UsarConta = true;
        tela.Mensagem.Should().Be("O assistente passa a usar a sua conta ProjectPlanner.");
        tela.UsarConta = false;
        tela.Mensagem.Should().Be("O assistente passa a usar a sua chave do Gemini.");
    }

    [Fact]
    public void TelaDeConfiguracoes_AbreComAEscolhaSalva_SemGravarDeNovo()
    {
        var configuracao = new ConfiguracaoAssistenteFalsa { Provedor = ProvedorDoAssistente.Conta };
        var tela = new ConfiguracaoAssistenteViewModel(configuracao, new DialogosFalsos());
        (tela.UsarConta, tela.Mensagem, tela.ContaConectada).Should().Be((true, "", false));
    }

    [Fact]
    public void ProvedorConformeCredencial_AMarcaVaiPelaConta_OResto_PelaChave()
    {
        var chave = new ProvedorFalso();
        var conta = new ProvedorFalso();
        var provedor = new ProvedorConformeCredencial(chave, conta);

        provedor.CriarAssistente(ProvedorDaConta.Credencial, "gemini-2.5-flash");
        provedor.CriarAssistente(Chave, "gemini-2.5-pro");
        _ = provedor.TestarAsync(ProvedorDaConta.Credencial, "gemini-2.5-flash", default);

        (conta.Criados.Single(), chave.Criados.Single(), conta.Testes.Single().Chave)
            .Should().Be(((ProvedorDaConta.Credencial, "gemini-2.5-flash"), (Chave, "gemini-2.5-pro"), ProvedorDaConta.Credencial));
        (provedor.Nome, provedor.ModeloPadrao, provedor.NormalizarModelo("models/gemini-2.5-pro")).Should().Be(("Gemini (Google)", "gemini-2.5-flash", "gemini-2.5-pro"));
        provedor.CriarAnalista(ProvedorDaConta.Credencial, "m").Should().NotBeNull();
        provedor.CriarConsultor(Chave, "m").Should().NotBeNull();
        provedor.CriarAnalistaDaObra(Chave, "m").Should().NotBeNull();
    }

    [Fact]
    public void ProvedorDaConta_NomeEModelos()
    {
        var provedor = new ProvedorDaConta(new HttpClient(), new Uri("https://servidor.teste"), _conta);
        (provedor.Nome, provedor.ModeloPadrao, provedor.NormalizarModelo(" models/gemini-2.5-pro ")).Should().Be(("Conta ProjectPlanner", "gemini-2.5-flash", "gemini-2.5-pro"));
        FluentActions.Invoking(() => provedor.CriarAssistente(ProvedorDaConta.Credencial, "Modelo Inválido!")).Should().Throw<ArgumentException>();
    }
}
